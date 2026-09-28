using BazaarPlusPlus.Game.BundlePipeline;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Storage.BundleQueue;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;
using BepInEx.Logging;
using Microsoft.Data.Sqlite;

internal static class BundleSealLoggingTests
{
    internal static async Task RunAsync(string root)
    {
        var paths = new TestPaths(root);
        var store = new RunLogStore(paths);
        var database = PathConstants.RunLogDatabase(root);
        var queue = new BundleQueueStore(database);
        var old = DateTimeOffset.UtcNow.AddMinutes(-5);
        const string runId = "abcdefgh-private-run-identity";
        foreach (var id in new[] { runId, "ijklmnop-second-private-run" })
        {
            store.CreateRun(
                new RunLogCreateRequest
                {
                    RunId = id,
                    StartedAtUtc = old,
                    Hero = "Vanessa",
                    GameMode = "Ranked",
                    PlayerAccountId = "private-account-identity",
                }
            );
            store.CompleteRun(
                id,
                new RunLogCompletion { EndedAtUtc = old.AddMinutes(1), Status = "completed" }
            );
            queue.EnsureEligibleJobs(TimeSpan.Zero);
            queue.EnsureAllocation(id, "invalid-ulid-" + id, 1000, old);
        }
        queue.ResetInterruptedSeals();
        using var logger = new ManualLogSource("bundle-seal-tests");
        var output = new List<string>();
        logger.LogEvent += (_, args) => output.Add(args.Data?.ToString() ?? string.Empty);
        BppLog.Install(logger);
        using var coordinator = new BundleSealCoordinator(new TestServices(paths));
        await coordinator.ReconcileAsync(CancellationToken.None);
        var terminal = output
            .Where(line =>
                line.Contains("event=bundle_pipeline.seal.terminal", StringComparison.Ordinal)
            )
            .ToArray();
        Require(
            terminal.Length == 1,
            "The existing category storm policy should suppress repeated failures: "
                + string.Join("\n", output)
        );
        var line = terminal[0];
        Require(
            line.Contains(
                "exception_type=BazaarPlusPlus.ModApi.Bundle.BundleV5Exception",
                StringComparison.Ordinal
            ),
            "Build failure log must include the actual exception type."
        );
        Require(
            line.Contains("exception_message=", StringComparison.Ordinal)
                && line.Contains("bundle_id", StringComparison.Ordinal),
            "Build failure log must include the exception message."
        );
        Require(
            line.Contains("category=bundle_build_failed", StringComparison.Ordinal),
            "Public category stays bounded."
        );
        Require(
            !line.Contains(runId, StringComparison.Ordinal)
                && !line.Contains("private-account-identity", StringComparison.Ordinal),
            "Structured fields must not expose full run or player identifiers."
        );
        Require(
            queue.ReadJob(runId)?.State == BundleSealJobState.TerminalFailure,
            "Logging must preserve the terminal outcome."
        );
        using var connection = new SqliteConnection($"Data Source={database}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT last_error_detail FROM bundle_seal_jobs WHERE run_id=$runId;";
        command.Parameters.AddWithValue("$runId", runId);
        Require(
            (command.ExecuteScalar() as string)?.Contains("bundle_id", StringComparison.Ordinal)
                == true,
            "The diagnostic must still be persisted in SQLite."
        );
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

#nullable enable
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;

// The Android Host's Compatibility Mode records History-Only Runs: a History-Only store writes
// upload_policy 'history_only', resumes no Run and reopens no row, and demoting turns only the
// active 'bundle' Runs History-Only. A default store still writes 'bundle'.
internal static class RunLogUploadPolicyTests
{
    internal static void Run()
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "bpp-storage-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(tempRoot);

        try
        {
            var paths = new TempDirPathProvider(tempRoot);
            var bundle = new RunLogStore(paths);
            bundle.CreateRun(Request("normal-active"));
            bundle.CreateRun(Request("normal-done"));
            bundle.CompleteRun(
                "normal-done",
                new RunLogCompletion
                {
                    EndedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(1767229200000),
                }
            );
            Expect(Policy(tempRoot, "normal-active") == "bundle", "A default store writes bundle.");

            var demoted = bundle.DemoteActiveRunsToHistoryOnly();
            Expect(demoted == 1, $"Only the active bundle Run is demoted; demoted={demoted}.");
            Expect(
                Policy(tempRoot, "normal-active") == "history_only",
                "The active Run becomes History-Only."
            );
            Expect(Policy(tempRoot, "normal-done") == "bundle", "A finished Run keeps its policy.");
            Expect(bundle.DemoteActiveRunsToHistoryOnly() == 0, "Demoting again changes nothing.");
            Expect(bundle.TryResumeActiveRun() == null, "A demoted Run is not resumed.");

            var historyOnly = new RunLogStore(paths, uploadPolicy: RunUploadPolicy.HistoryOnly);
            var created = historyOnly.CreateRun(Request("compat"));
            Expect(created.RunId == "compat", $"A new id is kept; got {created.RunId}.");
            Expect(
                Policy(tempRoot, "compat") == "history_only",
                "A History-Only store writes history_only."
            );
            Expect(
                historyOnly.TryResumeActiveRun() == null,
                "A History-Only store resumes no Run, not even its own."
            );
            var again = historyOnly.CreateRun(Request("compat"));
            Expect(
                again.RunId != "compat"
                    && RunLogRunIdentity.MatchesServerRunId(again.RunId, "compat"),
                $"A History-Only store reopens no row; got {again.RunId}."
            );

            // A bundle Run a normal store could resume is not reopened as History-Only either.
            bundle.CreateRun(Request("still-bundle"));
            var beside = historyOnly.CreateRun(Request("still-bundle"));
            Expect(beside.RunId != "still-bundle", "The bundle row is left alone.");
            Expect(
                Policy(tempRoot, "still-bundle") == "bundle",
                "The bundle row keeps its policy."
            );
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static RunLogCreateRequest Request(string runId) =>
        new()
        {
            RunId = runId,
            StartedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(1767225600000),
            Hero = "Vanessa",
            GameMode = "Ranked",
        };

    private static string? Policy(string root, string runId)
    {
        using var connection = new SqliteConnection(
            $"Data Source={PathConstants.RunLogDatabase(root)}"
        );
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT upload_policy FROM runs WHERE run_id = $runId;";
        command.Parameters.AddWithValue("$runId", runId);
        return command.ExecuteScalar() as string;
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

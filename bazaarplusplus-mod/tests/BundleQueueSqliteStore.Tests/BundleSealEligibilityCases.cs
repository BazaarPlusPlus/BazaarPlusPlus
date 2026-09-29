#nullable enable
using System.Text.Json;
using BazaarPlusPlus.TestSupport;
using Microsoft.Data.Sqlite;

// Reads the shared seal-eligibility fixture and seeds one case into a mod database.
// HistoryPanelRepository.Tests Compile-Includes this file to evaluate replay maintenance.
internal sealed record BundleSealEligibilityCase(
    string Name,
    JsonElement Run,
    IReadOnlyList<string> OutboxStatuses,
    string? SealJobState,
    string? SealJobLastErrorCode,
    bool EligibleForNewJob,
    bool ProtectedFromCleanup
)
{
    internal const string FixtureFileName = "bundle-seal-eligibility.json";

    internal static IReadOnlyList<BundleSealEligibilityCase> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", FixtureFileName);
        if (!File.Exists(path))
            throw new InvalidOperationException($"Seal eligibility fixture missing at '{path}'.");

        using var document = JsonDocument.Parse(TestInputs.Scratch(path));
        var root = document.RootElement;
        if (root.GetProperty("formatVersion").GetInt32() != 1)
            throw new InvalidOperationException("Unsupported seal eligibility fixture format.");

        var cases = new List<BundleSealEligibilityCase>();
        foreach (var item in root.GetProperty("cases").EnumerateArray())
        {
            var job = item.GetProperty("sealJob");
            var expected = item.GetProperty("expected");
            cases.Add(
                new BundleSealEligibilityCase(
                    item.GetProperty("name").GetString()!,
                    item.GetProperty("run").Clone(),
                    item.GetProperty("outbox")
                        .EnumerateArray()
                        .Select(outbox => outbox.GetProperty("status").GetString()!)
                        .ToArray(),
                    job.ValueKind == JsonValueKind.Null
                        ? null
                        : job.GetProperty("state").GetString(),
                    job.ValueKind == JsonValueKind.Null
                        ? null
                        : job.GetProperty("lastErrorCode").GetString(),
                    expected.GetProperty("eligibleForNewJob").GetBoolean(),
                    expected.GetProperty("protectedFromCleanup").GetBoolean()
                )
            );
        }
        if (cases.Count == 0)
            throw new InvalidOperationException("Seal eligibility fixture has no cases.");
        return cases;
    }

    // Uses the case name as run id, so every case can share one database.
    internal void Seed(SqliteConnection connection)
    {
        Execute(
            connection,
            """
            INSERT INTO runs (
                run_id, started_at_utc, last_seen_at_utc, ended_at_utc, status, completed,
                hero, game_mode, build_channel
            ) VALUES (
                $runId, '2026-01-01T00:00:00Z', '2026-01-01T00:10:00Z', '2026-01-01T00:10:00Z',
                $status, $completed, 'Vanessa', $gameMode, $buildChannel
            );
            """,
            ("$runId", Name),
            ("$status", Run.GetProperty("status").GetString()),
            ("$completed", Run.GetProperty("completed").GetInt32()),
            ("$gameMode", Run.GetProperty("gameMode").GetString()),
            ("$buildChannel", Run.GetProperty("buildChannel").GetString())
        );
        for (var index = 0; index < OutboxStatuses.Count; index++)
        {
            Execute(
                connection,
                """
                INSERT INTO bundle_outbox (
                    bundle_id, run_id, file_name, content_sha256_hex, content_digest,
                    total_bytes, has_screenshot, sealed_at_utc, status
                ) VALUES (
                    $bundleId, $runId, $fileName, 'sha', 'digest',
                    1, 0, '2026-01-01T00:12:00Z', $status
                );
                """,
                ("$bundleId", $"{Name}-bundle-{index}"),
                ("$runId", Name),
                ("$fileName", $"{Name}-{index}.bundle"),
                ("$status", OutboxStatuses[index])
            );
        }
        if (SealJobState != null)
        {
            Execute(
                connection,
                """
                INSERT INTO bundle_seal_jobs (
                    run_id, state, screenshot_requested, screenshot_state,
                    input_deadline_at_utc, last_error_code
                ) VALUES (
                    $runId, $state, 0, 'not_requested', '2026-01-01T00:12:00Z', $lastErrorCode
                );
                """,
                ("$runId", Name),
                ("$state", SealJobState),
                ("$lastErrorCode", SealJobLastErrorCode)
            );
        }
    }

    private static void Execute(
        SqliteConnection connection,
        string sql,
        params (string Name, object? Value)[] parameters
    )
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }
}

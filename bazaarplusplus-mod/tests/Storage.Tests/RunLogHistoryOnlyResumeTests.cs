#nullable enable
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;

internal static class RunLogHistoryOnlyResumeTests
{
    // A normal store never writes history_only, and the V5 import closes unfinished Runs. If an
    // imported Run is still active anyway, resume skips it and a game Run with the same id
    // gets a collision id instead of writing into the imported row.
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
            var store = new RunLogStore(new TempDirPathProvider(tempRoot));
            if (store.TryResumeActiveRun() != null)
                throw new InvalidOperationException("A fresh database has no Run to resume.");

            using (
                var connection = new SqliteConnection(
                    $"Data Source={PathConstants.RunLogDatabase(tempRoot)}"
                )
            )
            {
                connection.Open();
                InsertActiveRun(connection, "imported", 1767226200000, "history_only");
            }

            if (store.TryResumeActiveRun() != null)
                throw new InvalidOperationException(
                    "An active History-Only Run must not be resumed."
                );

            using (
                var connection = new SqliteConnection(
                    $"Data Source={PathConstants.RunLogDatabase(tempRoot)}"
                )
            )
            {
                connection.Open();
                InsertActiveRun(connection, "captured", 1767225600000, "bundle");
            }

            var resumed = store.TryResumeActiveRun();
            if (resumed?.RunId != "captured")
                throw new InvalidOperationException(
                    $"Resume must skip the newer History-Only Run; resumed={resumed?.RunId ?? "null"}."
                );

            var created = store.CreateRun(
                new RunLogCreateRequest
                {
                    RunId = "imported",
                    StartedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(1767229800000),
                    Hero = "Test",
                    GameMode = "Ranked",
                }
            );
            if (
                created.RunId == "imported"
                || !RunLogRunIdentity.MatchesServerRunId(created.RunId, "imported")
            )
                throw new InvalidOperationException(
                    $"A Run id held by a History-Only Run must get a collision id; got {created.RunId}."
                );

            using (
                var connection = new SqliteConnection(
                    $"Data Source={PathConstants.RunLogDatabase(tempRoot)}"
                )
            )
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "SELECT hero || '|' || upload_policy FROM runs WHERE run_id = 'imported';";
                var imported = Convert.ToString(command.ExecuteScalar());
                if (imported != "Vanessa|history_only")
                    throw new InvalidOperationException(
                        $"CreateRun must leave the History-Only row untouched; got {imported}."
                    );
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static void InsertActiveRun(
        SqliteConnection connection,
        string runId,
        long lastSeenAtMs,
        string uploadPolicy
    )
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO runs (
                run_id, started_at_ms, last_seen_at_ms, status,
                hero, game_mode, build_channel, upload_policy
            ) VALUES (
                $runId, $lastSeenAtMs, $lastSeenAtMs, 'active',
                'Vanessa', 'Ranked', 'Online', $uploadPolicy
            );
            """;
        command.Parameters.AddWithValue("$runId", runId);
        command.Parameters.AddWithValue("$lastSeenAtMs", lastSeenAtMs);
        command.Parameters.AddWithValue("$uploadPolicy", uploadPolicy);
        command.ExecuteNonQuery();
    }
}

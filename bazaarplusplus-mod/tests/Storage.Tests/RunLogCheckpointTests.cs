#nullable enable
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;

internal static class RunLogCheckpointTests
{
    // A checkpoint carries progress, never terminal state: a late checkpoint for a run that is
    // already completed or abandoned must not reopen it as the active run.
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
            const string runId = "run_20260301t100000z_test_ranked_003_feedface";
            var startedAt = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);
            store.CreateRun(
                new RunLogCreateRequest
                {
                    RunId = runId,
                    StartedAtUtc = startedAt,
                    Hero = "Test",
                    GameMode = "Ranked",
                    Day = 1,
                    Hour = 1,
                }
            );
            store.CompleteRun(
                runId,
                new RunLogCompletion { EndedAtUtc = startedAt.AddMinutes(10), FinalDay = 1 }
            );

            store.SaveCheckpoint(
                runId,
                new RunLogCheckpoint
                {
                    LastSeq = 1,
                    LastSeenAtUtc = startedAt.AddMinutes(11),
                    Day = 1,
                    Hour = 2,
                }
            );

            using (
                var connection = new SqliteConnection(
                    $"Data Source={PathConstants.RunLogDatabase(tempRoot)}"
                )
            )
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT completed FROM runs WHERE run_id = $runId;";
                command.Parameters.AddWithValue("$runId", runId);
                var completed = Convert.ToInt64(command.ExecuteScalar());
                if (completed != 1)
                    throw new InvalidOperationException(
                        $"A checkpoint after completion must keep the run completed; completed={completed}."
                    );
            }

            if (store.TryResumeActiveRun() != null)
                throw new InvalidOperationException(
                    "A checkpoint after completion must not resume the run."
                );
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }
}

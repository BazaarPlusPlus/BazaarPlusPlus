#nullable enable
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;

internal static class RunLogRatingDeltaTests
{
    // History shows final_player_rating_delta as the Run's rating change, so a Run whose
    // starting rating was never captured stores NULL (unknown) rather than 0 (no change).
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
            var known = CompleteRun(store, "known", startingRating: 1400, finalRating: 1436);
            var unknown = CompleteRun(store, "unknown", startingRating: null, finalRating: 1436);

            using var connection = new SqliteConnection(
                $"Data Source={PathConstants.RunLogDatabase(tempRoot)}"
            );
            connection.Open();
            var knownDelta = ReadDelta(connection, known);
            if (knownDelta != 36)
                throw new InvalidOperationException(
                    $"A known starting rating must store the difference; got {knownDelta?.ToString() ?? "NULL"}."
                );
            var unknownDelta = ReadDelta(connection, unknown);
            if (unknownDelta != null)
                throw new InvalidOperationException(
                    $"A missing starting rating must store NULL, not {unknownDelta}."
                );
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static string CompleteRun(
        RunLogStore store,
        string runId,
        int? startingRating,
        int finalRating
    )
    {
        var session = store.CreateRun(
            new RunLogCreateRequest
            {
                RunId = runId,
                StartedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(1767225600000),
                Hero = "Vanessa",
                GameMode = "Ranked",
                PlayerRank = "Legendary",
                PlayerRating = startingRating,
            }
        );
        store.CompleteRun(
            session.RunId,
            new RunLogCompletion
            {
                EndedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(1767229200000),
                FinalPlayerRank = "Legendary",
                FinalPlayerRating = finalRating,
            }
        );
        return session.RunId;
    }

    private static long? ReadDelta(SqliteConnection connection, string runId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT final_player_rating_delta FROM runs WHERE run_id = $runId;";
        command.Parameters.AddWithValue("$runId", runId);
        var value = command.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToInt64(value);
    }
}

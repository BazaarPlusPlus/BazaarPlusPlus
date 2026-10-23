#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;

// The History Run row both hosts list: HistoryRunColumns.Select read through
// HistoryPanelRowMapper.ReadRun. The desktop pages it with HistoryPageQuery; the Android reader
// selects the same columns, so these rules hold on both.
internal static class HistoryRunRowTests
{
    internal static void Run(string root)
    {
        var path = Path.Combine(root, "history-run-rows.sqlite3");
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        RunLogSchema.EnsureInitialized(connection);
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO runs (run_id, started_at_ms, last_seen_at_ms, status, hero, game_mode,
                                  day, hour, player_rank, player_rating, ended_at_ms, final_day,
                                  final_hour, max_health, prestige, level, income, gold,
                                  victories, losses, final_player_rank, final_player_rating,
                                  final_player_rating_delta, upload_policy)
                VALUES ('finished', 1000, 2000, 'completed', 'Vanessa', 'ranked', 5, 4, 'Gold',
                        900, 3000, 9, 6, 120, 8, 7, 11, 13, 10, 3, 'Diamond', 1000, 16, 'bundle'),
                       ('unfinished', 4000, 5000, 'active', 'Mak', 'unranked', 3, 2, 'Silver',
                        700, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL,
                        NULL, NULL, 'bundle'),
                       ('imported', 7000, 7000, 'active', 'Dooley', 'ranked', NULL, NULL, NULL,
                        NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL,
                        NULL, NULL, 'history_only');
                """;
            insert.ExecuteNonQuery();
        }

        var runs = new Dictionary<string, HistoryRunRecord>();
        using (var select = connection.CreateCommand())
        {
            select.CommandText = $"SELECT {HistoryRunColumns.Select} FROM runs;";
            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                var run = HistoryPanelRowMapper.ReadRun(reader);
                runs[run.RunId] = run;
            }
        }

        var finished = runs["finished"];
        Check(
            finished.FinalDay == 9
                && finished.FinalHour == 6
                && finished.MaxHealth == 120
                && finished.Prestige == 8
                && finished.Level == 7
                && finished.Income == 11
                && finished.Gold == 13
                && finished.PlayerRank == "Diamond"
                && finished.PlayerRating == 1000
                && finished.PlayerRatingDelta == 16
                && finished.Victories == 10
                && finished.Losses == 3
                && finished.EndedAtUtc == DateTimeOffset.FromUnixTimeMilliseconds(3000)
                && finished.LastSeenAtUtc == finished.EndedAtUtc,
            "A finished Run reads its final values and ends where it was last seen."
        );
        var unfinished = runs["unfinished"];
        Check(
            unfinished.FinalDay == 3
                && unfinished.FinalHour == 2
                && unfinished.PlayerRank == "Silver"
                && unfinished.PlayerRating == 700
                && unfinished.EndedAtUtc == null
                && unfinished.LastSeenAtUtc == DateTimeOffset.FromUnixTimeMilliseconds(5000)
                && unfinished.RawStatus == "active",
            "An unfinished Run falls back to the values it was last seen with and stays active."
        );
        Check(
            runs["imported"].RawStatus == "abandoned",
            "An active Run the mod could not resume reads as abandoned."
        );
        connection.Close();
        SqliteConnection.ClearAllPools();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

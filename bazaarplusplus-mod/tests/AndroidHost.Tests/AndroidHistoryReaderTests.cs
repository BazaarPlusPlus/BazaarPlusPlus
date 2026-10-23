using BazaarPlusPlus.Android.History;
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AndroidHost.Tests;

public sealed class AndroidHistoryReaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "bpp-android-history-" + Guid.NewGuid().ToString("N")
    );

    public AndroidHistoryReaderTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Runs_page_newest_first_with_the_finished_values_and_the_total()
    {
        var database = CreateDatabase(
            """
            INSERT INTO runs (run_id, started_at_ms, last_seen_at_ms, status, hero, game_mode, day,
                              player_rank, player_rating, ended_at_ms, final_day, victories, losses,
                              final_player_rank, final_player_rating, final_player_rating_delta)
            VALUES ('old', 1000, 2000, 'completed', 'Vanessa', 'ranked', 5, 'Gold', 900, 3000, 9,
                    10, 3, 'Diamond', 1000, 16),
                   ('mid', 4000, 5000, 'active', 'Mak', 'unranked', 3, NULL, NULL, NULL, NULL,
                    NULL, NULL, NULL, NULL, NULL),
                   ('new', 6000, 7000, 'abandoned', 'Dooley', 'unranked', 2, NULL, NULL, NULL,
                    NULL, 1, 1, NULL, NULL, NULL);
            """
        );
        var reader = new AndroidHistoryReader(database);

        var first = reader.ReadRuns(offset: 0, limit: 2);
        var second = reader.ReadRuns(offset: 2, limit: 2);

        Assert.Equal(3, first.Total);
        Assert.Equal(new[] { "new", "mid" }, first.Runs.Select(run => run.RunId));
        Assert.Equal(new[] { "old" }, second.Runs.Select(run => run.RunId));
        var finished = second.Runs[0];
        Assert.Equal(9, finished.FinalDay);
        Assert.Equal("Diamond", finished.PlayerRank);
        Assert.Equal(1000, finished.PlayerRating);
        Assert.Equal(16, finished.PlayerRatingDelta);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(3000), finished.EndedAtUtc);
        var unfinished = first.Runs[1];
        Assert.Equal(3, unfinished.FinalDay);
        Assert.Null(unfinished.EndedAtUtc);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(5000), unfinished.LastSeenAtUtc);
    }

    [Fact]
    public void Battles_list_the_runs_local_battles_newest_first()
    {
        var database = CreateDatabase(
            """
            INSERT INTO runs (run_id, started_at_ms, last_seen_at_ms, status, hero, game_mode)
            VALUES ('run', 1000, 2000, 'completed', 'Vanessa', 'ranked'),
                   ('other', 1000, 2000, 'completed', 'Mak', 'ranked');
            INSERT INTO battles (battle_id, source, run_id, recorded_at_ms, day, combat_kind, result,
                                 is_final_battle, opponent_name, opponent_hero, opponent_rank,
                                 opponent_rating, local_payload_state)
            VALUES ('b1', 'LOCAL', 'run', 100, 1, 'PVPCombat', 'loss', 0, 'Ann', 'Mak', 'Bronze',
                    500, 'ready'),
                   ('b2', 'LOCAL', 'run', 200, 2, 'PVPCombat', 'win', 1, 'Bob', 'Jules', NULL,
                    NULL, 'ready'),
                   ('b4', 'LOCAL', 'other', 400, 1, 'PVPCombat', 'win', 0, 'Di', 'Mak', NULL,
                    NULL, 'ready');
            """
        );

        var battles = new AndroidHistoryReader(database).ReadBattles("run");

        Assert.Equal(new[] { "Bob", "Ann" }, battles.Select(battle => battle.OpponentName));
        Assert.True(battles[0].IsFinal);
        Assert.Equal("win", battles[0].Result);
        Assert.Equal(2, battles[0].Day);
        Assert.Equal("Bronze", battles[1].OpponentRank);
        Assert.Equal(500, battles[1].OpponentRating);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(100), battles[1].RecordedAtUtc);
    }

    [Fact]
    public void Runs_order_by_their_history_time_and_an_unresumable_active_run_reads_abandoned()
    {
        var database = CreateDatabase(
            """
            INSERT INTO runs (run_id, started_at_ms, last_seen_at_ms, status, hero, game_mode,
                              upload_policy, ended_at_ms)
            VALUES ('long', 1000, 9000, 'completed', 'Vanessa', 'ranked', 'bundle', 9500),
                   ('current', 5000, 6000, 'active', 'Mak', 'ranked', 'bundle', NULL),
                   ('imported', 7000, 7000, 'active', 'Dooley', 'ranked', 'history_only', NULL);
            """
        );

        var runs = new AndroidHistoryReader(database).ReadRuns(0, 10).Runs;

        Assert.Equal(new[] { "long", "imported", "current" }, runs.Select(run => run.RunId));
        Assert.Equal("abandoned", runs[1].RawStatus);
        Assert.Equal("active", runs[2].RawStatus);
    }

    [Fact]
    public void An_empty_history_reads_as_an_empty_page()
    {
        var page = new AndroidHistoryReader(CreateDatabase("")).ReadRuns(0, 7);

        Assert.Equal(0, page.Total);
        Assert.Empty(page.Runs);
    }

    private string CreateDatabase(string rows)
    {
        var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".db");
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        RunLogSchema.EnsureInitialized(connection);
        if (rows.Length > 0)
        {
            using var command = connection.CreateCommand();
            command.CommandText = rows;
            command.ExecuteNonQuery();
        }
        return path;
    }
}

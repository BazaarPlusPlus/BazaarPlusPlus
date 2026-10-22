using System.Globalization;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Android.History;

/// <summary>One recorded PvP battle of a Run, as the Android History panel lists it.</summary>
internal sealed record AndroidHistoryBattle(
    int? Day,
    string? Result,
    bool IsFinal,
    string? OpponentName,
    string? OpponentHero,
    string? OpponentRank,
    int? OpponentRating,
    DateTimeOffset RecordedAtUtc
);

/// <summary>A page of Runs, newest first, with the total the page sits in.</summary>
internal sealed record AndroidHistoryPage(
    IReadOnlyList<HistoryRunRecord> Runs,
    int Offset,
    int Total
);

/// <summary>
/// Read-only queries over the history database for the Android History panel: local Runs and
/// the battles recorded in each. It opens its own read-only connection per read, so it never
/// competes with the record chain's writer for the connection, only for the file lock.
/// </summary>
internal sealed class AndroidHistoryReader
{
    private readonly string _connectionString;

    public AndroidHistoryReader(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            DefaultTimeout = 2,
        }.ConnectionString;
    }

    public AndroidHistoryPage ReadRuns(int offset, int limit)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        int total;
        using (var count = connection.CreateCommand())
        {
            count.Transaction = transaction;
            count.CommandText = "SELECT count(*) FROM runs";
            total = Convert.ToInt32(count.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        var runs = new List<HistoryRunRecord>();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT run_id, started_at_ms, last_seen_at_ms, ended_at_ms, status, hero, game_mode,
                       day, hour, final_day, final_hour, max_health, prestige, level, income, gold,
                       player_rank, player_rating, final_player_rank, final_player_rating,
                       final_player_rating_delta, victories, losses
                FROM runs
                ORDER BY started_at_ms DESC, run_id DESC
                LIMIT $limit OFFSET $offset
                """;
            command.Parameters.AddWithValue("$limit", limit);
            command.Parameters.AddWithValue("$offset", offset);
            using var reader = command.ExecuteReader();
            while (reader.Read())
                runs.Add(ReadRun(reader));
        }
        transaction.Commit();
        return new AndroidHistoryPage(runs, offset, total);
    }

    public IReadOnlyList<AndroidHistoryBattle> ReadBattles(string runId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT day, result, is_final_battle, opponent_name, opponent_hero, opponent_rank,
                   opponent_rating, recorded_at_ms
            FROM battles
            WHERE run_id = $run AND source = 'LOCAL' AND deleted_at_ms IS NULL
            ORDER BY recorded_at_ms DESC, battle_id DESC
            """;
        command.Parameters.AddWithValue("$run", runId);
        using var reader = command.ExecuteReader();
        var battles = new List<AndroidHistoryBattle>();
        while (reader.Read())
        {
            battles.Add(
                new AndroidHistoryBattle(
                    Int(reader, "day"),
                    Text(reader, "result"),
                    Int(reader, "is_final_battle") == 1,
                    Text(reader, "opponent_name"),
                    Text(reader, "opponent_hero"),
                    Text(reader, "opponent_rank"),
                    Int(reader, "opponent_rating"),
                    DateTimeOffset.FromUnixTimeMilliseconds(
                        reader.GetInt64(reader.GetOrdinal("recorded_at_ms"))
                    )
                )
            );
        }
        return battles;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    // The same projection as the desktop panel: a finished Run's final_* values, else the values
    // it was last seen with.
    private static HistoryRunRecord ReadRun(SqliteDataReader reader)
    {
        var startedAt = DateTimeOffset.FromUnixTimeMilliseconds(
            reader.GetInt64(reader.GetOrdinal("started_at_ms"))
        );
        var endedAt = Time(reader, "ended_at_ms");
        return new HistoryRunRecord(
            reader.GetString(reader.GetOrdinal("run_id")),
            reader.GetString(reader.GetOrdinal("hero")),
            reader.GetString(reader.GetOrdinal("game_mode")),
            startedAt,
            endedAt,
            endedAt ?? Time(reader, "last_seen_at_ms") ?? startedAt,
            Int(reader, "final_day") ?? Int(reader, "day"),
            Int(reader, "final_hour") ?? Int(reader, "hour"),
            Int(reader, "max_health"),
            Int(reader, "prestige"),
            Int(reader, "level"),
            Int(reader, "income"),
            Int(reader, "gold"),
            Text(reader, "final_player_rank") ?? Text(reader, "player_rank"),
            Int(reader, "final_player_rating") ?? Int(reader, "player_rating"),
            Int(reader, "final_player_rating_delta"),
            Int(reader, "victories"),
            Int(reader, "losses"),
            reader.GetString(reader.GetOrdinal("status"))
        );
    }

    private static int? Int(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static string? Text(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset? Time(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal)
            ? null
            : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(ordinal));
    }
}

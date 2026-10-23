using System.Globalization;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.Storage.RunLog;
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
/// competes with the record chain's writer for the connection, only for the file lock. A Run row
/// reads through the desktop panel's own Storage owners: Runs order by
/// <see cref="RunLogSchema.HistoryRunTime"/>, select <see cref="HistoryRunColumns.Select"/> (whose
/// run status reads an active Run the mod could not resume as abandoned), and map through
/// <see cref="HistoryPanelRowMapper.ReadRun"/>.
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
            command.CommandText = $"""
                SELECT {HistoryRunColumns.Select}
                FROM runs
                ORDER BY {RunLogSchema.HistoryRunTime} DESC, run_id DESC
                LIMIT $limit OFFSET $offset
                """;
            command.Parameters.AddWithValue("$limit", limit);
            command.Parameters.AddWithValue("$offset", offset);
            using var reader = command.ExecuteReader();
            while (reader.Read())
                runs.Add(HistoryPanelRowMapper.ReadRun(reader));
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
            WHERE run_id = $run AND source = 'LOCAL'
            ORDER BY recorded_at_ms DESC, battle_id DESC
            """;
        command.Parameters.AddWithValue("$run", runId);
        using var reader = command.ExecuteReader();
        var battles = new List<AndroidHistoryBattle>();
        while (reader.Read())
        {
            battles.Add(
                new AndroidHistoryBattle(
                    HistoryPanelRowMapper.GetNullableInt32(reader, "day"),
                    HistoryPanelRowMapper.GetNullableString(reader, "result"),
                    HistoryPanelRowMapper.GetNullableInt32(reader, "is_final_battle") == 1,
                    HistoryPanelRowMapper.GetNullableString(reader, "opponent_name"),
                    HistoryPanelRowMapper.GetNullableString(reader, "opponent_hero"),
                    HistoryPanelRowMapper.GetNullableString(reader, "opponent_rank"),
                    HistoryPanelRowMapper.GetNullableInt32(reader, "opponent_rating"),
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
}

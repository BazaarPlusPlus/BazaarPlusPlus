#nullable enable
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Game.HistoryPanel.Storage;

public sealed class HistoryPageQuery
{
    private readonly string _table;
    private readonly string _where;
    private readonly string? _index;
    private readonly (string Name, object Value)[] _parameters;

    private HistoryPageQuery(
        string table,
        string id,
        string time,
        string where,
        string? index,
        params (string Name, object Value)[] parameters
    )
    {
        _table = table;
        Id = id;
        Time = time;
        _where = where;
        _index = index;
        _parameters = parameters;
    }

    public string Id { get; }
    public string Time { get; }

    // heroKey uses the canonical lower-case key from the shared hero-alias table.
    public static HistoryPageQuery Runs(string? heroKey) =>
        heroKey == null
            ? new("runs", "run_id", RunLogSchema.HistoryRunTime, "1=1", null)
            : new(
                "runs",
                "run_id",
                RunLogSchema.HistoryRunTime,
                $"{RunLogSchema.HistoryHeroKey} = $hero",
                null,
                ("$hero", heroKey)
            );

    public static HistoryPageQuery LocalBattles(string runId) =>
        new(
            "battles",
            "battle_id",
            "recorded_at_ms",
            "source = 'LOCAL' AND run_id = $runId",
            null,
            ("$runId", runId)
        );

    // Filter by the recorder's result, even when the local player was their opponent.
    public static HistoryPageQuery Ghosts(string account, int? recorderOutcome, bool dayMin10)
    {
        var where =
            "source = 'GHOST' AND deleted_at_ms IS NULL AND local_player_account_id = $account";
        var parameters = new List<(string, object)> { ("$account", account) };
        if (recorderOutcome is { } outcome)
        {
            where += $" AND ({RunLogSchema.HistoryRecorderOutcome}) = $outcome";
            parameters.Add(("$outcome", outcome));
        }
        if (dayMin10)
            where += " AND day >= 10";
        // The broader discovery index makes COUNT inspect deleted rows in the table.
        // These existing partial indexes match this page's predicate and keep counts in-index.
        var index =
            "idx_battles_history_ghost"
            + (dayMin10 ? "_day" : "")
            + (recorderOutcome != null ? "_outcome" : "");
        return new("battles", "battle_id", "recorded_at_ms", where, index, parameters.ToArray());
    }

    public static HistoryPageQuery HiddenGhosts(string account) =>
        new(
            "battles",
            "battle_id",
            "recorded_at_ms",
            "source = 'GHOST' AND local_player_account_id = $account AND deleted_at_ms IS NOT NULL AND ghost_replay_state = 'local_ready'",
            null,
            ("$account", account)
        );

    // Favorites are shared across accounts on this machine.
    public static HistoryPageQuery Favorites() =>
        new(RunLogSchema.FavoriteBattlesTableName, "battle_id", "favorited_at_ms", "1=1", null);

    // ID anchors should still seek the primary key, not scan a history index for an ID.
    public string AnchorSql =>
        $"SELECT {Time}, {Id} FROM {_table} WHERE {_where} AND {Id} = $anchor;";

    public string RowsSql(string columns, bool newer, bool bounded, bool inclusive)
    {
        var direction = newer ? "ASC" : "DESC";
        var boundary = bounded ? $" AND {Seek(newer ? ">" : "<", inclusive)}" : string.Empty;
        return $"SELECT {columns}, {Time} AS history_time {From}{boundary} ORDER BY {Time} {direction}, {Id} {direction} LIMIT $limit;";
    }

    public string ExistsSql(bool newer) =>
        $"SELECT 1 {From} AND {Seek(newer ? ">" : "<", inclusive: false)} LIMIT 1;";

    public string CountSql => $"SELECT COUNT(*) {From};";

    public string CountNewerSql => $"SELECT COUNT(*) {From} AND {Seek(">", inclusive: false)};";

    public void Bind(SqliteCommand command)
    {
        foreach (var (name, value) in _parameters)
            command.Parameters.AddWithValue(name, value);
    }

    private string From =>
        $"FROM {(_index == null ? _table : $"{_table} INDEXED BY {_index}")} WHERE {_where}";

    private string Seek(string op, bool inclusive) =>
        $"{Time} {op}= $time AND ({Time} {op} $time OR {Id} {op}{(inclusive ? "=" : "")} $id)";
}

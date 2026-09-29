#nullable enable
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Game.HistoryPanel.Storage;

// One History list's filter: its WHERE fragment, parameters and index hint, ordered by time then ID.
// The page read, its counts and the query-plan tests all render their SQL from this value.
internal sealed class HistoryPageQuery
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

    internal string Id { get; }
    internal string Time { get; }

    internal static HistoryPageQuery Runs(string? hero) =>
        string.IsNullOrWhiteSpace(hero)
            ? new("runs", "run_id", RunLogSchema.HistoryRunTime, "1=1", null)
            : new(
                "runs",
                "run_id",
                RunLogSchema.HistoryRunTime,
                $"{RunLogSchema.HistoryHeroKey} = $hero",
                null,
                (
                    "$hero",
                    HistoryPanelHeroPresentation.CanonicalFilterId(hero)?.Trim().ToLowerInvariant()
                        ?? ""
                )
            );

    internal static HistoryPageQuery LocalBattles(string runId) =>
        new(
            "battles",
            "battle_id",
            "recorded_at_utc",
            "source = 'LOCAL' AND run_id = $runId",
            null,
            ("$runId", runId)
        );

    internal static HistoryPageQuery Ghosts(string account, GhostBattleFilter filter, bool dayMin10)
    {
        var where =
            "source = 'GHOST' AND deleted_at_utc IS NULL AND local_player_account_id = $account";
        var parameters = new List<(string, object)> { ("$account", account) };
        if (filter != GhostBattleFilter.All)
        {
            where += $" AND ({RunLogSchema.HistoryRecorderOutcome}) = $outcome";
            parameters.Add(("$outcome", filter == GhostBattleFilter.IWon ? -1 : 1));
        }
        if (dayMin10)
            where += " AND day >= 10";
        // The broader discovery index makes COUNT inspect deleted rows in the table.
        // These existing partial indexes match this page's predicate and keep counts in-index.
        var index =
            "idx_battles_history_ghost"
            + (dayMin10 ? "_day" : "")
            + (filter != GhostBattleFilter.All ? "_outcome" : "");
        return new("battles", "battle_id", "recorded_at_utc", where, index, parameters.ToArray());
    }

    internal static HistoryPageQuery HiddenGhosts(string account) =>
        new(
            "battles",
            "battle_id",
            "recorded_at_utc",
            "source = 'GHOST' AND local_player_account_id = $account AND deleted_at_utc IS NOT NULL AND ghost_replay_state = 'local_ready'",
            null,
            ("$account", account)
        );

    // ID anchors should still seek the primary key, not scan a history index for an ID.
    internal string AnchorSql =>
        $"SELECT {Time}, {Id} FROM {_table} WHERE {_where} AND {Id} = $anchor;";

    internal string RowsSql(string columns, bool newer, bool bounded, bool inclusive)
    {
        var direction = newer ? "ASC" : "DESC";
        var boundary = bounded ? $" AND {Seek(newer ? ">" : "<", inclusive)}" : string.Empty;
        return $"SELECT {columns}, {Time} AS history_time {From}{boundary} ORDER BY {Time} {direction}, {Id} {direction} LIMIT $limit;";
    }

    internal string ExistsSql(bool newer) =>
        $"SELECT 1 {From} AND {Seek(newer ? ">" : "<", inclusive: false)} LIMIT 1;";

    internal string CountSql => $"SELECT COUNT(*) {From};";

    internal string CountNewerSql => $"SELECT COUNT(*) {From} AND {Seek(">", inclusive: false)};";

    internal void Bind(SqliteCommand command)
    {
        foreach (var (name, value) in _parameters)
            command.Parameters.AddWithValue(name, value);
    }

    private string From =>
        $"FROM {(_index == null ? _table : $"{_table} INDEXED BY {_index}")} WHERE {_where}";

    private string Seek(string op, bool inclusive) =>
        $"{Time} {op}= $time AND ({Time} {op} $time OR {Id} {op}{(inclusive ? "=" : "")} $id)";
}

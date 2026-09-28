#nullable enable
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Game.HistoryPanel.Storage;

internal sealed partial class HistoryPanelRepository
{
    private HistoryPage<T> ReadPage<T>(
        string table,
        string id,
        string time,
        string filter,
        string columns,
        HistoryPageRequest request,
        Func<SqliteDataReader, T> map,
        bool includeCounts,
        string? index,
        params (string Name, object Value)[] parameters
    )
    {
        if (!DatabaseExists)
            return HistoryPage<T>.Empty;
        using var connection = OpenConnection(ensureSchema: true);
        using var transaction = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 2;
        foreach (var parameter in parameters)
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        var indexedTable = index == null ? table : $"{table} INDEXED BY {index}";
        var filteredFrom = $"FROM {indexedTable} WHERE {filter}";
        var cursor = request.Cursor;
        var inclusive = request.Inclusive;
        if (request.AnchorId != null)
        {
            // ID anchors should still seek the primary key, not scan a history index for an ID.
            command.CommandText =
                $"SELECT {time}, {id} FROM {table} WHERE {filter} AND {id} = $anchor;";
            command.Parameters.AddWithValue("$anchor", request.AnchorId);
            using var anchor = command.ExecuteReader();
            cursor = anchor.Read()
                ? new HistoryCursor(anchor.GetString(0), anchor.GetString(1))
                : null;
            inclusive = true;
        }
        var limit = Math.Clamp(request.Limit, 1, 40);
        var direction = request.Newer ? "ASC" : "DESC";
        var comparison = request.Newer ? ">" : "<";
        var boundary = cursor.HasValue
            ? $" AND {time} {comparison}= $time AND ({time} {comparison} $time OR {id} {comparison}{(inclusive ? "=" : "")} $id)"
            : string.Empty;
        command.Parameters.AddWithValue("$time", cursor?.Time ?? "");
        command.Parameters.AddWithValue("$id", cursor?.Id ?? "");
        command.Parameters.AddWithValue("$limit", limit);
        command.CommandText =
            $"SELECT {columns}, {time} AS history_time {filteredFrom}{boundary} ORDER BY {time} {direction}, {id} {direction} LIMIT $limit;";
        var rows = new List<T>();
        var keys = new List<HistoryCursor>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                rows.Add(map(reader));
                keys.Add(
                    new(
                        reader.GetString(reader.GetOrdinal("history_time")),
                        reader.GetString(reader.GetOrdinal(id))
                    )
                );
            }
        }
        if (request.Newer)
        {
            rows.Reverse();
            keys.Reverse();
        }
        // Battle detail and background recovery need cursor navigation, not archive totals.
        if (!includeCounts)
        {
            if (rows.Count == 0)
                return HistoryPage<T>.Empty;
            bool Exists(HistoryCursor key, string op)
            {
                command.Parameters["$time"].Value = key.Time;
                command.Parameters["$id"].Value = key.Id;
                command.CommandText =
                    $"SELECT 1 {filteredFrom} AND {time} {op}= $time AND ({time} {op} $time OR {id} {op} $id) LIMIT 1;";
                return command.ExecuteScalar() != null;
            }
            return new(
                rows,
                keys[0],
                keys[keys.Count - 1],
                Exists(keys[0], ">"),
                Exists(keys[keys.Count - 1], "<")
            );
        }
        command.CommandText = $"SELECT COUNT(*) {filteredFrom};";
        var total = (long)command.ExecuteScalar()!;
        if (rows.Count == 0)
            return HistoryPage<T>.Empty with { TotalCount = total };
        command.Parameters["$time"].Value = keys[0].Time;
        command.Parameters["$id"].Value = keys[0].Id;
        command.CommandText =
            $"SELECT COUNT(*) {filteredFrom} AND {time} >= $time AND ({time} > $time OR {id} > $id);";
        var newer = (long)command.ExecuteScalar()!;
        return new HistoryPage<T>(
            rows,
            keys[0],
            keys[keys.Count - 1],
            newer > 0,
            newer + rows.Count < total
        )
        {
            TotalCount = total,
            FirstPosition = newer + 1,
        };
    }
}

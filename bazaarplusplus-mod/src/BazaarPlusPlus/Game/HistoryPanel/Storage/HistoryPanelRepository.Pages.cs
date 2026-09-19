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
        var cursor = request.Cursor;
        var inclusive = request.Inclusive;
        if (request.AnchorId != null)
        {
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
        command.Parameters.AddWithValue("$limit", limit + 1);
        command.CommandText =
            $"SELECT {columns}, {time} AS history_time FROM {table} WHERE {filter}{boundary} ORDER BY {time} {direction}, {id} {direction} LIMIT $limit;";
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
        if (rows.Count > limit)
        {
            rows.RemoveAt(limit);
            keys.RemoveAt(limit);
        }
        if (request.Newer)
        {
            rows.Reverse();
            keys.Reverse();
        }
        if (rows.Count == 0)
            return HistoryPage<T>.Empty;
        bool Exists(HistoryCursor key, string op)
        {
            command.Parameters["$time"].Value = key.Time;
            command.Parameters["$id"].Value = key.Id;
            command.CommandText =
                $"SELECT 1 FROM {table} WHERE {filter} AND {time} {op}= $time AND ({time} {op} $time OR {id} {op} $id) LIMIT 1;";
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
}

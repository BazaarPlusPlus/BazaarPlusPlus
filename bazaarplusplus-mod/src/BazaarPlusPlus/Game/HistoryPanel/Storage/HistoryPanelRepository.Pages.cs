#nullable enable
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Game.HistoryPanel.Storage;

internal sealed partial class HistoryPanelRepository
{
    // Battle detail and background recovery need cursor navigation, not archive totals.
    private HistoryCursorPage<T> ReadCursorPage<T>(
        HistoryPageQuery query,
        string columns,
        HistoryPageRequest request,
        Func<SqliteDataReader, T> map
    )
    {
        if (!DatabaseExists)
            return HistoryCursorPage<T>.Empty;
        return InPageRead(
            query,
            command =>
            {
                var (rows, keys) = ReadRows(command, query, columns, request, map);
                if (rows.Count == 0)
                    return HistoryCursorPage<T>.Empty;
                bool Exists(HistoryCursor key, bool newer)
                {
                    SeekFrom(command, key);
                    command.CommandText = query.ExistsSql(newer);
                    return command.ExecuteScalar() != null;
                }
                return new HistoryCursorPage<T>(
                    rows,
                    keys[0],
                    keys[^1],
                    Exists(keys[0], newer: true),
                    Exists(keys[^1], newer: false)
                );
            }
        );
    }

    // The rows and both counts come from one read transaction, so the counted page's position
    // invariants hold by construction rather than by reconciliation.
    private HistoryCountedPage<T> ReadCountedPage<T>(
        HistoryPageQuery query,
        string columns,
        HistoryPageRequest request,
        Func<SqliteDataReader, T> map
    )
    {
        if (!DatabaseExists)
            return HistoryCountedPage<T>.Empty();
        return InPageRead(
            query,
            command =>
            {
                var (rows, keys) = ReadRows(command, query, columns, request, map);
                command.CommandText = query.CountSql;
                var total = (long)command.ExecuteScalar()!;
                if (rows.Count == 0)
                    return HistoryCountedPage<T>.Empty(total);
                SeekFrom(command, keys[0]);
                command.CommandText = query.CountNewerSql;
                var newer = (long)command.ExecuteScalar()!;
                return new HistoryCountedPage<T>(rows, keys[0], keys[^1], newer + 1, total);
            }
        );
    }

    private TPage InPageRead<TPage>(HistoryPageQuery query, Func<SqliteCommand, TPage> read)
    {
        using var connection = OpenConnection(ensureSchema: true);
        using var transaction = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 2;
        query.Bind(command);
        return read(command);
    }

    private static (List<T> Rows, List<HistoryCursor> Keys) ReadRows<T>(
        SqliteCommand command,
        HistoryPageQuery query,
        string columns,
        HistoryPageRequest request,
        Func<SqliteDataReader, T> map
    )
    {
        var cursor = request.Cursor;
        var inclusive = request.Inclusive;
        if (request.AnchorId != null)
        {
            command.CommandText = query.AnchorSql;
            command.Parameters.AddWithValue("$anchor", request.AnchorId);
            using var anchor = command.ExecuteReader();
            cursor = anchor.Read()
                ? new HistoryCursor(anchor.GetString(0), anchor.GetString(1))
                : null;
            inclusive = true;
        }
        command.Parameters.AddWithValue("$time", cursor?.Time ?? "");
        command.Parameters.AddWithValue("$id", cursor?.Id ?? "");
        command.Parameters.AddWithValue("$limit", Math.Clamp(request.Limit, 1, 40));
        command.CommandText = query.RowsSql(columns, request.Newer, cursor.HasValue, inclusive);
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
                        reader.GetString(reader.GetOrdinal(query.Id))
                    )
                );
            }
        }
        if (request.Newer)
        {
            rows.Reverse();
            keys.Reverse();
        }
        return (rows, keys);
    }

    private static void SeekFrom(SqliteCommand command, HistoryCursor key)
    {
        command.Parameters["$time"].Value = key.Time;
        command.Parameters["$id"].Value = key.Id;
    }
}

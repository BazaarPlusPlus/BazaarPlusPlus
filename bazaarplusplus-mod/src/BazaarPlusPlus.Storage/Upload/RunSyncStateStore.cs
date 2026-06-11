#nullable enable
using System;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.Storage.Sqlite;

namespace BazaarPlusPlus.Storage.Upload;

public sealed class RunSyncStateStore : SqliteStoreBase
{
    public RunSyncStateStore(IPathProvider paths)
        : base(
            paths.RunLogDatabasePath
                ?? throw new InvalidOperationException("RunLogDatabasePath is not set")
        ) { }

    public void MarkRunDirty(string runId)
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            INSERT INTO {RunLogSchema.RunSyncStateTableName} (
                run_id,
                dirty,
                retry_count
            ) VALUES (
                $runId,
                1,
                0
            )
            ON CONFLICT(run_id) DO UPDATE SET
                dirty = 1,
                last_error = NULL;
            """;
        command.Parameters.AddWithValue("$runId", runId);
        command.ExecuteNonQuery();
    }
}

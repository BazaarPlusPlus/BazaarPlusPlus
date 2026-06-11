#nullable enable
using System;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.Storage.Sqlite;

namespace BazaarPlusPlus.Storage.Upload;

public sealed class BattleReplaySyncStateStore : SqliteStoreBase
{
    public BattleReplaySyncStateStore(IPathProvider paths)
        : base(
            paths.RunLogDatabasePath
                ?? throw new InvalidOperationException("RunLogDatabasePath is not set")
        ) { }

    public void MarkReplayDirty(string battleId)
    {
        if (string.IsNullOrWhiteSpace(battleId))
            throw new ArgumentException("Battle id is required.", nameof(battleId));

        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            UPDATE {RunLogSchema.BattlesTableName}
            SET replay_dirty = 1,
                replay_last_error = NULL
            WHERE battle_id = $battleId
              AND source = 'LOCAL';
            """;
        command.Parameters.AddWithValue("$battleId", battleId);
        command.ExecuteNonQuery();
    }
}

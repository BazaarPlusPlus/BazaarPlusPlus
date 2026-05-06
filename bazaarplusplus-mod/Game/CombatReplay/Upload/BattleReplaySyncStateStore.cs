#nullable enable
using System;
using BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Game.CombatReplay.Upload;

internal sealed class BattleReplaySyncStateStore
{
    private readonly string _databasePath;

    public BattleReplaySyncStateStore(string databasePath, string replayRootPath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Database path is required.", nameof(databasePath));
        if (string.IsNullOrWhiteSpace(replayRootPath))
            throw new ArgumentException("Replay root path is required.", nameof(replayRootPath));

        _databasePath = databasePath;
        EnsureSchema();
    }

    public void MarkReplayDirty(string battleId)
    {
        if (string.IsNullOrWhiteSpace(battleId))
            throw new ArgumentException("Battle id is required.", nameof(battleId));

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            UPDATE {RunLogSqliteSchema.BattlesTableName}
            SET replay_dirty = 1,
                replay_last_error = NULL
            WHERE battle_id = $battleId
              AND source = 'LOCAL';
            """;
        command.Parameters.AddWithValue("$battleId", battleId);
        command.ExecuteNonQuery();
    }

    private void EnsureSchema()
    {
        using var connection = OpenConnection();
        RunLogSqliteSchema.EnsureInitialized(connection);
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandTimeout = 2;
            command.CommandText = """
                PRAGMA foreign_keys = ON;
                PRAGMA busy_timeout = 2000;
                """;
            command.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }
}

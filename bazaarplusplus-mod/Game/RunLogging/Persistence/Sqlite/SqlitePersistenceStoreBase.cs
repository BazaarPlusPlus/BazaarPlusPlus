#nullable enable
using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite;

public abstract class SqlitePersistenceStoreBase
{
    private readonly string _databasePath;

    protected SqlitePersistenceStoreBase(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Database path is required.", nameof(databasePath));

        _databasePath = databasePath;

        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using var connection = OpenConnection();
        EnableWriteAheadLogging(connection);
        RunLogSqliteSchema.EnsureInitialized(connection);
    }

    protected SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        try
        {
            connection.Open();
            using var command = CreateCommand(connection);
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

    protected static SqliteCommand CreateCommand(
        SqliteConnection connection,
        SqliteTransaction? transaction = null
    )
    {
        var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.Transaction = transaction;
        return command;
    }

    protected static void EnableWriteAheadLogging(SqliteConnection connection)
    {
        using var command = CreateCommand(connection);
        command.CommandText = "PRAGMA journal_mode = WAL;";
        command.ExecuteNonQuery();
    }

    protected static void AddNullableInt32(SqliteCommand command, string name, int? value)
    {
        command.Parameters.AddWithValue(name, value.HasValue ? value.Value : DBNull.Value);
    }

    protected static void AddNullableInt64(SqliteCommand command, string name, long? value)
    {
        command.Parameters.AddWithValue(name, value.HasValue ? value.Value : DBNull.Value);
    }

    protected static void AddNullableString(SqliteCommand command, string name, string? value)
    {
        command.Parameters.AddWithValue(name, value ?? (object)DBNull.Value);
    }

    protected static int? GetNullableInt32(SqliteDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    protected static long? GetNullableInt64(SqliteDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    protected static string? GetNullableString(SqliteDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
}

#nullable enable
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Storage.Sqlite;

public abstract class SqliteStoreBase
{
    private readonly string _databasePath;
    private readonly string _connectionString;

    protected SqliteStoreBase(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Database path is required.", nameof(databasePath));

        _databasePath = databasePath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            ForeignKeys = true,
        }.ConnectionString;

        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using var connection = OpenConnection();
        EnableWriteAheadLogging(connection);
        RunLogSchema.EnsureInitialized(connection);
    }

    protected SqliteConnection OpenConnection()
    {
        // Foreign-key enforcement rides the connection string so it costs no extra round trip.
        // busy_timeout stays a pragma: Microsoft.Data.Sqlite 8.0's "Default Timeout" only seeds
        // SqliteCommand.CommandTimeout (a managed SQLITE_BUSY retry loop, and CreateCommand
        // already sets it), never sqlite3_busy_timeout's native blocking wait.
        var connection = new SqliteConnection(_connectionString);
        try
        {
            connection.Open();
            using var command = CreateCommand(connection);
            command.CommandText = "PRAGMA busy_timeout = 2000;";
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

#nullable enable
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Storage.RunLog;

/// <summary>
/// Reads a Run's recorded income and gold, which a replay falls back to when its battle manifest
/// lacks them. It opens the database the record chain already initialized, so it runs no schema
/// DDL and sets no journal mode.
/// </summary>
public static class RunEconomyReader
{
    public static (int? Income, int? Gold) TryRead(string databasePath, string runId)
    {
        if (string.IsNullOrWhiteSpace(databasePath) || string.IsNullOrWhiteSpace(runId))
            return default;

        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString()
        );
        connection.Open();
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA busy_timeout = 2000;";
            pragma.ExecuteNonQuery();
        }

        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            SELECT income, gold
            FROM {RunLogSchema.RunsTableName}
            WHERE run_id = $runId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$runId", runId);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return default;

        return (GetNullableInt32(reader, "income"), GetNullableInt32(reader, "gold"));
    }

    private static int? GetNullableInt32(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }
}

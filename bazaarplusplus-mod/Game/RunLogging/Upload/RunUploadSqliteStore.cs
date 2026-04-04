#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal sealed class RunUploadSqliteStore
{
    private readonly string _databasePath;

    public RunUploadSqliteStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Database path is required.", nameof(databasePath));

        _databasePath = databasePath;
        EnsureSchema();
    }

    public void MarkRunDirty(string runId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            INSERT INTO {RunLogSqliteSchema.RunSyncStateTableName} (
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

    public IReadOnlyList<string> GetPendingCompletedRunIds(int limit)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            SELECT s.run_id
            FROM {RunLogSqliteSchema.RunSyncStateTableName} AS s
            INNER JOIN {RunLogSqliteSchema.RunsTableName} AS r
                ON r.run_id = s.run_id
            WHERE s.dirty = 1
              AND r.completed = 1
            ORDER BY COALESCE(s.last_attempt_at_utc, r.started_at_utc) ASC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var runIds = new List<string>();
        while (reader.Read())
            runIds.Add(reader.GetString(0));

        return runIds;
    }

    public bool HasMorePendingCompletedRuns()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            SELECT 1
            FROM {RunLogSqliteSchema.RunSyncStateTableName} AS s
            INNER JOIN {RunLogSqliteSchema.RunsTableName} AS r
                ON r.run_id = s.run_id
            WHERE s.dirty = 1
              AND r.completed = 1
            LIMIT 1;
            """;
        return command.ExecuteScalar() != null;
    }

    public void MarkRunUploadFailed(string runId, DateTimeOffset attemptedAtUtc, string error)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            UPDATE {RunLogSqliteSchema.RunSyncStateTableName}
            SET last_attempt_at_utc = $attemptedAtUtc,
                retry_count = retry_count + 1,
                last_error = $error
            WHERE run_id = $runId;
            """;
        command.Parameters.AddWithValue("$runId", runId);
        command.Parameters.AddWithValue("$attemptedAtUtc", attemptedAtUtc.ToString("o"));
        command.Parameters.AddWithValue("$error", error);
        command.ExecuteNonQuery();
    }

    public void MarkRunUploaded(
        string runId,
        long uploadedSeq,
        string? uploadedStatus,
        DateTimeOffset uploadedAtUtc
    )
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            UPDATE {RunLogSqliteSchema.RunSyncStateTableName}
            SET dirty = 0,
                uploaded_seq = $uploadedSeq,
                uploaded_status = $uploadedStatus,
                last_attempt_at_utc = $uploadedAtUtc,
                last_uploaded_at_utc = $uploadedAtUtc,
                retry_count = 0,
                last_error = NULL
            WHERE run_id = $runId;
            """;
        command.Parameters.AddWithValue("$runId", runId);
        command.Parameters.AddWithValue("$uploadedSeq", uploadedSeq);
        command.Parameters.AddWithValue("$uploadedStatus", uploadedStatus ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$uploadedAtUtc", uploadedAtUtc.ToString("o"));
        command.ExecuteNonQuery();
    }

    public RunSummaryUploadSnapshot? TryBuildRunSummarySnapshot(
        string runId,
        string installId,
        string? clientId = null
    )
    {
        using var connection = OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            SELECT
                run_id,
                started_at_utc,
                hero,
                game_mode,
                player_rank,
                player_rating,
                day,
                hour,
                seed,
                status,
                completed,
                last_seq,
                last_seen_at_utc,
                max_health,
                prestige,
                level,
                income,
                gold,
                ended_at_utc,
                final_day,
                final_hour,
                victories,
                losses,
                final_player_rank,
                final_player_rating,
                final_player_rating_delta,
                reason
            FROM {RunLogSqliteSchema.RunsTableName}
            WHERE run_id = $runId;
            """;
        command.Parameters.AddWithValue("$runId", runId);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        var lastSeqOrdinal = reader.GetOrdinal("last_seq");
        var lastSeq = reader.IsDBNull(lastSeqOrdinal) ? 0L : reader.GetInt64(lastSeqOrdinal);
        var statusOrdinal = reader.GetOrdinal("status");
        var startedAtUtc = GetNullableString(reader, "started_at_utc");
        var endedAtUtc = GetNullableString(reader, "ended_at_utc") ?? string.Empty;
        var finalDay = GetNullableInt32(reader, "final_day") ?? GetNullableInt32(reader, "day");
        var finalWins = GetNullableInt32(reader, "victories");
        var finalLosses = GetNullableInt32(reader, "losses");
        var mmr = GetNullableInt32(reader, "final_player_rating");
        var heroName = GetNullableString(reader, "hero");

        return new RunSummaryUploadSnapshot
        {
            LastSeq = lastSeq,
            UploadedStatus = reader.IsDBNull(statusOrdinal)
                ? null
                : reader.GetString(statusOrdinal),
            Payload = new RunSummaryUploadPayload
            {
                SchemaVersion = RunLogSqliteSchema.UploadPayloadSchemaVersion,
                InstallId = installId,
                ClientId = clientId,
                PluginVersion = BppPluginVersion.Current,
                SubmittedAtUtc = DateTimeOffset.UtcNow,
                RunId = runId,
                Status = reader.IsDBNull(statusOrdinal)
                    ? string.Empty
                    : reader.GetString(statusOrdinal),
                HeroId = null,
                HeroName = heroName,
                StartedAtUtc = startedAtUtc,
                EndedAtUtc = endedAtUtc,
                FinalDay = finalDay,
                FinalWins = finalWins,
                FinalLosses = finalLosses,
                Mmr = mmr,
            },
        };
    }

    private void EnsureSchema()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = RunLogSqliteSchema.BootstrapSql;
        command.ExecuteNonQuery();
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

    private static string? GetNullableString(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static int? GetNullableInt32(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }
}

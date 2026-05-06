#nullable enable
using System;
using BazaarPlusPlus.Game.RunLogging.Models;
using BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BazaarPlusPlus.Game.RunLogging.Persistence;

public sealed class SqliteRunLogStore : SqlitePersistenceStoreBase, IRunLogStore
{
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        ContractResolver = new DefaultContractResolver
        {
            NamingStrategy = new SnakeCaseNamingStrategy(),
        },
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        DateFormatString = "yyyy-MM-dd'T'HH:mm:ss.fffK",
    };

    public SqliteRunLogStore(string databasePath)
        : base(databasePath) { }

    public RunLogSessionState? TryResumeActiveRun()
    {
        using var connection = OpenConnection();
        return TryReadActiveRun(connection);
    }

    public RunLogSessionState CreateRun(RunLogCreateRequest request)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        if (HasTerminalStatus(connection, transaction, request.RunId))
        {
            throw new InvalidOperationException(
                $"Run {request.RunId} already has terminal status and cannot be recreated."
            );
        }

        using var command = CreateCommand(connection, transaction);
        command.CommandText = $"""
            INSERT INTO {RunLogSqliteSchema.RunsTableName} (
                run_id,
                started_at_utc,
                last_seen_at_utc,
                status,
                completed,
                hero,
                game_mode,
                seed,
                player_rank,
                player_rating,
                day,
                hour,
                last_seq
            ) VALUES (
                $runId,
                $startedAtUtc,
                $lastSeenAtUtc,
                $status,
                0,
                $hero,
                $gameMode,
                $seed,
                $playerRank,
                $playerRating,
                $day,
                $hour,
                0
            )
            ON CONFLICT(run_id) DO UPDATE SET
                hero = excluded.hero,
                game_mode = excluded.game_mode,
                seed = COALESCE(excluded.seed, {RunLogSqliteSchema.RunsTableName}.seed),
                player_rank = COALESCE(excluded.player_rank, {RunLogSqliteSchema.RunsTableName}.player_rank),
                player_rating = COALESCE(excluded.player_rating, {RunLogSqliteSchema.RunsTableName}.player_rating),
                day = COALESCE({RunLogSqliteSchema.RunsTableName}.day, excluded.day),
                hour = COALESCE({RunLogSqliteSchema.RunsTableName}.hour, excluded.hour),
                status = excluded.status,
                completed = 0;
            """;
        command.Parameters.AddWithValue("$runId", request.RunId);
        command.Parameters.AddWithValue("$startedAtUtc", request.StartedAtUtc.ToString("o"));
        command.Parameters.AddWithValue("$lastSeenAtUtc", request.StartedAtUtc.ToString("o"));
        command.Parameters.AddWithValue("$status", request.Status);
        command.Parameters.AddWithValue("$hero", request.Hero);
        command.Parameters.AddWithValue("$gameMode", request.GameMode);
        AddNullableInt32(command, "$seed", request.Seed);
        AddNullableString(command, "$playerRank", request.PlayerRank);
        AddNullableInt32(command, "$playerRating", request.PlayerRating);
        AddNullableInt32(command, "$day", request.Day);
        AddNullableInt32(command, "$hour", request.Hour);
        command.ExecuteNonQuery();

        var session =
            TryReadActiveRun(connection, transaction, request.RunId)
            ?? throw new InvalidOperationException(
                $"Run {request.RunId} could not be loaded after create."
            );

        transaction.Commit();
        return session;
    }

    public void AppendEvent(string runId, RunLogEvent entry)
    {
        var payloadJson = JsonConvert.SerializeObject(entry, SerializerSettings);
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var command = CreateCommand(connection, transaction);
        command.CommandText = $"""
            INSERT INTO {RunLogSqliteSchema.RunEventsTableName} (
                run_id,
                seq,
                ts_utc,
                kind,
                payload_json
            ) VALUES (
                $runId,
                $seq,
                $tsUtc,
                $kind,
                $payloadJson
            );
            """;
        command.Parameters.AddWithValue("$runId", runId);
        command.Parameters.AddWithValue("$seq", entry.Seq);
        command.Parameters.AddWithValue("$tsUtc", entry.Ts.ToString("o"));
        command.Parameters.AddWithValue("$kind", entry.Kind);
        command.Parameters.AddWithValue("$payloadJson", payloadJson);
        command.ExecuteNonQuery();

        using var updateRun = CreateCommand(connection, transaction);
        updateRun.CommandText = $"""
            UPDATE {RunLogSqliteSchema.RunsTableName}
            SET last_seq = MAX(last_seq, $seq),
                last_seen_at_utc = MAX(last_seen_at_utc, $tsUtc),
                day = COALESCE($day, day),
                hour = COALESCE($hour, hour)
            WHERE run_id = $runId;
            """;
        updateRun.Parameters.AddWithValue("$runId", runId);
        updateRun.Parameters.AddWithValue("$seq", entry.Seq);
        updateRun.Parameters.AddWithValue("$tsUtc", entry.Ts.ToString("o"));
        AddNullableInt32(updateRun, "$day", entry.Day);
        AddNullableInt32(updateRun, "$hour", entry.Hour);
        updateRun.ExecuteNonQuery();

        transaction.Commit();
    }

    public void SaveCheckpoint(string runId, RunLogCheckpoint checkpoint)
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            UPDATE {RunLogSqliteSchema.RunsTableName}
            SET last_seq = $lastSeq,
                last_seen_at_utc = $lastSeenAtUtc,
                day = $day,
                hour = $hour,
                max_health = $maxHealth,
                prestige = $prestige,
                level = $level,
                income = $income,
                gold = $gold,
                completed = $completed
            WHERE run_id = $runId;
            """;
        command.Parameters.AddWithValue("$runId", runId);
        command.Parameters.AddWithValue("$lastSeq", checkpoint.LastSeq);
        command.Parameters.AddWithValue("$lastSeenAtUtc", checkpoint.LastSeenAtUtc.ToString("o"));
        AddNullableInt32(command, "$day", checkpoint.Day);
        AddNullableInt32(command, "$hour", checkpoint.Hour);
        AddNullableInt32(command, "$maxHealth", checkpoint.MaxHealth);
        AddNullableInt32(command, "$prestige", checkpoint.Prestige);
        AddNullableInt32(command, "$level", checkpoint.Level);
        AddNullableInt32(command, "$income", checkpoint.Income);
        AddNullableInt32(command, "$gold", checkpoint.Gold);
        command.Parameters.AddWithValue("$completed", checkpoint.Completed ? 1 : 0);
        command.ExecuteNonQuery();
    }

    public void CompleteRun(string runId, RunLogCompletion completion)
    {
        WriteTerminalStatus(
            runId,
            completion.Status,
            completion.EndedAtUtc,
            completion.FinalDay,
            completion.FinalHour,
            completion.MaxHealth,
            completion.Prestige,
            completion.Level,
            completion.Income,
            completion.Gold,
            completion.Victories,
            completion.Losses,
            completion.FinalPlayerRank,
            completion.FinalPlayerRating,
            completion.FinalPlayerRatingDelta,
            completion.Reason
        );
    }

    public void MarkRunAbandoned(string runId, RunLogAbandonment abandonment)
    {
        WriteTerminalStatus(
            runId,
            abandonment.Status,
            abandonment.EndedAtUtc,
            abandonment.FinalDay,
            abandonment.FinalHour,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            abandonment.Reason
        );
    }

    private void WriteTerminalStatus(
        string runId,
        string status,
        DateTimeOffset endedAtUtc,
        int? finalDay,
        int? finalHour,
        int? maxHealth,
        int? prestige,
        int? level,
        int? income,
        int? gold,
        int? victories,
        int? losses,
        string? finalPlayerRank,
        int? finalPlayerRating,
        int? finalPlayerRatingDelta,
        string? reason
    )
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var resolvedFinalPlayerRatingDelta =
            finalPlayerRatingDelta
            ?? ResolvePlayerRatingDelta(connection, transaction, runId, finalPlayerRating);

        using var command = CreateCommand(connection, transaction);
        command.CommandText = $"""
            UPDATE {RunLogSqliteSchema.RunsTableName}
            SET status = $status,
                completed = 1,
                ended_at_utc = $endedAtUtc,
                final_day = $finalDay,
                final_hour = $finalHour,
                day = COALESCE($finalDay, day),
                hour = COALESCE($finalHour, hour),
                max_health = COALESCE($maxHealth, max_health),
                prestige = COALESCE($prestige, prestige),
                level = COALESCE($level, level),
                income = COALESCE($income, income),
                gold = COALESCE($gold, gold),
                victories = $victories,
                losses = $losses,
                final_player_rank = $finalPlayerRank,
                final_player_rating = $finalPlayerRating,
                final_player_rating_delta = $finalPlayerRatingDelta,
                reason = $reason
            WHERE run_id = $runId;
            """;
        command.Parameters.AddWithValue("$runId", runId);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$endedAtUtc", endedAtUtc.ToString("o"));
        AddNullableInt32(command, "$finalDay", finalDay);
        AddNullableInt32(command, "$finalHour", finalHour);
        AddNullableInt32(command, "$maxHealth", maxHealth);
        AddNullableInt32(command, "$prestige", prestige);
        AddNullableInt32(command, "$level", level);
        AddNullableInt32(command, "$income", income);
        AddNullableInt32(command, "$gold", gold);
        AddNullableInt32(command, "$victories", victories);
        AddNullableInt32(command, "$losses", losses);
        AddNullableString(command, "$finalPlayerRank", finalPlayerRank);
        AddNullableInt32(command, "$finalPlayerRating", finalPlayerRating);
        AddNullableInt32(command, "$finalPlayerRatingDelta", resolvedFinalPlayerRatingDelta);
        AddNullableString(command, "$reason", reason);
        command.ExecuteNonQuery();

        transaction.Commit();
    }

    private static int? ResolvePlayerRatingDelta(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string runId,
        int? finalPlayerRating
    )
    {
        if (!finalPlayerRating.HasValue)
            return null;

        using var command = CreateCommand(connection, transaction);
        command.CommandText = $"""
            SELECT player_rating
            FROM {RunLogSqliteSchema.RunsTableName}
            WHERE run_id = $runId;
            """;
        command.Parameters.AddWithValue("$runId", runId);

        var initialPlayerRating = command.ExecuteScalar();
        if (initialPlayerRating == null || initialPlayerRating is DBNull)
            return 0;

        return finalPlayerRating.Value - Convert.ToInt32(initialPlayerRating);
    }

    private static bool HasTerminalStatus(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string runId
    )
    {
        using var command = CreateCommand(connection, transaction);
        command.CommandText = $"""
            SELECT completed
            FROM {RunLogSqliteSchema.RunsTableName}
            WHERE run_id = $runId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$runId", runId);
        var value = command.ExecuteScalar();
        return value != null && value is not DBNull && Convert.ToInt32(value) == 1;
    }

    private static RunLogSessionState? TryReadActiveRun(
        SqliteConnection connection,
        SqliteTransaction? transaction = null,
        string? runId = null
    )
    {
        using var command = CreateCommand(connection, transaction);
        command.CommandText =
            runId == null
                ? $"""
                    SELECT *
                    FROM {RunLogSqliteSchema.RunsTableName}
                    WHERE completed = 0
                    ORDER BY last_seen_at_utc DESC
                    LIMIT 1;
                    """
                : $"""
                    SELECT *
                    FROM {RunLogSqliteSchema.RunsTableName}
                    WHERE completed = 0
                      AND run_id = $runId
                    LIMIT 1;
                    """;
        if (runId != null)
            command.Parameters.AddWithValue("$runId", runId);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        return ReadSessionState(reader);
    }

    private static RunLogSessionState? ReadSessionState(SqliteDataReader reader)
    {
        if (GetNullableInt64(reader, "completed") == 1)
            return null;

        var startedAtUtc = DateTimeOffset.Parse(
            reader.GetString(reader.GetOrdinal("started_at_utc"))
        );
        var lastSeenAtUtc = DateTimeOffset.Parse(
            reader.GetString(reader.GetOrdinal("last_seen_at_utc"))
        );

        return new RunLogSessionState
        {
            RunId = reader.GetString(reader.GetOrdinal("run_id")),
            SchemaVersion = RunLogSqliteSchema.CurrentSchemaVersion,
            StartedAtUtc = startedAtUtc,
            LastSeenAtUtc = lastSeenAtUtc,
            LastSeq = GetNullableInt64(reader, "last_seq") ?? 0,
            Day = GetNullableInt32(reader, "day"),
            Hour = GetNullableInt32(reader, "hour"),
            MaxHealth = GetNullableInt32(reader, "max_health"),
            Prestige = GetNullableInt32(reader, "prestige"),
            Level = GetNullableInt32(reader, "level"),
            Income = GetNullableInt32(reader, "income"),
            Gold = GetNullableInt32(reader, "gold"),
            Completed = false,
        };
    }
}

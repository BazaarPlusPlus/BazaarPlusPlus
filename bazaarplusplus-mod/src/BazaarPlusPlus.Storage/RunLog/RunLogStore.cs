#nullable enable
using System.Globalization;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.Sqlite;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Storage.RunLog;

/// <summary>
/// The upload policy of the Runs a <see cref="RunLogStore"/> creates: <see cref="Bundle"/> Runs
/// may be sealed and uploaded; <see cref="HistoryOnly"/> Runs stay in local history (the
/// History-Only Run, root <c>GLOSSARY.md</c>), as the Android Host records them in Compatibility
/// Mode.
/// </summary>
public enum RunUploadPolicy
{
    Bundle,
    HistoryOnly,
}

public sealed class RunLogStore : SqliteStoreBase, IRunLogStore
{
    private const string ActiveStatus = "active";
    private const string BundlePolicy = "bundle";
    private const string HistoryOnlyPolicy = "history_only";

    private static readonly JsonSerializerSettings SerializerSettings =
        SerializerSettingsFactory.CreateSerializerSettings(includeStringEnumConverter: false);

    private readonly RunUploadPolicy _uploadPolicy;

    // uploadPolicy: the policy of the Runs this store creates. A History-Only store resumes no
    // Run and reopens no row: every Run it starts is a new History-Only Run.
    public RunLogStore(
        IPathProvider paths,
        SqliteOpenOptions? options = null,
        RunUploadPolicy uploadPolicy = RunUploadPolicy.Bundle
    )
        : base(PathConstants.RunLogDatabase(paths.RequireDataRoot()), options) =>
        _uploadPolicy = uploadPolicy;

    public RunUploadPolicy UploadPolicy => _uploadPolicy;

    public RunLogSessionState? TryResumeActiveRun()
    {
        if (_uploadPolicy != RunUploadPolicy.Bundle)
            return null;
        using var connection = OpenConnection();
        return TryReadActiveRun(connection);
    }

    /// <summary>
    /// Turns every active <c>bundle</c> Run into a History-Only Run, so nothing recorded so far
    /// or from now on in it is ever sealed; returns how many changed. The Android Host calls it
    /// as a Compatibility Mode launch starts, before the record chain could resume such a Run.
    /// </summary>
    public int DemoteActiveRunsToHistoryOnly()
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            UPDATE {RunLogSchema.RunsTableName}
            SET upload_policy = '{HistoryOnlyPolicy}'
            WHERE status = '{ActiveStatus}' AND upload_policy = '{BundlePolicy}';
            """;
        return command.ExecuteNonQuery();
    }

    public RunLogSessionState CreateRun(RunLogCreateRequest request)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var effectiveRunId = IsClosedToResume(connection, transaction, request.RunId)
            ? RunLogRunIdentity.CreateCollisionId(request.RunId)
            : request.RunId;
        var uploadPolicy =
            _uploadPolicy == RunUploadPolicy.HistoryOnly ? HistoryOnlyPolicy : BundlePolicy;

        using var command = CreateCommand(connection, transaction);
        command.CommandText = $"""
            INSERT INTO {RunLogSchema.RunsTableName} (
                run_id,
                started_at_ms,
                last_seen_at_ms,
                status,
                hero,
                game_mode,
                player_rank,
                player_rating,
                day,
                hour,
                last_seq,
                build_channel,
                player_account_id,
                bundle_screenshot_requested,
                mod_version,
                upload_policy
            ) VALUES (
                $runId,
                $startedAtMs,
                $lastSeenAtMs,
                $status,
                $hero,
                $gameMode,
                $playerRank,
                $playerRating,
                $day,
                $hour,
                0,
                $buildChannel,
                $playerAccountId,
                $bundleScreenshotRequested,
                $modVersion,
                $uploadPolicy
            )
            ON CONFLICT(run_id) DO UPDATE SET
                hero = excluded.hero,
                game_mode = excluded.game_mode,
                player_rank = COALESCE(excluded.player_rank, {RunLogSchema.RunsTableName}.player_rank),
                player_rating = COALESCE(excluded.player_rating, {RunLogSchema.RunsTableName}.player_rating),
                day = COALESCE({RunLogSchema.RunsTableName}.day, excluded.day),
                hour = COALESCE({RunLogSchema.RunsTableName}.hour, excluded.hour),
                player_account_id = COALESCE({RunLogSchema.RunsTableName}.player_account_id, excluded.player_account_id),
                status = excluded.status;
            """;
        command.Parameters.AddWithValue("$runId", effectiveRunId);
        command.Parameters.AddWithValue(
            "$startedAtMs",
            request.StartedAtUtc.ToUnixTimeMilliseconds()
        );
        command.Parameters.AddWithValue(
            "$lastSeenAtMs",
            request.StartedAtUtc.ToUnixTimeMilliseconds()
        );
        command.Parameters.AddWithValue("$status", request.Status);
        command.Parameters.AddWithValue("$hero", request.Hero);
        command.Parameters.AddWithValue("$gameMode", request.GameMode);
        AddNullableString(command, "$playerRank", request.PlayerRank);
        AddNullableInt32(command, "$playerRating", request.PlayerRating);
        AddNullableInt32(command, "$day", request.Day);
        AddNullableInt32(command, "$hour", request.Hour);
        AddNullableString(command, "$buildChannel", request.BuildChannel);
        AddNullableString(command, "$playerAccountId", NormalizeAccountId(request.PlayerAccountId));
        command.Parameters.AddWithValue(
            "$bundleScreenshotRequested",
            request.BundleScreenshotRequested ? 1 : 0
        );
        AddNullableString(command, "$modVersion", request.ModVersion);
        command.Parameters.AddWithValue("$uploadPolicy", uploadPolicy);
        command.ExecuteNonQuery();

        var session =
            ReadCreatedRun(connection, transaction, effectiveRunId, uploadPolicy)
            ?? throw new InvalidOperationException(
                $"Run {effectiveRunId} could not be loaded after create."
            );

        transaction.Commit();
        return session;
    }

    public void SetPlayerAccountIdOnce(string runId, string? playerAccountId)
    {
        var normalizedAccountId = NormalizeAccountId(playerAccountId);
        if (normalizedAccountId == null)
            return;

        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            UPDATE {RunLogSchema.RunsTableName}
            SET player_account_id = $playerAccountId
            WHERE run_id = $runId
              AND player_account_id IS NULL;
            """;
        command.Parameters.AddWithValue("$runId", runId);
        command.Parameters.AddWithValue("$playerAccountId", normalizedAccountId);
        command.ExecuteNonQuery();
    }

    public void AppendEvent(string runId, RunLogEvent entry)
    {
        var payloadJson = JsonConvert.SerializeObject(entry, SerializerSettings);
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var command = CreateCommand(connection, transaction);
        command.CommandText = $"""
            INSERT INTO {RunLogSchema.RunEventsTableName} (
                run_id,
                seq,
                ts_at_ms,
                kind,
                payload_json
            ) VALUES (
                $runId,
                $seq,
                $tsAtMs,
                $kind,
                $payloadJson
            );
            """;
        command.Parameters.AddWithValue("$runId", runId);
        command.Parameters.AddWithValue("$seq", entry.Seq);
        command.Parameters.AddWithValue("$tsAtMs", entry.Ts.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$kind", entry.Kind);
        command.Parameters.AddWithValue("$payloadJson", payloadJson);
        command.ExecuteNonQuery();

        using var updateRun = CreateCommand(connection, transaction);
        updateRun.CommandText = $"""
            UPDATE {RunLogSchema.RunsTableName}
            SET last_seq = MAX(last_seq, $seq),
                last_seen_at_ms = MAX(last_seen_at_ms, $tsAtMs),
                day = COALESCE($day, day),
                hour = COALESCE($hour, hour)
            WHERE run_id = $runId;
            """;
        updateRun.Parameters.AddWithValue("$runId", runId);
        updateRun.Parameters.AddWithValue("$seq", entry.Seq);
        updateRun.Parameters.AddWithValue("$tsAtMs", entry.Ts.ToUnixTimeMilliseconds());
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
            UPDATE {RunLogSchema.RunsTableName}
            SET last_seq = $lastSeq,
                last_seen_at_ms = $lastSeenAtMs,
                day = $day,
                hour = $hour,
                max_health = $maxHealth,
                prestige = $prestige,
                level = $level,
                income = $income,
                gold = $gold
            WHERE run_id = $runId;
            """;
        command.Parameters.AddWithValue("$runId", runId);
        command.Parameters.AddWithValue("$lastSeq", checkpoint.LastSeq);
        command.Parameters.AddWithValue(
            "$lastSeenAtMs",
            checkpoint.LastSeenAtUtc.ToUnixTimeMilliseconds()
        );
        AddNullableInt32(command, "$day", checkpoint.Day);
        AddNullableInt32(command, "$hour", checkpoint.Hour);
        AddNullableInt32(command, "$maxHealth", checkpoint.MaxHealth);
        AddNullableInt32(command, "$prestige", checkpoint.Prestige);
        AddNullableInt32(command, "$level", checkpoint.Level);
        AddNullableInt32(command, "$income", checkpoint.Income);
        AddNullableInt32(command, "$gold", checkpoint.Gold);
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
            UPDATE {RunLogSchema.RunsTableName}
            SET status = $status,
                ended_at_ms = $endedAtMs,
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
        command.Parameters.AddWithValue("$endedAtMs", endedAtUtc.ToUnixTimeMilliseconds());
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
            FROM {RunLogSchema.RunsTableName}
            WHERE run_id = $runId;
            """;
        command.Parameters.AddWithValue("$runId", runId);

        // Without a starting rating the change is unknown, not zero: History renders NULL as "—".
        var initialPlayerRating = command.ExecuteScalar();
        if (initialPlayerRating == null || initialPlayerRating is DBNull)
            return null;

        return finalPlayerRating.Value
            - Convert.ToInt32(initialPlayerRating, CultureInfo.InvariantCulture);
    }

    // A Run id this session cannot continue gets a collision id instead of reopening the row: a
    // terminal Run, or a History-Only Run (imported history, or a Run recorded or demoted in
    // Compatibility Mode). A History-Only store continues no row at all.
    private bool IsClosedToResume(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string runId
    )
    {
        using var command = CreateCommand(connection, transaction);
        command.CommandText =
            _uploadPolicy == RunUploadPolicy.Bundle
                ? $"""
                    SELECT 1
                    FROM {RunLogSchema.RunsTableName}
                    WHERE run_id = $runId
                      AND NOT ({ResumableRunCondition(RunLogSchema.RunsTableName)})
                    LIMIT 1;
                    """
                : $"""
                    SELECT 1
                    FROM {RunLogSchema.RunsTableName}
                    WHERE run_id = $runId
                    LIMIT 1;
                    """;
        command.Parameters.AddWithValue("$runId", runId);
        return command.ExecuteScalar() != null;
    }

    /// <summary>
    /// The SQL condition for a Run the mod may resume or show as the current Run, over the runs
    /// row aliased <paramref name="runAlias"/>: active and not History-Only. The mod writes
    /// <c>history_only</c> only in the Android Host's Compatibility Mode, whose store resumes
    /// nothing; this keeps an unfinished imported Run, or one recorded or demoted in
    /// Compatibility Mode, from being continued as a Run that may be sealed.
    /// </summary>
    public static string ResumableRunCondition(string runAlias) =>
        $"({runAlias}.status = '{ActiveStatus}' AND {runAlias}.upload_policy = '{BundlePolicy}')";

    // The row CreateRun just wrote, whatever its policy.
    private static RunLogSessionState? ReadCreatedRun(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string runId,
        string uploadPolicy
    )
    {
        using var command = CreateCommand(connection, transaction);
        command.CommandText = $"""
            SELECT *
            FROM {RunLogSchema.RunsTableName}
            WHERE run_id = $runId
              AND status = '{ActiveStatus}'
              AND upload_policy = $uploadPolicy
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$runId", runId);
        command.Parameters.AddWithValue("$uploadPolicy", uploadPolicy);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadSessionState(reader) : null;
    }

    private static RunLogSessionState? TryReadActiveRun(SqliteConnection connection)
    {
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            SELECT *
            FROM {RunLogSchema.RunsTableName}
            WHERE {ResumableRunCondition(RunLogSchema.RunsTableName)}
            ORDER BY last_seen_at_ms DESC
            LIMIT 1;
            """;

        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        return ReadSessionState(reader);
    }

    private static RunLogSessionState? ReadSessionState(SqliteDataReader reader)
    {
        if (
            !string.Equals(
                reader.GetString(reader.GetOrdinal("status")),
                ActiveStatus,
                StringComparison.Ordinal
            )
        )
            return null;

        var startedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(
            reader.GetInt64(reader.GetOrdinal("started_at_ms"))
        );
        var lastSeenAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(
            reader.GetInt64(reader.GetOrdinal("last_seen_at_ms"))
        );

        return new RunLogSessionState
        {
            RunId = reader.GetString(reader.GetOrdinal("run_id")),
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
        };
    }

    internal static string? NormalizeAccountId(string? playerAccountId)
    {
        var normalized = playerAccountId?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}

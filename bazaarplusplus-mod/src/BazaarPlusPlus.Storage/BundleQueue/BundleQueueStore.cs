#nullable enable
using System.Globalization;
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Storage.BundleQueue;

public sealed class BundleQueueStore : SqliteStoreBase
{
    public BundleQueueStore(string databasePath)
        : base(databasePath) { }

    public void ResetInterruptedSeals()
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText =
            $"UPDATE {RunLogSchema.BundleSealJobsTableName} SET state = 'waiting' WHERE state = 'sealing';";
        command.ExecuteNonQuery();
    }

    public void EnsureEligibleJobs(TimeSpan inputConvergenceWindow)
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            INSERT OR IGNORE INTO {RunLogSchema.BundleSealJobsTableName} (
                run_id, state, player_account_id, screenshot_requested, screenshot_state,
                input_deadline_at_utc
            )
            SELECT r.run_id, 'waiting', r.player_account_id, r.bundle_screenshot_requested,
                   CASE WHEN r.bundle_screenshot_requested = 1 THEN 'waiting' ELSE 'not_requested' END,
                   datetime(COALESCE(r.ended_at_utc, r.last_seen_at_utc), $deadlineModifier)
            FROM {RunLogSchema.RunsTableName} AS r
            WHERE r.completed = 1
              AND r.status = 'completed'
              AND lower(r.game_mode) = 'ranked'
              AND lower(COALESCE(r.build_channel, 'unknown')) <> 'ptr'
              AND NOT EXISTS (
                  SELECT 1 FROM {RunLogSchema.BundleOutboxTableName} AS o
                  WHERE o.run_id = r.run_id AND o.status IN ('pending', 'uploaded')
              );
            """;
        command.Parameters.AddWithValue(
            "$deadlineModifier",
            $"+{inputConvergenceWindow.TotalMinutes.ToString(CultureInfo.InvariantCulture)} minutes"
        );
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<string> ListWaitingRunIds()
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText =
            $"SELECT run_id FROM {RunLogSchema.BundleSealJobsTableName} WHERE state = 'waiting' ORDER BY input_deadline_at_utc, run_id;";
        using var reader = command.ExecuteReader();
        var ids = new List<string>();
        while (reader.Read())
            ids.Add(reader.GetString(0));
        return ids;
    }

    public BundleSealJobRecord? ReadJob(string runId)
    {
        using var connection = OpenConnection();
        return ReadJob(connection, runId);
    }

    public BundleAllocationRecord EnsureAllocation(
        string runId,
        string proposedBundleId,
        long proposedCreatedAtMs,
        DateTimeOffset now
    )
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            UPDATE {RunLogSchema.BundleSealJobsTableName}
            SET bundle_id = COALESCE(bundle_id, $bundleId),
                created_at_ms = COALESCE(created_at_ms, $createdAtMs),
                state = 'sealing', attempts = attempts + 1,
                last_attempt_at_utc = $now
            WHERE run_id = $runId;
            SELECT bundle_id, created_at_ms
            FROM {RunLogSchema.BundleSealJobsTableName} WHERE run_id = $runId;
            """;
        command.Parameters.AddWithValue("$runId", runId);
        command.Parameters.AddWithValue("$bundleId", proposedBundleId);
        command.Parameters.AddWithValue("$createdAtMs", proposedCreatedAtMs);
        command.Parameters.AddWithValue("$now", now.ToString("o"));
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new InvalidOperationException("Seal job disappeared during allocation.");
        return new BundleAllocationRecord(reader.GetString(0), reader.GetInt64(1));
    }

    public void FreezePlayerAccountId(string runId, string accountId)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        Execute(
            connection,
            transaction,
            $"UPDATE {RunLogSchema.RunsTableName} SET player_account_id = COALESCE(player_account_id, $accountId) WHERE run_id = $runId;",
            ("$runId", runId),
            ("$accountId", accountId)
        );
        Execute(
            connection,
            transaction,
            $"UPDATE {RunLogSchema.BundleSealJobsTableName} SET player_account_id = COALESCE(player_account_id, $accountId) WHERE run_id = $runId;",
            ("$runId", runId),
            ("$accountId", accountId)
        );
        transaction.Commit();
    }

    public void UpdateScreenshotState(string runId, BundleScreenshotState state) =>
        Execute(
            $"UPDATE {RunLogSchema.BundleSealJobsTableName} SET screenshot_state = $state WHERE run_id = $runId;",
            ("$runId", runId),
            ("$state", ToStorage(state))
        );

    public void MarkJobWaiting(string runId, string code, string? detail) =>
        MarkJob(runId, BundleSealJobState.Waiting, code, detail);

    public void MarkJobTerminal(string runId, string code, string? detail) =>
        MarkJob(runId, BundleSealJobState.TerminalFailure, code, detail);

    public bool ContainsOutbox(string bundleId)
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText =
            $"SELECT 1 FROM {RunLogSchema.BundleOutboxTableName} WHERE bundle_id = $bundleId LIMIT 1;";
        command.Parameters.AddWithValue("$bundleId", bundleId);
        return command.ExecuteScalar() != null;
    }

    public bool PublishOutbox(
        BundleAllocationRecord allocation,
        BundleOutboxPublishRecord outbox,
        DateTimeOffset sealedAt
    )
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var job = ReadJob(connection, outbox.RunId, transaction);
        if (job?.BundleId != allocation.BundleId || job.CreatedAtMs != allocation.CreatedAtMs)
            return false;
        InsertOutbox(connection, transaction, outbox, sealedAt);
        DeleteJob(connection, transaction, outbox.RunId);
        transaction.Commit();
        return true;
    }

    public IReadOnlyList<BundleOutboxStateRecord> ListPendingForValidation()
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText =
            $"SELECT bundle_id, run_id, file_name FROM {RunLogSchema.BundleOutboxTableName} WHERE status = 'pending';";
        using var reader = command.ExecuteReader();
        var rows = new List<BundleOutboxStateRecord>();
        while (reader.Read())
            rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return rows;
    }

    public IReadOnlyList<BundleOutboxRecord> ListDue(DateTimeOffset now, int maximumCount)
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            SELECT bundle_id, run_id, file_name, content_sha256_hex, content_digest, total_bytes
            FROM {RunLogSchema.BundleOutboxTableName}
            WHERE status = 'pending'
              AND (next_attempt_at_utc IS NULL OR next_attempt_at_utc <= $now)
            ORDER BY attempts ASC, sealed_at_utc ASC
            LIMIT $maximumCount;
            """;
        command.Parameters.AddWithValue("$now", now.ToString("o"));
        command.Parameters.AddWithValue("$maximumCount", maximumCount);
        using var reader = command.ExecuteReader();
        var rows = new List<BundleOutboxRecord>();
        while (reader.Read())
            rows.Add(ReadOutbox(reader));
        return rows;
    }

    public void RecordOutcome(
        string bundleId,
        BundleUploadOutcomeRecord outcome,
        DateTimeOffset now
    )
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            UPDATE {RunLogSchema.BundleOutboxTableName}
            SET status = $status, attempts = attempts + 1, last_attempt_at_utc = $now,
                failed_at_utc = CASE WHEN $uploaded = 1 THEN NULL ELSE $now END,
                last_error_code = CASE WHEN $uploaded = 1 THEN NULL ELSE $code END,
                last_error_detail = CASE WHEN $uploaded = 1 THEN NULL ELSE $detail END,
                server_request_id = $requestId, server_outcome = $outcome,
                uploaded_at_utc = CASE WHEN $uploaded = 1 THEN $now ELSE NULL END
            WHERE bundle_id = $bundleId AND status = 'pending';
            """;
        command.Parameters.AddWithValue("$bundleId", bundleId);
        command.Parameters.AddWithValue(
            "$status",
            outcome.Uploaded ? "uploaded" : "permanent_failure"
        );
        command.Parameters.AddWithValue("$uploaded", outcome.Uploaded ? 1 : 0);
        command.Parameters.AddWithValue("$now", now.ToString("o"));
        command.Parameters.AddWithValue("$code", outcome.Code);
        AddNullableString(command, "$detail", outcome.Detail);
        AddNullableString(command, "$requestId", outcome.RequestId);
        AddNullableString(command, "$outcome", outcome.Outcome);
        command.ExecuteNonQuery();
    }

    public void RecordTransient(
        string bundleId,
        string code,
        string? detail,
        DateTimeOffset now,
        DateTimeOffset nextAttempt
    ) =>
        Execute(
            $"UPDATE {RunLogSchema.BundleOutboxTableName} SET attempts = attempts + 1, last_attempt_at_utc = $now, next_attempt_at_utc = $next, last_error_code = $code, last_error_detail = $detail WHERE bundle_id = $bundleId AND status = 'pending';",
            ("$bundleId", bundleId),
            ("$now", now.ToString("o")),
            ("$next", nextAttempt.ToString("o")),
            ("$code", code),
            ("$detail", detail)
        );

    public void FailOutboxAndScheduleReseal(
        string bundleId,
        string runId,
        string reason,
        DateTimeOffset now
    )
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        Execute(
            connection,
            transaction,
            $"UPDATE {RunLogSchema.BundleOutboxTableName} SET status = 'permanent_failure', failed_at_utc = $now, last_error_code = $reason WHERE bundle_id = $bundleId;",
            ("$bundleId", bundleId),
            ("$now", now.ToString("o")),
            ("$reason", reason)
        );
        Execute(
            connection,
            transaction,
            $"""
            INSERT OR IGNORE INTO {RunLogSchema.BundleSealJobsTableName} (
                run_id, state, player_account_id, screenshot_requested, screenshot_state,
                input_deadline_at_utc
            )
            SELECT run_id, 'waiting', player_account_id, bundle_screenshot_requested,
                   CASE WHEN bundle_screenshot_requested = 1 THEN 'waiting' ELSE 'not_requested' END,
                   $now
            FROM {RunLogSchema.RunsTableName} WHERE run_id = $runId;
            """,
            ("$runId", runId),
            ("$now", now.ToString("o"))
        );
        transaction.Commit();
    }

    public void ExpirePending(DateTimeOffset cutoff, DateTimeOffset now) =>
        Execute(
            $"UPDATE {RunLogSchema.BundleOutboxTableName} SET status = 'permanent_failure', failed_at_utc = $now, last_error_code = 'local_retention_expired' WHERE status = 'pending' AND sealed_at_utc < $cutoff;",
            ("$now", now.ToString("o")),
            ("$cutoff", cutoff.ToString("o"))
        );

    public IReadOnlyList<string> ListRetentionFileNames(DateTimeOffset permanentFailureCutoff) =>
        ListFileNames(
            $"SELECT file_name FROM {RunLogSchema.BundleOutboxTableName} WHERE status = 'uploaded' OR (status = 'permanent_failure' AND failed_at_utc < $cutoff) ORDER BY COALESCE(failed_at_utc, uploaded_at_utc, sealed_at_utc);",
            ("$cutoff", permanentFailureCutoff.ToString("o"))
        );

    public IReadOnlyList<string> ListReclaimFileNames(DateTimeOffset pendingCutoff) =>
        ListFileNames(
            $"SELECT file_name FROM {RunLogSchema.BundleOutboxTableName} WHERE status = 'permanent_failure' OR (status = 'pending' AND sealed_at_utc < $pendingCutoff) ORDER BY sealed_at_utc ASC;",
            ("$pendingCutoff", pendingCutoff.ToString("o"))
        );

    private static BundleSealJobRecord? ReadJob(
        SqliteConnection connection,
        string runId,
        SqliteTransaction? transaction = null
    )
    {
        using var command = CreateCommand(connection, transaction);
        command.CommandText =
            $"SELECT run_id, state, player_account_id, screenshot_requested, screenshot_state, input_deadline_at_utc, bundle_id, created_at_ms FROM {RunLogSchema.BundleSealJobsTableName} WHERE run_id = $runId LIMIT 1;";
        command.Parameters.AddWithValue("$runId", runId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;
        return new BundleSealJobRecord(
            reader.GetString(0),
            ParseJobState(reader.GetString(1)),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetInt32(3) == 1,
            ParseScreenshotState(reader.GetString(4)),
            SqliteUtcInstant.Parse(reader.GetString(5)),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetInt64(7)
        );
    }

    private static BundleOutboxRecord ReadOutbox(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt64(5)
        );

    private static void InsertOutbox(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BundleOutboxPublishRecord outbox,
        DateTimeOffset sealedAt
    )
    {
        using var command = CreateCommand(connection, transaction);
        command.CommandText = $"""
            INSERT OR IGNORE INTO {RunLogSchema.BundleOutboxTableName} (
                bundle_id, run_id, file_name, content_sha256_hex, content_digest,
                total_bytes, has_screenshot, sealed_at_utc, status, next_attempt_at_utc
            ) VALUES (
                $bundleId, $runId, $fileName, $sha256, $contentDigest,
                $totalBytes, $hasScreenshot, $sealedAt, 'pending', $sealedAt
            );
            """;
        command.Parameters.AddWithValue("$bundleId", outbox.BundleId);
        command.Parameters.AddWithValue("$runId", outbox.RunId);
        command.Parameters.AddWithValue("$fileName", outbox.FileName);
        command.Parameters.AddWithValue("$sha256", outbox.ContentSha256Hex);
        command.Parameters.AddWithValue("$contentDigest", outbox.ContentDigest);
        command.Parameters.AddWithValue("$totalBytes", outbox.TotalBytes);
        command.Parameters.AddWithValue("$hasScreenshot", outbox.HasScreenshot ? 1 : 0);
        command.Parameters.AddWithValue("$sealedAt", sealedAt.ToString("o"));
        command.ExecuteNonQuery();
    }

    private static void DeleteJob(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string runId
    ) =>
        Execute(
            connection,
            transaction,
            $"DELETE FROM {RunLogSchema.BundleSealJobsTableName} WHERE run_id = $runId;",
            ("$runId", runId)
        );

    private void MarkJob(string runId, BundleSealJobState state, string code, string? detail) =>
        Execute(
            $"UPDATE {RunLogSchema.BundleSealJobsTableName} SET state = $state, last_error_code = $code, last_error_detail = $detail WHERE run_id = $runId;",
            ("$runId", runId),
            ("$state", ToStorage(state)),
            ("$code", code),
            ("$detail", detail)
        );

    private static BundleSealJobState ParseJobState(string value) =>
        value switch
        {
            "waiting" => BundleSealJobState.Waiting,
            "sealing" => BundleSealJobState.Sealing,
            "terminal_failure" => BundleSealJobState.TerminalFailure,
            _ => throw new InvalidDataException($"Unknown bundle seal job state '{value}'."),
        };

    private static BundleScreenshotState ParseScreenshotState(string value) =>
        value switch
        {
            "not_requested" => BundleScreenshotState.NotRequested,
            "waiting" => BundleScreenshotState.Waiting,
            "available" => BundleScreenshotState.Available,
            "unavailable" => BundleScreenshotState.Unavailable,
            "timed_out" => BundleScreenshotState.TimedOut,
            _ => throw new InvalidDataException($"Unknown bundle screenshot state '{value}'."),
        };

    private static string ToStorage(BundleSealJobState state) =>
        state switch
        {
            BundleSealJobState.Waiting => "waiting",
            BundleSealJobState.Sealing => "sealing",
            BundleSealJobState.TerminalFailure => "terminal_failure",
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };

    private static string ToStorage(BundleScreenshotState state) =>
        state switch
        {
            BundleScreenshotState.NotRequested => "not_requested",
            BundleScreenshotState.Waiting => "waiting",
            BundleScreenshotState.Available => "available",
            BundleScreenshotState.Unavailable => "unavailable",
            BundleScreenshotState.TimedOut => "timed_out",
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };

    private IReadOnlyList<string> ListFileNames(
        string sql,
        params (string Name, object? Value)[] parameters
    )
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = sql;
        AddParameters(command, parameters);
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));
        return names;
    }

    private void Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        using var connection = OpenConnection();
        Execute(connection, null, sql, parameters);
    }

    private static void Execute(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        params (string Name, object? Value)[] parameters
    )
    {
        using var command = CreateCommand(connection, transaction);
        command.CommandText = sql;
        AddParameters(command, parameters);
        command.ExecuteNonQuery();
    }

    private static void AddParameters(
        SqliteCommand command,
        IEnumerable<(string Name, object? Value)> parameters
    )
    {
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }
}

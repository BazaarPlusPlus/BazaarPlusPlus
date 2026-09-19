#nullable enable
using System.Globalization;
using BazaarPlusPlus.Storage.Paths;
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Storage.RunLog;

public static class RunLogSchema
{
    public const int LocalDatabaseSchemaVersion = 2;
    public const int RowSchemaVersion = 2;

    private static readonly object InitializationGate = new();

    public const string RunsTableName = "runs";
    public const string RunEventsTableName = "run_events";
    public const string BattlesTableName = "battles";
    public const string BattleSnapshotsTableName = "battle_snapshots";
    public const string RunScreenshotsTableName = "run_screenshots";
    public const string CombatReplayVideosTableName = "combat_replay_videos";
    public const string BundleSealJobsTableName = "bundle_seal_jobs";
    public const string BundleOutboxTableName = "bundle_outbox";
    public const string CaptureSourceEndOfRunAuto = "end_of_run_auto";

    public static int CurrentSchemaVersion => LocalDatabaseSchemaVersion;
    public static string DatabaseFileName => PathConstants.RunLogDatabaseFileName;

    // Stable SQL expressions shared by history queries and their expression indexes.
    public const string HistoryRunTime = "COALESCE(ended_at_utc,last_seen_at_utc,started_at_utc)";
    public const string HistoryWhitespace =
        " \t\n\v\f\r\u0085\u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000";
    public const string HistoryHeroKey =
        "CASE lower(trim(hero,'"
        + HistoryWhitespace
        + "')) WHEN 'hero8' THEN 'thedragons' ELSE lower(trim(hero,'"
        + HistoryWhitespace
        + "')) END";
    public const string HistoryRecorderOutcome =
        "CASE lower(trim(winner_combatant_id,'"
        + HistoryWhitespace
        + "')) WHEN 'player' THEN 1 WHEN 'opponent' THEN -1 ELSE CASE lower(trim(result,'"
        + HistoryWhitespace
        + "')) WHEN 'win' THEN 1 WHEN 'won' THEN 1 WHEN 'loss' THEN -1 WHEN 'lost' THEN -1 ELSE 0 END END";

    public static string BootstrapSql =>
        $"""
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS {RunsTableName} (
                run_id TEXT PRIMARY KEY,
                started_at_utc TEXT NOT NULL,
                last_seen_at_utc TEXT NOT NULL,
                status TEXT NOT NULL,
                completed INTEGER NOT NULL DEFAULT 0,
                hero TEXT NOT NULL,
                game_mode TEXT NOT NULL,
                seed INTEGER NULL,
                player_rank TEXT NULL,
                player_rating INTEGER NULL,
                day INTEGER NULL,
                hour INTEGER NULL,
                max_health INTEGER NULL,
                prestige INTEGER NULL,
                level INTEGER NULL,
                income INTEGER NULL,
                gold INTEGER NULL,
                last_seq INTEGER NOT NULL DEFAULT 0,
                ended_at_utc TEXT NULL,
                final_day INTEGER NULL,
                final_hour INTEGER NULL,
                victories INTEGER NULL,
                losses INTEGER NULL,
                final_player_rank TEXT NULL,
                final_player_rating INTEGER NULL,
                final_player_rating_delta INTEGER NULL,
                reason TEXT NULL,
                build_channel TEXT NULL,
                player_account_id TEXT NULL,
                bundle_screenshot_requested INTEGER NOT NULL DEFAULT 0,
                mod_version TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS {RunEventsTableName} (
                run_id TEXT NOT NULL,
                seq INTEGER NOT NULL,
                ts_utc TEXT NOT NULL,
                kind TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                PRIMARY KEY (run_id, seq),
                FOREIGN KEY (run_id) REFERENCES {RunsTableName}(run_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS {BattlesTableName} (
                battle_id TEXT PRIMARY KEY,
                remote_battle_id TEXT NULL,
                uploader_account_id TEXT NULL,
                source TEXT NOT NULL,
                run_id TEXT NULL,
                local_player_account_id TEXT NULL,
                recorded_at_utc TEXT NOT NULL,
                day INTEGER NULL,
                hour INTEGER NULL,
                encounter_id TEXT NULL,
                combat_kind TEXT NOT NULL,
                player_name TEXT NULL,
                player_account_id TEXT NULL,
                player_hero TEXT NULL,
                player_rank TEXT NULL,
                player_rating INTEGER NULL,
                player_level INTEGER NULL,
                player_prestige INTEGER NULL,
                player_income INTEGER NULL,
                player_gold INTEGER NULL,
                player_victories INTEGER NULL,
                player_hand_item_count INTEGER NULL,
                player_skill_count INTEGER NULL,
                opponent_name TEXT NULL,
                opponent_account_id TEXT NULL,
                opponent_hero TEXT NULL,
                opponent_rank TEXT NULL,
                opponent_rating INTEGER NULL,
                opponent_level INTEGER NULL,
                opponent_prestige INTEGER NULL,
                opponent_victories INTEGER NULL,
                opponent_hand_item_count INTEGER NULL,
                opponent_skill_count INTEGER NULL,
                result TEXT NULL,
                winner_combatant_id TEXT NULL,
                loser_combatant_id TEXT NULL,
                is_final_battle INTEGER NOT NULL DEFAULT 0,
                has_local_payload INTEGER NOT NULL DEFAULT 0,
                bundle_id TEXT NULL,
                download_url TEXT NULL,
                download_url_expires_at_ms INTEGER NULL,
                ghost_replay_state TEXT NULL,
                ghost_replay_unavailable_reason TEXT NULL,
                deleted_at_utc TEXT NULL,
                local_payload_state TEXT NULL,
                local_payload_maintenance_at_utc TEXT NULL,
                FOREIGN KEY (run_id) REFERENCES {RunsTableName}(run_id) ON DELETE CASCADE,
                CHECK (
                    (source = 'LOCAL' AND remote_battle_id IS NULL AND uploader_account_id IS NULL)
                    OR
                    (source = 'GHOST' AND run_id IS NULL AND remote_battle_id IS NOT NULL AND uploader_account_id IS NOT NULL)
                ),
                CHECK (
                    ghost_replay_state IS NULL OR ghost_replay_state IN (
                        'remote_available', 'local_ready', 'unavailable_payload', 'expired'
                    )
                ),
                CHECK (
                    (source = 'LOCAL' AND local_payload_state IS NOT NULL AND (
                        (local_payload_state = 'ready' AND has_local_payload = 1)
                        OR
                        (local_payload_state IN ('delete_pending', 'evicted', 'missing')
                         AND has_local_payload = 0)
                    ))
                    OR
                    (source <> 'LOCAL' AND local_payload_state IS NULL)
                )
            );

            CREATE TABLE IF NOT EXISTS {BattleSnapshotsTableName} (
                battle_id TEXT PRIMARY KEY,
                player_hand_json TEXT NOT NULL,
                player_skills_json TEXT NOT NULL,
                opponent_hand_json TEXT NOT NULL,
                opponent_skills_json TEXT NOT NULL,
                FOREIGN KEY (battle_id) REFERENCES {BattlesTableName}(battle_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS {RunScreenshotsTableName} (
                screenshot_id TEXT PRIMARY KEY,
                run_id TEXT NULL,
                hero_name TEXT NULL,
                battle_id TEXT NULL,
                capture_source TEXT NOT NULL,
                is_primary INTEGER NOT NULL DEFAULT 0,
                image_relative_path TEXT NOT NULL,
                captured_at_local TEXT NOT NULL,
                captured_at_utc TEXT NOT NULL,
                day INTEGER NULL,
                player_rank TEXT NULL,
                player_rating INTEGER NULL,
                player_position INTEGER NULL,
                victories_at_capture INTEGER NULL,
                build_channel TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS {CombatReplayVideosTableName} (
                video_id TEXT PRIMARY KEY,
                battle_id TEXT NOT NULL,
                source TEXT NOT NULL,
                video_relative_path TEXT NOT NULL,
                width INTEGER NOT NULL,
                height INTEGER NOT NULL,
                fps INTEGER NOT NULL,
                codec TEXT NOT NULL,
                crf INTEGER NULL,
                preset TEXT NULL,
                started_at_utc TEXT NOT NULL,
                ended_at_utc TEXT NULL,
                duration_ms INTEGER NULL,
                captured_frames INTEGER NOT NULL DEFAULT 0,
                dropped_frames INTEGER NOT NULL DEFAULT 0,
                file_size_bytes INTEGER NULL,
                status TEXT NOT NULL,
                error TEXT NULL,
                attachment_state TEXT NOT NULL DEFAULT 'attached',
                file_state TEXT NOT NULL DEFAULT 'pending',
                detached_at_utc TEXT NULL,
                missing_at_utc TEXT NULL,
                last_reconciled_at_utc TEXT NULL,
                CHECK (attachment_state IN ('attached', 'detached')),
                CHECK (file_state IN ('pending', 'present', 'missing', 'deleted'))
            );

            CREATE TABLE IF NOT EXISTS {BundleSealJobsTableName} (
                run_id TEXT PRIMARY KEY,
                state TEXT NOT NULL DEFAULT 'waiting',
                player_account_id TEXT NULL,
                screenshot_requested INTEGER NOT NULL,
                screenshot_state TEXT NOT NULL DEFAULT 'waiting',
                input_deadline_at_utc TEXT NOT NULL,
                bundle_id TEXT NULL UNIQUE,
                created_at_ms INTEGER NULL,
                attempts INTEGER NOT NULL DEFAULT 0,
                last_attempt_at_utc TEXT NULL,
                last_error_code TEXT NULL,
                last_error_detail TEXT NULL,
                FOREIGN KEY (run_id) REFERENCES {RunsTableName}(run_id) ON DELETE CASCADE,
                CHECK (state IN ('waiting', 'sealing', 'terminal_failure')),
                CHECK (screenshot_state IN ('not_requested', 'waiting', 'available', 'unavailable', 'timed_out'))
            );

            CREATE TABLE IF NOT EXISTS {BundleOutboxTableName} (
                bundle_id TEXT PRIMARY KEY,
                run_id TEXT NOT NULL,
                file_name TEXT NOT NULL UNIQUE,
                content_sha256_hex TEXT NOT NULL,
                content_digest TEXT NOT NULL,
                total_bytes INTEGER NOT NULL,
                has_screenshot INTEGER NOT NULL,
                sealed_at_utc TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'pending',
                attempts INTEGER NOT NULL DEFAULT 0,
                last_attempt_at_utc TEXT NULL,
                next_attempt_at_utc TEXT NULL,
                failed_at_utc TEXT NULL,
                last_error_code TEXT NULL,
                last_error_detail TEXT NULL,
                server_request_id TEXT NULL,
                server_outcome TEXT NULL,
                uploaded_at_utc TEXT NULL,
                CHECK (status IN ('pending', 'uploaded', 'permanent_failure'))
            );

            CREATE INDEX IF NOT EXISTS idx_runs_history_recent ON runs({HistoryRunTime} DESC, run_id DESC);
            CREATE INDEX IF NOT EXISTS idx_runs_history_hero ON runs({HistoryHeroKey}, {HistoryRunTime} DESC, run_id DESC);
            CREATE INDEX IF NOT EXISTS idx_battles_history_local ON battles(run_id, recorded_at_utc DESC, battle_id DESC) WHERE source = 'LOCAL';
            CREATE INDEX IF NOT EXISTS idx_battles_history_ghost ON battles(local_player_account_id, recorded_at_utc DESC, battle_id DESC) WHERE source = 'GHOST' AND deleted_at_utc IS NULL;
            CREATE INDEX IF NOT EXISTS idx_battles_history_ghost_outcome ON battles(local_player_account_id, {HistoryRecorderOutcome}, recorded_at_utc DESC, battle_id DESC) WHERE source = 'GHOST' AND deleted_at_utc IS NULL;
            CREATE INDEX IF NOT EXISTS idx_battles_history_ghost_day ON battles(local_player_account_id, recorded_at_utc DESC, battle_id DESC) WHERE source = 'GHOST' AND deleted_at_utc IS NULL AND day >= 10;
            CREATE INDEX IF NOT EXISTS idx_battles_history_ghost_day_outcome ON battles(local_player_account_id, {HistoryRecorderOutcome}, recorded_at_utc DESC, battle_id DESC) WHERE source = 'GHOST' AND deleted_at_utc IS NULL AND day >= 10;

            CREATE INDEX IF NOT EXISTS idx_{RunEventsTableName}_ts_utc
                ON {RunEventsTableName}(ts_utc);
            DROP INDEX IF EXISTS idx_{RunsTableName}_status_last_seen;
            CREATE INDEX IF NOT EXISTS idx_{RunsTableName}_completed_last_seen
                ON {RunsTableName}(completed, last_seen_at_utc DESC);
            CREATE INDEX IF NOT EXISTS idx_{RunsTableName}_started_at_utc
                ON {RunsTableName}(started_at_utc DESC);
            CREATE INDEX IF NOT EXISTS idx_{BattlesTableName}_run_id_recorded
                ON {BattlesTableName}(run_id, recorded_at_utc DESC);
            CREATE INDEX IF NOT EXISTS idx_{BattlesTableName}_source_recorded
                ON {BattlesTableName}(source, recorded_at_utc DESC);
            CREATE INDEX IF NOT EXISTS idx_{BattlesTableName}_local_player_recent
                ON {BattlesTableName}(local_player_account_id, recorded_at_utc DESC);
            CREATE UNIQUE INDEX IF NOT EXISTS idx_battles_ghost_identity
                ON {BattlesTableName}(uploader_account_id, remote_battle_id)
                WHERE source = 'GHOST';
            CREATE INDEX IF NOT EXISTS idx_battles_ghost_discovery
                ON {BattlesTableName}(local_player_account_id, source, recorded_at_utc DESC);
            CREATE INDEX IF NOT EXISTS idx_{RunScreenshotsTableName}_run_id_captured_at_utc
                ON {RunScreenshotsTableName}(run_id, captured_at_utc DESC);
            CREATE INDEX IF NOT EXISTS idx_run_screenshots_source_captured
                ON {RunScreenshotsTableName}(capture_source, captured_at_utc ASC, screenshot_id ASC);
            CREATE UNIQUE INDEX IF NOT EXISTS idx_run_screenshots_primary_run
                ON {RunScreenshotsTableName}(run_id)
                WHERE is_primary = 1 AND run_id IS NOT NULL;
            CREATE INDEX IF NOT EXISTS idx_{CombatReplayVideosTableName}_battle
                ON {CombatReplayVideosTableName}(battle_id, started_at_utc DESC);
            CREATE INDEX IF NOT EXISTS idx_combat_replay_videos_attachment
                ON {CombatReplayVideosTableName}(
                    attachment_state, started_at_utc DESC, video_id
                );
            CREATE INDEX IF NOT EXISTS idx_bundle_seal_jobs_state
                ON {BundleSealJobsTableName}(state, input_deadline_at_utc, run_id);
            CREATE UNIQUE INDEX IF NOT EXISTS idx_bundle_outbox_active_run
                ON {BundleOutboxTableName}(run_id)
                WHERE status = 'pending';
            CREATE INDEX IF NOT EXISTS idx_bundle_outbox_due
                ON {BundleOutboxTableName}(status, next_attempt_at_utc, attempts, sealed_at_utc);

            CREATE TRIGGER IF NOT EXISTS trg_battles_local_payload_insert
            BEFORE INSERT ON {BattlesTableName}
            WHEN NOT (
                (NEW.source = 'LOCAL' AND NEW.local_payload_state IS NOT NULL AND (
                    (NEW.local_payload_state = 'ready' AND NEW.has_local_payload = 1)
                    OR
                    (NEW.local_payload_state IN ('delete_pending', 'evicted', 'missing')
                     AND NEW.has_local_payload = 0)
                ))
                OR
                (NEW.source <> 'LOCAL' AND NEW.local_payload_state IS NULL)
            )
            BEGIN
                SELECT RAISE(ABORT, 'invalid local payload lifecycle state');
            END;

            CREATE TRIGGER IF NOT EXISTS trg_battles_local_payload_update
            BEFORE UPDATE OF source, has_local_payload, local_payload_state ON {BattlesTableName}
            WHEN NOT (
                (NEW.source = 'LOCAL' AND NEW.local_payload_state IS NOT NULL AND (
                    (NEW.local_payload_state = 'ready' AND NEW.has_local_payload = 1)
                    OR
                    (NEW.local_payload_state IN ('delete_pending', 'evicted', 'missing')
                     AND NEW.has_local_payload = 0)
                ))
                OR
                (NEW.source <> 'LOCAL' AND NEW.local_payload_state IS NULL)
            )
            BEGIN
                SELECT RAISE(ABORT, 'invalid local payload lifecycle state');
            END;

            PRAGMA user_version = {LocalDatabaseSchemaVersion};
            """;

    public static void EnsureInitialized(SqliteConnection connection)
    {
        if (connection == null)
            throw new ArgumentNullException(nameof(connection));

        lock (InitializationGate)
        {
            var currentVersion = ReadUserVersion(connection);
            if (currentVersion > LocalDatabaseSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Run log schema version {currentVersion} is newer than supported version {LocalDatabaseSchemaVersion}."
                );
            }

            if (currentVersion == 1)
            {
                UpgradeVersionOneToTwo(connection);
                currentVersion = ReadUserVersion(connection);
            }

            if (currentVersion != 0 && currentVersion != LocalDatabaseSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Run log schema version {currentVersion} cannot be initialized as version {LocalDatabaseSchemaVersion}."
                );
            }

            using var transaction = connection.BeginTransaction(deferred: false);
            Execute(connection, transaction, BootstrapSql);
            ValidateVersionTwoColumns(connection, transaction);
            transaction.Commit();
        }
    }

    private static void UpgradeVersionOneToTwo(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction(deferred: false);
        var versionInsideTransaction = ReadUserVersion(connection, transaction);
        if (versionInsideTransaction == LocalDatabaseSchemaVersion)
        {
            transaction.Commit();
            return;
        }
        if (versionInsideTransaction != 1)
        {
            throw new InvalidOperationException(
                $"Run log schema changed from version 1 to {versionInsideTransaction} during initialization."
            );
        }

        Execute(
            connection,
            transaction,
            $"""
            ALTER TABLE {BattlesTableName} ADD COLUMN local_payload_state TEXT NULL;
            ALTER TABLE {BattlesTableName} ADD COLUMN local_payload_maintenance_at_utc TEXT NULL;
            UPDATE {BattlesTableName}
            SET local_payload_state = CASE
                WHEN source = 'LOCAL' AND has_local_payload = 1 THEN 'ready'
                WHEN source = 'LOCAL' THEN 'missing'
                ELSE NULL
            END;

            ALTER TABLE {CombatReplayVideosTableName}
                ADD COLUMN attachment_state TEXT NOT NULL DEFAULT 'attached';
            ALTER TABLE {CombatReplayVideosTableName}
                ADD COLUMN file_state TEXT NOT NULL DEFAULT 'pending';
            ALTER TABLE {CombatReplayVideosTableName} ADD COLUMN detached_at_utc TEXT NULL;
            ALTER TABLE {CombatReplayVideosTableName} ADD COLUMN missing_at_utc TEXT NULL;
            ALTER TABLE {CombatReplayVideosTableName} ADD COLUMN last_reconciled_at_utc TEXT NULL;
            UPDATE {CombatReplayVideosTableName}
            SET attachment_state = CASE
                    WHEN EXISTS (
                        SELECT 1 FROM {BattlesTableName} AS b
                        WHERE b.battle_id = {CombatReplayVideosTableName}.battle_id
                    ) THEN 'attached'
                    ELSE 'detached'
                END,
                detached_at_utc = CASE
                    WHEN EXISTS (
                        SELECT 1 FROM {BattlesTableName} AS b
                        WHERE b.battle_id = {CombatReplayVideosTableName}.battle_id
                    ) THEN NULL
                    ELSE strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
                END,
                file_state = 'pending';

            CREATE INDEX IF NOT EXISTS idx_combat_replay_videos_attachment
                ON {CombatReplayVideosTableName}(
                    attachment_state, started_at_utc DESC, video_id
                );
            CREATE TRIGGER IF NOT EXISTS trg_battles_local_payload_insert
            BEFORE INSERT ON {BattlesTableName}
            WHEN NOT (
                (NEW.source = 'LOCAL' AND NEW.local_payload_state IS NOT NULL AND (
                    (NEW.local_payload_state = 'ready' AND NEW.has_local_payload = 1)
                    OR
                    (NEW.local_payload_state IN ('delete_pending', 'evicted', 'missing')
                     AND NEW.has_local_payload = 0)
                ))
                OR
                (NEW.source <> 'LOCAL' AND NEW.local_payload_state IS NULL)
            )
            BEGIN
                SELECT RAISE(ABORT, 'invalid local payload lifecycle state');
            END;
            CREATE TRIGGER IF NOT EXISTS trg_battles_local_payload_update
            BEFORE UPDATE OF source, has_local_payload, local_payload_state ON {BattlesTableName}
            WHEN NOT (
                (NEW.source = 'LOCAL' AND NEW.local_payload_state IS NOT NULL AND (
                    (NEW.local_payload_state = 'ready' AND NEW.has_local_payload = 1)
                    OR
                    (NEW.local_payload_state IN ('delete_pending', 'evicted', 'missing')
                     AND NEW.has_local_payload = 0)
                ))
                OR
                (NEW.source <> 'LOCAL' AND NEW.local_payload_state IS NULL)
            )
            BEGIN
                SELECT RAISE(ABORT, 'invalid local payload lifecycle state');
            END;
            PRAGMA user_version = {LocalDatabaseSchemaVersion};
            """
        );
        ValidateVersionTwoColumns(connection, transaction);
        transaction.Commit();
    }

    private static int ReadUserVersion(
        SqliteConnection connection,
        SqliteTransaction? transaction = null
    )
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void ValidateVersionTwoColumns(
        SqliteConnection connection,
        SqliteTransaction transaction
    )
    {
        RequireColumn(connection, transaction, BattlesTableName, "local_payload_state");
        RequireColumn(
            connection,
            transaction,
            BattlesTableName,
            "local_payload_maintenance_at_utc"
        );
        RequireColumn(connection, transaction, CombatReplayVideosTableName, "attachment_state");
        RequireColumn(connection, transaction, CombatReplayVideosTableName, "file_state");
        RequireColumn(connection, transaction, CombatReplayVideosTableName, "detached_at_utc");
        RequireColumn(connection, transaction, CombatReplayVideosTableName, "missing_at_utc");
        RequireColumn(
            connection,
            transaction,
            CombatReplayVideosTableName,
            "last_reconciled_at_utc"
        );
    }

    private static void RequireColumn(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        string columnName
    )
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.Ordinal))
                return;
        }

        throw new InvalidOperationException(
            $"Run log schema is missing required column {tableName}.{columnName}."
        );
    }

    private static void Execute(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql
    )
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}

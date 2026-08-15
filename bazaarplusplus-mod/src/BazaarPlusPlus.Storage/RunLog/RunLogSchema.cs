#nullable enable
using BazaarPlusPlus.Storage.Paths;
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Storage.RunLog;

public static class RunLogSchema
{
    public const int LocalDatabaseSchemaVersion = 1;
    public const int RowSchemaVersion = 1;

    public const string RunsTableName = "runs";
    public const string RunEventsTableName = "run_events";
    public const string BattlesTableName = "battles";
    public const string BattleSnapshotsTableName = "battle_snapshots";
    public const string RunScreenshotsTableName = "run_screenshots";
    public const string CombatReplayVideosTableName = "combat_replay_videos";
    public const string BundleSealJobsTableName = "bundle_seal_jobs";
    public const string BundleOutboxTableName = "bundle_outbox";
    public const string CaptureSourceEndOfRunAuto = "end_of_run_auto";
    public const string GameModeRanked = "Ranked";

    public static int CurrentSchemaVersion => LocalDatabaseSchemaVersion;
    public static string DatabaseFileName => PathConstants.RunLogDatabaseFileName;
    public static string RunCheckpointsTableName => RunsTableName;
    public static string RunStatusTableName => RunsTableName;
    public static string PvpBattlesTableName => BattlesTableName;
    public static string GhostBattlesTableName => BattlesTableName;

    public static string BootstrapSql =>
        $"""
            PRAGMA foreign_keys = ON;
            PRAGMA user_version = {LocalDatabaseSchemaVersion};

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
                error TEXT NULL
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

            CREATE INDEX IF NOT EXISTS idx_{RunEventsTableName}_ts_utc
                ON {RunEventsTableName}(ts_utc);
            CREATE INDEX IF NOT EXISTS idx_{RunsTableName}_status_last_seen
                ON {RunsTableName}(status, last_seen_at_utc DESC);
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
            CREATE INDEX IF NOT EXISTS idx_bundle_seal_jobs_state
                ON {BundleSealJobsTableName}(state, input_deadline_at_utc, run_id);
            CREATE UNIQUE INDEX IF NOT EXISTS idx_bundle_outbox_active_run
                ON {BundleOutboxTableName}(run_id)
                WHERE status = 'pending';
            CREATE INDEX IF NOT EXISTS idx_bundle_outbox_due
                ON {BundleOutboxTableName}(status, next_attempt_at_utc, attempts, sealed_at_utc);
            """;

    public static void EnsureInitialized(SqliteConnection connection)
    {
        if (connection == null)
            throw new ArgumentNullException(nameof(connection));
        using var command = connection.CreateCommand();
        command.CommandText = BootstrapSql;
        command.ExecuteNonQuery();
    }
}

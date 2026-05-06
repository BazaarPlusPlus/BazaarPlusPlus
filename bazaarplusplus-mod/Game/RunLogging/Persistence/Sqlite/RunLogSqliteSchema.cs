#nullable enable
using System;
using BazaarPlusPlus.Core.Paths;
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite;

public static class RunLogSqliteSchema
{
    public static int LocalDatabaseSchemaVersion => 11;

    public static int RowSchemaVersion => 11;

    public static int UploadPayloadSchemaVersion => 1;

    public static int CurrentSchemaVersion => LocalDatabaseSchemaVersion;

    public static string DatabaseFileName => BppPathConstants.RunLogDatabaseFileName;

    public static string RunsTableName => "runs";

    public static string RunEventsTableName => "run_events";

    public static string BattlesTableName => "battles";

    public static string BattleSnapshotsTableName => "battle_snapshots";

    public static string RunScreenshotsTableName => "run_screenshots";

    public static string SyncCursorsTableName => "sync_cursors";

    public static string RunSyncStateTableName => "run_sync_state";

    public static string RunCheckpointsTableName => RunsTableName;

    public static string RunStatusTableName => RunsTableName;

    public static string PvpBattlesTableName => BattlesTableName;

    public static string GhostBattlesTableName => BattlesTableName;

    public static string GhostSyncStateTableName => SyncCursorsTableName;

    public static string ReplaySyncStateTableName => BattlesTableName;

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
                reason TEXT NULL
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
                opponent_name TEXT NULL,
                opponent_account_id TEXT NULL,
                opponent_hero TEXT NULL,
                opponent_rank TEXT NULL,
                opponent_rating INTEGER NULL,
                opponent_level INTEGER NULL,
                result TEXT NULL,
                winner_combatant_id TEXT NULL,
                loser_combatant_id TEXT NULL,
                is_bundle_final_battle INTEGER NOT NULL DEFAULT 0,
                replay_available INTEGER NOT NULL DEFAULT 0,
                replay_downloaded INTEGER NOT NULL DEFAULT 0,
                has_local_payload INTEGER NOT NULL DEFAULT 0,
                replay_dirty INTEGER NOT NULL DEFAULT 0,
                replay_last_attempt_at_utc TEXT NULL,
                replay_last_uploaded_at_utc TEXT NULL,
                replay_retry_count INTEGER NOT NULL DEFAULT 0,
                replay_last_error TEXT NULL,
                last_synced_at_utc TEXT NULL,
                deleted_at_utc TEXT NULL,
                FOREIGN KEY (run_id) REFERENCES {RunsTableName}(run_id) ON DELETE CASCADE,
                CHECK (
                    (source = 'LOCAL') OR
                    (source = 'GHOST' AND run_id IS NULL)
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
                victories_at_capture INTEGER NULL
            );

            CREATE TABLE IF NOT EXISTS {SyncCursorsTableName} (
                scope TEXT PRIMARY KEY,
                cursor_value TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS {RunSyncStateTableName} (
                run_id TEXT PRIMARY KEY,
                dirty INTEGER NOT NULL,
                uploaded_seq INTEGER NULL,
                uploaded_status TEXT NULL,
                last_attempt_at_utc TEXT NULL,
                last_uploaded_at_utc TEXT NULL,
                retry_count INTEGER NOT NULL DEFAULT 0,
                last_error TEXT NULL,
                FOREIGN KEY (run_id) REFERENCES {RunsTableName}(run_id) ON DELETE CASCADE
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

            CREATE INDEX IF NOT EXISTS idx_{BattlesTableName}_replay_dirty
                ON {BattlesTableName}(replay_dirty, replay_last_attempt_at_utc);

            CREATE INDEX IF NOT EXISTS idx_{RunSyncStateTableName}_dirty
                ON {RunSyncStateTableName}(dirty, last_attempt_at_utc);

            CREATE INDEX IF NOT EXISTS idx_{RunScreenshotsTableName}_run_id_captured_at_utc
                ON {RunScreenshotsTableName}(run_id, captured_at_utc DESC);

            CREATE UNIQUE INDEX IF NOT EXISTS idx_run_screenshots_primary_run
                ON {RunScreenshotsTableName}(run_id)
                WHERE is_primary = 1 AND run_id IS NOT NULL;
            """;

    public static void EnsureInitialized(SqliteConnection connection)
    {
        if (connection == null)
            throw new ArgumentNullException(nameof(connection));

        using (var command = connection.CreateCommand())
        {
            command.CommandText = BootstrapSql;
            command.ExecuteNonQuery();
        }
    }
}

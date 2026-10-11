#nullable enable
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Storage.RunLog;

public static class RunLogSchema
{
    // `PRAGMA user_version`: the column shape the installer reads. Bump it only with a column change
    // and a migration; a one-time data repair does not bump it (ADR-0011). V6 starts at 1 in its own
    // Data Root; a V5 database is imported, never opened here.
    public const int LocalDatabaseSchemaVersion = 1;

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

    // Stable SQL expressions shared by history queries and their expression indexes.
    public const string HistoryRunTime = "COALESCE(ended_at_ms,last_seen_at_ms,started_at_ms)";
    public const string HistoryWhitespace =
        " \t\n\v\f\r\u0085\u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000";
    public const string HistoryHeroKey = "lower(trim(hero,'" + HistoryWhitespace + "'))";

    // Writers store 'player'/'opponent' and 'win'/'loss' verbatim; a Ghost Battle value the server
    // did not normalize reads as undecided.
    public const string HistoryRecorderOutcome =
        "CASE lower(winner_combatant_id) WHEN 'player' THEN 1 WHEN 'opponent' THEN -1 "
        + "ELSE CASE lower(result) WHEN 'win' THEN 1 WHEN 'loss' THEN -1 ELSE 0 END END";

    // Every *_at_ms column holds Unix epoch milliseconds (UTC). bundle_outbox.run_id,
    // run_screenshots.run_id and combat_replay_videos.battle_id have no foreign key on purpose:
    // outbox rows outlive their Run, and screenshots and videos survive a deleted Run or battle.
    public static string BootstrapSql =>
        $"""
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS {RunsTableName} (
                run_id TEXT PRIMARY KEY,
                started_at_ms INTEGER NOT NULL,
                last_seen_at_ms INTEGER NOT NULL,
                status TEXT NOT NULL,
                hero TEXT NOT NULL,
                game_mode TEXT NOT NULL,
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
                ended_at_ms INTEGER NULL,
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
                mod_version TEXT NULL,
                upload_policy TEXT NOT NULL DEFAULT 'bundle',
                CHECK (status IN ('active', 'completed', 'abandoned')),
                CHECK (upload_policy IN ('bundle', 'history_only'))
            );

            CREATE TABLE IF NOT EXISTS {RunEventsTableName} (
                run_id TEXT NOT NULL,
                seq INTEGER NOT NULL,
                ts_at_ms INTEGER NOT NULL,
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
                recorded_at_ms INTEGER NOT NULL,
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
                bundle_id TEXT NULL,
                download_url TEXT NULL,
                download_url_expires_at_ms INTEGER NULL,
                ghost_replay_state TEXT NULL,
                ghost_replay_unavailable_reason TEXT NULL,
                deleted_at_ms INTEGER NULL,
                local_payload_state TEXT NULL,
                local_payload_maintenance_at_ms INTEGER NULL,
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
                    (source = 'LOCAL' AND local_payload_state IS NOT NULL
                     AND local_payload_state IN ('ready', 'delete_pending', 'evicted', 'missing'))
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
                capture_source TEXT NOT NULL,
                is_primary INTEGER NOT NULL DEFAULT 0,
                image_relative_path TEXT NOT NULL,
                captured_at_ms INTEGER NOT NULL,
                captured_utc_offset_minutes INTEGER NOT NULL,
                day INTEGER NULL,
                player_rank TEXT NULL,
                player_rating INTEGER NULL,
                player_position INTEGER NULL,
                victories_at_capture INTEGER NULL
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
                started_at_ms INTEGER NOT NULL,
                ended_at_ms INTEGER NULL,
                duration_ms INTEGER NULL,
                captured_frames INTEGER NOT NULL DEFAULT 0,
                dropped_frames INTEGER NOT NULL DEFAULT 0,
                file_size_bytes INTEGER NULL,
                status TEXT NOT NULL,
                error TEXT NULL,
                attachment_state TEXT NOT NULL DEFAULT 'attached',
                file_state TEXT NOT NULL DEFAULT 'pending',
                detached_at_ms INTEGER NULL,
                missing_at_ms INTEGER NULL,
                last_reconciled_at_ms INTEGER NULL,
                CHECK (status IN ('RECORDING', 'COMPLETED', 'FAILED')),
                CHECK (attachment_state IN ('attached', 'detached')),
                CHECK (file_state IN ('pending', 'present', 'missing'))
            );

            CREATE TABLE IF NOT EXISTS {BundleSealJobsTableName} (
                run_id TEXT PRIMARY KEY,
                state TEXT NOT NULL DEFAULT 'waiting',
                player_account_id TEXT NULL,
                screenshot_requested INTEGER NOT NULL,
                screenshot_state TEXT NOT NULL DEFAULT 'waiting',
                input_deadline_at_ms INTEGER NOT NULL,
                bundle_id TEXT NULL UNIQUE,
                created_at_ms INTEGER NULL,
                attempts INTEGER NOT NULL DEFAULT 0,
                last_attempt_at_ms INTEGER NULL,
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
                sealed_at_ms INTEGER NOT NULL,
                status TEXT NOT NULL DEFAULT 'pending',
                attempts INTEGER NOT NULL DEFAULT 0,
                last_attempt_at_ms INTEGER NULL,
                next_attempt_at_ms INTEGER NULL,
                failed_at_ms INTEGER NULL,
                last_error_code TEXT NULL,
                last_error_detail TEXT NULL,
                server_request_id TEXT NULL,
                server_outcome TEXT NULL,
                uploaded_at_ms INTEGER NULL,
                CHECK (status IN ('pending', 'uploaded', 'permanent_failure'))
            );

            CREATE INDEX IF NOT EXISTS idx_runs_history_recent ON runs({HistoryRunTime} DESC, run_id DESC);
            CREATE INDEX IF NOT EXISTS idx_runs_history_hero ON runs({HistoryHeroKey}, {HistoryRunTime} DESC, run_id DESC);
            CREATE INDEX IF NOT EXISTS idx_battles_history_local ON battles(run_id, recorded_at_ms DESC, battle_id DESC) WHERE source = 'LOCAL';
            CREATE INDEX IF NOT EXISTS idx_battles_history_ghost ON battles(local_player_account_id, recorded_at_ms DESC, battle_id DESC) WHERE source = 'GHOST' AND deleted_at_ms IS NULL;
            CREATE INDEX IF NOT EXISTS idx_battles_history_ghost_outcome ON battles(local_player_account_id, {HistoryRecorderOutcome}, recorded_at_ms DESC, battle_id DESC) WHERE source = 'GHOST' AND deleted_at_ms IS NULL;
            CREATE INDEX IF NOT EXISTS idx_battles_history_ghost_day ON battles(local_player_account_id, recorded_at_ms DESC, battle_id DESC) WHERE source = 'GHOST' AND deleted_at_ms IS NULL AND day >= 10;
            CREATE INDEX IF NOT EXISTS idx_battles_history_ghost_day_outcome ON battles(local_player_account_id, {HistoryRecorderOutcome}, recorded_at_ms DESC, battle_id DESC) WHERE source = 'GHOST' AND deleted_at_ms IS NULL AND day >= 10;

            CREATE INDEX IF NOT EXISTS idx_{RunsTableName}_status_last_seen
                ON {RunsTableName}(status, last_seen_at_ms DESC);
            CREATE INDEX IF NOT EXISTS idx_{BattlesTableName}_run_id_recorded
                ON {BattlesTableName}(run_id, recorded_at_ms DESC);
            CREATE INDEX IF NOT EXISTS idx_{BattlesTableName}_source_recorded
                ON {BattlesTableName}(source, recorded_at_ms DESC);
            CREATE INDEX IF NOT EXISTS idx_{BattlesTableName}_local_player_recent
                ON {BattlesTableName}(local_player_account_id, recorded_at_ms DESC);
            CREATE UNIQUE INDEX IF NOT EXISTS idx_battles_ghost_identity
                ON {BattlesTableName}(uploader_account_id, remote_battle_id)
                WHERE source = 'GHOST';
            CREATE INDEX IF NOT EXISTS idx_battles_ghost_discovery
                ON {BattlesTableName}(local_player_account_id, source, recorded_at_ms DESC);
            CREATE INDEX IF NOT EXISTS idx_{RunScreenshotsTableName}_run_id_captured
                ON {RunScreenshotsTableName}(run_id, captured_at_ms DESC);
            CREATE INDEX IF NOT EXISTS idx_run_screenshots_source_captured
                ON {RunScreenshotsTableName}(capture_source, captured_at_ms ASC, screenshot_id ASC);
            CREATE UNIQUE INDEX IF NOT EXISTS idx_run_screenshots_primary_run
                ON {RunScreenshotsTableName}(run_id)
                WHERE is_primary = 1 AND run_id IS NOT NULL;
            CREATE INDEX IF NOT EXISTS idx_{CombatReplayVideosTableName}_battle
                ON {CombatReplayVideosTableName}(battle_id, started_at_ms DESC);
            CREATE INDEX IF NOT EXISTS idx_combat_replay_videos_attachment
                ON {CombatReplayVideosTableName}(
                    attachment_state, started_at_ms DESC, video_id
                );
            CREATE INDEX IF NOT EXISTS idx_bundle_seal_jobs_state
                ON {BundleSealJobsTableName}(state, input_deadline_at_ms, run_id);
            CREATE UNIQUE INDEX IF NOT EXISTS idx_bundle_outbox_active_run
                ON {BundleOutboxTableName}(run_id)
                WHERE status = 'pending';
            CREATE INDEX IF NOT EXISTS idx_bundle_outbox_due
                ON {BundleOutboxTableName}(status, next_attempt_at_ms, attempts, sealed_at_ms);
            CREATE INDEX IF NOT EXISTS idx_bundle_outbox_run
                ON {BundleOutboxTableName}(run_id, status);

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

            using var transaction = connection.BeginTransaction(deferred: false);
            // Re-read under the write lock: another process may have initialized this DB meanwhile.
            var versionInsideTransaction = ReadUserVersion(connection, transaction);
            if (
                versionInsideTransaction != 0
                && versionInsideTransaction != LocalDatabaseSchemaVersion
            )
                throw new InvalidOperationException(
                    $"Unsupported run log schema version {versionInsideTransaction}."
                );
            DropDriftedIndexes(connection, transaction);
            Execute(connection, transaction, BootstrapSql);
            transaction.Commit();
        }
    }

    // CREATE INDEX IF NOT EXISTS never redefines an existing index, and History pages name
    // their index in INDEXED BY, which fails when the stored definition cannot serve the query.
    // An index whose stored SQL differs from BootstrapSql's is dropped here so BootstrapSql
    // recreates it. Retired index names still need an explicit DROP INDEX IF EXISTS.
    private static void DropDriftedIndexes(
        SqliteConnection connection,
        SqliteTransaction transaction
    )
    {
        var drifted = new List<string>();
        foreach (var (name, storedSql) in ReadIndexDefinitions(connection, transaction))
        {
            if (
                ExpectedIndexDefinitions.Value.TryGetValue(name, out var expectedSql)
                && !string.Equals(storedSql, expectedSql, StringComparison.Ordinal)
            )
                drifted.Add(name);
        }
        foreach (var name in drifted)
            Execute(connection, transaction, $"DROP INDEX \"{name}\";");
    }

    // SQLite stores an index's CREATE statement minus IF NOT EXISTS, so the definitions
    // BootstrapSql produces on an empty database are the exact text a current index carries.
    private static readonly Lazy<Dictionary<string, string>> ExpectedIndexDefinitions = new(() =>
    {
        using var scratch = new SqliteConnection("Data Source=:memory:");
        scratch.Open();
        using var transaction = scratch.BeginTransaction();
        Execute(scratch, transaction, BootstrapSql);
        var definitions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, sql) in ReadIndexDefinitions(scratch, transaction))
            definitions[name] = sql;
        return definitions;
    });

    private static List<(string Name, string Sql)> ReadIndexDefinitions(
        SqliteConnection connection,
        SqliteTransaction transaction
    )
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT name, sql FROM sqlite_master WHERE type = 'index' AND sql IS NOT NULL;";
        using var reader = command.ExecuteReader();
        var definitions = new List<(string, string)>();
        while (reader.Read())
            definitions.Add((reader.GetString(0), reader.GetString(1)));
        return definitions;
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

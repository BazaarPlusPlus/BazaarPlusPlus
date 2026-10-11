//! The frozen V5 history databases the V5 import reads (`tests/fixtures/v5-import/`).
//! Each schema fixture must build into a database with its `user_version` and
//! column set, and the seed rows and Data Root sample must agree with each other.
//! README.md beside the fixtures records where each one came from.

use std::path::{Path, PathBuf};

use rusqlite::Connection;
use sha2::{Digest, Sha256};

const V1_SCHEMA: &str = include_str!("fixtures/v5-import/v1.schema.sql");
const V2_SCHEMA: &str = include_str!("fixtures/v5-import/v2.schema.sql");
const V3_SCHEMA: &str = include_str!("fixtures/v5-import/v3.schema.sql");
const V3_UPGRADED_SCHEMA: &str = include_str!("fixtures/v5-import/v3-upgraded-from-v1.schema.sql");
const ROWS_COMMON: &str = include_str!("fixtures/v5-import/rows-common.sql");
const BATTLES_V1: &str = include_str!("fixtures/v5-import/battles-v1.sql");
const BATTLES_V2_V3: &str = include_str!("fixtures/v5-import/battles-v2-v3.sql");

/// `ShippedColumnShapes[3]` in the mod's `RunLogSchemaColumnShapeTests`: version 3 and
/// version 2 share it.
const V3_COLUMN_SHAPE: &str = "b3458fe8b3a4f5c5";

/// Start of the 30-day replay retention window at the fixtures' reference clock,
/// 2026-10-10T00:00:00Z.
const RETENTION_CUTOFF: &str = "2026-09-10";

const RUNS: &[&str] = &[
    "run_id",
    "started_at_utc",
    "last_seen_at_utc",
    "status",
    "completed",
    "hero",
    "game_mode",
    "seed",
    "player_rank",
    "player_rating",
    "day",
    "hour",
    "max_health",
    "prestige",
    "level",
    "income",
    "gold",
    "last_seq",
    "ended_at_utc",
    "final_day",
    "final_hour",
    "victories",
    "losses",
    "final_player_rank",
    "final_player_rating",
    "final_player_rating_delta",
    "reason",
    "build_channel",
    "player_account_id",
    "bundle_screenshot_requested",
    "mod_version",
];
const RUN_EVENTS: &[&str] = &["run_id", "seq", "ts_utc", "kind", "payload_json"];
const BATTLES_V1_COLUMNS: &[&str] = &[
    "battle_id",
    "remote_battle_id",
    "uploader_account_id",
    "source",
    "run_id",
    "local_player_account_id",
    "recorded_at_utc",
    "day",
    "hour",
    "encounter_id",
    "combat_kind",
    "player_name",
    "player_account_id",
    "player_hero",
    "player_rank",
    "player_rating",
    "player_level",
    "player_prestige",
    "player_income",
    "player_gold",
    "player_victories",
    "player_hand_item_count",
    "player_skill_count",
    "opponent_name",
    "opponent_account_id",
    "opponent_hero",
    "opponent_rank",
    "opponent_rating",
    "opponent_level",
    "opponent_prestige",
    "opponent_victories",
    "opponent_hand_item_count",
    "opponent_skill_count",
    "result",
    "winner_combatant_id",
    "loser_combatant_id",
    "is_final_battle",
    "has_local_payload",
    "bundle_id",
    "download_url",
    "download_url_expires_at_ms",
    "ghost_replay_state",
    "ghost_replay_unavailable_reason",
    "deleted_at_utc",
];
const BATTLES_LIFECYCLE: &[&str] = &["local_payload_state", "local_payload_maintenance_at_utc"];
const BATTLE_SNAPSHOTS: &[&str] = &[
    "battle_id",
    "player_hand_json",
    "player_skills_json",
    "opponent_hand_json",
    "opponent_skills_json",
];
const RUN_SCREENSHOTS: &[&str] = &[
    "screenshot_id",
    "run_id",
    "hero_name",
    "battle_id",
    "capture_source",
    "is_primary",
    "image_relative_path",
    "captured_at_local",
    "captured_at_utc",
    "day",
    "player_rank",
    "player_rating",
    "player_position",
    "victories_at_capture",
    "build_channel",
];
const VIDEOS_V1_COLUMNS: &[&str] = &[
    "video_id",
    "battle_id",
    "source",
    "video_relative_path",
    "width",
    "height",
    "fps",
    "codec",
    "crf",
    "preset",
    "started_at_utc",
    "ended_at_utc",
    "duration_ms",
    "captured_frames",
    "dropped_frames",
    "file_size_bytes",
    "status",
    "error",
];
const VIDEOS_LIFECYCLE: &[&str] = &[
    "attachment_state",
    "file_state",
    "detached_at_utc",
    "missing_at_utc",
    "last_reconciled_at_utc",
];
const BUNDLE_SEAL_JOBS: &[&str] = &[
    "run_id",
    "state",
    "player_account_id",
    "screenshot_requested",
    "screenshot_state",
    "input_deadline_at_utc",
    "bundle_id",
    "created_at_ms",
    "attempts",
    "last_attempt_at_utc",
    "last_error_code",
    "last_error_detail",
];
const BUNDLE_OUTBOX: &[&str] = &[
    "bundle_id",
    "run_id",
    "file_name",
    "content_sha256_hex",
    "content_digest",
    "total_bytes",
    "has_screenshot",
    "sealed_at_utc",
    "status",
    "attempts",
    "last_attempt_at_utc",
    "next_attempt_at_utc",
    "failed_at_utc",
    "last_error_code",
    "last_error_detail",
    "server_request_id",
    "server_outcome",
    "uploaded_at_utc",
];

struct Fixture {
    name: &'static str,
    schema: &'static str,
    battles: &'static str,
    user_version: i64,
    lifecycle_columns: bool,
}

const FIXTURES: &[Fixture] = &[
    Fixture {
        name: "v1",
        schema: V1_SCHEMA,
        battles: BATTLES_V1,
        user_version: 1,
        lifecycle_columns: false,
    },
    Fixture {
        name: "v2",
        schema: V2_SCHEMA,
        battles: BATTLES_V2_V3,
        user_version: 2,
        lifecycle_columns: true,
    },
    Fixture {
        name: "v3",
        schema: V3_SCHEMA,
        battles: BATTLES_V2_V3,
        user_version: 3,
        lifecycle_columns: true,
    },
    Fixture {
        name: "v3-upgraded-from-v1",
        schema: V3_UPGRADED_SCHEMA,
        battles: BATTLES_V2_V3,
        user_version: 3,
        lifecycle_columns: true,
    },
];

fn fixture_dir() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR")).join("tests/fixtures/v5-import")
}

fn v5_root() -> PathBuf {
    fixture_dir().join("game/BazaarPlusPlusV5")
}

fn outside_root() -> PathBuf {
    fixture_dir().join("outside")
}

fn build(schema: &str) -> Connection {
    let conn = Connection::open_in_memory().unwrap();
    conn.execute_batch(schema).unwrap();
    conn
}

/// Loads the seed rows with foreign keys on, as the mod opens the database.
fn seed(conn: &Connection, battles: &str) {
    let sql_path = |path: PathBuf| path.to_str().unwrap().replace('\'', "''");
    let substitute = |sql: &str| {
        sql.replace("{{V5_ROOT}}", &sql_path(v5_root()))
            .replace("{{OUTSIDE_ROOT}}", &sql_path(outside_root()))
    };
    conn.execute_batch("pragma foreign_keys = on;").unwrap();
    conn.execute_batch(&substitute(ROWS_COMMON)).unwrap();
    conn.execute_batch(&substitute(battles)).unwrap();
    let violations: i64 = conn
        .query_row("select count(*) from pragma_foreign_key_check", [], |row| {
            row.get(0)
        })
        .unwrap();
    assert_eq!(violations, 0);
}

fn user_version(conn: &Connection) -> i64 {
    conn.query_row("pragma user_version", [], |row| row.get(0))
        .unwrap()
}

fn tables(conn: &Connection) -> Vec<String> {
    strings(
        conn,
        "select name from sqlite_master where type = 'table' and name not like 'sqlite_%' \
         order by name",
    )
}

fn columns(conn: &Connection, table: &str) -> Vec<String> {
    strings(
        conn,
        &format!("select name from pragma_table_info('{table}') order by cid"),
    )
}

fn strings(conn: &Connection, sql: &str) -> Vec<String> {
    let mut statement = conn.prepare(sql).unwrap();
    statement
        .query_map([], |row| row.get(0))
        .unwrap()
        .collect::<Result<_, _>>()
        .unwrap()
}

fn count(conn: &Connection, sql: &str) -> i64 {
    conn.query_row(sql, [], |row| row.get(0)).unwrap()
}

fn table_sql(conn: &Connection, table: &str) -> String {
    conn.query_row(
        "select sql from sqlite_master where type = 'table' and name = ?1",
        [table],
        |row| row.get(0),
    )
    .unwrap()
}

/// The mod's `RunLogSchemaColumnShapeTests.FreshColumnShape` over an already built
/// database: name, declared type, NOT NULL, default, and primary-key position of every
/// column, table by table, hashed with SHA-256 and cut to 16 hex digits.
fn column_shape(conn: &Connection) -> String {
    let mut shape = String::new();
    for table in tables(conn) {
        shape.push_str(&table);
        shape.push('(');
        let mut statement = conn
            .prepare(&format!("pragma table_info({table})"))
            .unwrap();
        let mut rows = statement.query([]).unwrap();
        while let Some(row) = rows.next().unwrap() {
            let name: String = row.get(1).unwrap();
            let declared: String = row.get(2).unwrap();
            let not_null: i64 = row.get(3).unwrap();
            let default: Option<String> = row.get(4).unwrap();
            let primary_key: i64 = row.get(5).unwrap();
            shape.push_str(&format!(
                "{name} {declared} {not_null} {} {primary_key};",
                default.as_deref().unwrap_or("-")
            ));
        }
        shape.push_str(")\n");
    }
    Sha256::digest(shape.as_bytes())[..8]
        .iter()
        .map(|byte| format!("{byte:02x}"))
        .collect()
}

fn concat(base: &[&str], extra: &[&str]) -> Vec<String> {
    base.iter().chain(extra).map(ToString::to_string).collect()
}

#[test]
fn every_fixture_builds_with_its_user_version_and_columns() {
    for fixture in FIXTURES {
        let conn = build(fixture.schema);
        assert_eq!(
            user_version(&conn),
            fixture.user_version,
            "{}",
            fixture.name
        );
        assert_eq!(
            tables(&conn),
            [
                "battle_snapshots",
                "battles",
                "bundle_outbox",
                "bundle_seal_jobs",
                "combat_replay_videos",
                "run_events",
                "run_screenshots",
                "runs",
            ],
            "{}",
            fixture.name
        );
        let (battles, videos): (&[&str], &[&str]) = if fixture.lifecycle_columns {
            (BATTLES_LIFECYCLE, VIDEOS_LIFECYCLE)
        } else {
            (&[], &[])
        };
        for (table, expected) in [
            ("runs", concat(RUNS, &[])),
            ("run_events", concat(RUN_EVENTS, &[])),
            ("battles", concat(BATTLES_V1_COLUMNS, battles)),
            ("battle_snapshots", concat(BATTLE_SNAPSHOTS, &[])),
            ("run_screenshots", concat(RUN_SCREENSHOTS, &[])),
            ("combat_replay_videos", concat(VIDEOS_V1_COLUMNS, videos)),
            ("bundle_seal_jobs", concat(BUNDLE_SEAL_JOBS, &[])),
            ("bundle_outbox", concat(BUNDLE_OUTBOX, &[])),
        ] {
            assert_eq!(columns(&conn, table), expected, "{} {table}", fixture.name);
        }
    }
}

#[test]
fn v2_and_both_v3_shapes_carry_the_shipped_version_3_column_shape() {
    for schema in [V2_SCHEMA, V3_SCHEMA, V3_UPGRADED_SCHEMA] {
        assert_eq!(column_shape(&build(schema)), V3_COLUMN_SHAPE);
    }
    assert_ne!(column_shape(&build(V1_SCHEMA)), V3_COLUMN_SHAPE);
}

#[test]
fn upgraded_v3_differs_from_fresh_v3_only_in_its_table_definitions() {
    let fresh = build(V3_SCHEMA);
    let upgraded = build(V3_UPGRADED_SCHEMA);

    // ALTER TABLE ADD COLUMN appends each lifecycle column after the last v1 column,
    // which is where a fresh v3 declares it, so the column order is the same.
    for table in tables(&fresh) {
        assert_eq!(
            columns(&fresh, &table),
            columns(&upgraded, &table),
            "{table}"
        );
    }

    // Indexes and triggers are identical.
    let others = "select type, name, sql from sqlite_master \
                  where type <> 'table' and sql is not null order by type, name";
    let dump = |conn: &Connection| -> Vec<(String, String, String)> {
        let mut statement = conn.prepare(others).unwrap();
        statement
            .query_map([], |row| Ok((row.get(0)?, row.get(1)?, row.get(2)?)))
            .unwrap()
            .collect::<Result<_, _>>()
            .unwrap()
    };
    assert_eq!(dump(&fresh), dump(&upgraded));

    // The table definitions differ: ALTER cannot add the table-level CHECKs a fresh v3
    // declares on the lifecycle columns, and appends the new definitions on one line.
    let lifecycle_check = "(source <> 'LOCAL' AND local_payload_state IS NULL)";
    let video_checks = [
        "CHECK (attachment_state IN ('attached', 'detached'))",
        "CHECK (file_state IN ('pending', 'present', 'missing', 'deleted'))",
    ];
    let fresh_battles = table_sql(&fresh, "battles");
    let upgraded_battles = table_sql(&upgraded, "battles");
    assert!(fresh_battles.contains(lifecycle_check));
    assert!(!upgraded_battles.contains(lifecycle_check));
    assert!(upgraded_battles.contains(
        "deleted_at_utc TEXT NULL, local_payload_state TEXT NULL, \
         local_payload_maintenance_at_utc TEXT NULL,"
    ));
    let fresh_videos = table_sql(&fresh, "combat_replay_videos");
    let upgraded_videos = table_sql(&upgraded, "combat_replay_videos");
    for check in video_checks {
        assert!(fresh_videos.contains(check), "{check}");
        assert!(!upgraded_videos.contains(check), "{check}");
    }
    for table in tables(&fresh) {
        let differs = table_sql(&fresh, &table) != table_sql(&upgraded, &table);
        assert_eq!(
            differs,
            table == "battles" || table == "combat_replay_videos",
            "{table}"
        );
    }

    // Only the upgraded shape stores a video state outside the CHECK lists. Battles stay
    // guarded in both by the lifecycle triggers.
    let invalid_video = "insert into combat_replay_videos (
            video_id, battle_id, source, video_relative_path, width, height, fps, codec,
            started_at_utc, status, attachment_state, file_state
        ) values ('v', 'b', 'LocalSaved', 'v.mp4', 1, 1, 1, 'h264', 't', 'COMPLETED',
            'orphaned', 'unknown')";
    let error = fresh.execute(invalid_video, []).unwrap_err().to_string();
    assert!(error.contains("CHECK constraint failed"), "{error}");
    upgraded.execute(invalid_video, []).unwrap();
    let invalid_battle = "insert into battles (
            battle_id, source, run_id, recorded_at_utc, combat_kind, has_local_payload,
            local_payload_state
        ) values ('b', 'LOCAL', null, 't', 'PVPCombat', 0, 'ready')";
    for conn in [&fresh, &upgraded] {
        let error = conn.execute(invalid_battle, []).unwrap_err().to_string();
        assert!(
            error.contains("invalid local payload lifecycle state"),
            "{error}"
        );
    }
}

#[test]
fn seed_rows_load_into_every_fixture() {
    for fixture in FIXTURES {
        let conn = build(fixture.schema);
        seed(&conn, fixture.battles);
        for (table, rows) in [
            ("runs", 8),
            ("run_events", 5),
            ("battles", 9),
            ("battle_snapshots", 1),
            ("run_screenshots", 5),
            ("combat_replay_videos", 4),
            ("bundle_seal_jobs", 2),
            ("bundle_outbox", 2),
        ] {
            assert_eq!(
                count(&conn, &format!("select count(*) from {table}")),
                rows,
                "{} {table}",
                fixture.name
            );
        }
    }
}

/// Every case the import must handle is present in the rows, and every file the rows
/// name exists in the Data Root sample (or outside it, where the row says so).
#[test]
fn seed_rows_cover_the_import_cases_and_match_the_data_root_sample() {
    let conn = build(V3_SCHEMA);
    seed(&conn, BATTLES_V2_V3);
    let root = v5_root();
    let outside = outside_root();

    // Several runs share one screenshot file and one video file.
    assert_eq!(
        count(
            &conn,
            "select count(*) from (select image_relative_path from run_screenshots \
             group by 1 having count(distinct run_id) > 1)"
        ),
        1
    );
    assert_eq!(
        count(
            &conn,
            "select count(*) from (select v.video_relative_path from combat_replay_videos v \
             join battles b using (battle_id) group by 1 having count(distinct b.run_id) > 1)"
        ),
        1
    );

    // Every screenshot and video resolves to a sample file; absolute paths point once
    // inside the V5 root and once outside it, for images and for videos alike.
    for (sql, directory) in [
        (
            "select image_relative_path from run_screenshots",
            "Screenshots",
        ),
        (
            "select video_relative_path from combat_replay_videos",
            "CombatReplayVideos",
        ),
    ] {
        let paths = strings(&conn, sql);
        let mut inside = 0;
        let mut beyond = 0;
        for raw in &paths {
            let path = PathBuf::from(raw);
            let resolved = if path.is_absolute() {
                if path.starts_with(&root) {
                    inside += 1;
                } else {
                    assert!(path.starts_with(&outside), "{raw}");
                    beyond += 1;
                }
                path
            } else {
                raw.split(['/', '\\'])
                    .fold(root.join(directory), |dir, part| dir.join(part))
            };
            assert!(resolved.is_file(), "{raw} -> {}", resolved.display());
        }
        assert_eq!((inside, beyond), (1, 1), "{sql}");
    }

    // A game run id and its ':bpp:' collision id.
    assert_eq!(
        count(
            &conn,
            "select count(*) from runs c join runs r \
             on substr(c.run_id, 1, length(r.run_id) + 5) = r.run_id || ':bpp:' \
             where length(c.run_id) = length(r.run_id) + 37"
        ),
        1
    );
    assert_eq!(
        count(&conn, "select count(*) from runs where completed = 0"),
        1
    );
    assert_eq!(
        count(&conn, "select count(*) from runs where hero = 'hero8'"),
        1
    );

    // Ghost battles: one downloaded, one only listed remotely.
    assert_eq!(
        strings(
            &conn,
            "select ghost_replay_state from battles where source = 'GHOST' order by 1"
        ),
        ["local_ready", "remote_available"]
    );
    for battle_id in strings(
        &conn,
        "select battle_id from battles where ghost_replay_state = 'local_ready'",
    ) {
        let payload = root
            .join("GhostBattlePayloads")
            .join(format!("{battle_id}.ghost.mpack.gz"));
        assert!(payload.is_file(), "{}", payload.display());
    }

    // Outbox: one uploaded, one pending whose Bundle file is still queued.
    assert_eq!(
        strings(&conn, "select status from bundle_outbox order by 1"),
        ["pending", "uploaded"]
    );
    for file_name in strings(
        &conn,
        "select file_name from bundle_outbox where status = 'pending'",
    ) {
        assert!(root.join("BundleOutbox").join(&file_name).is_file());
    }

    // Seal jobs: one deadline in the old ISO "o" text, one in SQLite datetime() text.
    assert_eq!(
        strings(
            &conn,
            "select input_deadline_at_utc from bundle_seal_jobs order by run_id"
        ),
        ["2026-10-05T13:20:00.0000000+00:00", "2026-10-05 14:18:00"]
    );

    // Local replay payloads on disk, inside and outside the retention window.
    let ready = strings(
        &conn,
        "select battle_id from battles where source = 'LOCAL' and has_local_payload = 1",
    );
    for battle_id in &ready {
        let payload = root
            .join("CombatReplays")
            .join(format!("{battle_id}.payload.mpack.gz"));
        assert!(payload.is_file(), "{}", payload.display());
    }
    let before_cutoff = format!(
        "select count(*) from battles where source = 'LOCAL' and has_local_payload = 1 \
         and recorded_at_utc < '{RETENTION_CUTOFF}'"
    );
    assert_eq!(count(&conn, &before_cutoff), 1);
    assert!(ready.len() > 1);

    // Remote caches the mod keeps in the Data Root.
    for cache in [
        "builds.json",
        "supporter-list.json",
        "voice-lines.json",
        "EncounterPreview/preview-plans.json",
    ] {
        assert!(root.join(cache).is_file(), "{cache}");
    }
}

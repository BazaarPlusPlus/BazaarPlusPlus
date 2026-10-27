//! End-to-end import tests over a copy of the frozen V5 fixtures
//! (`tests/fixtures/v5-import/`), with an injected game probe and placement
//! backend for the failures a real system cannot be driven into on demand.
//! The headless goldens in `tests/v5_import_cli.rs` cover the successful shapes.
//! `ipc_golden_tests.rs` reuses [`v3_game`] and [`import_copying`] for the
//! `import_v5_history` DTO goldens.

use anyhow::anyhow;
use std::cell::Cell;
use std::io;
use std::path::{Path, PathBuf};

use rusqlite::Connection;
use tempfile::TempDir;

use super::run::{import_blocking_with, ImportReport};
use super::{v5_import_marker_path, ImportError};
use crate::services::file_manifest::tree_manifest;
use crate::services::placement::{LinkKind, PlacementBackend, SystemBackend};

const FIXTURES: &str = concat!(env!("CARGO_MANIFEST_DIR"), "/tests/fixtures/v5-import");
const V1_SCHEMA: &str = include_str!("../../tests/fixtures/v5-import/v1.schema.sql");
const V3_SCHEMA: &str = include_str!("../../tests/fixtures/v5-import/v3.schema.sql");
const V3_UPGRADED_SCHEMA: &str =
    include_str!("../../tests/fixtures/v5-import/v3-upgraded-from-v1.schema.sql");
const ROWS_COMMON: &str = include_str!("../../tests/fixtures/v5-import/rows-common.sql");
const BATTLES_V1: &str = include_str!("../../tests/fixtures/v5-import/battles-v1.sql");
const BATTLES_V2_V3: &str = include_str!("../../tests/fixtures/v5-import/battles-v2-v3.sql");
const MOD_CONTRACT: &str = include_str!(
    "../../../../bazaarplusplus-mod/src/BazaarPlusPlus.Storage/BazaarPlusPlus.history-database.json"
);
/// The fixtures' reference clock, 2026-10-10T00:00:00Z.
const NOW_MS: i64 = 1_791_590_400_000;

pub(crate) struct FixtureGame {
    _dir: TempDir,
    pub(crate) game: PathBuf,
}

impl FixtureGame {
    fn v5(&self) -> PathBuf {
        self.game.join("BazaarPlusPlusV5")
    }

    fn v6(&self) -> PathBuf {
        crate::services::paths::bpp_data_dir(&self.game)
    }

    fn snapshot(&self) -> Vec<(String, String)> {
        tree_manifest(&self.game).unwrap()
    }

    fn import(&self) -> Result<ImportReport, ImportError> {
        import_blocking_with(&self.game, &stopped, SystemBackend, NOW_MS)
    }
}

fn stopped(_: &Path) -> anyhow::Result<bool> {
    Ok(false)
}

fn copy_tree(source: &Path, target: &Path) {
    std::fs::create_dir_all(target).unwrap();
    for entry in std::fs::read_dir(source).unwrap() {
        let entry = entry.unwrap();
        let to = target.join(entry.file_name());
        if entry.file_type().unwrap().is_dir() {
            copy_tree(&entry.path(), &to);
        } else {
            std::fs::copy(entry.path(), to).unwrap();
        }
    }
}

fn write(path: &Path, bytes: &[u8]) {
    std::fs::create_dir_all(path.parent().unwrap()).unwrap();
    std::fs::write(path, bytes).unwrap();
}

/// A game directory with the installed 6.x mod contract and the V5 Data Root
/// sample, its database built from `schema` and the seed rows.
pub(super) fn fixture_game(schema: &str, battles: &str) -> FixtureGame {
    let dir = tempfile::tempdir().unwrap();
    let root = dir.path().canonicalize().unwrap();
    let game = root.join("game");
    copy_tree(&Path::new(FIXTURES).join("game"), &game);
    copy_tree(&Path::new(FIXTURES).join("outside"), &root.join("outside"));
    #[cfg(target_os = "macos")]
    std::fs::create_dir_all(game.join("TheBazaar.app")).unwrap();
    #[cfg(target_os = "windows")]
    write(&game.join("TheBazaar.exe"), b"exe");
    #[cfg(not(any(target_os = "macos", target_os = "windows")))]
    write(&game.join("TheBazaar"), b"exe");
    write(
        &game.join("BepInEx/plugins/BazaarPlusPlus.history-database.json"),
        MOD_CONTRACT.as_bytes(),
    );
    let sql_path = |path: PathBuf| path.to_str().unwrap().replace('\'', "''");
    let substitute = |sql: &str| {
        sql.replace("{{V5_ROOT}}", &sql_path(game.join("BazaarPlusPlusV5")))
            .replace("{{OUTSIDE_ROOT}}", &sql_path(root.join("outside")))
    };
    let conn = Connection::open(game.join("BazaarPlusPlusV5/bazaarplusplus.db")).unwrap();
    conn.execute_batch(schema).unwrap();
    conn.execute_batch(&substitute(ROWS_COMMON)).unwrap();
    conn.execute_batch(&substitute(battles)).unwrap();
    drop(conn);
    FixtureGame { _dir: dir, game }
}

pub(crate) fn v3_game() -> FixtureGame {
    fixture_game(V3_SCHEMA, BATTLES_V2_V3)
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

/// The V6 database a successful import leaves.
fn v6_conn(fixture: &FixtureGame) -> Connection {
    Connection::open(fixture.v6().join("bazaarplusplus.db")).unwrap()
}

#[test]
fn a_new_current_database_is_the_mods_schema_in_wal_mode_with_history_only_runs() {
    for (schema, battles) in [
        (V1_SCHEMA, BATTLES_V1),
        (V3_SCHEMA, BATTLES_V2_V3),
        (V3_UPGRADED_SCHEMA, BATTLES_V2_V3),
    ] {
        let fixture = fixture_game(schema, battles);
        let v5_before = tree_manifest(&fixture.v5()).unwrap();
        let report = fixture.import().unwrap();

        let conn = v6_conn(&fixture);
        // The file the mod's EnsureInitialized creates, so it accepts this one.
        assert_eq!(
            crate::history::mod_schema::dump_schema(&conn),
            crate::history::mod_schema::mod_schema_sql()
        );
        let mode: String = conn
            .query_row("pragma journal_mode", [], |row| row.get(0))
            .unwrap();
        assert_eq!(mode, "wal");

        // Shared seal eligibility: no imported Run is a seal candidate or
        // protected from cleanup, and none can be resumed.
        assert_eq!(
            count(
                &conn,
                "select count(*) from runs where upload_policy <> 'history_only'"
            ),
            0
        );
        let protected = format!(
            "select count(*) from runs r where {}",
            crate::history::cleanup::PROTECTED_RUN_PREDICATE
        );
        assert_eq!(count(&conn, &protected), 0);
        assert_eq!(
            count(&conn, "select count(*) from runs where status = 'active'"),
            0
        );
        assert_eq!(report.plan.closed_runs, ["run-foxtrot"]);
        assert_eq!(
            count(
                &conn,
                "select count(*) from runs where run_id = 'run-foxtrot' \
                 and status = 'abandoned' and ended_at_ms = last_seen_at_ms"
            ),
            1
        );

        // No Ghost battle, Bundle outbox row, seal job, or Ghost payload.
        assert_eq!(
            count(
                &conn,
                "select count(*) from battles where source <> 'LOCAL'"
            ),
            0
        );
        assert_eq!(count(&conn, "select count(*) from bundle_outbox"), 0);
        assert_eq!(count(&conn, "select count(*) from bundle_seal_jobs"), 0);
        for absent in ["GhostBattlePayloads", "BundleOutbox", "builds.json"] {
            assert!(!fixture.v6().join(absent).exists(), "{absent}");
        }

        // Run ids, collision ids included, are copied verbatim. V5's legacy
        // `hero8` is stored as `TheDragons`, as the mod writes it, so the
        // History hero filter finds the Run.
        assert_eq!(
            strings(&conn, "select run_id from runs order by run_id"),
            [
                "run-alpha",
                "run-bravo",
                "run-charlie",
                "run-delta",
                "run-echo",
                "run-echo:bpp:0f1e2d3c4b5a69788796a5b4c3d2e1f0",
                "run-foxtrot",
                "run-golf",
            ]
        );
        assert_eq!(
            strings(&conn, "select hero from runs where run_id = 'run-golf'"),
            ["TheDragons"]
        );
        assert_eq!(
            strings(
                &conn,
                "select player_hero from battles where run_id = 'run-golf'"
            ),
            ["TheDragons"]
        );

        // An absolute path inside the V5 root becomes relative; one outside
        // it is neither copied nor referenced.
        assert_eq!(
            strings(
                &conn,
                "select image_relative_path from run_screenshots where screenshot_id = 'shot-charlie'"
            ),
            ["2026-10-03/2026-10-03_21-21-00-000_final_run-run-charlie.png"]
        );
        assert_eq!(
            strings(
                &conn,
                "select video_relative_path from combat_replay_videos where video_id = 'video-charlie-1'"
            ),
            ["battle-charlie-1.20261003-211000.rec0003.mp4"]
        );
        assert_eq!(
            count(
                &conn,
                "select count(*) from run_screenshots where screenshot_id = 'shot-delta'"
            ),
            0
        );
        assert_eq!(
            report
                .plan
                .dropped_references
                .iter()
                .map(|reference| (reference.id.as_str(), reference.reason))
                .collect::<Vec<_>>(),
            [
                ("video-delta-1", "outside_data_root"),
                ("shot-delta", "outside_data_root")
            ]
        );
        assert!(!fixture.v6().join("Pictures").exists());

        // V5 is untouched except for the marker.
        let mut v5_after = tree_manifest(&fixture.v5()).unwrap();
        v5_after.retain(|(path, _)| path != super::V5_IMPORT_MARKER_FILE_NAME);
        assert_eq!(v5_after, v5_before);
        assert!(v5_import_marker_path(&fixture.game).is_file());
    }
}

#[test]
fn the_installed_mods_retention_decides_which_replays_are_copied() {
    let fixture = v3_game();
    write(
        &fixture
            .game
            .join("BepInEx/plugins/BazaarPlusPlus.history-database.json"),
        br#"{"formatVersion":1,"dataRootDirectoryName":"BazaarPlusPlusV6","historyDatabaseUserVersion":1,"replayPayloadRetainedNewestCount":2,"replayPayloadRetainedAgeDays":30}"#,
    );
    let report = fixture.import().unwrap();

    // Newest two (foxtrot, echo) plus the 30-day window (bravo, alpha); golf is
    // older than both.
    assert_eq!(report.plan.evicted_replays, ["battle-golf-1"]);
    let conn = v6_conn(&fixture);
    assert_eq!(
        strings(
            &conn,
            "select battle_id || ':' || local_payload_state || ':' || \
             coalesce(local_payload_maintenance_at_ms, '') from battles order by battle_id"
        ),
        [
            "battle-alpha-1:ready:",
            "battle-bravo-1:ready:",
            "battle-charlie-1:evicted:1791072000000",
            "battle-delta-1:missing:",
            "battle-echo-1:ready:",
            "battle-foxtrot-1:ready:",
            &format!("battle-golf-1:evicted:{NOW_MS}"),
        ]
    );
    let replays = fixture.v6().join("CombatReplays");
    assert!(!replays.join("battle-golf-1.payload.mpack.gz").exists());
    assert!(replays.join("battle-alpha-1.payload.mpack.gz").is_file());
}

#[test]
fn a_game_started_before_the_commit_rolls_everything_back() {
    for existing_v6 in [false, true] {
        let fixture = v3_game();
        if existing_v6 {
            std::fs::create_dir_all(fixture.v6()).unwrap();
            let conn = v6_conn(&fixture);
            crate::history::mod_schema::execute_mod_schema(&conn).unwrap();
            conn.query_row("pragma journal_mode = wal", [], |_| Ok(()))
                .unwrap();
            drop(conn);
            write(&fixture.v6().join("Screenshots/keep.png"), b"keep");
        }
        let before = fixture.snapshot();
        let checks = Cell::new(0);
        let probe = |_: &Path| {
            checks.set(checks.get() + 1);
            Ok(checks.get() > 1)
        };

        let error = import_blocking_with(&fixture.game, &probe, SystemBackend, NOW_MS).unwrap_err();

        assert!(
            matches!(
                error,
                ImportError::GameRunning {
                    stage: "before_commit",
                    ..
                }
            ),
            "{error:?}"
        );
        assert_eq!(error.code(), "import_blocked_by_game");
        assert_eq!(checks.get(), 2);
        assert_eq!(fixture.snapshot(), before, "existing V6: {existing_v6}");
        assert_eq!(fixture.v6().exists(), existing_v6);
    }
}

#[test]
fn a_game_running_at_the_start_changes_nothing() {
    let fixture = v3_game();
    let before = fixture.snapshot();
    let error = import_blocking_with(&fixture.game, &|_: &Path| Ok(true), SystemBackend, NOW_MS)
        .unwrap_err();
    assert!(matches!(
        error,
        ImportError::GameRunning { stage: "start", .. }
    ));
    let error = import_blocking_with(
        &fixture.game,
        &|_: &Path| Err(anyhow!("ps failed")),
        SystemBackend,
        NOW_MS,
    )
    .unwrap_err();
    assert!(matches!(
        error,
        ImportError::GameRunning { stage: "start", .. }
    ));
    assert_eq!(fixture.snapshot(), before);
}

/// Imports `fixture` with `probe`, copying every file so the result does not
/// depend on whether the platform links or clones.
pub(crate) fn import_copying(
    fixture: &FixtureGame,
    probe: &impl crate::services::game_process::GameProcessProbe,
) -> Result<ImportReport, ImportError> {
    import_blocking_with(
        &fixture.game,
        probe,
        CopyOnlyBackend {
            available: u64::MAX,
        },
        NOW_MS,
    )
}

/// The real filesystem on a volume that cannot link and has `available` bytes free.
struct CopyOnlyBackend {
    available: u64,
}

impl PlacementBackend for CopyOnlyBackend {
    fn volume_id(&self, existing: &Path) -> io::Result<String> {
        SystemBackend.volume_id(existing)
    }

    fn available_space(&self, _: &Path) -> io::Result<u64> {
        Ok(self.available)
    }

    fn link(&self, _: &Path, _: &Path) -> io::Result<LinkKind> {
        Err(io::Error::other("links unsupported"))
    }

    fn copy(&self, source: &Path, partial: &Path) -> io::Result<()> {
        SystemBackend.copy(source, partial)
    }
}

#[test]
fn insufficient_space_fails_before_anything_is_kept() {
    let fixture = v3_game();
    let before = fixture.snapshot();

    let error = import_blocking_with(
        &fixture.game,
        &stopped,
        CopyOnlyBackend { available: 10 },
        NOW_MS,
    )
    .unwrap_err();

    assert_eq!(error.code(), "insufficient_space", "{error}");
    assert_eq!(fixture.snapshot(), before);

    // With room, the same volume copies instead of linking.
    import_blocking_with(
        &fixture.game,
        &stopped,
        CopyOnlyBackend {
            available: u64::MAX,
        },
        NOW_MS,
    )
    .unwrap();
}

#[test]
fn a_retry_after_an_interrupted_import_completes() {
    let reference = v3_game();
    reference.import().unwrap();
    let expected = super::manifest::manifest_json(&reference.game, &reference.import())
        .unwrap()
        .replace("already_imported", "_");

    // An import killed mid-way: the new database file exists but its
    // transaction never committed, and some files were already placed.
    let fixture = v3_game();
    std::fs::create_dir_all(fixture.v6()).unwrap();
    Connection::open(fixture.v6().join("bazaarplusplus.db"))
        .unwrap()
        .query_row("pragma journal_mode = wal", [], |_| Ok(()))
        .unwrap();
    let placed = "Screenshots/2026-10-01/2026-10-01_20-15-04-120_final_run-run-alpha.png";
    copy_tree(
        &fixture.v5().join("Screenshots"),
        &fixture.v6().join("Screenshots"),
    );
    assert!(fixture.v6().join(placed).is_file());

    fixture.import().unwrap();
    let actual = super::manifest::manifest_json(&fixture.game, &fixture.import())
        .unwrap()
        .replace("already_imported", "_");
    assert_eq!(actual, expected);
}

#[test]
fn an_unclean_wal_without_shm_is_read_and_left_as_it_was() {
    let fixture = v3_game();
    let database = fixture.v5().join("bazaarplusplus.db");
    // Write a Run into the WAL of a copy and abandon the connection without a
    // checkpoint, as a crashed game would; then keep only the db and -wal.
    let staging = tempfile::tempdir().unwrap();
    let staged = staging.path().join("bazaarplusplus.db");
    std::fs::copy(&database, &staged).unwrap();
    let conn = Connection::open(&staged).unwrap();
    conn.query_row("pragma journal_mode = wal", [], |_| Ok(()))
        .unwrap();
    conn.execute_batch(
        "pragma wal_autocheckpoint = 0;
         insert into runs (run_id, started_at_utc, last_seen_at_utc, status, completed, hero,
             game_mode, ended_at_utc)
         values ('run-in-wal', '2026-10-07T13:00:00.0000000+00:00',
             '2026-10-07T13:20:00.0000000+00:00', 'completed', 1, 'Mak', 'Ranked',
             '2026-10-07T13:20:00.0000000+00:00');",
    )
    .unwrap();
    std::fs::copy(&staged, &database).unwrap();
    std::fs::copy(
        staged.with_file_name("bazaarplusplus.db-wal"),
        database.with_file_name("bazaarplusplus.db-wal"),
    )
    .unwrap();
    drop(conn);
    assert!(
        std::fs::metadata(database.with_file_name("bazaarplusplus.db-wal"))
            .unwrap()
            .len()
            > 0
    );
    assert!(!database.with_file_name("bazaarplusplus.db-shm").exists());
    let v5_before = tree_manifest(&fixture.v5()).unwrap();

    let report = fixture.import().unwrap();

    assert!(report
        .plan
        .imported_runs
        .iter()
        .any(|run| run == "run-in-wal"));
    let mut v5_after = tree_manifest(&fixture.v5()).unwrap();
    v5_after.retain(|(path, _)| path != super::V5_IMPORT_MARKER_FILE_NAME);
    assert_eq!(v5_after, v5_before);
}

#[test]
fn an_unknown_v5_shape_is_refused_without_changes() {
    for (sql, code) in [
        ("pragma user_version = 4;", "v5_schema_unsupported"),
        ("pragma user_version = 0;", "v5_schema_unsupported"),
        (
            "alter table run_screenshots drop column captured_at_local;",
            "v5_schema_missing_column",
        ),
        ("drop table runs;", "v5_schema_missing_column"),
    ] {
        let fixture = v3_game();
        let conn = Connection::open(fixture.v5().join("bazaarplusplus.db")).unwrap();
        conn.execute_batch(&format!("pragma foreign_keys = off; {sql}"))
            .unwrap();
        drop(conn);
        let before = fixture.snapshot();

        let error = fixture.import().unwrap_err();

        assert_eq!(error.code(), code, "{sql}: {error}");
        assert_eq!(fixture.snapshot(), before, "{sql}");
    }
}

#[test]
fn an_unreadable_instant_fails_the_import_without_changes() {
    let fixture = v3_game();
    let conn = Connection::open(fixture.v5().join("bazaarplusplus.db")).unwrap();
    conn.execute(
        "update battles set recorded_at_utc = 'soon' where battle_id = 'battle-echo-1'",
        [],
    )
    .unwrap();
    drop(conn);
    let before = fixture.snapshot();

    let error = fixture.import().unwrap_err();

    assert_eq!(error.code(), "v5_value_invalid");
    assert!(error.to_string().contains("battle-echo-1"), "{error}");
    assert_eq!(fixture.snapshot(), before);
}

#[test]
fn an_existing_marker_means_nothing_is_done() {
    let fixture = v3_game();
    write(&v5_import_marker_path(&fixture.game), b"");
    let before = fixture.snapshot();

    assert!(matches!(
        fixture.import().unwrap_err(),
        ImportError::AlreadyImported
    ));
    assert_eq!(fixture.snapshot(), before);

    // A completed import is not repeated.
    let fixture = v3_game();
    fixture.import().unwrap();
    let after_first = fixture.snapshot();
    assert!(matches!(
        fixture.import().unwrap_err(),
        ImportError::AlreadyImported
    ));
    assert_eq!(fixture.snapshot(), after_first);
}

#[test]
fn an_ineligible_game_is_refused_without_changes() {
    // A 5.x mod is still installed: it would keep writing V5.
    let fixture = v3_game();
    write(
        &fixture
            .game
            .join("BepInEx/plugins/BazaarPlusPlus.history-database.json"),
        br#"{"formatVersion":1,"historyDatabaseUserVersion":3}"#,
    );
    let before = fixture.snapshot();
    let error = fixture.import().unwrap_err();
    assert_eq!(error.code(), "not_eligible");
    assert!(error.to_string().contains("installed_mod_writes_old_root"));
    assert_eq!(fixture.snapshot(), before);

    // No V5 Data Root.
    let fixture = v3_game();
    std::fs::remove_dir_all(fixture.v5()).unwrap();
    let before = fixture.snapshot();
    let error = fixture.import().unwrap_err();
    assert!(error.to_string().contains("no_v5_data_root"), "{error}");
    assert_eq!(fixture.snapshot(), before);

    // A 6.x mod contract without the replay retention.
    let fixture = v3_game();
    write(
        &fixture
            .game
            .join("BepInEx/plugins/BazaarPlusPlus.history-database.json"),
        br#"{"formatVersion":1,"dataRootDirectoryName":"BazaarPlusPlusV6","historyDatabaseUserVersion":1}"#,
    );
    let before = fixture.snapshot();
    assert_eq!(fixture.import().unwrap_err().code(), "mod_contract_invalid");
    assert_eq!(fixture.snapshot(), before);
}

#[tokio::test]
async fn history_cleanup_started_during_an_import_waits_for_it() {
    use crate::services::data_maintenance::DataMaintenanceLock;
    use crate::services::history::{
        execute_storage_cleanup_with, StorageCleanupPreset, StorageCleanupScope,
    };
    use std::sync::mpsc;
    use std::time::Duration;

    let lock = DataMaintenanceLock::default();
    let fixture = std::sync::Arc::new(v3_game());

    // The import's first game check parks inside the locked section.
    let (entered_tx, entered) = mpsc::channel::<()>();
    let (release_tx, release) = mpsc::channel::<()>();
    let import = tokio::spawn({
        let lock = lock.clone();
        let fixture = fixture.clone();
        async move {
            lock.run_blocking(move || {
                let release = std::sync::Mutex::new(release);
                let probe = |_: &Path| {
                    if entered_tx.send(()).is_ok() {
                        release.lock().unwrap().recv().unwrap();
                    }
                    Ok(false)
                };
                import_blocking_with(&fixture.game, &probe, SystemBackend, NOW_MS)
                    .map(|report| report.plan.imported_runs.len())
                    .map_err(anyhow::Error::from)
            })
            .await
        }
    });
    tokio::task::spawn_blocking(move || entered.recv_timeout(Duration::from_secs(10)))
        .await
        .unwrap()
        .expect("import reached its game check");

    // Cleanup resolves its installation only once it holds the lock.
    let (resolved_tx, resolved) = mpsc::channel::<bool>();
    let cleanup = tokio::spawn({
        let lock = lock.clone();
        let fixture = fixture.clone();
        async move {
            let game = fixture.game.clone();
            let database = fixture.v6().join("bazaarplusplus.db");
            execute_storage_cleanup_with(
                &lock,
                Default::default(),
                move || {
                    resolved_tx.send(database.exists()).unwrap();
                    Some(game)
                },
                StorageCleanupScope::RunData,
                StorageCleanupPreset::All,
                0,
            )
            .await
        }
    });
    for _ in 0..16 {
        tokio::task::yield_now().await;
    }
    tokio::time::sleep(Duration::from_millis(50)).await;
    assert!(!cleanup.is_finished());
    assert!(
        resolved.try_recv().is_err(),
        "cleanup entered during the import"
    );

    release_tx.send(()).unwrap();
    assert_eq!(import.await.unwrap().unwrap().unwrap(), 8);
    let _ = cleanup.await.unwrap();
    // Cleanup saw the committed import.
    assert_eq!(resolved.try_recv(), Ok(true));
}

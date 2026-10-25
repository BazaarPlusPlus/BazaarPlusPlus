//! `bppinstaller import` end to end: each frozen V5 shape (`tests/fixtures/v5-import/`)
//! and a current Data Root that already holds some of its Runs, imported by the
//! built binary. The manifest it writes (outcome, Data Root files, database
//! rows) must equal `tests/goldens/v5-import/<case>.json` byte for byte;
//! `BPP_UPDATE_GOLDENS=1` rewrites the goldens. Failure modes that need an
//! injected game probe or volume run in `src/v5_import/tests_e2e.rs`.

#![expect(
    clippy::unwrap_used,
    reason = "a failed fixture setup step fails the test"
)]

use std::path::{Path, PathBuf};
use std::process::Command;

use rusqlite::Connection;
use sha2::{Digest, Sha256};

const FIXTURES: &str = concat!(env!("CARGO_MANIFEST_DIR"), "/tests/fixtures/v5-import");
const ROWS_COMMON: &str = include_str!("fixtures/v5-import/rows-common.sql");
const BATTLES_V1: &str = include_str!("fixtures/v5-import/battles-v1.sql");
const BATTLES_V2_V3: &str = include_str!("fixtures/v5-import/battles-v2-v3.sql");
const MOD_CONTRACT: &str = include_str!(
    "../../../bazaarplusplus-mod/src/BazaarPlusPlus.Storage/BazaarPlusPlus.history-database.json"
);
const MOD_SCHEMA: &str = include_str!(
    "../../../bazaarplusplus-mod/src/BazaarPlusPlus.Storage/history-database-v6.schema.sql"
);
const NOW: &str = "2026-10-10T00:00:00Z";

struct Case {
    name: &'static str,
    schema: &'static str,
    battles: &'static str,
}

const V1: Case = Case {
    name: "v1",
    schema: include_str!("fixtures/v5-import/v1.schema.sql"),
    battles: BATTLES_V1,
};
const V2: Case = Case {
    name: "v2",
    schema: include_str!("fixtures/v5-import/v2.schema.sql"),
    battles: BATTLES_V2_V3,
};
const V3: Case = Case {
    name: "v3",
    schema: include_str!("fixtures/v5-import/v3.schema.sql"),
    battles: BATTLES_V2_V3,
};
const V3_UPGRADED: Case = Case {
    name: "v3-upgraded-from-v1",
    schema: include_str!("fixtures/v5-import/v3-upgraded-from-v1.schema.sql"),
    battles: BATTLES_V2_V3,
};

struct Game {
    _dir: tempfile::TempDir,
    root: PathBuf,
    game: PathBuf,
}

impl Game {
    fn v5(&self) -> PathBuf {
        self.game.join("BazaarPlusPlusV5")
    }

    fn v6(&self) -> PathBuf {
        self.game.join("BazaarPlusPlusV6")
    }
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
/// sample; `outside/` sits beside it.
fn game(case: &Case) -> Game {
    let dir = tempfile::tempdir().unwrap();
    let root = dir.path().canonicalize().unwrap();
    let game = root.join("game");
    copy_tree(&Path::new(FIXTURES).join("game"), &game);
    copy_tree(&Path::new(FIXTURES).join("outside"), &root.join("outside"));
    if cfg!(target_os = "macos") {
        std::fs::create_dir_all(game.join("TheBazaar.app")).unwrap();
    } else if cfg!(windows) {
        write(&game.join("TheBazaar.exe"), b"exe");
    } else {
        write(&game.join("TheBazaar"), b"exe");
    }
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
    conn.execute_batch(case.schema).unwrap();
    conn.execute_batch(&substitute(ROWS_COMMON)).unwrap();
    conn.execute_batch(&substitute(case.battles)).unwrap();
    drop(conn);
    Game {
        _dir: dir,
        root,
        game,
    }
}

/// Every file under `root` with the sha256 of its bytes.
fn tree(root: &Path) -> Vec<(String, String)> {
    fn walk(root: &Path, directory: &Path, entries: &mut Vec<(String, String)>) {
        for entry in std::fs::read_dir(directory).unwrap() {
            let path = entry.unwrap().path();
            if path.is_dir() {
                walk(root, &path, entries);
            } else {
                let relative = path.strip_prefix(root).unwrap().to_string_lossy();
                let digest: String = Sha256::digest(std::fs::read(&path).unwrap())
                    .iter()
                    .map(|byte| format!("{byte:02x}"))
                    .collect();
                entries.push((relative.replace('\\', "/"), digest));
            }
        }
    }
    let mut entries = Vec::new();
    walk(root, root, &mut entries);
    entries.sort();
    entries
}

struct Outcome {
    success: bool,
    stderr: String,
    manifest: String,
}

fn import(game: &Game) -> Outcome {
    let manifest = game.root.join("manifest.json");
    let output = Command::new(env!("CARGO_BIN_EXE_bppinstaller"))
        .args(["import", "--game"])
        .arg(&game.game)
        .arg("--manifest")
        .arg(&manifest)
        .args(["--now", NOW])
        .output()
        .unwrap();
    let mut text = std::fs::read_to_string(&manifest).unwrap();
    for (path, placeholder) in [
        (game.v5(), "{{V5_ROOT}}"),
        (game.root.join("outside"), "{{OUTSIDE_ROOT}}"),
        (game.game.clone(), "{{GAME}}"),
    ] {
        let raw = path.to_string_lossy();
        let escaped = serde_json::to_string(raw.as_ref()).unwrap();
        text = text
            .replace(&escaped[1..escaped.len() - 1], placeholder)
            .replace(raw.as_ref(), placeholder);
    }
    Outcome {
        success: output.status.success(),
        stderr: String::from_utf8_lossy(&output.stderr).into_owned(),
        manifest: text,
    }
}

fn assert_golden(name: &str, actual: &str) {
    let path = Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("tests/goldens/v5-import")
        .join(format!("{name}.json"));
    if std::env::var("BPP_UPDATE_GOLDENS").as_deref() == Ok("1") {
        std::fs::create_dir_all(path.parent().unwrap()).unwrap();
        std::fs::write(&path, actual).unwrap();
        return;
    }
    let expected = std::fs::read_to_string(&path).unwrap_or_else(|error| {
        panic!(
            "read golden {}: {error}; regenerate with BPP_UPDATE_GOLDENS=1",
            path.display()
        )
    });
    assert!(
        expected == actual,
        "golden {} differs; review and regenerate with BPP_UPDATE_GOLDENS=1\n--- expected\n{expected}\n--- actual\n{actual}",
        path.display()
    );
}

fn assert_imports_to_golden(case: &Case) {
    let game = game(case);
    let v5_before = tree(&game.v5());

    let outcome = import(&game);

    assert!(outcome.success, "{}", outcome.stderr);
    assert_golden(case.name, &outcome.manifest);
    let mut v5_after = tree(&game.v5());
    v5_after.retain(|(path, _)| path != "imported-to-BazaarPlusPlusV6");
    assert_eq!(v5_after, v5_before, "V5 changed beyond the marker");
}

#[test]
fn imports_a_v1_database() {
    assert_imports_to_golden(&V1);
}

#[test]
fn imports_a_v2_database() {
    assert_imports_to_golden(&V2);
}

#[test]
fn imports_a_fresh_v3_database() {
    assert_imports_to_golden(&V3);
}

#[test]
fn imports_a_v3_database_upgraded_from_v1() {
    assert_imports_to_golden(&V3_UPGRADED);
}

/// The game ran the 6.x mod before the import: the current Data Root holds
/// `run-alpha` with its screenshot and replay, and a Run whose battle id is
/// V5's `battle-golf-1`. Both V5 Runs are skipped with all their children;
/// `run-bravo` shares `run-alpha`'s screenshot file, which is already in place.
#[test]
fn merges_into_a_current_database_that_already_holds_runs() {
    let game = game(&V3);
    std::fs::create_dir_all(game.v6()).unwrap();
    let conn = Connection::open(game.v6().join("bazaarplusplus.db")).unwrap();
    conn.execute_batch(MOD_SCHEMA).unwrap();
    conn.query_row("pragma journal_mode = wal", [], |_| Ok(()))
        .unwrap();
    conn.execute_batch(
        "insert into runs (run_id, started_at_ms, last_seen_at_ms, status, hero, game_mode,
             ended_at_ms, build_channel, upload_policy)
         values ('run-alpha', 1790856000000, 1790856904000, 'completed', 'Vanessa', 'Ranked',
             1790856904000, 'Online', 'bundle'),
            ('run-v6', 1791504000000, 1791505200000, 'completed', 'Mak', 'Ranked',
             1791505200000, 'Online', 'bundle');
         insert into battles (battle_id, source, run_id, recorded_at_ms, combat_kind,
             local_payload_state)
         values ('battle-alpha-1', 'LOCAL', 'run-alpha', 1790856600000, 'PVPCombat', 'ready'),
            ('battle-golf-1', 'LOCAL', 'run-v6', 1791504600000, 'PVPCombat', 'ready');
         insert into run_screenshots (screenshot_id, run_id, hero_name, capture_source,
             is_primary, image_relative_path, captured_at_ms, captured_utc_offset_minutes)
         values ('shot-alpha', 'run-alpha', 'Vanessa', 'end_of_run_auto', 1,
             '2026-10-01/2026-10-01_20-15-04-120_final_run-run-alpha.png', 1790856904120, 480);",
    )
    .unwrap();
    drop(conn);
    let shot = "Screenshots/2026-10-01/2026-10-01_20-15-04-120_final_run-run-alpha.png";
    write(
        &game.v6().join(shot),
        &std::fs::read(game.v5().join(shot)).unwrap(),
    );
    for battle in ["battle-alpha-1", "battle-golf-1"] {
        let payload = format!("CombatReplays/{battle}.payload.mpack.gz");
        write(&game.v6().join(&payload), b"current payload\n");
    }

    let outcome = import(&game);

    assert!(outcome.success, "{}", outcome.stderr);
    assert_golden("v6-overlap", &outcome.manifest);
}

#[test]
fn a_refused_import_reports_failure_and_changes_nothing() {
    let game = game(&V3);
    Connection::open(game.v5().join("bazaarplusplus.db"))
        .unwrap()
        .execute_batch("pragma user_version = 7;")
        .unwrap();
    let before = tree(&game.game);

    let outcome = import(&game);

    assert!(!outcome.success);
    assert!(
        outcome.stderr.contains("v5_schema_unsupported"),
        "{}",
        outcome.stderr
    );
    assert_golden("refused-unknown-user-version", &outcome.manifest);
    assert_eq!(tree(&game.game), before);
}

#[test]
fn an_import_already_done_succeeds_without_changes() {
    let game = game(&V3);
    assert!(import(&game).success);
    let before = tree(&game.game);

    let outcome = import(&game);

    assert!(outcome.success, "{}", outcome.stderr);
    assert!(outcome
        .manifest
        .contains("\"status\": \"already_imported\""));
    assert_eq!(tree(&game.game), before);
}

#[test]
fn the_manifest_must_be_written_outside_the_game_directory() {
    let game = game(&V3);
    let output = Command::new(env!("CARGO_BIN_EXE_bppinstaller"))
        .args(["import", "--game"])
        .arg(&game.game)
        .arg("--manifest")
        .arg(game.game.join("manifest.json"))
        .output()
        .unwrap();
    assert!(!output.status.success());
    assert!(String::from_utf8_lossy(&output.stderr).contains("outside the game directory"));
    assert!(!game.v6().exists());
}

//! IPC JSON goldens: the DTOs `get_install_state`, `get_legacy_data_state`,
//! `get_legacy_data_presence`, `import_v5_history`, `delete_legacy_root`, `list_history_runs`,
//! `get_history_run_detail` and `preview_storage_cleanup` return to the frontend, produced by the service layer each command wraps from a fixture
//! game directory and database. The commands themselves are not driven through
//! the IPC bridge: through IPC, `get_install_state` and history path resolution
//! read the host's Steam installation and bundled resources (see #154), so the
//! output would not be repeatable.
//!
//! Every input is platform neutral, so macOS and Windows produce the same
//! bytes. Writes `tests/goldens/ipc/<command>.<state>.json`; regenerate with
//! `BPP_UPDATE_GOLDENS=1 npm run generate:bindings:test` (see the installer `AGENTS.md`).

use std::path::{Path, PathBuf};

use crate::goldens::{assert_golden, json};
use crate::history::mod_schema::create_mod_schema;
use crate::services::detect::InstallEnvironmentSnapshot;
use crate::services::history::{CleanupPlans, History, StorageCleanupPreset, StorageCleanupScope};
use crate::services::install::install_state_from_snapshot;
use crate::services::legacy_data;
use crate::services::paths;
use crate::services::vdf::SteamLaunchOptionsState;
use crate::stream::history_thumbnails::HistoryThumbnails;

const GAME: &str = "<GAME>";
const STEAM: &str = "<STEAM>";

struct Fixture {
    _temp: tempfile::TempDir,
    game: PathBuf,
    steam: PathBuf,
}

impl Fixture {
    fn new() -> Self {
        let temp = tempfile::tempdir().unwrap();
        let game = temp.path().join("The Bazaar");
        let steam = temp.path().join("Steam");
        std::fs::create_dir_all(&game).unwrap();
        std::fs::create_dir_all(&steam).unwrap();
        Self {
            _temp: temp,
            game,
            steam,
        }
    }

    fn json(&self, value: &impl serde::Serialize) -> String {
        json(value, &[(&self.game, GAME), (&self.steam, STEAM)])
    }
}

fn write(path: &Path, bytes: &[u8]) {
    std::fs::create_dir_all(path.parent().unwrap()).unwrap();
    std::fs::write(path, bytes).unwrap();
}

/// Detection facts for the fixture installation. The macOS bootstrap inputs
/// are the ready ones (empty launch options, current trampoline), so the
/// platform-specific warnings stay out of every state.
fn snapshot(fixture: &Fixture, installed_version: Option<&str>) -> InstallEnvironmentSnapshot {
    InstallEnvironmentSnapshot {
        steam_path: Some(fixture.steam.to_string_lossy().into_owned()),
        game_path: Some(fixture.game.to_string_lossy().into_owned()),
        game_path_valid: true,
        bepinex_installed: installed_version.is_some(),
        bpp_version: installed_version.map(str::to_owned),
        bundled_bpp_version: Some("1.2.3".into()),
        steam_launch_options: SteamLaunchOptionsState::Empty,
        trampoline_current: true,
        obsolete_macos_artifacts_present: false,
    }
}

fn install_installation(fixture: &Fixture) {
    std::fs::create_dir_all(fixture.game.join("BepInEx/plugins")).unwrap();
    std::fs::create_dir_all(paths::bpp_data_dir(&fixture.game)).unwrap();
}

#[test]
fn get_install_state() {
    let fixture = Fixture::new();
    let missing = InstallEnvironmentSnapshot {
        steam_path: None,
        game_path: None,
        game_path_valid: false,
        bepinex_installed: false,
        bpp_version: None,
        bundled_bpp_version: Some("1.2.3".into()),
        steam_launch_options: SteamLaunchOptionsState::Empty,
        trampoline_current: true,
        obsolete_macos_artifacts_present: false,
    };
    assert_golden(
        "ipc/get_install_state.game-missing.json",
        &fixture.json(&install_state_from_snapshot(missing)),
    );
    assert_golden(
        "ipc/get_install_state.installable.json",
        &fixture.json(&install_state_from_snapshot(snapshot(&fixture, None))),
    );

    install_installation(&fixture);
    assert_golden(
        "ipc/get_install_state.installed.json",
        &fixture.json(&install_state_from_snapshot(snapshot(
            &fixture,
            Some("1.2.3"),
        ))),
    );
    assert_golden(
        "ipc/get_install_state.outdated.json",
        &fixture.json(&install_state_from_snapshot(snapshot(
            &fixture,
            Some("1.2.2"),
        ))),
    );
}

/// Every Legacy Root with a few bytes, the current Data Root, and a 6.x mod
/// contract, as an upgraded 5.x user's game directory looks before importing.
fn seed_legacy_roots(fixture: &Fixture) {
    let game = &fixture.game;
    #[cfg(target_os = "macos")]
    std::fs::create_dir_all(game.join("TheBazaar.app")).unwrap();
    #[cfg(target_os = "windows")]
    write(&game.join("TheBazaar.exe"), b"exe");
    #[cfg(not(any(target_os = "macos", target_os = "windows")))]
    write(&game.join("TheBazaar"), b"exe");
    write(&game.join("BazaarPlusPlus/bazaarplusplus.db"), &[0; 12]);
    write(&game.join("BazaarPlusPlusV4/bazaarplusplus.db"), &[0; 34]);
    write(&game.join("BazaarPlusPlusV5/bazaarplusplus.db"), &[0; 56]);
    write(&game.join("BazaarPlusPlusV5/Screenshots/run.png"), &[0; 7]);
    write(&paths::database_path(game), b"current");
    write(
        &game.join("BepInEx/plugins/BazaarPlusPlus.history-database.json"),
        format!(
            r#"{{ "formatVersion": 1, "dataRootDirectoryName": "{}", "historyDatabaseUserVersion": 1 }}"#,
            crate::config::BAZAAR_DATA_DIRECTORY
        )
        .as_bytes(),
    );
}

#[test]
fn get_legacy_data_state() {
    let fixture = Fixture::new();
    assert_golden(
        "ipc/get_legacy_data_state.none.json",
        &fixture.json(&legacy_data::legacy_data_state(&fixture.game)),
    );

    seed_legacy_roots(&fixture);
    assert_golden(
        "ipc/get_legacy_data_state.legacy-roots.json",
        &fixture.json(&legacy_data::legacy_data_state(&fixture.game)),
    );
}

#[test]
fn get_legacy_data_presence() {
    let fixture = Fixture::new();
    assert_golden(
        "ipc/get_legacy_data_presence.none.json",
        &fixture.json(&legacy_data::legacy_data_presence(&fixture.game)),
    );

    seed_legacy_roots(&fixture);
    assert_golden(
        "ipc/get_legacy_data_presence.legacy-roots.json",
        &fixture.json(&legacy_data::legacy_data_presence(&fixture.game)),
    );
}

#[test]
fn delete_legacy_root() {
    let fixture = Fixture::new();
    seed_legacy_roots(&fixture);
    let stopped = |_: &Path| Ok(false);
    assert_golden(
        "ipc/delete_legacy_root.removed.json",
        &fixture.json(
            &legacy_data::delete_legacy_root_blocking_with(
                &fixture.game,
                "BazaarPlusPlusV5",
                &stopped,
            )
            .unwrap(),
        ),
    );
}

/// The V5 fixture game (`v5_import::tests_e2e`) imported with every file
/// copied, then remeasured: `BazaarPlusPlusV5` stays listed with the marker
/// inside and is no longer eligible. A game that is running refuses with the
/// import's own code.
#[test]
fn import_v5_history() {
    use crate::v5_import::tests_e2e::{import_copying, v3_game};
    use crate::v5_import::ImportV5HistoryResult;

    let fixture = v3_game();
    let report = import_copying(&fixture, &|_: &Path| Ok(false)).unwrap();
    let result = ImportV5HistoryResult::new(&report, legacy_data::legacy_data_state(&fixture.game));
    assert_golden(
        "ipc/import_v5_history.imported.json",
        &json(&result, &[(&fixture.game, GAME)]),
    );

    let running = v3_game();
    let refused = import_copying(&running, &|_: &Path| Ok(true)).unwrap_err();
    assert_golden(
        "ipc/import_v5_history.blocked-by-game.json",
        &json(&refused.into_problem(), &[(&running.game, GAME)]),
    );
}

/// Three runs: a Ranked win with a primary screenshot and two battles (one
/// recorded) that is not yet sealed into a Bundle, so cleanup must skip it; a
/// Normal loss under the legacy `Hero8` name whose Bundle is already in the
/// outbox; and a run still in progress.
fn seed_history(fixture: &Fixture) {
    let game = &fixture.game;
    write(
        &paths::screenshots_dir(game).join("2026-01-01/run-1.png"),
        b"fixture screenshot 1",
    );
    write(
        &paths::screenshots_dir(game).join("2026-01-02/run-2.png"),
        b"fixture screenshot 2",
    );
    write(
        &paths::combat_replay_videos_dir(game).join("2026-01-01/battle-1.mp4"),
        b"fixture video",
    );
    let conn = rusqlite::Connection::open(paths::database_path(game)).unwrap();
    create_mod_schema(&conn);
    conn.execute_batch(
        "
        insert into runs (
            run_id, started_at_ms, last_seen_at_ms, status, hero, game_mode,
            ended_at_ms, final_day, victories, losses, final_player_rank, final_player_rating,
            final_player_rating_delta
        ) values
            ('run-1', 1767258000000, 1767260520000, 'completed',
             'Vanessa', 'Ranked', 1767260520000, 12, 10, 2, 'Gold', 1520, 16),
            ('run-2', 1767344400000, 1767346200000, 'completed',
             'Hero8', 'Normal', 1767346200000, 8, 4, 5, null, null, null),
            ('run-3', 1767430800000, 1767431400000, 'active',
             'Mak', 'Ranked', null, 3, 2, 1, null, null, null);
        insert into battles (
            battle_id, source, run_id, recorded_at_ms, combat_kind, day, hour, player_name,
            opponent_hero, opponent_name, opponent_rank, opponent_rating, result, local_payload_state
        ) values
            ('battle-1', 'LOCAL', 'run-1', 1767258600000, 'PVP', 1, 2,
             'Fixture Player', 'Pygmalien', 'Opponent A', 'Silver', 1400, 'win', 'missing'),
            ('battle-2', 'LOCAL', 'run-1', 1767259200000, 'PVP', 2, 4,
             'Fixture Player', 'Dooley', null, null, null, 'loss', 'missing'),
            ('battle-3', 'LOCAL', 'run-2', 1767345000000, 'PVP', 1, 1,
             'Fixture Player', 'Stelle', null, null, null, 'loss', 'missing');
        insert into run_screenshots (
            screenshot_id, run_id, hero_name, capture_source, is_primary, image_relative_path,
            captured_at_ms, captured_utc_offset_minutes, day, victories_at_capture
        ) values
            ('shot-1', 'run-1', 'Vanessa', 'end_of_run_auto', 1, '2026-01-01/run-1.png',
             1767260520000, 480, 12, 10),
            ('shot-2', 'run-2', 'Hero8', 'end_of_run_auto', 0, '2026-01-02/run-2.png',
             1767346200000, 480, 8, 4);
        insert into combat_replay_videos (
            video_id, battle_id, source, video_relative_path, width, height, fps, codec,
            started_at_ms, duration_ms, file_size_bytes, status
        ) values
            ('video-1', 'battle-1', 'GAME_CAPTURE', '2026-01-01/battle-1.mp4', 1920, 1080, 60,
             'h264', 1767258600000, 95000, 13, 'COMPLETED');
        insert into bundle_outbox (
            bundle_id, run_id, file_name, content_sha256_hex, content_digest, total_bytes,
            has_screenshot, sealed_at_ms, status
        ) values
            ('bundle-2', 'run-2', 'bundle-2.bppbundle', 'ab', 'sha256:ab', 10, 1,
             1767346260000, 'pending');
        ",
    )
    .unwrap();
}

fn history(fixture: &Fixture) -> History {
    History::at_with(Some(fixture.game.clone()), || false).unwrap()
}

#[test]
fn list_history_runs() {
    let fixture = Fixture::new();
    std::fs::create_dir_all(paths::bpp_data_dir(&fixture.game)).unwrap();
    let conn = rusqlite::Connection::open(paths::database_path(&fixture.game)).unwrap();
    create_mod_schema(&conn);
    drop(conn);
    assert_golden(
        "ipc/list_history_runs.empty.json",
        &fixture.json(
            &history(&fixture)
                .list_runs_for_page(50, 0, &HistoryThumbnails::default())
                .unwrap(),
        ),
    );

    let fixture = Fixture::new();
    seed_history(&fixture);
    let history = history(&fixture);
    assert_golden(
        "ipc/list_history_runs.page-1.json",
        &fixture.json(
            &history
                .list_runs_for_page(50, 0, &HistoryThumbnails::default())
                .unwrap(),
        ),
    );
    assert_golden(
        "ipc/list_history_runs.page-2-of-2.json",
        &fixture.json(
            &history
                .list_runs_for_page(2, 2, &HistoryThumbnails::default())
                .unwrap(),
        ),
    );
}

#[test]
fn get_history_run_detail() {
    let fixture = Fixture::new();
    seed_history(&fixture);
    let history = history(&fixture);
    assert_golden(
        "ipc/get_history_run_detail.found.json",
        &fixture.json(&history.run_detail_for_page("run-1").unwrap()),
    );
    assert_golden(
        "ipc/get_history_run_detail.not-found.json",
        &fixture.json(&history.run_detail_for_page("run-404").unwrap()),
    );
}

#[test]
fn preview_storage_cleanup() {
    let fixture = Fixture::new();
    seed_history(&fixture);
    let history = history(&fixture);
    let plans = CleanupPlans::default();
    assert_golden(
        "ipc/preview_storage_cleanup.screenshots-all.json",
        &fixture.json(
            &history
                .preview_cleanup(
                    StorageCleanupScope::Screenshots,
                    StorageCleanupPreset::All,
                    &plans,
                )
                .unwrap(),
        ),
    );
    assert_golden(
        "ipc/preview_storage_cleanup.run-data-all.json",
        &fixture.json(
            &history
                .preview_cleanup(
                    StorageCleanupScope::RunData,
                    StorageCleanupPreset::All,
                    &plans,
                )
                .unwrap(),
        ),
    );
}

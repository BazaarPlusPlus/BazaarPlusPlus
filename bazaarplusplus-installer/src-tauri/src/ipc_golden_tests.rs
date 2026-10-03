//! IPC JSON goldens: the DTOs `get_install_state`, `list_history_runs`,
//! `get_history_run_detail` and `preview_storage_cleanup` return to the
//! frontend, produced by the service layer each command wraps from a fixture
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
use crate::history::test_schema::create_history_schema;
use crate::services::detect::InstallEnvironmentSnapshot;
use crate::services::history::{History, StorageCleanupPreset, StorageCleanupScope};
use crate::services::install::install_state_from_snapshot;
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
    create_history_schema(&conn);
    conn.execute_batch(
        "
        insert into runs (
            run_id, started_at_utc, last_seen_at_utc, status, completed, hero, game_mode,
            ended_at_utc, final_day, victories, losses, final_player_rank, final_player_rating
        ) values
            ('run-1', '2026-01-01T09:00:00Z', '2026-01-01T09:42:00Z', 'completed', 1,
             'Vanessa', 'Ranked', '2026-01-01T09:42:00Z', 12, 10, 2, 'Gold', 1520),
            ('run-2', '2026-01-02T09:00:00Z', '2026-01-02T09:30:00Z', 'completed', 1,
             'Hero8', 'Normal', '2026-01-02T09:30:00Z', 8, 4, 5, null, null),
            ('run-3', '2026-01-03T09:00:00Z', '2026-01-03T09:10:00Z', 'active', 0,
             'Mak', 'Ranked', null, 3, 2, 1, null, null);
        insert into battles (
            battle_id, source, run_id, recorded_at_utc, day, hour, player_name,
            opponent_hero, opponent_name, opponent_rank, opponent_rating, result
        ) values
            ('battle-1', 'LOCAL', 'run-1', '2026-01-01T09:10:00Z', 1, 2, 'Fixture Player',
             'Pygmalien', 'Opponent A', 'Silver', 1400, 'win'),
            ('battle-2', 'LOCAL', 'run-1', '2026-01-01T09:20:00Z', 2, 4, 'Fixture Player',
             'Dooley', null, null, null, 'loss'),
            ('battle-3', 'LOCAL', 'run-2', '2026-01-02T09:10:00Z', 1, 1, 'Fixture Player',
             'Stelle', null, null, null, 'loss');
        insert into run_screenshots (
            screenshot_id, run_id, hero_name, capture_source, is_primary, image_relative_path,
            captured_at_utc, captured_at_local, day, victories_at_capture
        ) values
            ('shot-1', 'run-1', 'Vanessa', 'end_of_run_auto', 1, '2026-01-01/run-1.png',
             '2026-01-01T09:42:00Z', '2026-01-01T17:42:00+08:00', 12, 10),
            ('shot-2', 'run-2', 'Hero8', 'end_of_run_auto', 0, '2026-01-02/run-2.png',
             '2026-01-02T09:30:00Z', '2026-01-02T17:30:00+08:00', 8, 4);
        insert into combat_replay_videos (
            video_id, battle_id, video_relative_path, started_at_utc, duration_ms,
            file_size_bytes, status
        ) values
            ('video-1', 'battle-1', '2026-01-01/battle-1.mp4', '2026-01-01T09:10:00Z',
             95000, 13, 'COMPLETED');
        insert into bundle_outbox (
            bundle_id, run_id, file_name, content_sha256_hex, content_digest, total_bytes,
            has_screenshot, sealed_at_utc, status
        ) values
            ('bundle-2', 'run-2', 'bundle-2.bppbundle', 'ab', 'sha256:ab', 10, 1,
             '2026-01-02T09:31:00Z', 'pending');
        ",
    )
    .unwrap();
}

fn history(fixture: &Fixture) -> History {
    History::from_resolved_game_path_for_page_with(Some(fixture.game.clone()), || false).unwrap()
}

#[test]
fn list_history_runs() {
    let fixture = Fixture::new();
    std::fs::create_dir_all(paths::bpp_data_dir(&fixture.game)).unwrap();
    let conn = rusqlite::Connection::open(paths::database_path(&fixture.game)).unwrap();
    create_history_schema(&conn);
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
    assert_golden(
        "ipc/preview_storage_cleanup.screenshots-all.json",
        &fixture.json(
            &history
                .preview_cleanup(StorageCleanupScope::Screenshots, StorageCleanupPreset::All)
                .unwrap(),
        ),
    );
    assert_golden(
        "ipc/preview_storage_cleanup.run-data-all.json",
        &fixture.json(
            &history
                .preview_cleanup(StorageCleanupScope::RunData, StorageCleanupPreset::All)
                .unwrap(),
        ),
    );
}

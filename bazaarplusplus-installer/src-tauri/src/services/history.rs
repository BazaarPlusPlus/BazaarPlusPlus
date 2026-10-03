use std::path::{Path, PathBuf};
use std::process::Command;

use crate::history::{
    cleanup::{
        self, RunDataCleanupPreview, RunDataCleanupResult, ScreenshotCleanupPreview,
        ScreenshotCleanupResult,
    },
    delete_battle_video as delete_battle_video_in_repo, get_history_run_detail, list_history_runs,
    load_battle_video_path, load_run_id_for_battle, load_run_screenshot_path, HistoryReadError,
};
use crate::problem::{SemanticProblem, SemanticProblemCode};
use crate::services::game_path::GamePathAcceptance;
use crate::services::paths;
use crate::services::selected_game_installation::SelectedGameInstallationState;
use crate::stream::history_thumbnails::{self, HistoryThumbnails};
use tauri::Manager;

pub use crate::history::cleanup::StorageCleanupPreset;
pub(crate) use crate::history::{HistoryRunDetail, HistoryRunList};

const HISTORY_UNAVAILABLE: &str =
    "No selected game installation with a history database is available.";
const REVEAL_SCREENSHOT: &str = "reveal_screenshot";
const REVEAL_VIDEO: &str = "reveal_video";

#[derive(Clone, Copy, Debug, PartialEq, Eq, serde::Serialize, serde::Deserialize, specta::Type)]
#[serde(rename_all = "snake_case")]
pub enum StorageCleanupScope {
    Screenshots,
    RunData,
}

#[derive(Clone, Debug, PartialEq, serde::Serialize, specta::Type)]
#[serde(tag = "scope", rename_all = "snake_case")]
pub enum StorageCleanupPreview {
    Screenshots { preview: ScreenshotCleanupPreview },
    RunData { preview: RunDataCleanupPreview },
}

#[derive(Clone, Debug, PartialEq, serde::Serialize, specta::Type)]
#[serde(tag = "scope", rename_all = "snake_case")]
pub enum StorageCleanupExecution {
    Screenshots { result: ScreenshotCleanupResult },
    RunData { result: RunDataCleanupResult },
}

struct HistoryStorage {
    game_path: PathBuf,
    combat_replay_videos_dir: PathBuf,
    database_path: PathBuf,
}

pub(crate) struct History {
    paths: HistoryStorage,
    is_game_running: fn() -> bool,
}

impl History {
    fn resolved_game_path(app: &tauri::AppHandle) -> Option<PathBuf> {
        app.state::<SelectedGameInstallationState>()
            .resolve(app, None, GamePathAcceptance::DatabaseExists)
            .map(|resolution| resolution.game_path)
    }

    fn from_resolved_game_path_with(
        game_path: Option<PathBuf>,
        is_game_running: fn() -> bool,
    ) -> Result<Self, String> {
        game_path
            .map(history_paths_for_game_path)
            .map(|paths| Self {
                paths,
                is_game_running,
            })
            .ok_or_else(|| HISTORY_UNAVAILABLE.to_string())
    }

    fn from_resolved_game_path_for_page(
        game_path: Option<PathBuf>,
    ) -> Result<Self, SemanticProblem> {
        Self::from_resolved_game_path_for_page_with(
            game_path,
            crate::services::game_process::is_bazaar_running_best_effort,
        )
    }

    pub(crate) fn from_resolved_game_path_for_page_with(
        game_path: Option<PathBuf>,
        is_game_running: fn() -> bool,
    ) -> Result<Self, SemanticProblem> {
        Self::from_resolved_game_path_with(game_path, is_game_running)
            .map_err(|_| SemanticProblem::new(SemanticProblemCode::HistoryUnavailable))
    }

    pub(crate) fn list_runs_for_page(
        &self,
        limit: usize,
        offset: usize,
        thumbnails: &HistoryThumbnails,
    ) -> Result<HistoryRunList, SemanticProblem> {
        // The page's thumbnails stay bound to the installation it was read from.
        let source = thumbnails.register(&self.paths.game_path);
        list_history_runs(
            &self.paths.database_path,
            limit.clamp(1, 200),
            offset,
            |screenshot_id| history_thumbnails::url(source, screenshot_id),
        )
        .map_err(|error| history_read_problem_with("list_runs", error, self.is_game_running))
    }

    pub(crate) fn run_detail_for_page(
        &self,
        run_id: &str,
    ) -> Result<Option<HistoryRunDetail>, SemanticProblem> {
        get_history_run_detail(&self.paths.database_path, run_id).map_err(|error| {
            history_read_problem_with("get_run_detail", error, self.is_game_running)
        })
    }

    fn run_detail(&self, run_id: &str) -> Result<HistoryRunDetail, String> {
        get_history_run_detail(&self.paths.database_path, run_id)?
            .ok_or_else(|| format!("History run {run_id} was not found."))
    }

    /// The screenshot file `reveal_run_screenshot` shows for `run_id`.
    fn run_screenshot_path(&self, run_id: &str) -> Result<PathBuf, SemanticProblem> {
        as_action_problem(REVEAL_SCREENSHOT, || {
            self.require_database_exists()?;
            load_run_screenshot_path(&self.paths.database_path, &self.paths.game_path, run_id)?
                .ok_or_else(|| format!("No screenshot is available for run {run_id}."))
        })
    }

    /// The video file `reveal_battle_video` shows for `battle_id`.
    fn battle_video_path(
        &self,
        battle_id: &str,
        video_id: Option<&str>,
    ) -> Result<PathBuf, SemanticProblem> {
        as_action_problem(REVEAL_VIDEO, || {
            self.require_database_exists()?;
            let path = load_battle_video_path(
                &self.paths.database_path,
                &self.paths.combat_replay_videos_dir,
                battle_id,
                video_id,
            )?
            .ok_or_else(|| format!("No completed video is available for battle {battle_id}."))?;
            require_video_file_exists(&path)?;
            Ok(path)
        })
    }

    fn delete_battle_video(
        &self,
        battle_id: &str,
        video_id: &str,
    ) -> Result<HistoryRunDetail, SemanticProblem> {
        as_action_problem("delete_video", || {
            self.require_database_exists()?;
            let run_id = load_run_id_for_battle(&self.paths.database_path, battle_id)?
                .ok_or_else(|| format!("Battle {battle_id} was not found."))?;
            let deleted = delete_battle_video_in_repo(
                &self.paths.database_path,
                &self.paths.combat_replay_videos_dir,
                battle_id,
                video_id,
            )?;
            if !deleted {
                return Err(format!(
                    "Video {video_id} was not found for battle {battle_id}."
                ));
            }

            self.run_detail(&run_id)
        })
    }

    pub(crate) fn preview_cleanup(
        &self,
        scope: StorageCleanupScope,
        preset: StorageCleanupPreset,
    ) -> Result<StorageCleanupPreview, SemanticProblem> {
        as_action_problem("preview_storage_cleanup", || {
            let now = chrono::Local::now();
            let today = now.date_naive();
            let cutoff = cleanup::CleanupCutoff::for_preset(preset, now);
            match scope {
                StorageCleanupScope::Screenshots => {
                    let plan = cleanup::plan_screenshot_cleanup(
                        &self.paths.database_path,
                        &self.paths.game_path,
                        cutoff.as_ref(),
                        today,
                    )?;
                    Ok(StorageCleanupPreview::Screenshots {
                        preview: plan.to_preview(),
                    })
                }
                StorageCleanupScope::RunData => {
                    let plan = cleanup::plan_run_data_cleanup(
                        &self.paths.database_path,
                        &self.paths.game_path,
                        cutoff.as_ref(),
                    )?;
                    Ok(StorageCleanupPreview::RunData {
                        preview: plan.to_preview(),
                    })
                }
            }
        })
    }

    fn execute_cleanup(
        &self,
        scope: StorageCleanupScope,
        preset: StorageCleanupPreset,
    ) -> Result<StorageCleanupExecution, SemanticProblem> {
        as_action_problem("execute_storage_cleanup", || {
            let now = chrono::Local::now();
            let today = now.date_naive();
            let cutoff = cleanup::CleanupCutoff::for_preset(preset, now);
            match scope {
                StorageCleanupScope::Screenshots => cleanup::execute_screenshot_cleanup(
                    &self.paths.database_path,
                    &self.paths.game_path,
                    cutoff.as_ref(),
                    today,
                )
                .map(|result| StorageCleanupExecution::Screenshots { result }),
                StorageCleanupScope::RunData => cleanup::execute_run_data_cleanup(
                    &self.paths.database_path,
                    &self.paths.game_path,
                    cutoff.as_ref(),
                )
                .map(|result| StorageCleanupExecution::RunData { result }),
            }
        })
    }

    fn require_database_exists(&self) -> Result<(), String> {
        self.paths
            .database_path
            .exists()
            .then_some(())
            .ok_or_else(|| {
                format!(
                    "History database was not found at {}.",
                    self.paths.database_path.display()
                )
            })
    }
}

pub fn list_runs(
    app: &tauri::AppHandle,
    limit: Option<usize>,
    offset: Option<usize>,
) -> Result<HistoryRunList, SemanticProblem> {
    History::from_resolved_game_path_for_page(History::resolved_game_path(app))?.list_runs_for_page(
        limit.unwrap_or(50),
        offset.unwrap_or(0),
        &app.state::<HistoryThumbnails>(),
    )
}

pub fn get_run_detail(
    app: &tauri::AppHandle,
    run_id: &str,
) -> Result<Option<HistoryRunDetail>, SemanticProblem> {
    History::from_resolved_game_path_for_page(History::resolved_game_path(app))?
        .run_detail_for_page(run_id)
}

pub fn reveal_run_screenshot(app: &tauri::AppHandle, run_id: &str) -> Result<(), SemanticProblem> {
    let path = History::from_resolved_game_path_for_page(History::resolved_game_path(app))?
        .run_screenshot_path(run_id)?;
    as_action_problem(REVEAL_SCREENSHOT, || reveal_in_file_browser(&path))
}

pub fn reveal_battle_video(
    app: &tauri::AppHandle,
    battle_id: &str,
    video_id: Option<&str>,
) -> Result<(), SemanticProblem> {
    let path = History::from_resolved_game_path_for_page(History::resolved_game_path(app))?
        .battle_video_path(battle_id, video_id)?;
    as_action_problem(REVEAL_VIDEO, || reveal_in_file_browser(&path))
}

pub fn delete_battle_video(
    app: &tauri::AppHandle,
    battle_id: &str,
    video_id: &str,
) -> Result<HistoryRunDetail, SemanticProblem> {
    History::from_resolved_game_path_for_page(History::resolved_game_path(app))?
        .delete_battle_video(battle_id, video_id)
}

pub fn preview_storage_cleanup(
    app: &tauri::AppHandle,
    scope: StorageCleanupScope,
    preset: StorageCleanupPreset,
) -> Result<StorageCleanupPreview, SemanticProblem> {
    History::from_resolved_game_path_for_page(History::resolved_game_path(app))?
        .preview_cleanup(scope, preset)
}

pub fn execute_storage_cleanup(
    app: &tauri::AppHandle,
    scope: StorageCleanupScope,
    preset: StorageCleanupPreset,
) -> Result<StorageCleanupExecution, SemanticProblem> {
    History::from_resolved_game_path_for_page(History::resolved_game_path(app))?
        .execute_cleanup(scope, preset)
}

fn history_paths_for_game_path(game_path: PathBuf) -> HistoryStorage {
    HistoryStorage {
        combat_replay_videos_dir: paths::combat_replay_videos_dir(&game_path),
        database_path: paths::database_path(&game_path),
        game_path,
    }
}

/// Runs one history action and reports its diagnostic as the semantic
/// `HistoryActionFailed` problem the frontend renders for `operation`.
fn as_action_problem<T>(
    operation: &str,
    action: impl FnOnce() -> Result<T, String>,
) -> Result<T, SemanticProblem> {
    action().map_err(|diagnostic| {
        SemanticProblem::new(SemanticProblemCode::HistoryActionFailed)
            .with_param("operation", operation)
            .with_diagnostic(diagnostic)
    })
}

fn history_read_problem_with(
    operation: &str,
    error: HistoryReadError,
    is_game_running: impl FnOnce() -> bool,
) -> SemanticProblem {
    let diagnostic = error.to_string();
    if let HistoryReadError::UnsupportedSchema { found, supported } = error {
        return SemanticProblem::new(SemanticProblemCode::HistoryDatabaseUnsupportedSchema)
            .with_param("found", found.to_string())
            .with_param("supported", supported)
            .with_diagnostic(diagnostic);
    }

    // The game keeps this database open, and a game process that outlived its
    // window is the one cause the user can act on. Naming it turns an opaque
    // SQLite failure into a fixable one, so it wins over the generic read code
    // whenever the process is still there.
    if is_game_running() {
        return SemanticProblem::new(SemanticProblemCode::HistoryReadBlockedByGame)
            .with_param("operation", operation)
            .with_diagnostic(diagnostic);
    }

    SemanticProblem::new(SemanticProblemCode::HistoryReadFailed)
        .with_param("operation", operation)
        .with_diagnostic(diagnostic)
}

fn require_video_file_exists(path: &Path) -> Result<(), String> {
    path.try_exists()
        .map_err(|err| format!("Failed to inspect video file at {}: {err}", path.display()))?
        .then_some(())
        .ok_or_else(|| format!("Video file was not found at {}.", path.display()))
}

#[cfg(target_os = "windows")]
fn strip_extended_length_prefix(value: &str) -> String {
    if let Some(stripped) = value.strip_prefix(r"\\?\UNC\") {
        format!(r"\\{stripped}")
    } else if let Some(stripped) = value.strip_prefix(r"\\?\") {
        stripped.to_string()
    } else {
        value.to_string()
    }
}

fn reveal_in_file_browser(path: &Path) -> Result<(), String> {
    #[cfg(target_os = "windows")]
    {
        use std::os::windows::process::CommandExt;

        let canonical = std::fs::canonicalize(path)
            .map(|buf| strip_extended_length_prefix(&buf.to_string_lossy()))
            .unwrap_or_else(|_| path.to_string_lossy().into_owned());

        Command::new("explorer")
            .raw_arg(format!("/select,\"{}\"", canonical))
            .spawn()
            .map_err(|err| format!("failed to reveal file in Explorer: {err}"))?;
        Ok(())
    }

    #[cfg(target_os = "macos")]
    {
        Command::new("open")
            .args(["-R", &path.to_string_lossy()])
            .spawn()
            .map_err(|err| format!("failed to reveal file in Finder: {err}"))?;
        Ok(())
    }

    #[cfg(all(not(target_os = "windows"), not(target_os = "macos")))]
    {
        let parent = path
            .parent()
            .ok_or_else(|| "file parent directory is missing".to_string())?;
        Command::new("xdg-open")
            .arg(parent)
            .spawn()
            .map_err(|err| format!("failed to open file directory: {err}"))?;
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::{
        history_paths_for_game_path, history_read_problem_with, history_thumbnails,
        require_video_file_exists, History, HistoryReadError, HistoryThumbnails,
        StorageCleanupExecution, StorageCleanupPreset, StorageCleanupPreview, StorageCleanupScope,
        HISTORY_UNAVAILABLE,
    };
    use crate::history::test_schema::create_history_schema;
    use crate::problem::SemanticProblemCode;
    use crate::services::paths;
    use std::path::Path;

    #[test]
    fn history_paths_use_combat_replay_videos_as_video_root() {
        let game_path = std::path::PathBuf::from("/tmp/The Bazaar");

        let resolved = history_paths_for_game_path(game_path.clone());

        assert_eq!(
            resolved.combat_replay_videos_dir,
            paths::combat_replay_videos_dir(&game_path)
        );
        assert_eq!(resolved.database_path, paths::database_path(&game_path));
    }

    #[test]
    fn missing_selected_history_returns_a_domain_error() {
        let error = History::from_resolved_game_path_with(None, || false)
            .err()
            .unwrap();

        assert_eq!(error, HISTORY_UNAVAILABLE);
    }

    #[test]
    fn history_page_list_uses_semantic_unavailable_and_read_failed_problems() {
        let unavailable = History::from_resolved_game_path_for_page(None)
            .err()
            .unwrap();
        assert_eq!(unavailable.code, SemanticProblemCode::HistoryUnavailable);
        assert!(unavailable.params.is_empty());
        assert_eq!(unavailable.diagnostic, None);

        let temp = tempfile::tempdir().unwrap();
        let game_path = temp.path().join("The Bazaar");
        let history =
            History::from_resolved_game_path_for_page_with(Some(game_path.clone()), || false)
                .unwrap();
        std::fs::create_dir_all(history.paths.database_path.parent().unwrap()).unwrap();
        std::fs::write(&history.paths.database_path, b"not sqlite").unwrap();

        let read_failed = history
            .list_runs_for_page(50, 0, &HistoryThumbnails::default())
            .unwrap_err();
        assert_eq!(read_failed.code, SemanticProblemCode::HistoryReadFailed);
        assert_eq!(
            read_failed.params.get("operation").map(String::as_str),
            Some("list_runs")
        );
        assert!(read_failed.diagnostic.is_some());
    }

    #[test]
    fn history_read_problem_uses_the_injected_game_state() {
        let problem = history_read_problem_with(
            "list_runs",
            HistoryReadError::Failed("database busy".to_string()),
            || true,
        );

        assert_eq!(problem.code, SemanticProblemCode::HistoryReadBlockedByGame);
        assert_eq!(
            problem.params.get("operation").map(String::as_str),
            Some("list_runs")
        );
        assert_eq!(problem.diagnostic.as_deref(), Some("database busy"));
    }

    #[test]
    fn history_pages_map_unsupported_schema_to_a_specific_problem() {
        let temp = tempfile::tempdir().unwrap();
        let game_path = temp.path().join("The Bazaar");
        let history = History::from_resolved_game_path_for_page(Some(game_path)).unwrap();
        std::fs::create_dir_all(history.paths.database_path.parent().unwrap()).unwrap();
        rusqlite::Connection::open(&history.paths.database_path).unwrap();

        for problem in [
            history
                .list_runs_for_page(50, 0, &HistoryThumbnails::default())
                .unwrap_err(),
            history.run_detail_for_page("run-1").unwrap_err(),
        ] {
            assert_eq!(
                problem.code,
                SemanticProblemCode::HistoryDatabaseUnsupportedSchema
            );
            assert_eq!(problem.params.get("found").map(String::as_str), Some("0"));
            assert_eq!(
                problem.params.get("supported"),
                Some(&crate::config::supported_mod_db_user_versions_label())
            );
            assert!(problem.diagnostic.is_some());
        }
    }

    #[test]
    fn cleanup_keeps_unsupported_schema_on_the_existing_action_problem_surface() {
        let temp = tempfile::tempdir().unwrap();
        let game_path = temp.path().join("The Bazaar");
        let history = History::from_resolved_game_path_for_page(Some(game_path)).unwrap();
        std::fs::create_dir_all(history.paths.database_path.parent().unwrap()).unwrap();
        rusqlite::Connection::open(&history.paths.database_path).unwrap();

        let problem = history
            .preview_cleanup(StorageCleanupScope::RunData, StorageCleanupPreset::All)
            .unwrap_err();

        assert_eq!(problem.code, SemanticProblemCode::HistoryActionFailed);
        let supported = format!(
            "supported={}",
            crate::config::supported_mod_db_user_versions_label()
        );
        assert!(problem
            .diagnostic
            .as_deref()
            .is_some_and(|value| value.contains("found=0") && value.contains(&supported)));
    }

    #[test]
    fn run_detail_page_distinguishes_not_found_read_and_action_problems() {
        let unavailable = History::from_resolved_game_path_for_page(None)
            .err()
            .unwrap();
        assert_eq!(unavailable.code, SemanticProblemCode::HistoryUnavailable);

        let temp = tempfile::tempdir().unwrap();
        let game_path = temp.path().join("The Bazaar");
        let history =
            History::from_resolved_game_path_for_page_with(Some(game_path.clone()), || false)
                .unwrap();
        assert_eq!(history.run_detail_for_page("missing").unwrap(), None);

        std::fs::create_dir_all(history.paths.database_path.parent().unwrap()).unwrap();
        std::fs::write(&history.paths.database_path, b"not sqlite").unwrap();

        let read_failed = history.run_detail_for_page("run-1").unwrap_err();
        assert_eq!(read_failed.code, SemanticProblemCode::HistoryReadFailed);
        assert_eq!(
            read_failed.params.get("operation").map(String::as_str),
            Some("get_run_detail")
        );
        assert!(read_failed.diagnostic.is_some());

        std::fs::remove_file(&history.paths.database_path).unwrap();
        for (operation, problem) in [
            (
                "reveal_screenshot",
                history.run_screenshot_path("run-1").unwrap_err(),
            ),
            (
                "reveal_video",
                history.battle_video_path("battle-1", None).unwrap_err(),
            ),
            (
                "delete_video",
                history
                    .delete_battle_video("battle-1", "video-1")
                    .unwrap_err(),
            ),
        ] {
            assert_eq!(problem.code, SemanticProblemCode::HistoryActionFailed);
            assert_eq!(
                problem.params.get("operation").map(String::as_str),
                Some(operation)
            );
            assert!(problem.diagnostic.is_some());
        }
    }

    #[test]
    fn storage_cleanup_page_classifies_preview_and_execute_failures() {
        let temp = tempfile::tempdir().unwrap();
        let game_path = temp.path().join("The Bazaar");
        let history = History::from_resolved_game_path_for_page(Some(game_path.clone())).unwrap();
        std::fs::create_dir_all(history.paths.database_path.parent().unwrap()).unwrap();
        std::fs::write(&history.paths.database_path, b"not sqlite").unwrap();

        for (operation, problem) in [
            (
                "preview_storage_cleanup",
                history
                    .preview_cleanup(StorageCleanupScope::RunData, StorageCleanupPreset::All)
                    .unwrap_err(),
            ),
            (
                "execute_storage_cleanup",
                history
                    .execute_cleanup(StorageCleanupScope::RunData, StorageCleanupPreset::All)
                    .unwrap_err(),
            ),
        ] {
            assert_eq!(problem.code, SemanticProblemCode::HistoryActionFailed);
            assert_eq!(
                problem.params.get("operation").map(String::as_str),
                Some(operation)
            );
            assert!(problem.diagnostic.is_some());
        }
    }

    #[test]
    fn video_file_exists_accepts_existing_file_and_rejects_missing_file() {
        let dir = tempfile::tempdir().expect("tempdir");
        let existing = dir.path().join("battle.mp4");
        std::fs::write(&existing, b"video").expect("write video file");

        assert!(require_video_file_exists(&existing).is_ok());
        let missing = dir.path().join("missing.mp4");
        assert_eq!(
            require_video_file_exists(&missing).unwrap_err(),
            format!("Video file was not found at {}.", missing.display())
        );
    }

    #[test]
    fn history_facade_owns_paths_queries_reveals_deletes_and_cleanup() {
        let temp = tempfile::tempdir().unwrap();
        let game_path = temp.path().join("The Bazaar");
        let database_path = paths::database_path(&game_path);
        let screenshots_dir = paths::screenshots_dir(&game_path);
        let videos_dir = paths::combat_replay_videos_dir(&game_path);
        std::fs::create_dir_all(database_path.parent().unwrap()).unwrap();
        std::fs::create_dir_all(screenshots_dir.join("2026-01-01")).unwrap();
        std::fs::create_dir_all(videos_dir.join("2026-01-01")).unwrap();
        let screenshot_path = screenshots_dir.join("2026-01-01/run.png");
        let video_path = videos_dir.join("2026-01-01/battle.mp4");
        std::fs::write(&screenshot_path, b"shot").unwrap();
        std::fs::write(&video_path, b"video").unwrap();

        let conn = rusqlite::Connection::open(&database_path).unwrap();
        create_history_schema(&conn);
        conn.execute_batch(
            "
            insert into runs (
                run_id, started_at_utc, last_seen_at_utc, status, completed,
                hero, game_mode, ended_at_utc, victories, losses
            ) values (
                'run-1', '2026-01-01T09:00:00Z', '2026-01-01T10:00:00Z',
                'completed', 1, 'Vanessa', 'Normal', '2026-01-01T10:00:00Z', 10, 2
            );
            insert into battles (
                battle_id, source, run_id, recorded_at_utc, player_name,
                opponent_hero, opponent_name, result
            ) values (
                'battle-1', 'LOCAL', 'run-1', '2026-01-01T09:30:00Z', 'Player',
                'Dooley', 'Opponent', 'win'
            );
            insert into run_screenshots (
                screenshot_id, run_id, capture_source, is_primary,
                image_relative_path, captured_at_utc, captured_at_local
            ) values (
                'shot-1', 'run-1', 'end_of_run_auto', 1,
                '2026-01-01/run.png', '2026-01-01T10:00:00Z',
                '2026-01-01T18:00:00+08:00'
            );
            insert into combat_replay_videos (
                video_id, battle_id, video_relative_path, started_at_utc,
                file_size_bytes, status
            ) values (
                'video-1', 'battle-1', '2026-01-01/battle.mp4',
                '2026-01-01T09:30:00Z', 5, 'COMPLETED'
            );
            ",
        )
        .unwrap();
        drop(conn);

        let history =
            History::from_resolved_game_path_with(Some(game_path.clone()), || false).unwrap();
        let thumbnails = HistoryThumbnails::default();
        thumbnails.register(Path::new("/another/installation"));
        let list = history.list_runs_for_page(50, 0, &thumbnails).unwrap();
        assert_eq!(
            list.runs[0].thumbnail_url,
            Some(history_thumbnails::url(
                thumbnails.register(&game_path),
                "shot-1"
            ))
        );
        assert_eq!(list.summary.runs, 1);
        assert_eq!(list.summary.videos, 1);
        assert_eq!(list.runs.len(), 1);
        assert_eq!(history.run_detail("run-1").unwrap().battles.len(), 1);

        assert_eq!(
            history.run_screenshot_path("run-1").unwrap(),
            screenshot_path
        );
        assert_eq!(
            history
                .battle_video_path("battle-1", Some("video-1"))
                .unwrap(),
            video_path
        );

        let detail = history.delete_battle_video("battle-1", "video-1").unwrap();
        assert_eq!(detail.battles[0].video, None);
        assert!(!video_path.exists());

        let preview = history
            .preview_cleanup(StorageCleanupScope::Screenshots, StorageCleanupPreset::All)
            .unwrap();
        let StorageCleanupPreview::Screenshots { preview } = preview else {
            panic!("expected screenshot preview");
        };
        assert_eq!(preview.screenshots, 1);
        let execution = history
            .execute_cleanup(StorageCleanupScope::Screenshots, StorageCleanupPreset::All)
            .unwrap();
        let StorageCleanupExecution::Screenshots { result } = execution else {
            panic!("expected screenshot result");
        };
        assert_eq!(result.deleted_rows, 1);
        assert!(!screenshot_path.exists());

        let preview = history
            .preview_cleanup(StorageCleanupScope::RunData, StorageCleanupPreset::All)
            .unwrap();
        let StorageCleanupPreview::RunData { preview } = preview else {
            panic!("expected run-data preview");
        };
        assert_eq!(preview.runs, 1);
        assert_eq!(preview.battles, 1);
        let execution = history
            .execute_cleanup(StorageCleanupScope::RunData, StorageCleanupPreset::All)
            .unwrap();
        let StorageCleanupExecution::RunData { result } = execution else {
            panic!("expected run-data result");
        };
        assert_eq!(result.deleted_runs, 1);
        assert_eq!(
            history
                .list_runs_for_page(50, 0, &HistoryThumbnails::default())
                .unwrap()
                .summary
                .runs,
            0
        );
    }

    #[test]
    fn storage_cleanup_preview_serializes_as_a_scope_tagged_result() {
        let preview = StorageCleanupPreview::Screenshots {
            preview: crate::history::cleanup::ScreenshotCleanupPreview {
                screenshots: 2,
                orphan_files: 1,
                estimated_bytes: 42,
                skipped_pending_uploads: 0,
            },
        };

        assert_eq!(
            serde_json::to_value(preview).unwrap(),
            serde_json::json!({
                "scope": "screenshots",
                "preview": {
                    "screenshots": 2,
                    "orphan_files": 1,
                    "estimated_bytes": 42,
                    "skipped_pending_uploads": 0
                }
            })
        );
        assert_eq!(
            serde_json::to_value(StorageCleanupScope::Screenshots).unwrap(),
            serde_json::json!("screenshots")
        );
    }
}

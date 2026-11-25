use anyhow::{bail, Context as _};
use std::path::{Path, PathBuf};
use std::process::Command;
use std::sync::{Arc, Mutex};

pub(crate) use crate::history::card_names::GameCardNames;
use crate::history::{
    cleanup::{
        self, RunDataCleanupPlan, RunDataCleanupPreview, RunDataCleanupResult,
        ScreenshotCleanupPlan, ScreenshotCleanupPreview, ScreenshotCleanupResult,
    },
    delete_battle_video as delete_battle_video_in_repo, get_history_run_detail, list_history_runs,
    load_battle_video_path, load_run_id_for_battle, load_run_screenshot_path, HistoryReadError,
    RunQuery,
};
use crate::problem::{SemanticProblem, SemanticProblemCode};
use crate::services::data_maintenance::DataMaintenanceLock;
use crate::services::game_path::GamePathAcceptance;
use crate::services::game_process::{GameProcessProbe as _, SystemGameProcessProbe};
use crate::services::paths;
use crate::services::selected_game_installation::SelectedGameInstallationState;
use crate::stream::history_thumbnails::{self, HistoryThumbnails};
use tauri::Manager;

pub use crate::history::cleanup::StorageCleanupPreset;
pub(crate) use crate::history::{HistoryRunDetail, HistoryRunFilter, HistoryRunList};

const REVEAL_SCREENSHOT: &str = "reveal_screenshot";
const REVEAL_VIDEO: &str = "reveal_video";

#[derive(Clone, Copy, Debug, PartialEq, Eq, serde::Serialize, serde::Deserialize, specta::Type)]
#[serde(rename_all = "snake_case")]
pub enum StorageCleanupScope {
    Screenshots,
    RunData,
}

/// A cleanup preview and the id of the plan behind it, which stays native.
/// Confirming passes the id back so execution runs the plan the user saw.
#[derive(Clone, Debug, PartialEq, serde::Serialize, specta::Type)]
#[serde(tag = "scope", rename_all = "snake_case")]
pub enum StorageCleanupPreview {
    Screenshots {
        preview: ScreenshotCleanupPreview,
        plan_id: u32,
    },
    RunData {
        preview: RunDataCleanupPreview,
        plan_id: u32,
    },
}

#[derive(Clone, Debug, PartialEq, serde::Serialize, specta::Type)]
#[serde(tag = "scope", rename_all = "snake_case")]
pub enum StorageCleanupExecution {
    Screenshots { result: ScreenshotCleanupResult },
    RunData { result: RunDataCleanupResult },
}

enum CleanupPlan {
    Screenshots(ScreenshotCleanupPlan),
    RunData(RunDataCleanupPlan),
}

struct StoredCleanupPlan {
    id: u32,
    data_root: PathBuf,
    scope: StorageCleanupScope,
    preset: StorageCleanupPreset,
    database_generation: u64,
    plan: CleanupPlan,
}

/// A confirmed preview may be stale. Execution revalidates its candidates, and Reset invalidates the stored plan.
#[derive(Clone, Default)]
pub(crate) struct CleanupPlans {
    slot: Arc<Mutex<CleanupPlanSlot>>,
    maintenance: DataMaintenanceLock,
}

#[derive(Default)]
struct CleanupPlanSlot {
    last_id: u32,
    stored: Option<StoredCleanupPlan>,
}

impl CleanupPlans {
    pub(crate) fn new(maintenance: DataMaintenanceLock) -> Self {
        Self {
            slot: Arc::default(),
            maintenance,
        }
    }

    fn database_generation(&self, data_root: &Path) -> u64 {
        self.maintenance.database_generation(data_root)
    }

    fn store(
        &self,
        data_root: &Path,
        scope: StorageCleanupScope,
        preset: StorageCleanupPreset,
        database_generation: u64,
        plan: CleanupPlan,
    ) -> u32 {
        let mut slot = self
            .slot
            .lock()
            .unwrap_or_else(std::sync::PoisonError::into_inner);
        let id = slot.last_id.wrapping_add(1).max(1);
        slot.last_id = id;
        slot.stored = Some(StoredCleanupPlan {
            id,
            data_root: data_root.to_path_buf(),
            scope,
            preset,
            database_generation,
            plan,
        });
        id
    }

    /// Call while holding the data maintenance lock so Reset cannot invalidate the plan during lookup.
    fn take(
        &self,
        id: u32,
        data_root: &Path,
        scope: StorageCleanupScope,
        preset: StorageCleanupPreset,
    ) -> Option<StoredCleanupPlan> {
        let mut slot = self
            .slot
            .lock()
            .unwrap_or_else(std::sync::PoisonError::into_inner);
        if slot.stored.as_ref().is_none_or(|stored| stored.id != id) {
            return None;
        }
        let stored = slot.stored.take()?;
        (stored.data_root == data_root
            && stored.scope == scope
            && stored.preset == preset
            && stored.database_generation == self.database_generation(data_root))
        .then_some(stored)
    }

    /// Retrying after partial execution is safe because each delete revalidates the candidate.
    fn restore(&self, stored: StoredCleanupPlan) {
        let mut slot = self
            .slot
            .lock()
            .unwrap_or_else(std::sync::PoisonError::into_inner);
        if slot.last_id == stored.id && slot.stored.is_none() {
            slot.stored = Some(stored);
        }
    }
}

struct HistoryStorage {
    game_path: PathBuf,
    data_root: PathBuf,
    combat_replay_videos_dir: PathBuf,
    database_path: PathBuf,
}

pub(crate) struct History {
    paths: HistoryStorage,
    is_game_running: fn(&Path) -> bool,
    cleanup_game_probe: fn(&Path) -> anyhow::Result<bool>,
}

impl History {
    pub(crate) fn resolved_game_path(app: &tauri::AppHandle) -> Option<PathBuf> {
        app.state::<SelectedGameInstallationState>()
            .resolve(app, None, GamePathAcceptance::DatabaseExists)
            .map(|resolution| resolution.game_path)
    }

    pub(crate) fn selected(app: &tauri::AppHandle) -> Result<Self, SemanticProblem> {
        Self::at(Self::resolved_game_path(app))
    }

    fn at(game_path: Option<PathBuf>) -> Result<Self, SemanticProblem> {
        Self::at_with(
            game_path,
            crate::services::game_process::is_bazaar_running_best_effort,
        )
    }

    pub(crate) fn at_with(
        game_path: Option<PathBuf>,
        is_game_running: fn(&Path) -> bool,
    ) -> Result<Self, SemanticProblem> {
        let game_path = game_path
            .ok_or_else(|| SemanticProblem::new(SemanticProblemCode::HistoryUnavailable))?;
        Ok(Self {
            paths: history_paths_for_game_path(game_path),
            is_game_running,
            cleanup_game_probe: |game_path| SystemGameProcessProbe.is_running(game_path),
        })
    }

    #[cfg(test)]
    pub(crate) fn with_cleanup_game_probe(
        mut self,
        probe: fn(&Path) -> anyhow::Result<bool>,
    ) -> Self {
        self.cleanup_game_probe = probe;
        self
    }

    pub(crate) fn list_runs_for_page(
        &self,
        limit: usize,
        offset: usize,
        query: &RunQuery,
        thumbnails: &HistoryThumbnails,
        card_names: &GameCardNames,
    ) -> Result<HistoryRunList, SemanticProblem> {
        let source = thumbnails.register(&self.paths.game_path);
        list_history_runs(
            &self.paths.database_path,
            query,
            limit.clamp(1, 200),
            offset,
            |screenshot_id| history_thumbnails::url(source, screenshot_id),
            card_names,
        )
        .map_err(|error| {
            history_read_problem_with("list_runs", &self.paths.game_path, error, || {
                (self.is_game_running)(&self.paths.game_path)
            })
        })
    }

    pub(crate) fn run_detail_for_page(
        &self,
        run_id: &str,
    ) -> Result<Option<HistoryRunDetail>, SemanticProblem> {
        get_history_run_detail(
            &self.paths.database_path,
            run_id,
            &GameCardNames::installed(),
        )
        .map_err(|error| {
            history_read_problem_with("get_run_detail", &self.paths.game_path, error, || {
                (self.is_game_running)(&self.paths.game_path)
            })
        })
    }

    fn run_detail(&self, run_id: &str) -> anyhow::Result<HistoryRunDetail> {
        get_history_run_detail(
            &self.paths.database_path,
            run_id,
            &GameCardNames::installed(),
        )?
        .with_context(|| format!("History run {run_id} was not found."))
    }

    pub(crate) fn reveal_run_screenshot(&self, run_id: &str) -> Result<(), SemanticProblem> {
        let path = self.run_screenshot_path(run_id)?;
        as_action_problem(REVEAL_SCREENSHOT, || reveal_in_file_browser(&path))
    }

    pub(crate) fn reveal_battle_video(
        &self,
        battle_id: &str,
        video_id: &str,
    ) -> Result<(), SemanticProblem> {
        let path = self.battle_video_path(battle_id, video_id)?;
        as_action_problem(REVEAL_VIDEO, || reveal_in_file_browser(&path))
    }

    fn run_screenshot_path(&self, run_id: &str) -> Result<PathBuf, SemanticProblem> {
        as_action_problem(REVEAL_SCREENSHOT, || {
            self.require_database_exists()?;
            load_run_screenshot_path(&self.paths.database_path, &self.paths.data_root, run_id)?
                .with_context(|| format!("No screenshot is available for run {run_id}."))
        })
    }

    fn battle_video_path(
        &self,
        battle_id: &str,
        video_id: &str,
    ) -> Result<PathBuf, SemanticProblem> {
        as_action_problem(REVEAL_VIDEO, || {
            self.require_database_exists()?;
            let path = load_battle_video_path(
                &self.paths.database_path,
                &self.paths.combat_replay_videos_dir,
                battle_id,
                video_id,
            )?
            .with_context(|| format!("No completed video is available for battle {battle_id}."))?;
            require_video_file_exists(&path)?;
            Ok(path)
        })
    }

    pub(crate) fn delete_battle_video(
        &self,
        battle_id: &str,
        video_id: &str,
    ) -> Result<HistoryRunDetail, SemanticProblem> {
        as_action_problem("delete_video", || {
            self.require_database_exists()?;
            let run_id = load_run_id_for_battle(&self.paths.database_path, battle_id)?
                .with_context(|| format!("Battle {battle_id} was not found."))?;
            let deleted = delete_battle_video_in_repo(
                &self.paths.database_path,
                &self.paths.combat_replay_videos_dir,
                battle_id,
                video_id,
            )?;
            if !deleted {
                bail!("Video {video_id} was not found for battle {battle_id}.");
            }

            self.run_detail(&run_id)
        })
    }

    pub(crate) fn preview_cleanup(
        &self,
        scope: StorageCleanupScope,
        preset: StorageCleanupPreset,
        plans: &CleanupPlans,
    ) -> Result<StorageCleanupPreview, SemanticProblem> {
        as_action_problem("preview_storage_cleanup", || {
            // Read before planning: a Reset that starts after this
            // read invalidates the plan.
            let database_generation = plans.database_generation(&self.paths.data_root);
            let plan = self.plan_cleanup(scope, preset)?;
            Ok(match plan {
                CleanupPlan::Screenshots(plan) => {
                    let preview = plan.to_preview();
                    StorageCleanupPreview::Screenshots {
                        preview,
                        plan_id: plans.store(
                            &self.paths.data_root,
                            scope,
                            preset,
                            database_generation,
                            CleanupPlan::Screenshots(plan),
                        ),
                    }
                }
                CleanupPlan::RunData(plan) => {
                    let preview = plan.to_preview();
                    StorageCleanupPreview::RunData {
                        preview,
                        plan_id: plans.store(
                            &self.paths.data_root,
                            scope,
                            preset,
                            database_generation,
                            CleanupPlan::RunData(plan),
                        ),
                    }
                }
            })
        })
    }

    fn plan_cleanup(
        &self,
        scope: StorageCleanupScope,
        preset: StorageCleanupPreset,
    ) -> anyhow::Result<CleanupPlan> {
        let now = chrono::Local::now();
        let cutoff = cleanup::CleanupCutoff::for_preset(preset, now);
        Ok(match scope {
            StorageCleanupScope::Screenshots => {
                CleanupPlan::Screenshots(cleanup::plan_screenshot_cleanup(
                    &self.paths.database_path,
                    &self.paths.data_root,
                    cutoff.as_ref(),
                    now.date_naive(),
                )?)
            }
            StorageCleanupScope::RunData => CleanupPlan::RunData(cleanup::plan_run_data_cleanup(
                &self.paths.database_path,
                &self.paths.data_root,
                cutoff.as_ref(),
            )?),
        })
    }

    /// Hold the data maintenance lock so Reset cannot replace the database during execution.
    fn execute_cleanup(
        &self,
        scope: StorageCleanupScope,
        preset: StorageCleanupPreset,
        plans: &CleanupPlans,
        plan_id: u32,
    ) -> Result<StorageCleanupExecution, SemanticProblem> {
        as_action_problem("execute_storage_cleanup", || {
            let Some(stored) = plans.take(plan_id, &self.paths.data_root, scope, preset) else {
                return self.execute_plan(&self.plan_cleanup(scope, preset)?);
            };
            let result = self.execute_plan(&stored.plan);
            if result.is_err() {
                plans.restore(stored);
            }
            result
        })
    }

    fn execute_plan(&self, plan: &CleanupPlan) -> anyhow::Result<StorageCleanupExecution> {
        match plan {
            CleanupPlan::Screenshots(plan) => cleanup::execute_screenshot_cleanup_plan(
                &self.paths.database_path,
                &self.paths.data_root,
                plan,
                &|| (self.cleanup_game_probe)(&self.paths.game_path),
            )
            .map(|result| StorageCleanupExecution::Screenshots { result }),
            CleanupPlan::RunData(plan) => cleanup::execute_run_data_cleanup_plan(
                &self.paths.database_path,
                &self.paths.data_root,
                plan,
                &|| (self.cleanup_game_probe)(&self.paths.game_path),
            )
            .map(|result| StorageCleanupExecution::RunData { result }),
        }
    }

    fn require_database_exists(&self) -> anyhow::Result<()> {
        self.paths
            .database_path
            .exists()
            .then_some(())
            .with_context(|| {
                format!(
                    "History database was not found at {}.",
                    self.paths.database_path.display()
                )
            })
    }
}

/// Resolve the installation under the data maintenance lock so cleanup cannot select a root being reset.
pub(crate) async fn execute_storage_cleanup_with(
    maintenance: &DataMaintenanceLock,
    plans: CleanupPlans,
    resolve_game_path: impl FnOnce() -> Option<PathBuf> + Send + 'static,
    scope: StorageCleanupScope,
    preset: StorageCleanupPreset,
    plan_id: u32,
) -> Result<StorageCleanupExecution, SemanticProblem> {
    maintenance
        .run_blocking(move || {
            History::at(resolve_game_path())?.execute_cleanup(scope, preset, &plans, plan_id)
        })
        .await
        .map_err(|error| {
            SemanticProblem::new(SemanticProblemCode::HistoryActionFailed)
                .with_param("operation", "execute_storage_cleanup")
                .with_error(&error)
        })?
}

fn history_paths_for_game_path(game_path: PathBuf) -> HistoryStorage {
    HistoryStorage {
        data_root: paths::bpp_data_dir(&game_path),
        combat_replay_videos_dir: paths::combat_replay_videos_dir(&game_path),
        database_path: paths::database_path(&game_path),
        game_path,
    }
}

fn as_action_problem<T>(
    operation: &str,
    action: impl FnOnce() -> anyhow::Result<T>,
) -> Result<T, SemanticProblem> {
    action().map_err(|error| {
        SemanticProblem::new(SemanticProblemCode::HistoryActionFailed)
            .with_param("operation", operation)
            .with_error(&error)
    })
}

fn history_read_problem_with(
    operation: &str,
    game_path: &Path,
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

    // A game process can outlive its window and hold the database open. Surface that actionable cause first.
    if is_game_running() {
        return SemanticProblem::new(SemanticProblemCode::HistoryReadBlockedByGame)
            .with_param("game_path", game_path.display().to_string())
            .with_param("operation", operation)
            .with_diagnostic(diagnostic);
    }

    SemanticProblem::new(SemanticProblemCode::HistoryReadFailed)
        .with_param("operation", operation)
        .with_diagnostic(diagnostic)
}

fn require_video_file_exists(path: &Path) -> anyhow::Result<()> {
    path.try_exists()
        .with_context(|| format!("Failed to inspect video file at {}", path.display()))?
        .then_some(())
        .with_context(|| format!("Video file was not found at {}.", path.display()))
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

fn reveal_in_file_browser(path: &Path) -> anyhow::Result<()> {
    #[cfg(target_os = "windows")]
    {
        use std::os::windows::process::CommandExt;

        let canonical = std::fs::canonicalize(path)
            .map(|buf| strip_extended_length_prefix(&buf.to_string_lossy()))
            .unwrap_or_else(|_| path.to_string_lossy().into_owned());

        Command::new("explorer")
            .raw_arg(format!("/select,\"{}\"", canonical))
            .spawn()
            .context("failed to reveal file in Explorer")?;
        Ok(())
    }

    #[cfg(target_os = "macos")]
    {
        Command::new("open")
            .args(["-R", &path.to_string_lossy()])
            .spawn()
            .context("failed to reveal file in Finder")?;
        Ok(())
    }

    #[cfg(all(not(target_os = "windows"), not(target_os = "macos")))]
    {
        let parent = path.parent().context("file parent directory is missing")?;
        Command::new("xdg-open")
            .arg(parent)
            .spawn()
            .context("failed to open file directory")?;
        Ok(())
    }
}

#[cfg(test)]
mod characterization_tests;

#[cfg(test)]
mod tests {
    use super::{
        history_thumbnails, require_video_file_exists, CleanupPlans, GameCardNames, History,
        HistoryThumbnails, RunQuery, StorageCleanupExecution, StorageCleanupPreset,
        StorageCleanupPreview, StorageCleanupScope,
    };
    use crate::history::cleanup::{RunDataCleanupPlan, ScreenshotCleanupPlan};
    use crate::history::mod_schema::create_mod_schema;
    use crate::problem::SemanticProblemCode;
    use crate::services::data_maintenance::DataMaintenanceLock;
    use crate::services::paths;
    use std::path::Path;

    #[test]
    fn history_page_list_uses_semantic_unavailable_and_read_failed_problems() {
        let unavailable = History::at(None).err().unwrap();
        assert_eq!(unavailable.code, SemanticProblemCode::HistoryUnavailable);
        assert!(unavailable.params.is_empty());
        assert_eq!(unavailable.diagnostic, None);

        let temp = tempfile::tempdir().unwrap();
        let game_path = temp.path().join("The Bazaar");
        let history = History::at_with(Some(game_path.clone()), |_| false).unwrap();
        std::fs::create_dir_all(history.paths.database_path.parent().unwrap()).unwrap();
        std::fs::write(&history.paths.database_path, b"not sqlite").unwrap();

        let read_failed = history
            .list_runs_for_page(
                50,
                0,
                &RunQuery::default(),
                &HistoryThumbnails::default(),
                &GameCardNames::none(),
            )
            .unwrap_err();
        assert_eq!(read_failed.code, SemanticProblemCode::HistoryReadFailed);
        assert_eq!(
            read_failed.params.get("operation").map(String::as_str),
            Some("list_runs")
        );
        assert!(read_failed.diagnostic.is_some());
    }

    #[test]
    fn history_pages_map_unsupported_schema_to_a_specific_problem() {
        let temp = tempfile::tempdir().unwrap();
        let game_path = temp.path().join("The Bazaar");
        let history = History::at(Some(game_path)).unwrap();
        std::fs::create_dir_all(history.paths.database_path.parent().unwrap()).unwrap();
        rusqlite::Connection::open(&history.paths.database_path).unwrap();

        for problem in [
            history
                .list_runs_for_page(
                    50,
                    0,
                    &RunQuery::default(),
                    &HistoryThumbnails::default(),
                    &GameCardNames::none(),
                )
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
        let history = History::at(Some(game_path)).unwrap();
        std::fs::create_dir_all(history.paths.database_path.parent().unwrap()).unwrap();
        rusqlite::Connection::open(&history.paths.database_path).unwrap();

        let problem = history
            .preview_cleanup(
                StorageCleanupScope::RunData,
                StorageCleanupPreset::All,
                &CleanupPlans::default(),
            )
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
        let unavailable = History::at(None).err().unwrap();
        assert_eq!(unavailable.code, SemanticProblemCode::HistoryUnavailable);

        let temp = tempfile::tempdir().unwrap();
        let game_path = temp.path().join("The Bazaar");
        let history = History::at_with(Some(game_path.clone()), |_| false).unwrap();
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
                history
                    .battle_video_path("battle-1", "video-1")
                    .unwrap_err(),
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
    fn video_file_exists_accepts_existing_file_and_rejects_missing_file() {
        let dir = tempfile::tempdir().expect("tempdir");
        let existing = dir.path().join("battle.mp4");
        std::fs::write(&existing, b"video").expect("write video file");

        assert!(require_video_file_exists(&existing).is_ok());
        let missing = dir.path().join("missing.mp4");
        assert_eq!(
            require_video_file_exists(&missing).unwrap_err().to_string(),
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
        create_mod_schema(&conn);
        conn.execute_batch(
            "
            insert into runs (
                run_id, started_at_ms, last_seen_at_ms, status,
                hero, game_mode, ended_at_ms, victories, losses
            ) values (
                'run-1', 1767258000000, 1767261600000,
                'completed', 'Vanessa', 'Normal', 1767261600000, 10, 2
            );
            insert into battles (
                battle_id, source, run_id, recorded_at_ms, combat_kind, player_name,
                opponent_hero, opponent_name, result, local_payload_state
            ) values (
                'battle-1', 'LOCAL', 'run-1', 1767259800000, 'PVP', 'Player',
                'Dooley', 'Opponent', 'win', 'missing'
            );
            insert into run_screenshots (
                screenshot_id, run_id, capture_source, is_primary,
                image_relative_path, captured_at_ms, captured_utc_offset_minutes
            ) values (
                'shot-1', 'run-1', 'end_of_run_auto', 1,
                '2026-01-01/run.png', 1767261600000,
                480
            );
            insert into combat_replay_videos (
                video_id, battle_id, source, video_relative_path, width, height, fps, codec,
                started_at_ms, file_size_bytes, status
            ) values (
                'video-1', 'battle-1', 'GAME_CAPTURE', '2026-01-01/battle.mp4', 1920, 1080, 60,
                'h264', 1767259800000, 5, 'COMPLETED'
            );
            ",
        )
        .unwrap();
        drop(conn);

        let history = History::at_with(Some(game_path.clone()), |_| false).unwrap();
        let thumbnails = HistoryThumbnails::default();
        thumbnails.register(Path::new("/another/installation"));
        let list = history
            .list_runs_for_page(
                50,
                0,
                &RunQuery::default(),
                &thumbnails,
                &GameCardNames::none(),
            )
            .unwrap();
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
            history.battle_video_path("battle-1", "video-1").unwrap(),
            video_path
        );

        let detail = history.delete_battle_video("battle-1", "video-1").unwrap();
        assert_eq!(detail.battles[0].video, None);
        assert!(!video_path.exists());

        let plans = CleanupPlans::default();
        let preview = history
            .preview_cleanup(
                StorageCleanupScope::Screenshots,
                StorageCleanupPreset::All,
                &plans,
            )
            .unwrap();
        let StorageCleanupPreview::Screenshots { preview, plan_id } = preview else {
            panic!("expected screenshot preview");
        };
        assert_eq!(preview.screenshots, 1);
        let execution = history
            .execute_cleanup(
                StorageCleanupScope::Screenshots,
                StorageCleanupPreset::All,
                &plans,
                plan_id,
            )
            .unwrap();
        let StorageCleanupExecution::Screenshots { result } = execution else {
            panic!("expected screenshot result");
        };
        assert_eq!(result.deleted_rows, 1);
        assert!(!screenshot_path.exists());

        let preview = history
            .preview_cleanup(
                StorageCleanupScope::RunData,
                StorageCleanupPreset::All,
                &plans,
            )
            .unwrap();
        let StorageCleanupPreview::RunData { preview, plan_id } = preview else {
            panic!("expected run-data preview");
        };
        assert_eq!(preview.runs, 1);
        assert_eq!(preview.battles, 1);

        let conn = rusqlite::Connection::open(&database_path).unwrap();
        conn.execute_batch(
            "
            insert into runs (
                run_id, started_at_ms, last_seen_at_ms, status,
                hero, game_mode, ended_at_ms, victories, losses
            ) values (
                'run-2', 1767344400000, 1767348000000,
                'completed', 'Dooley', 'Normal', 1767348000000, 4, 3
            );
            ",
        )
        .unwrap();
        drop(conn);
        let run_count = || {
            history
                .list_runs_for_page(
                    50,
                    0,
                    &RunQuery::default(),
                    &HistoryThumbnails::default(),
                    &GameCardNames::none(),
                )
                .unwrap()
                .summary
                .runs
        };

        let execution = history
            .execute_cleanup(
                StorageCleanupScope::RunData,
                StorageCleanupPreset::All,
                &plans,
                plan_id,
            )
            .unwrap();
        let StorageCleanupExecution::RunData { result } = execution else {
            panic!("expected run-data result");
        };
        assert_eq!(result.deleted_runs, 1);
        assert_eq!(run_count(), 1);

        let execution = history
            .execute_cleanup(
                StorageCleanupScope::RunData,
                StorageCleanupPreset::All,
                &plans,
                plan_id,
            )
            .unwrap();
        let StorageCleanupExecution::RunData { result } = execution else {
            panic!("expected run-data result");
        };
        assert_eq!(result.deleted_runs, 1);
        assert_eq!(run_count(), 0);
    }

    #[tokio::test]
    async fn cleanup_reuses_a_preview_plan_only_for_its_installation_scope_and_preset() {
        let maintenance = DataMaintenanceLock::default();
        let plans = CleanupPlans::new(maintenance.clone());
        let root = Path::new("/game/BazaarPlusPlusV6");
        let store = |scope| {
            plans.store(
                root,
                scope,
                StorageCleanupPreset::All,
                plans.database_generation(root),
                match scope {
                    StorageCleanupScope::Screenshots => {
                        super::CleanupPlan::Screenshots(ScreenshotCleanupPlan::empty())
                    }
                    StorageCleanupScope::RunData => {
                        super::CleanupPlan::RunData(RunDataCleanupPlan::empty())
                    }
                },
            )
        };

        let older = store(StorageCleanupScope::Screenshots);
        let latest = store(StorageCleanupScope::Screenshots);
        assert_ne!(older, latest);
        assert!(plans
            .take(
                older,
                root,
                StorageCleanupScope::Screenshots,
                StorageCleanupPreset::All
            )
            .is_none());
        assert!(plans
            .take(
                latest,
                root,
                StorageCleanupScope::Screenshots,
                StorageCleanupPreset::All
            )
            .is_some());
        assert!(plans
            .take(
                latest,
                root,
                StorageCleanupScope::Screenshots,
                StorageCleanupPreset::All
            )
            .is_none());

        for (other_root, scope, preset) in [
            (
                Path::new("/other/BazaarPlusPlusV6"),
                StorageCleanupScope::RunData,
                StorageCleanupPreset::All,
            ),
            (
                root,
                StorageCleanupScope::Screenshots,
                StorageCleanupPreset::All,
            ),
            (
                root,
                StorageCleanupScope::RunData,
                StorageCleanupPreset::OlderThan7Days,
            ),
        ] {
            let id = store(StorageCleanupScope::RunData);
            assert!(plans.take(id, other_root, scope, preset).is_none());
            assert!(plans
                .take(
                    id,
                    root,
                    StorageCleanupScope::RunData,
                    StorageCleanupPreset::All
                )
                .is_none());
        }

        let take = |id| {
            plans.take(
                id,
                root,
                StorageCleanupScope::RunData,
                StorageCleanupPreset::All,
            )
        };
        let id = store(StorageCleanupScope::RunData);
        plans.restore(take(id).unwrap());
        let failed = take(id).unwrap();
        let newer = store(StorageCleanupScope::RunData);
        plans.restore(failed);
        assert!(take(id).is_none());
        assert!(take(newer).is_some());

        let id = store(StorageCleanupScope::RunData);
        maintenance
            .run_blocking_with_overlay_stopped(
                &crate::stream::runtime::StreamRuntime::default(),
                Path::new("/game"),
                || (),
            )
            .await
            .unwrap();
        assert!(take(id).is_none());

        let id = store(StorageCleanupScope::RunData);
        maintenance
            .run_blocking_with_overlay_stopped(
                &crate::stream::runtime::StreamRuntime::default(),
                Path::new("/other"),
                || (),
            )
            .await
            .unwrap();
        assert!(take(id).is_some());

        let during = {
            let plans = plans.clone();
            maintenance
                .run_blocking_with_overlay_stopped(
                    &crate::stream::runtime::StreamRuntime::default(),
                    Path::new("/game"),
                    move || {
                        plans.store(
                            root,
                            StorageCleanupScope::RunData,
                            StorageCleanupPreset::All,
                            plans.database_generation(root),
                            super::CleanupPlan::RunData(RunDataCleanupPlan::empty()),
                        )
                    },
                )
                .await
                .unwrap()
        };
        assert!(take(during).is_none());
    }

    #[test]
    fn a_refused_cleanup_keeps_the_preview_plan_for_its_retry() {
        let temp = tempfile::tempdir().unwrap();
        let game_path = temp.path().join("The Bazaar");
        let database_path = paths::database_path(&game_path);
        std::fs::create_dir_all(database_path.parent().unwrap()).unwrap();
        create_mod_schema(&rusqlite::Connection::open(&database_path).unwrap());
        let insert_run = |run_id: &str| {
            rusqlite::Connection::open(&database_path)
                .unwrap()
                .execute(
                    "insert into runs (
                        run_id, started_at_ms, last_seen_at_ms, status,
                        hero, game_mode, ended_at_ms, victories, losses
                    ) values (?1, 1767258000000, 1767261600000,
                        'completed', 'Vanessa', 'Normal', 1767261600000, 3, 3)",
                    [run_id],
                )
                .unwrap();
        };
        insert_run("run-1");
        let plans = CleanupPlans::default();
        let running = History::at_with(Some(game_path.clone()), |_| false)
            .unwrap()
            .with_cleanup_game_probe(|_| Ok(true));
        let StorageCleanupPreview::RunData { preview, plan_id } = running
            .preview_cleanup(
                StorageCleanupScope::RunData,
                StorageCleanupPreset::All,
                &plans,
            )
            .unwrap()
        else {
            panic!("expected run-data preview");
        };
        assert_eq!(preview.runs, 1);
        insert_run("run-2");

        let refused = running
            .execute_cleanup(
                StorageCleanupScope::RunData,
                StorageCleanupPreset::All,
                &plans,
                plan_id,
            )
            .unwrap_err();
        assert_eq!(refused.code, SemanticProblemCode::HistoryActionFailed);

        let closed = History::at_with(Some(game_path), |_| false)
            .unwrap()
            .with_cleanup_game_probe(|_| Ok(false));
        let StorageCleanupExecution::RunData { result } = closed
            .execute_cleanup(
                StorageCleanupScope::RunData,
                StorageCleanupPreset::All,
                &plans,
                plan_id,
            )
            .unwrap()
        else {
            panic!("expected run-data result");
        };
        assert_eq!(result.deleted_runs, 1);
        assert_eq!(
            closed
                .list_runs_for_page(
                    50,
                    0,
                    &RunQuery::default(),
                    &HistoryThumbnails::default(),
                    &GameCardNames::none()
                )
                .unwrap()
                .summary
                .runs,
            1,
            "the run recorded after the preview survives"
        );
    }
}

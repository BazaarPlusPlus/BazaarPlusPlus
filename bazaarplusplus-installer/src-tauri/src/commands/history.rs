use crate::problem::{run_blocking, SemanticProblem, SemanticProblemCode};
use crate::services::data_maintenance::DataMaintenanceLock;
use crate::services::history::{
    execute_storage_cleanup_with, CleanupPlans, History, HistoryRunDetail, HistoryRunList,
    StorageCleanupExecution, StorageCleanupPreset, StorageCleanupPreview, StorageCleanupScope,
};
use crate::stream::history_thumbnails::HistoryThumbnails;
use tauri::Manager;

/// The problem a history command reports when its blocking task panics,
/// under the same code and `operation` its own failures use.
fn task_failed(
    code: SemanticProblemCode,
    operation: &'static str,
) -> impl FnOnce(anyhow::Error) -> SemanticProblem {
    move |error| {
        SemanticProblem::new(code)
            .with_param("operation", operation)
            .with_error(&error)
    }
}

#[tauri::command]
#[specta::specta]
pub async fn list_history_runs(
    app: tauri::AppHandle,
    limit: usize,
    offset: usize,
) -> Result<HistoryRunList, SemanticProblem> {
    run_blocking(
        task_failed(SemanticProblemCode::HistoryReadFailed, "list_runs"),
        move || {
            History::selected(&app)?.list_runs_for_page(
                limit,
                offset,
                &app.state::<HistoryThumbnails>(),
            )
        },
    )
    .await
}

#[tauri::command]
#[specta::specta]
pub async fn get_history_run_detail(
    app: tauri::AppHandle,
    run_id: String,
) -> Result<Option<HistoryRunDetail>, SemanticProblem> {
    run_blocking(
        task_failed(SemanticProblemCode::HistoryReadFailed, "get_run_detail"),
        move || History::selected(&app)?.run_detail_for_page(&run_id),
    )
    .await
}

#[tauri::command]
#[specta::specta]
pub async fn reveal_run_screenshot(
    app: tauri::AppHandle,
    run_id: String,
) -> Result<(), SemanticProblem> {
    run_blocking(
        task_failed(
            SemanticProblemCode::HistoryActionFailed,
            "reveal_screenshot",
        ),
        move || History::selected(&app)?.reveal_run_screenshot(&run_id),
    )
    .await
}

#[tauri::command]
#[specta::specta]
pub async fn reveal_battle_video(
    app: tauri::AppHandle,
    battle_id: String,
    video_id: String,
) -> Result<(), SemanticProblem> {
    run_blocking(
        task_failed(SemanticProblemCode::HistoryActionFailed, "reveal_video"),
        move || History::selected(&app)?.reveal_battle_video(&battle_id, &video_id),
    )
    .await
}

#[tauri::command]
#[specta::specta]
pub async fn delete_battle_video(
    app: tauri::AppHandle,
    battle_id: String,
    video_id: String,
) -> Result<HistoryRunDetail, SemanticProblem> {
    run_blocking(
        task_failed(SemanticProblemCode::HistoryActionFailed, "delete_video"),
        move || History::selected(&app)?.delete_battle_video(&battle_id, &video_id),
    )
    .await
}

#[tauri::command]
#[specta::specta]
pub async fn preview_storage_cleanup(
    app: tauri::AppHandle,
    scope: StorageCleanupScope,
    preset: StorageCleanupPreset,
) -> Result<StorageCleanupPreview, SemanticProblem> {
    run_blocking(
        task_failed(
            SemanticProblemCode::HistoryActionFailed,
            "preview_storage_cleanup",
        ),
        move || {
            History::selected(&app)?.preview_cleanup(scope, preset, &app.state::<CleanupPlans>())
        },
    )
    .await
}

/// Executes the cleanup the user confirmed. `plan_id` is the id the preview
/// returned; when that plan is gone the cleanup plans again from `scope` and
/// `preset`.
#[tauri::command]
#[specta::specta]
pub async fn execute_storage_cleanup(
    app: tauri::AppHandle,
    scope: StorageCleanupScope,
    preset: StorageCleanupPreset,
    plan_id: u32,
) -> Result<StorageCleanupExecution, SemanticProblem> {
    // The installation resolves only inside the data maintenance lock, so a
    // cleanup never acts on a data root a Reset is deleting.
    let maintenance = app.state::<DataMaintenanceLock>().inner().clone();
    let plans = app.state::<CleanupPlans>().inner().clone();
    execute_storage_cleanup_with(
        &maintenance,
        plans,
        move || History::resolved_game_path(&app),
        scope,
        preset,
        plan_id,
    )
    .await
}

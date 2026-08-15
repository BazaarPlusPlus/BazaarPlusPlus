use crate::problem::SemanticProblem;
use crate::services::history::{
    self, HistoryRunDetail, HistoryRunList, StorageCleanupExecution, StorageCleanupPreset,
    StorageCleanupPreview, StorageCleanupScope,
};

#[tauri::command]
#[specta::specta]
pub fn list_history_runs(
    app: tauri::AppHandle,
    limit: Option<usize>,
) -> Result<HistoryRunList, SemanticProblem> {
    history::list_runs(&app, limit)
}

#[tauri::command]
#[specta::specta]
pub fn get_history_run_detail(
    app: tauri::AppHandle,
    run_id: String,
) -> Result<Option<HistoryRunDetail>, SemanticProblem> {
    history::get_run_detail(&app, &run_id)
}

#[tauri::command]
#[specta::specta]
pub fn reveal_run_screenshot(app: tauri::AppHandle, run_id: String) -> Result<(), SemanticProblem> {
    history::reveal_run_screenshot(&app, &run_id)
}

#[tauri::command]
#[specta::specta]
pub fn reveal_battle_video(
    app: tauri::AppHandle,
    battle_id: String,
    video_id: Option<String>,
) -> Result<(), SemanticProblem> {
    history::reveal_battle_video(&app, &battle_id, video_id.as_deref())
}

#[tauri::command]
#[specta::specta]
pub fn delete_battle_video(
    app: tauri::AppHandle,
    battle_id: String,
    video_id: String,
) -> Result<HistoryRunDetail, SemanticProblem> {
    history::delete_battle_video(&app, &battle_id, &video_id)
}

#[tauri::command]
#[specta::specta]
pub fn delete_run_videos(
    app: tauri::AppHandle,
    run_id: String,
    limit: Option<usize>,
) -> Result<HistoryRunList, String> {
    history::delete_run_videos(&app, &run_id, limit)
}

#[tauri::command]
#[specta::specta]
pub fn preview_storage_cleanup(
    app: tauri::AppHandle,
    scope: StorageCleanupScope,
    preset: StorageCleanupPreset,
) -> Result<StorageCleanupPreview, SemanticProblem> {
    history::preview_storage_cleanup(&app, scope, preset)
}

#[tauri::command]
#[specta::specta]
pub fn execute_storage_cleanup(
    app: tauri::AppHandle,
    scope: StorageCleanupScope,
    preset: StorageCleanupPreset,
) -> Result<StorageCleanupExecution, SemanticProblem> {
    history::execute_storage_cleanup(&app, scope, preset)
}

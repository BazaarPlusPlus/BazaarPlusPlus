use crate::history::HistoryRunDetail;
use crate::services::history::{
    delete_battle_video as delete_battle_video_service,
    delete_run_videos as delete_run_videos_service, empty_history_list, get_run_detail, list_runs,
    require_history_paths, reveal_battle_video as reveal_battle_video_file,
    reveal_run_screenshot as reveal_run_screenshot_file,
};
use crate::stream::state::StreamRuntimeState;

#[tauri::command]
pub fn list_history_runs(
    app: tauri::AppHandle,
    state: tauri::State<'_, StreamRuntimeState>,
    game_path: Option<String>,
    limit: Option<usize>,
) -> Result<crate::history::HistoryRunList, String> {
    let Some(paths) =
        crate::services::history::resolve_history_paths(&app, state.get_game_path(), game_path)
    else {
        return Ok(empty_history_list());
    };

    list_runs(&paths.database_path, limit.unwrap_or(50).clamp(1, 200))
}

#[tauri::command]
pub fn get_history_run_detail(
    app: tauri::AppHandle,
    state: tauri::State<'_, StreamRuntimeState>,
    game_path: Option<String>,
    run_id: String,
) -> Result<HistoryRunDetail, String> {
    let paths = require_history_paths(&app, state.get_game_path(), game_path)?;
    get_run_detail(&paths.database_path, &run_id)
}

#[tauri::command]
pub fn reveal_run_screenshot(
    app: tauri::AppHandle,
    state: tauri::State<'_, StreamRuntimeState>,
    game_path: Option<String>,
    run_id: String,
) -> Result<(), String> {
    let paths = require_history_paths(&app, state.get_game_path(), game_path)?;
    reveal_run_screenshot_file(&paths.database_path, &paths.game_path, &run_id)
}

#[tauri::command]
pub fn reveal_battle_video(
    app: tauri::AppHandle,
    state: tauri::State<'_, StreamRuntimeState>,
    game_path: Option<String>,
    battle_id: String,
    video_id: Option<String>,
) -> Result<(), String> {
    let paths = require_history_paths(&app, state.get_game_path(), game_path)?;
    reveal_battle_video_file(
        &paths.database_path,
        &paths.combat_replay_videos_dir,
        &battle_id,
        video_id.as_deref(),
    )
}

#[tauri::command]
pub fn delete_battle_video(
    app: tauri::AppHandle,
    state: tauri::State<'_, StreamRuntimeState>,
    game_path: Option<String>,
    battle_id: String,
    video_id: String,
) -> Result<HistoryRunDetail, String> {
    let paths = require_history_paths(&app, state.get_game_path(), game_path)?;
    delete_battle_video_service(
        &paths.database_path,
        &paths.combat_replay_videos_dir,
        &battle_id,
        &video_id,
    )
}

#[tauri::command]
pub fn delete_run_videos(
    app: tauri::AppHandle,
    state: tauri::State<'_, StreamRuntimeState>,
    game_path: Option<String>,
    run_id: String,
    limit: Option<usize>,
) -> Result<crate::history::HistoryRunList, String> {
    let paths = require_history_paths(&app, state.get_game_path(), game_path)?;
    delete_run_videos_service(
        &paths.database_path,
        &paths.combat_replay_videos_dir,
        &run_id,
        limit.unwrap_or(50).clamp(1, 200),
    )
}

use crate::services::{
    path::normalize_requested_game_path, stream_window::apply_stream_window_offset,
};
use crate::stream::{
    overlay_settings::{
        OverlayCropSettings, OverlayCropSettingsPayload, OverlayDisplayMode, OverlaySettingsStore,
    },
    state::{StreamRuntimeState, StreamServiceStatus},
};

#[tauri::command]
pub fn get_stream_status(
    state: tauri::State<'_, StreamRuntimeState>,
) -> Result<StreamServiceStatus, String> {
    Ok(state.snapshot())
}

#[tauri::command]
pub async fn ensure_stream_session(
    app: tauri::AppHandle,
    state: tauri::State<'_, StreamRuntimeState>,
    game_path: Option<String>,
) -> Result<StreamServiceStatus, String> {
    crate::stream::server::start(app, state.inner(), normalize_requested_game_path(game_path)).await
}

#[tauri::command]
pub async fn restart_stream_session(
    app: tauri::AppHandle,
    state: tauri::State<'_, StreamRuntimeState>,
    game_path: Option<String>,
) -> Result<StreamServiceStatus, String> {
    crate::stream::server::restart(app, state.inner(), normalize_requested_game_path(game_path))
        .await
}

#[tauri::command]
pub fn set_stream_window(
    app: tauri::AppHandle,
    state: tauri::State<'_, StreamRuntimeState>,
    game_path: Option<String>,
    offset: usize,
) -> Result<StreamServiceStatus, String> {
    apply_stream_window_offset(&app, state.inner(), game_path, offset)
}

#[tauri::command]
pub fn get_overlay_settings() -> Result<OverlayCropSettingsPayload, String> {
    OverlaySettingsStore::default().load_payload()
}

#[tauri::command]
pub fn apply_overlay_crop_code(code: String) -> Result<OverlayCropSettingsPayload, String> {
    OverlaySettingsStore::default().import_code(&code)
}

#[tauri::command]
pub fn save_overlay_display_mode(
    display_mode: OverlayDisplayMode,
) -> Result<OverlayCropSettingsPayload, String> {
    OverlaySettingsStore::default().save_display_mode(display_mode)
}

#[tauri::command]
pub fn reset_overlay_crop() -> Result<OverlayCropSettingsPayload, String> {
    OverlaySettingsStore::default().save(OverlayCropSettings::default())
}

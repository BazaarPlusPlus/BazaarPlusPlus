use crate::problem::{SemanticProblem, SemanticProblemCode};
use crate::services::path::normalize_requested_game_path;
use crate::stream::{
    overlay_settings::{
        OverlayCropSettings, OverlayCropSettingsPayload, OverlaySettingsStore,
        StreamOverlayDisplayMode,
    },
    runtime::StreamRuntime,
    state::StreamServiceStatus,
};

#[tauri::command]
#[specta::specta]
pub fn get_stream_status(
    runtime: tauri::State<'_, StreamRuntime>,
) -> Result<StreamServiceStatus, SemanticProblem> {
    Ok(runtime.snapshot())
}

#[tauri::command]
#[specta::specta]
pub async fn ensure_stream_session(
    app: tauri::AppHandle,
    runtime: tauri::State<'_, StreamRuntime>,
    game_path: Option<String>,
) -> Result<StreamServiceStatus, SemanticProblem> {
    runtime
        .ensure(app, normalize_requested_game_path(game_path))
        .await
        .map_err(|diagnostic| stream_service_problem("ensure", diagnostic))
}

#[tauri::command]
#[specta::specta]
pub async fn restart_stream_session(
    app: tauri::AppHandle,
    runtime: tauri::State<'_, StreamRuntime>,
    game_path: Option<String>,
) -> Result<StreamServiceStatus, SemanticProblem> {
    runtime
        .restart(app, normalize_requested_game_path(game_path))
        .await
        .map_err(|diagnostic| stream_service_problem("restart", diagnostic))
}

#[tauri::command]
#[specta::specta]
pub async fn set_stream_window(
    runtime: tauri::State<'_, StreamRuntime>,
    offset: usize,
) -> Result<StreamServiceStatus, SemanticProblem> {
    runtime
        .set_window(offset)
        .await
        .map_err(|diagnostic| stream_window_problem(offset, diagnostic))
}

#[tauri::command]
#[specta::specta]
pub fn get_overlay_settings() -> Result<OverlayCropSettingsPayload, SemanticProblem> {
    OverlaySettingsStore::default()
        .load_payload()
        .map_err(|diagnostic| stream_crop_problem("load", diagnostic))
}

#[tauri::command]
#[specta::specta]
pub fn apply_overlay_crop_code(
    code: String,
) -> Result<OverlayCropSettingsPayload, SemanticProblem> {
    OverlaySettingsStore::default()
        .import_code(&code)
        .map_err(|diagnostic| stream_crop_problem("apply_code", diagnostic))
}

#[tauri::command]
#[specta::specta]
pub fn save_overlay_display_mode(
    display_mode: StreamOverlayDisplayMode,
) -> Result<OverlayCropSettingsPayload, SemanticProblem> {
    OverlaySettingsStore::default()
        .save_display_mode(display_mode)
        .map_err(|diagnostic| stream_crop_problem("save_display_mode", diagnostic))
}

#[tauri::command]
#[specta::specta]
pub fn reset_overlay_crop() -> Result<OverlayCropSettingsPayload, SemanticProblem> {
    OverlaySettingsStore::default()
        .save(OverlayCropSettings::default())
        .map_err(|diagnostic| stream_crop_problem("reset", diagnostic))
}

fn stream_service_problem(operation: &str, diagnostic: String) -> SemanticProblem {
    SemanticProblem::new(SemanticProblemCode::StreamServiceFailed)
        .with_param("operation", operation)
        .with_diagnostic(diagnostic)
}

fn stream_window_problem(offset: usize, diagnostic: String) -> SemanticProblem {
    SemanticProblem::new(SemanticProblemCode::StreamWindowFailed)
        .with_param("operation", "set_window")
        .with_param("offset", offset.to_string())
        .with_diagnostic(diagnostic)
}

fn stream_crop_problem(operation: &str, diagnostic: String) -> SemanticProblem {
    SemanticProblem::new(SemanticProblemCode::StreamCropFailed)
        .with_param("operation", operation)
        .with_diagnostic(diagnostic)
}

#[cfg(test)]
mod tests {
    use super::{stream_crop_problem, stream_service_problem, stream_window_problem};
    use crate::problem::SemanticProblemCode;

    #[test]
    fn stream_command_failures_keep_capability_operation_and_diagnostic() {
        let service = stream_service_problem("restart", "port occupied".to_string());
        assert_eq!(service.code, SemanticProblemCode::StreamServiceFailed);
        assert_eq!(
            service.params.get("operation").map(String::as_str),
            Some("restart")
        );
        assert_eq!(service.diagnostic.as_deref(), Some("port occupied"));

        let window = stream_window_problem(3, "record missing".to_string());
        assert_eq!(window.code, SemanticProblemCode::StreamWindowFailed);
        assert_eq!(window.params.get("offset").map(String::as_str), Some("3"));

        let unsupported_schema = stream_window_problem(
            0,
            "Unsupported mod database schema: found=2, expected=1.".to_string(),
        );
        assert_eq!(
            unsupported_schema.code,
            SemanticProblemCode::StreamWindowFailed
        );

        let crop = stream_crop_problem("apply_code", "invalid code".to_string());
        assert_eq!(crop.code, SemanticProblemCode::StreamCropFailed);
        assert_eq!(
            crop.params.get("operation").map(String::as_str),
            Some("apply_code")
        );
    }
}

use crate::problem::{run_blocking, SemanticProblem, SemanticProblemCode};
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
) -> Result<StreamServiceStatus, SemanticProblem> {
    runtime
        .ensure(app)
        .await
        .map_err(|error| stream_service_problem("ensure", error))
}

#[tauri::command]
#[specta::specta]
pub async fn prepare_history_thumbnails(
    app: tauri::AppHandle,
    runtime: tauri::State<'_, StreamRuntime>,
) -> Result<(), SemanticProblem> {
    runtime
        .prepare_history_thumbnails(app)
        .await
        .map_err(history_thumbnails_problem)
}

#[tauri::command]
#[specta::specta]
pub async fn restart_stream_session(
    app: tauri::AppHandle,
    runtime: tauri::State<'_, StreamRuntime>,
) -> Result<StreamServiceStatus, SemanticProblem> {
    runtime
        .restart(app)
        .await
        .map_err(|error| stream_service_problem("restart", error))
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
        .map_err(|error| stream_window_problem(offset, error))
}

#[tauri::command]
#[specta::specta]
pub async fn get_overlay_settings() -> Result<OverlayCropSettingsPayload, SemanticProblem> {
    with_overlay_settings("load", |store| store.load_payload()).await
}

#[tauri::command]
#[specta::specta]
pub async fn apply_overlay_crop_code(
    code: String,
) -> Result<OverlayCropSettingsPayload, SemanticProblem> {
    with_overlay_settings("apply_code", move |store| store.import_code(&code)).await
}

#[tauri::command]
#[specta::specta]
pub async fn save_overlay_display_mode(
    display_mode: StreamOverlayDisplayMode,
) -> Result<OverlayCropSettingsPayload, SemanticProblem> {
    with_overlay_settings("save_display_mode", move |store| {
        store.save_display_mode(display_mode)
    })
    .await
}

#[tauri::command]
#[specta::specta]
pub async fn reset_overlay_crop() -> Result<OverlayCropSettingsPayload, SemanticProblem> {
    with_overlay_settings("reset", |store| store.save(OverlayCropSettings::default())).await
}

/// The settings file is read and written on the blocking pool, as the overlay's
/// own crop routes do through `run_record_task`.
async fn with_overlay_settings<F>(
    operation: &'static str,
    task: F,
) -> Result<OverlayCropSettingsPayload, SemanticProblem>
where
    F: FnOnce(OverlaySettingsStore) -> anyhow::Result<OverlayCropSettingsPayload> + Send + 'static,
{
    run_blocking(
        move |error| stream_crop_problem(operation, error),
        move || {
            task(OverlaySettingsStore::default())
                .map_err(|error| stream_crop_problem(operation, error))
        },
    )
    .await
}

fn stream_service_problem(operation: &str, error: anyhow::Error) -> SemanticProblem {
    SemanticProblem::new(SemanticProblemCode::StreamServiceFailed)
        .with_param("operation", operation)
        .with_error(&error)
}

/// The only failure of History Thumbnail preparation is a service start
/// failure; History names it on its own while the Stream status keeps the error.
fn history_thumbnails_problem(error: anyhow::Error) -> SemanticProblem {
    SemanticProblem::new(SemanticProblemCode::HistoryThumbnailsUnavailable)
        .with_param("operation", "prepare_history_thumbnails")
        .with_error(&error)
}

fn stream_window_problem(offset: usize, error: anyhow::Error) -> SemanticProblem {
    SemanticProblem::new(SemanticProblemCode::StreamWindowFailed)
        .with_param("operation", "set_window")
        .with_param("offset", offset.to_string())
        .with_error(&error)
}

fn stream_crop_problem(operation: &str, error: anyhow::Error) -> SemanticProblem {
    SemanticProblem::new(SemanticProblemCode::StreamCropFailed)
        .with_param("operation", operation)
        .with_error(&error)
}

#[cfg(test)]
mod tests {
    use super::{
        history_thumbnails_problem, stream_crop_problem, stream_service_problem,
        stream_window_problem,
    };
    use crate::problem::SemanticProblemCode;

    #[test]
    fn history_thumbnail_start_failure_is_its_own_problem_with_the_bind_diagnostic() {
        let problem = history_thumbnails_problem(anyhow::anyhow!("address already in use"));
        assert_eq!(
            problem.code,
            SemanticProblemCode::HistoryThumbnailsUnavailable
        );
        assert_eq!(
            problem.params.get("operation").map(String::as_str),
            Some("prepare_history_thumbnails")
        );
        assert_eq!(
            problem.diagnostic.as_deref(),
            Some("address already in use")
        );
    }

    #[test]
    fn stream_command_failures_keep_capability_operation_and_diagnostic() {
        let service = stream_service_problem("restart", anyhow::anyhow!("port occupied"));
        assert_eq!(service.code, SemanticProblemCode::StreamServiceFailed);
        assert_eq!(
            service.params.get("operation").map(String::as_str),
            Some("restart")
        );
        assert_eq!(service.diagnostic.as_deref(), Some("port occupied"));

        let window = stream_window_problem(3, anyhow::anyhow!("record missing"));
        assert_eq!(window.code, SemanticProblemCode::StreamWindowFailed);
        assert_eq!(window.params.get("offset").map(String::as_str), Some("3"));

        let crop = stream_crop_problem("apply_code", anyhow::anyhow!("invalid code"));
        assert_eq!(crop.code, SemanticProblemCode::StreamCropFailed);
        assert_eq!(
            crop.params.get("operation").map(String::as_str),
            Some("apply_code")
        );
    }
}

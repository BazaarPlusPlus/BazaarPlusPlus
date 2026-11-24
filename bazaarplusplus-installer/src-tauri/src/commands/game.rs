use crate::problem::error_diagnostic;
use crate::services::game_process;

/// End a game process that outlived its window. Both History and Install stall
/// on that state — one cannot read the mod database, the other refuses to touch
/// installed files — so the recovery lives outside either page.
///
/// Returns whether a process was actually terminated: finding none means the
/// state the caller was recovering from is already gone.
#[tauri::command]
#[specta::specta]
pub async fn end_game_process(app: tauri::AppHandle, game_path: String) -> Result<bool, String> {
    // Snapshotting and terminating processes are blocking system calls.
    tauri::async_runtime::spawn_blocking(move || {
        use crate::services::{
            game_path::GamePathAcceptance,
            selected_game_installation::SelectedGameInstallationState,
        };
        use tauri::Manager as _;
        let Some(target) = app.state::<SelectedGameInstallationState>().resolve(
            &app,
            None,
            GamePathAcceptance::DetectionPick,
        ) else {
            return Ok(false);
        };
        anyhow::ensure!(
            target.game_path == std::path::Path::new(&game_path),
            "Selected game changed. Refresh History and retry."
        );
        game_process::terminate_game(&target.game_path)
    })
    .await
    .map_err(|error| error.to_string())?
    .map_err(|error| error_diagnostic(&error))
}

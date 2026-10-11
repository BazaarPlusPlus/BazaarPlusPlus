pub use crate::services::install::*;

use tauri_plugin_dialog::DialogExt;

use crate::problem::SemanticProblem;
use crate::services::{
    install::{
        build_install_state, install, launch_game_via_steam, run_delete_legacy_root,
        run_get_legacy_data_state, run_reset_bepinex, run_reset_bpp_data, run_uninstall,
        InstallRequest,
    },
    legacy_data::{DeleteLegacyRootResult, LegacyDataState},
    startup::InstallerContextState,
};
use crate::stream::runtime::StreamRuntime;

#[tauri::command(async)]
#[specta::specta]
pub fn get_install_state(
    app: tauri::AppHandle,
    state: tauri::State<'_, InstallerContextState>,
    game_path: Option<String>,
) -> Result<InstallState, SemanticProblem> {
    build_install_state(app, state, game_path)
}

#[tauri::command]
#[specta::specta]
pub async fn choose_game_directory(
    app: tauri::AppHandle,
) -> Result<GameDirectorySelection, SemanticProblem> {
    let folder =
        tauri::async_runtime::spawn_blocking(move || app.dialog().file().blocking_pick_folder())
            .await
            .map_err(|err| format!("failed to open game directory picker: {err}"))
            .map_err(|diagnostic| {
                crate::services::install::install_action_problem("choose_directory", diagnostic)
            })?;
    let game_path = folder
        .and_then(|path| path.into_path().ok())
        .map(|path| path.to_string_lossy().into_owned());

    Ok(GameDirectorySelection { game_path })
}

#[tauri::command]
#[specta::specta]
pub async fn install_mod(
    app: tauri::AppHandle,
    game_path: String,
) -> Result<InstallState, SemanticProblem> {
    install(app, InstallRequest { game_path }).await
}

#[tauri::command]
#[specta::specta]
pub async fn reset_bpp_data(
    app: tauri::AppHandle,
    install_state: tauri::State<'_, InstallerContextState>,
    stream_runtime: tauri::State<'_, StreamRuntime>,
    game_path: String,
) -> Result<ResetBppDataResult, SemanticProblem> {
    run_reset_bpp_data(app, install_state, stream_runtime, game_path).await
}

#[tauri::command]
#[specta::specta]
pub async fn reset_bepinex(
    app: tauri::AppHandle,
    install_state: tauri::State<'_, InstallerContextState>,
    game_path: String,
) -> Result<ResetBepinexResult, SemanticProblem> {
    run_reset_bepinex(app, install_state, game_path).await
}

#[tauri::command]
#[specta::specta]
pub async fn get_legacy_data_state(
    app: tauri::AppHandle,
    game_path: Option<String>,
) -> Result<LegacyDataState, SemanticProblem> {
    run_get_legacy_data_state(app, game_path).await
}

/// Deletes one Legacy Root the user confirmed. `name` must be one of the
/// directory names `get_legacy_data_state` lists.
#[tauri::command]
#[specta::specta]
pub async fn delete_legacy_root(
    app: tauri::AppHandle,
    game_path: String,
    name: String,
) -> Result<DeleteLegacyRootResult, SemanticProblem> {
    run_delete_legacy_root(app, game_path, name).await
}

#[tauri::command]
#[specta::specta]
pub async fn uninstall_mod(
    app: tauri::AppHandle,
    state: tauri::State<'_, InstallerContextState>,
    game_path: String,
) -> Result<InstallState, SemanticProblem> {
    run_uninstall(app, state, game_path).await
}

#[tauri::command(async)]
#[specta::specta]
pub fn launch_game() -> Result<(), SemanticProblem> {
    launch_game_via_steam().map_err(|diagnostic| {
        crate::services::install::install_action_problem("launch", diagnostic)
    })
}

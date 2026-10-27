pub use crate::services::install::*;

use anyhow::Context as _;
use std::path::PathBuf;

use tauri::Manager;
use tauri_plugin_dialog::DialogExt;

use crate::problem::SemanticProblem;
use crate::services::{
    data_maintenance::DataMaintenanceLock,
    install::{
        detect_install_state, install, launch_game_via_steam, run_delete_legacy_root,
        run_get_legacy_data_presence, run_get_legacy_data_state, run_reset_bepinex,
        run_reset_bpp_data, run_uninstall, InstallRequest,
    },
    legacy_data::{DeleteLegacyRootResult, LegacyDataPresence, LegacyDataState},
};
use crate::stream::runtime::StreamRuntime;
use crate::v5_import::ImportV5HistoryResult;

#[tauri::command]
#[specta::specta]
pub async fn get_install_state(
    app: tauri::AppHandle,
    game_path: Option<String>,
) -> Result<InstallState, SemanticProblem> {
    detect_install_state(app, game_path).await
}

#[tauri::command]
#[specta::specta]
pub async fn choose_game_directory(
    app: tauri::AppHandle,
) -> Result<GameDirectorySelection, SemanticProblem> {
    let folder =
        tauri::async_runtime::spawn_blocking(move || app.dialog().file().blocking_pick_folder())
            .await
            .context("failed to open game directory picker")
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
    stream_runtime: tauri::State<'_, StreamRuntime>,
    game_path: String,
) -> Result<ResetBppDataResult, SemanticProblem> {
    run_reset_bpp_data(app, stream_runtime, game_path).await
}

#[tauri::command]
#[specta::specta]
pub async fn reset_bepinex(
    app: tauri::AppHandle,
    game_path: String,
) -> Result<ResetBepinexResult, SemanticProblem> {
    run_reset_bepinex(app, game_path).await
}

#[tauri::command]
#[specta::specta]
pub async fn get_legacy_data_state(
    app: tauri::AppHandle,
    game_path: Option<String>,
) -> Result<LegacyDataState, SemanticProblem> {
    run_get_legacy_data_state(app, game_path).await
}

/// Which Legacy Roots exist, without measuring them. History's unavailable
/// guidance needs only this; the Install page measures through
/// `get_legacy_data_state`.
#[tauri::command]
#[specta::specta]
pub async fn get_legacy_data_presence(
    app: tauri::AppHandle,
    game_path: Option<String>,
) -> Result<LegacyDataPresence, SemanticProblem> {
    run_get_legacy_data_presence(app, game_path).await
}

/// Imports `BazaarPlusPlusV5/` history into the current Data Root once
/// (ADR-0008) and returns the remeasured Legacy Roots. It never deletes V5.
#[tauri::command]
#[specta::specta]
pub async fn import_v5_history(
    app: tauri::AppHandle,
    stream_runtime: tauri::State<'_, StreamRuntime>,
    game_path: String,
) -> Result<ImportV5HistoryResult, SemanticProblem> {
    let maintenance = app.state::<DataMaintenanceLock>().inner().clone();
    crate::v5_import::import_v5_history(&maintenance, &stream_runtime, PathBuf::from(game_path))
        .await
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
    game_path: String,
) -> Result<InstallState, SemanticProblem> {
    run_uninstall(app, game_path).await
}

#[tauri::command]
#[specta::specta]
pub async fn launch_game() -> Result<(), SemanticProblem> {
    // Opening the Steam URL spawns a subprocess.
    let problem = |diagnostic: anyhow::Error| {
        crate::services::install::install_action_problem("launch", diagnostic)
    };
    crate::problem::run_blocking(problem, move || launch_game_via_steam().map_err(problem)).await
}

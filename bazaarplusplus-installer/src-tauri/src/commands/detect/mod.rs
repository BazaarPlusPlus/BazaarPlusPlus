mod dotnet;
mod game;
mod steam;

pub(crate) use dotnet::detect_dotnet as dotnet_detect_for_startup;
pub(crate) use game::is_valid_game_path;
pub(crate) use steam::detect_installation_paths;

use crate::commands::startup::InstallerContextState;
use game::{is_bepinex_installed, normalize_game_path, read_installed_bpp_version};
use serde::{Deserialize, Serialize};
use std::path::PathBuf;
use tauri::{AppHandle, State};

#[derive(Debug, Serialize, Deserialize, ts_rs::TS)]
#[ts(export)]
pub struct EnvironmentInfo {
    pub steam_path: Option<String>,
    pub steam_launch_options_supported: bool,
    pub game_path: Option<String>,
    pub game_path_valid: bool,
    pub dotnet_version: Option<String>,
    pub dotnet_ok: bool,
    pub bepinex_installed: bool,
    pub bpp_version: Option<String>,
    pub bundled_bpp_version: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize, ts_rs::TS)]
#[ts(export)]
pub struct DotnetInfo {
    pub dotnet_version: Option<String>,
    pub dotnet_ok: bool,
}

#[tauri::command]
pub fn detect_environment(
    app: AppHandle,
    state: State<'_, InstallerContextState>,
    game_path: Option<String>,
) -> Result<EnvironmentInfo, String> {
    crate::commands::debug_log!(
        "[detect_environment] start requested_game_path={:?}",
        game_path
    );
    // Read cached startup context. On first call this lazily initializes
    // (reads the bundled payload, probes .NET, and resolves Steam/game paths)
    // as a safety net; normal flow calls `initialize_installer_context` from
    // the frontend first so this lookup is just a cached read.
    let startup = state.get_or_initialize(&app);

    let requested_game_path = normalize_game_path(game_path);
    let steam_path = startup.steam_path.clone();
    let game_path = requested_game_path
        .clone()
        .or_else(|| startup.game_path.clone());
    let game_path_valid = game_path
        .as_ref()
        .map(|path| is_valid_game_path(path))
        .unwrap_or(false);
    let steam_launch_options_supported = startup.steam_launch_options_supported;
    let bpp_version = game_path
        .as_ref()
        .and_then(|path| read_installed_bpp_version(path));
    let bepinex_installed = game_path
        .as_ref()
        .map(|path| is_bepinex_installed(path))
        .unwrap_or(false);

    crate::commands::debug_log!(
        "[detect_environment] resolved steam_path={:?} game_path={:?} bepinex_installed={} bundled_bpp_version={:?}",
        steam_path.as_ref().map(|path| path.display().to_string()),
        game_path.as_ref().map(|path| path.display().to_string()),
        bepinex_installed,
        startup.bundled_bpp_version
    );

    Ok(EnvironmentInfo {
        steam_path: steam_path.map(|path| path.to_string_lossy().into_owned()),
        steam_launch_options_supported,
        game_path: game_path.map(|path| path.to_string_lossy().into_owned()),
        game_path_valid,
        dotnet_version: startup.dotnet.dotnet_version.clone(),
        dotnet_ok: startup.dotnet.dotnet_ok,
        bepinex_installed,
        bpp_version,
        bundled_bpp_version: startup.bundled_bpp_version.clone(),
    })
}

/// Returns true if the game installation is found at the given path.
#[tauri::command]
pub fn verify_game_path(path: String) -> bool {
    let base = PathBuf::from(&path);
    is_valid_game_path(&base)
}

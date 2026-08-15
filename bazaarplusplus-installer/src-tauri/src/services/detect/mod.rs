mod game;
mod steam;

pub(crate) use game::{is_bepinex_installed, is_valid_game_path};
pub(crate) use steam::detect_installation_paths;

use crate::services::game_path::GamePathAcceptance;
use crate::services::selected_game_installation::SelectedGameInstallationState;
use crate::services::startup::InstallerContextState;
use crate::services::vdf::SteamLaunchOptionsState;
use game::read_installed_bpp_version;
use tauri::{AppHandle, Manager, State};

#[derive(Debug, Clone)]
pub(crate) struct InstallEnvironmentSnapshot {
    pub steam_path: Option<String>,
    pub game_path: Option<String>,
    pub game_path_valid: bool,
    pub bepinex_installed: bool,
    pub bpp_version: Option<String>,
    pub bundled_bpp_version: Option<String>,
    pub steam_launch_options: SteamLaunchOptionsState,
    pub trampoline_current: bool,
    pub obsolete_macos_artifacts_present: bool,
}

pub fn detect_for_install(
    app: AppHandle,
    state: State<'_, InstallerContextState>,
    game_path: Option<String>,
) -> Result<InstallEnvironmentSnapshot, String> {
    crate::services::debug_log!(
        "[detect_for_install] start requested_game_path={:?}",
        game_path
    );
    // Read cached startup context. On first call this lazily initializes:
    // reads the bundled payload and resolves Steam/game paths.
    let startup = state.get_or_initialize(&app);

    let steam_path = startup.steam_path.clone();
    let game_path = app
        .state::<SelectedGameInstallationState>()
        .resolve(&app, game_path, GamePathAcceptance::DetectionPick)
        .map(|resolution| resolution.game_path);
    let game_path_valid = game_path
        .as_ref()
        .map(|path| is_valid_game_path(path))
        .unwrap_or(false);
    let bpp_version = game_path
        .as_ref()
        .and_then(|path| read_installed_bpp_version(path));
    let bepinex_installed = game_path
        .as_ref()
        .map(|path| is_bepinex_installed(path))
        .unwrap_or(false);

    // macOS bootstrap facts are recomputed on every detection so Steam Verify,
    // game updates, launch-option edits, and obsolete files immediately route
    // the UI to Repair. Other platforms have no Steam launch-option invariant.
    let trampoline_current = game_path
        .as_ref()
        .map(|path| crate::services::bepinex::is_current_trampoline(&app, path).unwrap_or(false))
        .unwrap_or(false);
    let obsolete_macos_artifacts_present = game_path
        .as_ref()
        .map(|path| crate::services::bepinex::obsolete_macos_artifacts_present(path))
        .unwrap_or(false);
    let steam_launch_options = if cfg!(target_os = "macos") {
        steam_path
            .as_deref()
            .map(crate::services::vdf::inspect_launch_options_for_steam)
            .unwrap_or(SteamLaunchOptionsState::Unavailable)
    } else {
        SteamLaunchOptionsState::Empty
    };

    crate::services::debug_log!(
        "[detect_for_install] resolved steam_path={:?} game_path={:?} bepinex_installed={} bundled_bpp_version={:?}",
        steam_path.as_ref().map(|path| path.display().to_string()),
        game_path.as_ref().map(|path| path.display().to_string()),
        bepinex_installed,
        startup.bundled_bpp_version
    );

    Ok(InstallEnvironmentSnapshot {
        steam_path: steam_path.map(|path| path.to_string_lossy().into_owned()),
        game_path: game_path.map(|path| path.to_string_lossy().into_owned()),
        game_path_valid,
        bepinex_installed,
        bpp_version,
        bundled_bpp_version: startup.bundled_bpp_version.clone(),
        steam_launch_options,
        trampoline_current,
        obsolete_macos_artifacts_present,
    })
}

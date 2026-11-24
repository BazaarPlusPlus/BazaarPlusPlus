mod game;
mod steam;

pub(crate) use game::{is_bepinex_installed, is_valid_game_path, read_installed_bpp_version};
pub(crate) use steam::detect_installation_paths;

use crate::services::game_path::GamePathAcceptance;
use crate::services::selected_game_installation::SelectedGameInstallationState;
use crate::services::startup::InstallerContextState;
use crate::services::vdf::SteamLaunchOptionsState;
use std::path::PathBuf;
use tauri::{AppHandle, Manager, State};

#[derive(Clone, Debug, PartialEq, Eq)]
pub(crate) enum BundleVerification {
    Verify,
    /// The operation now detecting sealed and verified the bundle in this game
    /// directory, and nothing it ran since writes inside the bundle.
    VerifiedIn(PathBuf),
}

#[derive(Debug, Clone)]
pub(crate) struct InstallEnvironmentSnapshot {
    pub steam_path: Option<String>,
    pub game_path: Option<String>,
    pub game_path_valid: bool,
    pub tempo_migration_pending: bool,
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
    verification: &BundleVerification,
) -> anyhow::Result<InstallEnvironmentSnapshot> {
    crate::services::debug_log!(
        "[detect_for_install] start requested_game_path={:?}",
        game_path
    );
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

    // Recheck macOS readiness each time because Steam can replace signed files or the user can edit launch options.
    let trampoline_current = game_path
        .as_ref()
        .map(|path| {
            let signature_verified =
                matches!(verification, BundleVerification::VerifiedIn(verified) if verified == path);
            crate::services::bepinex::is_current_trampoline(&app, path, signature_verified)
                .unwrap_or(false)
        })
        .unwrap_or(false);
    let obsolete_macos_artifacts_present = game_path
        .as_ref()
        .map(|path| crate::services::bepinex::obsolete_macos_artifacts_present(path))
        .unwrap_or(false);
    #[cfg(target_os = "macos")]
    let steam_launch_options = launch_options(game_path.as_deref(), steam_path.as_deref());
    #[cfg(not(target_os = "macos"))]
    let steam_launch_options = SteamLaunchOptionsState::Empty;

    crate::services::debug_log!(
        "[detect_for_install] resolved steam_path={:?} game_path={:?} bepinex_installed={} bundled_bpp_version={:?}",
        steam_path.as_ref().map(|path| path.display().to_string()),
        game_path.as_ref().map(|path| path.display().to_string()),
        bepinex_installed,
        startup.bundled_bpp_version
    );

    Ok(InstallEnvironmentSnapshot {
        steam_path: steam_path.map(|path| path.to_string_lossy().into_owned()),
        tempo_migration_pending: game_path
            .as_deref()
            .is_some_and(crate::services::paths::tempo_migration_pending),
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

#[cfg(target_os = "macos")]
pub(crate) fn launch_options(
    game_path: Option<&std::path::Path>,
    steam_path: Option<&std::path::Path>,
) -> SteamLaunchOptionsState {
    if game_path.and_then(super::tempo::library).is_some() {
        SteamLaunchOptionsState::Empty
    } else {
        steam_path
            .map(crate::services::vdf::inspect_launch_options_for_steam)
            .unwrap_or(SteamLaunchOptionsState::Unavailable)
    }
}

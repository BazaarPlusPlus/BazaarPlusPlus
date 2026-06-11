mod game;
mod steam;

pub(crate) use game::{is_bepinex_installed, is_valid_game_path};
pub(crate) use steam::detect_installation_paths;

use crate::services::game_path::fallback_game_candidates;
use crate::services::path::normalize_requested_game_path;
use crate::services::startup::InstallerContextState;
use game::read_installed_bpp_version;
use serde::{Deserialize, Serialize};
use tauri::{AppHandle, State};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub(crate) struct InstallEnvironmentSnapshot {
    pub steam_path: Option<String>,
    pub steam_launch_options_supported: bool,
    pub game_path: Option<String>,
    pub game_path_valid: bool,
    pub bepinex_installed: bool,
    pub bpp_version: Option<String>,
    pub bundled_bpp_version: Option<String>,
    /// macOS <= 26: the "兼容模式" checkbox is offered as an opt-in. False on 27+
    /// (forced) and off macOS.
    pub compat_mode_available: bool,
    /// macOS 27+: trampoline is the only working launch path (checkbox locked on).
    pub trampoline_forced: bool,
    /// The launch mode the install SHOULD be in: forced, or recorded by the
    /// `.bpp-launch-mode` marker. A missing marker means prefix on <= 26.
    pub trampoline_desired: bool,
    /// The launch mode the bundle is ACTUALLY in right now (recomputed per call so
    /// it never goes stale after an install / Repair / Steam "Verify integrity").
    pub trampoline_applied: bool,
}

pub fn detect_for_install(
    app: AppHandle,
    state: State<'_, InstallerContextState>,
    game_path: Option<String>,
) -> Result<InstallEnvironmentSnapshot, String> {
    crate::services::debug_log!(
        "[detect_environment] start requested_game_path={:?}",
        game_path
    );
    // Read cached startup context. On first call this lazily initializes:
    // reads the bundled payload and resolves Steam/game paths.
    let startup = state.get_or_initialize(&app);

    let requested_game_path = normalize_requested_game_path(game_path);
    let steam_path = startup.steam_path.clone();
    let game_path = requested_game_path
        .clone()
        .or_else(|| startup.game_path.clone())
        .or_else(|| {
            fallback_game_candidates()
                .into_iter()
                .find(|path| is_valid_game_path(path))
        });
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

    // Launch-mode (trampoline) detection. `trampoline_applied` is recomputed here
    // (NOT cached in startup) so it tracks the live bundle state after an install,
    // Repair, or Steam "Verify integrity". Err -> false is fail-safe for a bundle
    // mid-update / unreadable.
    let trampoline_forced = crate::services::macos_version::trampoline_forced();
    let compat_mode_available = crate::services::macos_version::compat_mode_available();
    let trampoline_applied = game_path
        .as_ref()
        .map(|path| crate::services::bepinex::is_trampolined(path).unwrap_or(false))
        .unwrap_or(false);
    let trampoline_desired = trampoline_forced
        || game_path
            .as_ref()
            .and_then(|path| crate::services::bepinex::read_launch_mode_marker(path))
            == Some(crate::services::bepinex::LaunchMode::Trampoline);

    crate::services::debug_log!(
        "[detect_environment] resolved steam_path={:?} game_path={:?} bepinex_installed={} bundled_bpp_version={:?}",
        steam_path.as_ref().map(|path| path.display().to_string()),
        game_path.as_ref().map(|path| path.display().to_string()),
        bepinex_installed,
        startup.bundled_bpp_version
    );

    Ok(InstallEnvironmentSnapshot {
        steam_path: steam_path.map(|path| path.to_string_lossy().into_owned()),
        steam_launch_options_supported,
        game_path: game_path.map(|path| path.to_string_lossy().into_owned()),
        game_path_valid,
        bepinex_installed,
        bpp_version,
        bundled_bpp_version: startup.bundled_bpp_version.clone(),
        compat_mode_available,
        trampoline_forced,
        trampoline_desired,
        trampoline_applied,
    })
}

pub fn detect_environment(
    app: AppHandle,
    state: State<'_, InstallerContextState>,
    game_path: Option<String>,
) -> Result<InstallEnvironmentSnapshot, String> {
    detect_for_install(app, state, game_path)
}

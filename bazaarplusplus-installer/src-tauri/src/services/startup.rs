use std::path::PathBuf;
use std::sync::{Arc, OnceLock};

use tauri::AppHandle;

use super::bepinex;
use super::detect::detect_installation_paths;

/// Startup-time installer context.
///
/// These inputs don't change during a session, so we read them once, cache the
/// result, and let every subsequent `resolve_game_path` or `detect_for_install`
/// call reuse the cached values. Before this split, each detection pass re-read
/// the bundled `BepInEx.zip`, which on Windows was a dominant source of
/// detect-flow latency because Defender scans the zip on every `CreateFile`.
pub(crate) struct InstallerStartup {
    pub(crate) bundled_bpp_version: Option<String>,
    pub(crate) steam_path: Option<PathBuf>,
    pub(crate) game_path: Option<PathBuf>,
}

#[derive(Default)]
pub struct InstallerContextState {
    inner: OnceLock<Arc<InstallerStartup>>,
}

impl InstallerContextState {
    pub(crate) fn get_or_initialize(&self, app: &AppHandle) -> Arc<InstallerStartup> {
        self.inner
            .get_or_init(|| Arc::new(compute_startup(app)))
            .clone()
    }
}

fn compute_startup(app: &AppHandle) -> InstallerStartup {
    let bundled_bpp_version = bepinex::read_bundled_bpp_version(app).ok().flatten();
    let detected_paths = detect_installation_paths();

    crate::services::debug_log!(
        "[startup] initialized bundled_bpp_version={:?} steam_path={:?} game_path={:?}",
        bundled_bpp_version,
        detected_paths
            .steam_path
            .as_ref()
            .map(|path| path.display().to_string()),
        detected_paths
            .game_path
            .as_ref()
            .map(|path| path.display().to_string()),
    );

    InstallerStartup {
        bundled_bpp_version,
        steam_path: detected_paths.steam_path,
        game_path: detected_paths.game_path,
    }
}

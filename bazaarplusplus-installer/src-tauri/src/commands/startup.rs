use std::path::PathBuf;
use std::sync::{Arc, OnceLock};

use serde::{Deserialize, Serialize};
use tauri::{AppHandle, Manager};

use super::bepinex;
use super::detect::{detect_installation_paths, dotnet_detect_for_startup, DotnetInfo};

/// Startup-time installer context.
///
/// These inputs don't change during a session, so we read them once, cache the
/// result, and let every subsequent `detect_environment` call reuse the cached
/// values. Before this split, each detect_environment re-read the bundled
/// `BepInEx.zip` and re-spawned a `dotnet --list-runtimes` subprocess, which on
/// Windows was the dominant source of detect-flow latency (Defender scans the
/// zip on every `CreateFile`; Windows `CreateProcess` is expensive).
pub(crate) struct InstallerStartup {
    pub(crate) bundled_bpp_version: Option<String>,
    pub(crate) dotnet: DotnetInfo,
    pub(crate) steam_path: Option<PathBuf>,
    pub(crate) game_path: Option<PathBuf>,
    pub(crate) steam_launch_options_supported: bool,
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
    let (dotnet_version, dotnet_ok) = dotnet_detect_for_startup();
    let detected_paths = detect_installation_paths();

    crate::commands::debug_log!(
        "[startup] initialized bundled_bpp_version={:?} dotnet_version={:?} dotnet_ok={} steam_path={:?} game_path={:?} launch_options_supported={}",
        bundled_bpp_version,
        dotnet_version,
        dotnet_ok,
        detected_paths
            .steam_path
            .as_ref()
            .map(|path| path.display().to_string()),
        detected_paths
            .game_path
            .as_ref()
            .map(|path| path.display().to_string()),
        detected_paths.steam_launch_options_supported,
    );

    InstallerStartup {
        bundled_bpp_version,
        dotnet: DotnetInfo {
            dotnet_version,
            dotnet_ok,
        },
        steam_path: detected_paths.steam_path,
        game_path: detected_paths.game_path,
        steam_launch_options_supported: detected_paths.steam_launch_options_supported,
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, ts_rs::TS)]
#[ts(export)]
pub struct InstallerContextPayload {
    pub bundled_bpp_version: Option<String>,
    pub dotnet_version: Option<String>,
    pub dotnet_ok: bool,
}

impl InstallerContextPayload {
    fn from_startup(startup: &InstallerStartup) -> Self {
        InstallerContextPayload {
            bundled_bpp_version: startup.bundled_bpp_version.clone(),
            dotnet_version: startup.dotnet.dotnet_version.clone(),
            dotnet_ok: startup.dotnet.dotnet_ok,
        }
    }
}

#[tauri::command]
pub async fn initialize_installer_context(
    app: AppHandle,
) -> Result<InstallerContextPayload, String> {
    let app_clone = app.clone();
    let startup = tauri::async_runtime::spawn_blocking(move || {
        let state = app_clone.state::<InstallerContextState>();
        state.get_or_initialize(&app_clone)
    })
    .await
    .map_err(|err| format!("failed to initialize installer context: {err}"))?;

    Ok(InstallerContextPayload::from_startup(startup.as_ref()))
}

mod types;

pub use types::{
    FileActionResult, GameDirectorySelection, InstallActions, InstallCompatState, InstallGameState,
    InstallModState, InstallState, InstallWarning, ResetBppDataResult,
};

use std::process::Command;

use tauri::Manager;

use std::path::Path;

use crate::services::{
    bepinex::{self, install_bepinex, reset_bpp_data, uninstall_bpp, LaunchMode},
    detect::detect_for_install,
    macos_version::use_trampoline,
    startup::InstallerContextState,
    steam::prepare_steam_for_launch_option_update,
    vdf::{clear_launch_options_for_steam, patch_launch_options},
};
use crate::stream::state::StreamRuntimeState;

const STEAM_BAZAAR_URL: &str = "steam://rungameid/1617400";

pub fn build_install_state(
    app: tauri::AppHandle,
    state: tauri::State<'_, InstallerContextState>,
    game_path: Option<String>,
) -> Result<InstallState, String> {
    crate::services::tempo::recover_orphaned_backups_best_effort();
    let snapshot = detect_for_install(app, state, game_path)?;
    Ok(install_state_from_snapshot(snapshot))
}

pub async fn run_install(
    app: tauri::AppHandle,
    state: tauri::State<'_, InstallerContextState>,
    game_path: String,
    compat_opt_in: bool,
) -> Result<InstallState, String> {
    let before = detect_for_install(app.clone(), state, Some(game_path.clone()))?;
    let detected_game_path = before
        .game_path
        .as_deref()
        .filter(|path| !path.trim().is_empty())
        .or(Some(game_path.as_str()));
    let launch_flow = resolve_launch_flow(before.steam_path.as_deref(), detected_game_path);
    let steam_path = if launch_flow == LaunchFlow::Steam {
        before.steam_path.clone().unwrap_or_default()
    } else {
        String::new()
    };
    let has_steam_path = launch_flow == LaunchFlow::Steam && !steam_path.trim().is_empty();

    // Version-forced on macOS 27+, or <= 26 opt-in. Always false off macOS.
    let wants_trampoline = use_trampoline(compat_opt_in);
    let was_trampolined = bepinex::is_trampolined(Path::new(&game_path)).unwrap_or(false);

    let app_for_task = app.clone();
    let game_path_for_task = game_path.clone();
    let patch_launch_options_supported = before.steam_launch_options_supported;
    tauri::async_runtime::spawn_blocking(move || {
        let steam = Path::new(&steam_path);
        let game = Path::new(&game_path_for_task);

        if wants_trampoline {
            // Trampoline mode MUTATES the .app and needs a reliable localconfig
            // clear -> Steam MUST be closed.
            if has_steam_path {
                prepare_steam_for_launch_option_update(steam, false)?;
            }
            install_bepinex(
                app_for_task.clone(),
                steam_path.clone(),
                game_path_for_task.clone(),
            )?;
            bepinex::install_trampoline(&app_for_task, game)?;
            // Persist the desired mode AS SOON AS the bundle is trampolined, before
            // the Steam step below — otherwise a clear-launch-options failure would
            // leave a trampolined bundle with no marker, which a later detect would
            // mislabel as `trampoline_reverted` on macOS <= 26.
            bepinex::write_launch_mode_marker(game, LaunchMode::Trampoline)?;
            // LaunchOptions are driven by the MODE: trampoline => cleared (the
            // empty/vanilla launch the stub needs).
            if has_steam_path {
                clear_launch_options_for_steam(steam)?;
            }
        } else {
            // Prefix mode. Close Steam ONLY to un-apply a previous trampoline (mode
            // switch); a plain <= 26 prefix install keeps today's behavior exactly
            // (Steam stays up; patch_launch_options does its own prepare(.., true)).
            if was_trampolined && has_steam_path {
                prepare_steam_for_launch_option_update(steam, false)?;
            }
            install_bepinex(
                app_for_task.clone(),
                steam_path.clone(),
                game_path_for_task.clone(),
            )?;
            if was_trampolined {
                bepinex::uninstall_trampoline(game)?;
            }
            if patch_launch_options_supported && has_steam_path {
                let _ = patch_launch_options(
                    app_for_task,
                    steam_path.clone(),
                    game_path_for_task.clone(),
                )?;
            }
            bepinex::write_launch_mode_marker(game, LaunchMode::Prefix)?;
        }
        Ok::<(), String>(())
    })
    .await
    .map_err(|err| format!("failed to run install task: {err}"))??;

    let app_for_state = app.clone();
    let state = app_for_state.state::<InstallerContextState>();
    build_install_state(app, state, Some(game_path))
}

pub async fn run_reset_bpp_data(
    app: tauri::AppHandle,
    install_state: tauri::State<'_, InstallerContextState>,
    stream_state: tauri::State<'_, StreamRuntimeState>,
    game_path: String,
) -> Result<ResetBppDataResult, String> {
    let removed_data = reset_bpp_data(stream_state, game_path.clone()).await?;
    let state = build_install_state(app, install_state, Some(game_path))?;
    Ok(ResetBppDataResult {
        state,
        removed_data,
    })
}

pub async fn run_uninstall(
    app: tauri::AppHandle,
    state: tauri::State<'_, InstallerContextState>,
    game_path: String,
) -> Result<InstallState, String> {
    let before = detect_for_install(app.clone(), state, Some(game_path.clone()))?;
    let app_for_task = app.clone();
    let detected_game_path = before
        .game_path
        .as_deref()
        .filter(|path| !path.trim().is_empty())
        .or(Some(game_path.as_str()));
    let launch_flow = resolve_launch_flow(before.steam_path.as_deref(), detected_game_path);
    let steam_path = if launch_flow == LaunchFlow::Steam {
        before.steam_path.clone().unwrap_or_default()
    } else {
        String::new()
    };
    let game_path_for_task = game_path.clone();
    tauri::async_runtime::spawn_blocking(move || {
        uninstall_bpp(app_for_task, steam_path, game_path_for_task)
    })
    .await
    .map_err(|err| format!("failed to run uninstall task: {err}"))??;

    let app_for_state = app.clone();
    let state = app_for_state.state::<InstallerContextState>();
    build_install_state(app, state, Some(game_path))
}

pub fn launch_game_via_steam() -> Result<(), String> {
    open_url(STEAM_BAZAAR_URL)
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum LaunchFlow {
    Steam,
    TempoNative,
}

/// Steam flow only when BOTH a Steam client is detected AND the resolved game
/// dir is a Steam copy (has a `steamapps` path component). Everything else,
/// including a Tempo-native copy on a machine that also has Steam, uses the
/// Tempo capture flow so files are never removed from one copy while Tempo
/// validates another.
pub(crate) fn resolve_launch_flow(steam_path: Option<&str>, game_path: Option<&str>) -> LaunchFlow {
    let under_steamapps = game_path.map(path_contains_steamapps).unwrap_or(false);
    if steam_path.is_some() && under_steamapps {
        LaunchFlow::Steam
    } else {
        LaunchFlow::TempoNative
    }
}

fn path_contains_steamapps(path: &str) -> bool {
    path.split(['/', '\\'])
        .any(|component| component.eq_ignore_ascii_case("steamapps"))
}

pub fn launch_game_auto(
    app: tauri::AppHandle,
    state: tauri::State<'_, InstallerContextState>,
    game_path: Option<String>,
) -> Result<(), String> {
    let snapshot = detect_for_install(app.clone(), state, game_path)?;
    match resolve_launch_flow(
        snapshot.steam_path.as_deref(),
        snapshot.game_path.as_deref(),
    ) {
        LaunchFlow::Steam => launch_game_via_steam(),
        LaunchFlow::TempoNative => {
            crate::services::tempo::launch_game_via_tempo(app, snapshot.game_path.clone(), None)
        }
    }
}

fn install_state_from_snapshot(
    env: crate::services::detect::InstallEnvironmentSnapshot,
) -> InstallState {
    let selected_game_path = env.game_path.clone();
    let game_found = selected_game_path.is_some();
    let installed = env.bepinex_installed;
    let plugin_version_matches = match (&env.bpp_version, &env.bundled_bpp_version) {
        (Some(installed_version), Some(bundled)) => installed_version == bundled,
        (None, _) => false,
        (_, None) => installed,
    };
    // The bundle's actual launch mode must match the desired one. A Steam "Verify
    // integrity"/game update that reverts the trampoline (or a macOS 26->27 upgrade
    // after a prefix install) leaves the plugin DLL version matching yet the launch
    // broken; folding consistency into `version_matches` routes the UI to Reinstall
    // (Repair). On non-macOS (and matched macOS) `trampoline_consistent` is true, so
    // this reduces to today's plugin-version check.
    let trampoline_consistent = env.trampoline_desired == env.trampoline_applied;
    let version_matches = plugin_version_matches && trampoline_consistent;
    let needs_trampoline_repair = installed && !trampoline_consistent;
    let can_launch = game_found && env.game_path_valid;
    let has_resettable_data = has_resettable_bpp_data(env.game_path.as_deref());
    let launch_flow = match resolve_launch_flow(env.steam_path.as_deref(), env.game_path.as_deref())
    {
        LaunchFlow::Steam => "steam".to_string(),
        LaunchFlow::TempoNative => "tempo".to_string(),
    };
    let mut warnings = Vec::new();
    if !game_found || !env.game_path_valid {
        warnings.push(InstallWarning {
            code: "game_missing".to_string(),
            message: "未找到有效的 The Bazaar 安装目录。".to_string(),
        });
    }
    if launch_flow == "steam" && !env.steam_launch_options_supported {
        warnings.push(InstallWarning {
            code: "launch_options_unsupported".to_string(),
            message: "当前平台或 Steam 目录不支持自动写入启动项。".to_string(),
        });
    }
    if needs_trampoline_repair {
        warnings.push(InstallWarning {
            code: "trampoline_reverted".to_string(),
            message: "检测到游戏文件已被还原，BazaarPlusPlus 的启动配置需要修复，请点击重新安装。"
                .to_string(),
        });
    }

    InstallState {
        selected_game_path,
        steam_path: env.steam_path,
        steam_launch_options_supported: env.steam_launch_options_supported,
        launch_flow,
        game: InstallGameState {
            found: game_found,
            path_valid: env.game_path_valid,
            display_version: None,
        },
        mod_state: InstallModState {
            installed,
            installed_version: env.bpp_version,
            bundled_version: env.bundled_bpp_version,
            version_matches,
        },
        compat: InstallCompatState {
            mode_available: env.compat_mode_available,
            forced: env.trampoline_forced,
            desired: env.trampoline_desired,
            applied: env.trampoline_applied,
        },
        actions: InstallActions {
            can_install: can_launch && !installed,
            can_reinstall: can_launch && installed,
            can_reset_data: can_launch && has_resettable_data,
            can_uninstall: can_launch && installed,
            can_launch,
        },
        has_resettable_data,
        warnings,
    }
}

fn has_resettable_bpp_data(game_path: Option<&str>) -> bool {
    game_path
        .map(Path::new)
        .map(|path| crate::services::paths::bpp_data_dir(path).exists())
        .unwrap_or(false)
}

fn open_url(url: &str) -> Result<(), String> {
    #[cfg(target_os = "windows")]
    {
        Command::new("cmd")
            .args(["/C", "start", "", url])
            .spawn()
            .map_err(|err| format!("failed to open URL: {err}"))?;
        return Ok(());
    }

    #[cfg(target_os = "macos")]
    {
        Command::new("open")
            .arg(url)
            .spawn()
            .map_err(|err| format!("failed to open URL: {err}"))?;
        return Ok(());
    }

    #[cfg(all(not(target_os = "windows"), not(target_os = "macos")))]
    {
        Command::new("xdg-open")
            .arg(url)
            .spawn()
            .map_err(|err| format!("failed to open URL: {err}"))?;
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::{
        has_resettable_bpp_data, path_contains_steamapps, resolve_launch_flow, LaunchFlow,
    };

    #[test]
    fn steam_flow_when_steam_present_and_game_under_steamapps() {
        assert_eq!(
            resolve_launch_flow(
                Some("/Users/a/Library/Application Support/Steam"),
                Some("/Users/a/Library/Application Support/Steam/steamapps/common/The Bazaar"),
            ),
            LaunchFlow::Steam
        );
    }

    #[test]
    fn tempo_flow_for_tempo_native_game_dir_even_with_steam_installed() {
        assert_eq!(
            resolve_launch_flow(
                Some("C:\\Program Files (x86)\\Steam"),
                Some("C:\\Users\\a\\AppData\\Roaming\\Tempo Launcher - Beta\\game\\buildx64"),
            ),
            LaunchFlow::TempoNative
        );
    }

    #[test]
    fn tempo_flow_when_steam_missing() {
        assert_eq!(
            resolve_launch_flow(None, Some("/anything/steamapps/common/The Bazaar")),
            LaunchFlow::TempoNative
        );
    }

    #[test]
    fn test_has_resettable_bpp_data_detects_existing_data_directory() {
        let tmp = tempfile::tempdir().unwrap();
        std::fs::create_dir_all(tmp.path().join(crate::config::BAZAAR_DATA_DIRECTORY)).unwrap();
        let path = tmp.path().to_string_lossy().into_owned();

        assert!(has_resettable_bpp_data(Some(path.as_str())));
    }

    #[test]
    fn test_has_resettable_bpp_data_is_false_when_missing() {
        let tmp = tempfile::tempdir().unwrap();
        let path = tmp.path().to_string_lossy().into_owned();

        assert!(!has_resettable_bpp_data(Some(path.as_str())));
        assert!(!has_resettable_bpp_data(None));
    }

    #[test]
    fn steamapps_component_match_is_case_insensitive_and_component_exact() {
        assert!(path_contains_steamapps(
            "D:\\SteamLibrary\\SteamApps\\common\\The Bazaar"
        ));
        assert!(!path_contains_steamapps("/Users/a/my-steamapps-notes/game"));
    }
}

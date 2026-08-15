mod operation;
mod plan;
mod types;

pub(crate) use operation::{install, InstallRequest};
pub use types::{
    FileActionResult, GameDirectorySelection, InstallActions, InstallGameState, InstallModState,
    InstallState, InstallWarning, InstallWarningCode, ResetBepinexResult, ResetBppDataResult,
};

use std::process::Command;

use tauri::Manager;

use std::path::Path;

use crate::services::{
    bepinex::{
        reset_bepinex_folder, reset_bpp_data, uninstall_bpp, RESET_BEPINEX_ERR_GAME_RUNNING,
        RESET_BEPINEX_ERR_PARTIAL_FAILURE, RESET_BPP_DATA_ERR_GAME_RUNNING,
        RESET_BPP_DATA_ERR_PARTIAL_FAILURE,
    },
    detect::detect_for_install,
    startup::InstallerContextState,
};
use crate::{
    problem::{SemanticProblem, SemanticProblemCode},
    stream::runtime::StreamRuntime,
};

const STEAM_BAZAAR_URL: &str = "steam://rungameid/1617400";

pub fn build_install_state(
    app: tauri::AppHandle,
    state: tauri::State<'_, InstallerContextState>,
    game_path: Option<String>,
) -> Result<InstallState, SemanticProblem> {
    build_install_state_raw(app, state, game_path).map_err(install_detection_problem)
}

pub(super) fn build_install_state_raw(
    app: tauri::AppHandle,
    state: tauri::State<'_, InstallerContextState>,
    game_path: Option<String>,
) -> Result<InstallState, String> {
    let snapshot = detect_for_install(app, state, game_path)?;
    Ok(install_state_from_snapshot(snapshot))
}

pub async fn run_reset_bpp_data(
    app: tauri::AppHandle,
    install_state: tauri::State<'_, InstallerContextState>,
    stream_runtime: tauri::State<'_, StreamRuntime>,
    game_path: String,
) -> Result<ResetBppDataResult, SemanticProblem> {
    let removed_data = reset_bpp_data(stream_runtime, game_path.clone())
        .await
        .map_err(|diagnostic| install_action_problem("reset_bpp_data", diagnostic))?;
    let state = build_install_state(app, install_state, Some(game_path))?;
    Ok(ResetBppDataResult {
        state,
        removed_data,
    })
}

pub async fn run_reset_bepinex(
    app: tauri::AppHandle,
    install_state: tauri::State<'_, InstallerContextState>,
    game_path: String,
) -> Result<ResetBepinexResult, SemanticProblem> {
    let removed = reset_bepinex_folder(game_path.clone())
        .await
        .map_err(|diagnostic| install_action_problem("reset_bepinex", diagnostic))?;
    let state = build_install_state(app, install_state, Some(game_path))?;
    Ok(ResetBepinexResult { state, removed })
}

pub async fn run_uninstall(
    app: tauri::AppHandle,
    state: tauri::State<'_, InstallerContextState>,
    game_path: String,
) -> Result<InstallState, SemanticProblem> {
    let before = detect_for_install(app.clone(), state, Some(game_path.clone()))
        .map_err(install_detection_problem)?;
    let app_for_task = app.clone();
    let steam_path = before.steam_path.clone().unwrap_or_default();
    let game_path_for_task = game_path.clone();
    tauri::async_runtime::spawn_blocking(move || {
        uninstall_bpp(app_for_task, steam_path, game_path_for_task)
    })
    .await
    .map_err(|err| {
        install_action_problem("uninstall", format!("failed to run uninstall task: {err}"))
    })?
    .map_err(|diagnostic| install_action_problem("uninstall", diagnostic))?;

    let app_for_state = app.clone();
    let state = app_for_state.state::<InstallerContextState>();
    build_install_state(app, state, Some(game_path))
}

pub fn launch_game_via_steam() -> Result<(), String> {
    open_url(STEAM_BAZAAR_URL)
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
    let launch_options_empty =
        env.steam_launch_options == crate::services::vdf::SteamLaunchOptionsState::Empty;
    let platform_bootstrap_ready = !cfg!(target_os = "macos")
        || (env.trampoline_current
            && launch_options_empty
            && !env.obsolete_macos_artifacts_present);
    let ready = installed && plugin_version_matches && platform_bootstrap_ready;
    let can_launch = game_found && env.game_path_valid;
    let has_resettable_data = has_resettable_bpp_data(env.game_path.as_deref());
    let has_bepinex_files = has_bepinex_directory(env.game_path.as_deref());
    let warnings = install_warnings(
        game_found,
        env.game_path_valid,
        env.steam_launch_options,
        env.trampoline_current,
        env.obsolete_macos_artifacts_present,
    );

    InstallState {
        selected_game_path,
        steam_path: env.steam_path,
        game: InstallGameState {
            found: game_found,
            path_valid: env.game_path_valid,
            display_version: None,
        },
        mod_state: InstallModState {
            installed,
            installed_version: env.bpp_version,
            bundled_version: env.bundled_bpp_version,
            ready,
        },
        actions: InstallActions {
            can_install: can_launch && !installed,
            can_reinstall: can_launch && installed,
            can_reset_data: can_launch && has_resettable_data,
            can_reset_bepinex: can_launch && has_bepinex_files,
            can_uninstall: can_launch && installed,
            can_launch,
        },
        has_resettable_data,
        has_bepinex_files,
        warnings,
    }
}

fn install_warnings(
    game_found: bool,
    game_path_valid: bool,
    steam_launch_options: crate::services::vdf::SteamLaunchOptionsState,
    trampoline_current: bool,
    obsolete_macos_artifacts_present: bool,
) -> Vec<InstallWarning> {
    let mut warnings = Vec::new();
    if !game_found || !game_path_valid {
        warnings.push(InstallWarning {
            code: InstallWarningCode::GameMissing,
            params: Default::default(),
        });
    }
    if cfg!(target_os = "macos") {
        match steam_launch_options {
            crate::services::vdf::SteamLaunchOptionsState::Empty => {}
            crate::services::vdf::SteamLaunchOptionsState::NonEmpty => {
                warnings.push(InstallWarning {
                    code: InstallWarningCode::LaunchOptionsNotEmpty,
                    params: Default::default(),
                });
            }
            crate::services::vdf::SteamLaunchOptionsState::Unavailable => {
                warnings.push(InstallWarning {
                    code: InstallWarningCode::SteamConfigUnavailable,
                    params: Default::default(),
                });
            }
        }
        if !trampoline_current {
            warnings.push(InstallWarning {
                code: InstallWarningCode::TrampolineNotReady,
                params: Default::default(),
            });
        }
        if obsolete_macos_artifacts_present {
            warnings.push(InstallWarning {
                code: InstallWarningCode::ObsoleteMacosArtifacts,
                params: Default::default(),
            });
        }
    }
    warnings
}

fn install_detection_problem(diagnostic: String) -> SemanticProblem {
    SemanticProblem::new(SemanticProblemCode::InstallDetectionFailed)
        .with_param("operation", "detect_state")
        .with_diagnostic(diagnostic)
}

pub(crate) fn install_action_problem(operation: &str, diagnostic: String) -> SemanticProblem {
    if diagnostic == RESET_BPP_DATA_ERR_GAME_RUNNING || diagnostic == RESET_BEPINEX_ERR_GAME_RUNNING
    {
        return SemanticProblem::new(SemanticProblemCode::InstallGameRunning)
            .with_param("operation", operation);
    }

    for prefix in [
        RESET_BPP_DATA_ERR_PARTIAL_FAILURE,
        RESET_BEPINEX_ERR_PARTIAL_FAILURE,
    ] {
        if let Some(paths) = diagnostic
            .strip_prefix(prefix)
            .and_then(|remainder| remainder.strip_prefix(':'))
        {
            let count = paths
                .split('\u{1f}')
                .filter(|path| !path.trim().is_empty())
                .count();
            return SemanticProblem::new(SemanticProblemCode::InstallPartialFailure)
                .with_param("operation", operation)
                .with_param("count", count.to_string())
                .with_param("paths", paths);
        }
    }

    SemanticProblem::new(SemanticProblemCode::InstallActionFailed)
        .with_param("operation", operation)
        .with_diagnostic(diagnostic)
}

fn has_resettable_bpp_data(game_path: Option<&str>) -> bool {
    game_path
        .map(Path::new)
        .map(|path| crate::services::paths::bpp_data_dir(path).exists())
        .unwrap_or(false)
}

fn has_bepinex_directory(game_path: Option<&str>) -> bool {
    game_path
        .map(Path::new)
        .map(|path| path.join("BepInEx").is_dir())
        .unwrap_or(false)
}

fn open_url(url: &str) -> Result<(), String> {
    #[cfg(target_os = "windows")]
    {
        Command::new("cmd")
            .args(["/C", "start", "", url])
            .spawn()
            .map_err(|err| format!("failed to open URL: {err}"))?;
        Ok(())
    }

    #[cfg(target_os = "macos")]
    {
        Command::new("open")
            .arg(url)
            .spawn()
            .map_err(|err| format!("failed to open URL: {err}"))?;
        Ok(())
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
        has_bepinex_directory, has_resettable_bpp_data, install_action_problem, install_warnings,
        InstallWarningCode,
    };
    use crate::problem::SemanticProblemCode;
    use crate::services::bepinex::{
        RESET_BEPINEX_ERR_GAME_RUNNING, RESET_BPP_DATA_ERR_PARTIAL_FAILURE,
    };

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
    fn test_has_bepinex_directory_detects_existing_folder() {
        let tmp = tempfile::tempdir().unwrap();
        let path = tmp.path().to_string_lossy().into_owned();
        assert!(!has_bepinex_directory(Some(path.as_str())));

        std::fs::create_dir_all(tmp.path().join("BepInEx")).unwrap();

        assert!(has_bepinex_directory(Some(path.as_str())));
        assert!(!has_bepinex_directory(None));
    }

    #[test]
    fn install_warnings_are_semantic_codes_without_backend_copy() {
        let warnings = install_warnings(
            false,
            false,
            crate::services::vdf::SteamLaunchOptionsState::Empty,
            true,
            false,
        );

        assert_eq!(
            warnings
                .iter()
                .map(|warning| warning.code)
                .collect::<Vec<_>>(),
            vec![InstallWarningCode::GameMissing]
        );
        assert!(warnings.iter().all(|warning| warning.params.is_empty()));
    }

    #[cfg(target_os = "macos")]
    #[test]
    fn install_warnings_include_macos_bootstrap_problems() {
        let warnings = install_warnings(
            true,
            true,
            crate::services::vdf::SteamLaunchOptionsState::Unavailable,
            false,
            true,
        );

        assert_eq!(
            warnings
                .iter()
                .map(|warning| warning.code)
                .collect::<Vec<_>>(),
            vec![
                InstallWarningCode::SteamConfigUnavailable,
                InstallWarningCode::TrampolineNotReady,
                InstallWarningCode::ObsoleteMacosArtifacts,
            ]
        );
        assert!(warnings.iter().all(|warning| warning.params.is_empty()));
    }

    #[cfg(not(target_os = "macos"))]
    #[test]
    fn install_warnings_ignore_macos_bootstrap_inputs_on_other_platforms() {
        let warnings = install_warnings(
            true,
            true,
            crate::services::vdf::SteamLaunchOptionsState::Unavailable,
            false,
            true,
        );

        assert!(warnings.is_empty());
    }

    #[test]
    fn install_failures_classify_known_reset_conditions_and_generic_actions() {
        let blocked =
            install_action_problem("reset_bepinex", RESET_BEPINEX_ERR_GAME_RUNNING.to_string());
        assert_eq!(blocked.code, SemanticProblemCode::InstallGameRunning);
        assert_eq!(
            blocked.params.get("operation").map(String::as_str),
            Some("reset_bepinex")
        );
        assert_eq!(blocked.diagnostic, None);

        let partial = install_action_problem(
            "reset_bpp_data",
            format!("{RESET_BPP_DATA_ERR_PARTIAL_FAILURE}:/tmp/a\u{1f}/tmp/b"),
        );
        assert_eq!(partial.code, SemanticProblemCode::InstallPartialFailure);
        assert_eq!(partial.params.get("count").map(String::as_str), Some("2"));
        assert_eq!(
            partial.params.get("paths").map(String::as_str),
            Some("/tmp/a\u{1f}/tmp/b")
        );
        assert_eq!(partial.diagnostic, None);

        let generic = install_action_problem("install", "permission denied".to_string());
        assert_eq!(generic.code, SemanticProblemCode::InstallActionFailed);
        assert_eq!(generic.diagnostic.as_deref(), Some("permission denied"));
    }
}

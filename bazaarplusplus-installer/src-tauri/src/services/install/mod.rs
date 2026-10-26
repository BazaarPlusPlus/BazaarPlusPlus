mod operation;
mod plan;
mod types;

pub(crate) use operation::{install, InstallRequest};
pub use types::{
    GameDirectorySelection, InstallActions, InstallGameState, InstallModState, InstallState,
    InstallWarning, InstallWarningCode, ResetBepinexResult, ResetBppDataResult,
};

use anyhow::Context as _;
use std::process::Command;

use tauri::Manager;

use std::path::Path;

use crate::services::game_path::GamePathAcceptance;
use crate::services::{
    action_error::ActionError,
    bepinex::{reset_bepinex_folder, reset_bpp_data, uninstall_bpp},
    data_maintenance::DataMaintenanceLock,
    detect::detect_for_install,
    legacy_data::{delete_legacy_root, legacy_data_state, DeleteLegacyRootResult, LegacyDataState},
    selected_game_installation::SelectedGameInstallationState,
    startup::InstallerContextState,
};
use crate::{
    problem::{run_blocking, SemanticProblem, SemanticProblemCode},
    stream::runtime::StreamRuntime,
};

const STEAM_BAZAAR_URL: &str = "steam://rungameid/1617400";

/// Detects the install state on the blocking pool: on macOS, detection runs
/// `codesign --verify --deep --strict` over the whole game bundle.
pub async fn detect_install_state(
    app: tauri::AppHandle,
    game_path: Option<String>,
) -> Result<InstallState, SemanticProblem> {
    run_blocking(install_detection_problem, move || {
        build_install_state(&app, game_path)
    })
    .await
}

fn build_install_state(
    app: &tauri::AppHandle,
    game_path: Option<String>,
) -> Result<InstallState, SemanticProblem> {
    build_install_state_raw(app, game_path).map_err(install_detection_problem)
}

pub(super) fn build_install_state_raw(
    app: &tauri::AppHandle,
    game_path: Option<String>,
) -> anyhow::Result<InstallState> {
    let snapshot =
        detect_for_install(app.clone(), app.state::<InstallerContextState>(), game_path)?;
    Ok(install_state_from_snapshot(snapshot))
}

pub async fn run_reset_bpp_data(
    app: tauri::AppHandle,
    stream_runtime: tauri::State<'_, StreamRuntime>,
    game_path: String,
) -> Result<ResetBppDataResult, SemanticProblem> {
    let maintenance = app.state::<DataMaintenanceLock>().inner().clone();
    let removed_data = reset_bpp_data(&maintenance, &stream_runtime, game_path.clone())
        .await
        .map_err(|diagnostic| install_action_problem("reset_bpp_data", diagnostic))?;
    let state = detect_install_state(app, Some(game_path)).await?;
    Ok(ResetBppDataResult {
        state,
        removed_data,
    })
}

pub async fn run_reset_bepinex(
    app: tauri::AppHandle,
    game_path: String,
) -> Result<ResetBepinexResult, SemanticProblem> {
    let maintenance = app.state::<DataMaintenanceLock>().inner().clone();
    let removed = reset_bepinex_folder(&maintenance, game_path.clone())
        .await
        .map_err(|diagnostic| install_action_problem("reset_bepinex", diagnostic))?;
    let state = detect_install_state(app, Some(game_path)).await?;
    Ok(ResetBepinexResult { state, removed })
}

/// Legacy Roots of the selected game directory. History needs a database to
/// resolve its directory; this accepts any directory, so a user with only V5
/// data still sees it. Sizes are measured on the blocking pool.
pub async fn run_get_legacy_data_state(
    app: tauri::AppHandle,
    game_path: Option<String>,
) -> Result<LegacyDataState, SemanticProblem> {
    let resolved = app
        .state::<SelectedGameInstallationState>()
        .resolve(&app, game_path, GamePathAcceptance::Any)
        .map(|resolution| resolution.game_path);
    let Some(game_path) = resolved else {
        return Ok(LegacyDataState::without_game());
    };
    tauri::async_runtime::spawn_blocking(move || legacy_data_state(&game_path))
        .await
        .map_err(|err| {
            install_action_problem(
                "get_legacy_data_state",
                format!("failed to measure legacy data: {err}"),
            )
        })
}

pub async fn run_delete_legacy_root(
    app: tauri::AppHandle,
    game_path: String,
    name: String,
) -> Result<DeleteLegacyRootResult, SemanticProblem> {
    let maintenance = app.state::<DataMaintenanceLock>().inner().clone();
    delete_legacy_root(&maintenance, game_path, name)
        .await
        .map_err(|diagnostic| install_action_problem("delete_legacy_root", diagnostic))
}

/// Uninstalls, then returns the state detected afterwards. Uninstall needs only
/// the Steam path, which the startup context holds for the whole session, so
/// it does not detect (and verify the bundle signature) once more beforehand.
pub async fn run_uninstall(
    app: tauri::AppHandle,
    game_path: String,
) -> Result<InstallState, SemanticProblem> {
    run_blocking(
        |error| install_action_problem("uninstall", error),
        move || {
            let steam_path = app
                .state::<InstallerContextState>()
                .get_or_initialize(&app)
                .steam_path
                .as_ref()
                .map(|path| path.to_string_lossy().into_owned())
                .unwrap_or_default();
            uninstall_bpp(app.clone(), steam_path, game_path.clone())
                .map_err(|diagnostic| install_action_problem("uninstall", diagnostic))?;
            build_install_state(&app, Some(game_path))
        },
    )
    .await
}

pub fn launch_game_via_steam() -> anyhow::Result<()> {
    open_url(STEAM_BAZAAR_URL)
}

pub(crate) fn install_state_from_snapshot(
    env: crate::services::detect::InstallEnvironmentSnapshot,
) -> InstallState {
    let selected_game_path = env.game_path.clone();
    let game_found = selected_game_path.is_some();
    let installed = env.bepinex_installed;
    let ready = plan::plan_install(&env).is_empty();
    let can_launch = game_found && env.game_path_valid;
    let has_resettable_data = has_resettable_bpp_data(env.game_path.as_deref());
    let has_bepinex_files = has_bepinex_directory(env.game_path.as_deref());
    let warnings = install_warnings(&env);

    InstallState {
        selected_game_path,
        game: InstallGameState {
            path_valid: env.game_path_valid,
        },
        mod_state: InstallModState { installed, ready },
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
    env: &crate::services::detect::InstallEnvironmentSnapshot,
) -> Vec<InstallWarning> {
    let mut warnings = Vec::new();
    if env.game_path.is_none() || !env.game_path_valid {
        warnings.push(InstallWarning {
            code: InstallWarningCode::GameMissing,
            params: Default::default(),
        });
    }
    if cfg!(target_os = "macos") {
        match env.steam_launch_options {
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
        if !env.trampoline_current {
            warnings.push(InstallWarning {
                code: InstallWarningCode::TrampolineNotReady,
                params: Default::default(),
            });
        }
        if env.obsolete_macos_artifacts_present {
            warnings.push(InstallWarning {
                code: InstallWarningCode::ObsoleteMacosArtifacts,
                params: Default::default(),
            });
        }
    }
    warnings
}

fn install_detection_problem(error: anyhow::Error) -> SemanticProblem {
    SemanticProblem::new(SemanticProblemCode::InstallDetectionFailed)
        .with_param("operation", "detect_state")
        .with_error(&error)
}

pub(crate) fn install_action_problem(
    operation: &str,
    error: impl Into<ActionError>,
) -> SemanticProblem {
    error.into().into_problem(operation)
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

fn open_url(url: &str) -> anyhow::Result<()> {
    #[cfg(target_os = "windows")]
    {
        Command::new("cmd")
            .args(["/C", "start", "", url])
            .spawn()
            .context("failed to open URL")?;
        Ok(())
    }

    #[cfg(target_os = "macos")]
    {
        Command::new("open")
            .arg(url)
            .spawn()
            .context("failed to open URL")?;
        Ok(())
    }

    #[cfg(all(not(target_os = "windows"), not(target_os = "macos")))]
    {
        Command::new("xdg-open")
            .arg(url)
            .spawn()
            .context("failed to open URL")?;
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::{
        has_bepinex_directory, has_resettable_bpp_data, install_warnings, InstallWarningCode,
    };
    use crate::services::detect::InstallEnvironmentSnapshot;
    use crate::services::vdf::SteamLaunchOptionsState;

    /// A found, valid game with no warning-relevant facts set.
    fn snapshot() -> InstallEnvironmentSnapshot {
        InstallEnvironmentSnapshot {
            steam_path: None,
            game_path: Some("game".into()),
            game_path_valid: true,
            bepinex_installed: false,
            bpp_version: None,
            bundled_bpp_version: None,
            steam_launch_options: SteamLaunchOptionsState::Empty,
            trampoline_current: true,
            obsolete_macos_artifacts_present: false,
        }
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
        let warnings = install_warnings(&InstallEnvironmentSnapshot {
            game_path: None,
            game_path_valid: false,
            steam_launch_options: SteamLaunchOptionsState::Empty,
            trampoline_current: true,
            obsolete_macos_artifacts_present: false,
            ..snapshot()
        });

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
        let warnings = install_warnings(&InstallEnvironmentSnapshot {
            steam_launch_options: SteamLaunchOptionsState::Unavailable,
            trampoline_current: false,
            obsolete_macos_artifacts_present: true,
            ..snapshot()
        });

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
        let warnings = install_warnings(&InstallEnvironmentSnapshot {
            steam_launch_options: SteamLaunchOptionsState::Unavailable,
            trampoline_current: false,
            obsolete_macos_artifacts_present: true,
            ..snapshot()
        });

        assert!(warnings.is_empty());
    }
}

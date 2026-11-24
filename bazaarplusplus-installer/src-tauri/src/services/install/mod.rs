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
    detect::{detect_for_install, BundleVerification},
    legacy_data::{
        delete_legacy_root, legacy_data_presence, legacy_data_state, DeleteLegacyRootResult,
        LegacyDataPresence, LegacyDataState,
    },
    selected_game_installation::SelectedGameInstallationState,
    startup::InstallerContextState,
};
use crate::{
    problem::{run_blocking, SemanticProblem, SemanticProblemCode},
    stream::runtime::StreamRuntime,
};

const STEAM_BAZAAR_URL: &str = "steam://rungameid/1617400";

/// macOS code-signature verification walks the whole game bundle, so keep detection off async workers.
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
    build_install_state_raw(app, game_path, &BundleVerification::Verify)
        .map_err(install_detection_problem)
}

pub(super) fn build_install_state_raw(
    app: &tauri::AppHandle,
    game_path: Option<String>,
    verification: &BundleVerification,
) -> anyhow::Result<InstallState> {
    let snapshot = detect_for_install(
        app.clone(),
        app.state::<InstallerContextState>(),
        game_path,
        verification,
    )?;
    Ok(install_state_from_snapshot(snapshot))
}

pub async fn run_reset_bpp_data(
    app: tauri::AppHandle,
    stream_runtime: tauri::State<'_, StreamRuntime>,
    game_path: String,
    data_root_path: String,
) -> Result<ResetBppDataResult, SemanticProblem> {
    let maintenance = app.state::<DataMaintenanceLock>().inner().clone();
    let removed_data = reset_bpp_data(
        &maintenance,
        &stream_runtime,
        game_path.clone(),
        data_root_path.into(),
    )
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

/// Accept a directory without a database so users with only Legacy Roots can still manage them.
pub async fn run_get_legacy_data_state(
    app: tauri::AppHandle,
    game_path: Option<String>,
) -> Result<LegacyDataState, SemanticProblem> {
    run_blocking(
        |error| install_action_problem("get_legacy_data_state", error),
        move || {
            let resolved = app
                .state::<SelectedGameInstallationState>()
                .resolve(&app, game_path, GamePathAcceptance::Any)
                .map(|resolution| resolution.game_path);
            Ok(match resolved {
                Some(game_path) => legacy_data_state(&game_path),
                None => LegacyDataState::without_game(),
            })
        },
    )
    .await
}

pub async fn run_get_legacy_data_presence(
    app: tauri::AppHandle,
    game_path: Option<String>,
) -> Result<LegacyDataPresence, SemanticProblem> {
    run_blocking(
        |error| install_action_problem("get_legacy_data_presence", error),
        move || {
            let resolved = app
                .state::<SelectedGameInstallationState>()
                .resolve(&app, game_path, GamePathAcceptance::Any)
                .map(|resolution| resolution.game_path);
            Ok(match resolved {
                Some(game_path) => legacy_data_presence(&game_path),
                None => LegacyDataPresence::without_game(),
            })
        },
    )
    .await
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

/// Reuse the session's Steam path to avoid an extra bundle signature verification before uninstall.
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

pub fn launch_selected_game(game: &Path) -> anyhow::Result<()> {
    crate::services::bepinex::ensure_valid_game_path(game)?;
    #[cfg(any(target_os = "windows", target_os = "macos"))]
    if crate::services::tempo::library(game).is_some() {
        return crate::services::tempo::open_launcher();
    }
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
    let data_root = env
        .game_path
        .as_deref()
        .map(Path::new)
        .map(crate::services::paths::bpp_data_dir);
    let has_resettable_data = data_root.as_deref().is_some_and(Path::is_dir);
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
        data_root_path: data_root.map(|path| path.to_string_lossy().into_owned()),
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
    use super::install_warnings;
    #[cfg(target_os = "macos")]
    use super::InstallWarningCode;
    use crate::services::detect::InstallEnvironmentSnapshot;
    use crate::services::vdf::SteamLaunchOptionsState;

    fn snapshot() -> InstallEnvironmentSnapshot {
        InstallEnvironmentSnapshot {
            steam_path: None,
            game_path: Some("game".into()),
            game_path_valid: true,
            tempo_migration_pending: false,
            bepinex_installed: false,
            bpp_version: None,
            bundled_bpp_version: None,
            steam_launch_options: SteamLaunchOptionsState::Empty,
            trampoline_current: true,
            obsolete_macos_artifacts_present: false,
        }
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

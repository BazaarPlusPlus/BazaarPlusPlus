use std::path::Path;

use tauri::Manager;

use super::plan::{plan_install, InstallEffect, InstallPlanInputs, PayloadState};
use super::{build_install_state_raw, install_action_problem, InstallState};
use crate::problem::SemanticProblem;
use crate::services::{
    bepinex::{self, install_bepinex},
    detect::detect_for_install,
    startup::InstallerContextState,
    steam::prepare_steam_for_config_update,
    vdf::{clear_launch_options_for_steam, SteamLaunchOptionsState},
};

pub(crate) struct InstallRequest {
    pub(crate) game_path: String,
}

fn classify_payload(
    bepinex_installed: bool,
    installed_version: Option<&str>,
    bundled_version: Option<&str>,
) -> PayloadState {
    if !bepinex_installed || installed_version.is_none() {
        return PayloadState::Missing;
    }
    if bundled_version.is_none() || installed_version == bundled_version {
        PayloadState::Current
    } else {
        PayloadState::Changed
    }
}

pub(crate) async fn install(
    app: tauri::AppHandle,
    request: InstallRequest,
) -> Result<InstallState, SemanticProblem> {
    let task_app = app.clone();
    tauri::async_runtime::spawn_blocking(move || {
        let installer_state = task_app.state::<InstallerContextState>();
        let before = detect_for_install(
            task_app.clone(),
            installer_state,
            Some(request.game_path.clone()),
        )?;
        let steam_path = before.steam_path.clone().unwrap_or_default();
        let requires_macos_bootstrap = cfg!(target_os = "macos");
        if requires_macos_bootstrap
            && (steam_path.trim().is_empty()
                || before.steam_launch_options == SteamLaunchOptionsState::Unavailable)
        {
            return Err(
                "Steam localconfig.vdf is unavailable; start Steam once, close it, and retry."
                    .to_string(),
            );
        }
        let facts = InstallPlanInputs {
            requires_macos_bootstrap,
            payload: classify_payload(
                before.bepinex_installed,
                before.bpp_version.as_deref(),
                before.bundled_bpp_version.as_deref(),
            ),
            bootstrap_satisfied: !requires_macos_bootstrap
                || (before.trampoline_current
                    && before.steam_launch_options == SteamLaunchOptionsState::Empty
                    && !before.obsolete_macos_artifacts_present),
        };
        let mut effects = ProductionInstallEffects {
            app: task_app.clone(),
            steam_path,
            game_path: request.game_path.clone(),
        };

        execute_and_refresh(facts, &mut effects, || {
            let installer_state = task_app.state::<InstallerContextState>();
            build_install_state_raw(task_app.clone(), installer_state, Some(request.game_path))
        })
    })
    .await
    .map_err(|error| {
        install_action_problem("install", format!("failed to run install task: {error}"))
    })?
    .map_err(|diagnostic| install_action_problem("install", diagnostic))
}

trait InstallEffects {
    fn execute(&mut self, effect: InstallEffect) -> Result<(), String>;
}

struct ProductionInstallEffects {
    app: tauri::AppHandle,
    steam_path: String,
    game_path: String,
}

impl InstallEffects for ProductionInstallEffects {
    fn execute(&mut self, effect: InstallEffect) -> Result<(), String> {
        let steam = Path::new(&self.steam_path);
        let game = Path::new(&self.game_path);
        match effect {
            InstallEffect::CloseSteam => prepare_steam_for_config_update(steam),
            InstallEffect::InstallBepInEx => {
                install_bepinex(self.app.clone(), self.game_path.clone())
            }
            InstallEffect::InstallTrampoline => bepinex::install_trampoline(&self.app, game),
            InstallEffect::ClearLaunchOptions => clear_launch_options_for_steam(steam),
            InstallEffect::RemoveObsoleteMacosArtifacts => {
                bepinex::remove_obsolete_macos_artifacts(game)
            }
        }
    }
}

fn execute_and_refresh<T>(
    facts: InstallPlanInputs,
    effects: &mut impl InstallEffects,
    refresh: impl FnOnce() -> Result<T, String>,
) -> Result<T, String> {
    for effect in plan_install(facts) {
        effects.execute(effect)?;
    }
    refresh()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[derive(Default)]
    struct Recorder {
        effects: Vec<InstallEffect>,
        fail_on: Option<InstallEffect>,
    }

    impl InstallEffects for Recorder {
        fn execute(&mut self, effect: InstallEffect) -> Result<(), String> {
            self.effects.push(effect);
            if self.fail_on == Some(effect) {
                return Err("effect failed".to_string());
            }
            Ok(())
        }
    }

    fn facts(payload: PayloadState, bootstrap_satisfied: bool) -> InstallPlanInputs {
        InstallPlanInputs {
            requires_macos_bootstrap: true,
            payload,
            bootstrap_satisfied,
        }
    }

    #[test]
    fn payload_classification_covers_fresh_missing_changed_and_current() {
        for (installed, installed_version, bundled_version, expected) in [
            (false, None, Some("2"), PayloadState::Missing),
            (true, None, Some("2"), PayloadState::Missing),
            (true, Some("1"), Some("2"), PayloadState::Changed),
            (true, Some("2"), Some("2"), PayloadState::Current),
            (true, Some("2"), None, PayloadState::Current),
        ] {
            assert_eq!(
                classify_payload(installed, installed_version, bundled_version),
                expected
            );
        }
    }

    #[test]
    fn complete_install_records_one_macos_bootstrap_then_returns_refreshed_outcome() {
        let mut recorder = Recorder::default();

        let outcome =
            execute_and_refresh(facts(PayloadState::Missing, false), &mut recorder, || {
                Ok("state read back from disk")
            });

        assert_eq!(outcome.unwrap(), "state read back from disk");
        assert_eq!(
            recorder.effects,
            vec![
                InstallEffect::CloseSteam,
                InstallEffect::InstallBepInEx,
                InstallEffect::InstallTrampoline,
                InstallEffect::ClearLaunchOptions,
                InstallEffect::RemoveObsoleteMacosArtifacts,
            ]
        );
    }

    #[test]
    fn install_operation_covers_fresh_changed_current_and_bootstrap_repair_states() {
        let fresh = facts(PayloadState::Missing, false);
        let changed = facts(PayloadState::Changed, false);
        let no_op = facts(PayloadState::Current, true);
        let bootstrap_repair = facts(PayloadState::Current, false);

        for scenario in [fresh, changed] {
            let mut recorder = Recorder::default();
            execute_and_refresh(scenario, &mut recorder, || Ok(())).unwrap();
            assert!(recorder.effects.contains(&InstallEffect::InstallBepInEx));
        }

        let mut recorder = Recorder::default();
        execute_and_refresh(no_op, &mut recorder, || Ok(())).unwrap();
        assert!(recorder.effects.is_empty());

        let mut recorder = Recorder::default();
        execute_and_refresh(bootstrap_repair, &mut recorder, || Ok(())).unwrap();
        assert!(!recorder.effects.contains(&InstallEffect::InstallBepInEx));
        assert!(recorder.effects.contains(&InstallEffect::InstallTrampoline));
    }

    #[test]
    fn complete_install_stops_on_first_error_and_does_not_refresh() {
        let mut recorder = Recorder {
            fail_on: Some(InstallEffect::ClearLaunchOptions),
            ..Recorder::default()
        };
        let mut refreshed = false;

        let error = execute_and_refresh(facts(PayloadState::Missing, false), &mut recorder, || {
            refreshed = true;
            Ok(())
        })
        .unwrap_err();

        assert_eq!(error, "effect failed");
        assert!(!refreshed);
        assert_eq!(
            recorder.effects,
            vec![
                InstallEffect::CloseSteam,
                InstallEffect::InstallBepInEx,
                InstallEffect::InstallTrampoline,
                InstallEffect::ClearLaunchOptions,
            ]
        );
    }
}

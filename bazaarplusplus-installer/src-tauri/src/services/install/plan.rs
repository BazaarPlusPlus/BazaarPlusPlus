//! Private install planner: turns gathered facts into ordered effects. Each
//! platform owns one bootstrap: macOS installs a trampoline and requires empty
//! Steam launch options; Linux requires the Proton Doorstop DLL override;
//! Windows carries no bootstrap of its own.

use crate::services::detect::InstallEnvironmentSnapshot;
use crate::services::vdf::SteamLaunchOptionsState;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(super) enum InstallEffect {
    /// Refuse to modify an active game installation.
    EnsureGameStopped,
    /// Quit Steam only when its launch options need changing.
    CloseSteam,
    /// Extract the current BepInEx and BazaarPlusPlus payload.
    InstallBepInEx,
    /// Install or refresh the bundled trampoline and verify the signed bundle.
    InstallTrampoline,
    /// Remove every LaunchOptions entry for The Bazaar and verify the result.
    ClearLaunchOptions,
    /// Write the Proton Doorstop DLL override into The Bazaar's LaunchOptions.
    EnsureLaunchOptions,
    /// Delete installer-owned files that are not part of the canonical bootstrap.
    RemoveObsoleteMacosArtifacts,
}

/// The platform bootstrap the plan must satisfy in addition to the payload.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Bootstrap {
    None,
    Macos,
    Proton,
}

fn bootstrap_for_platform() -> Bootstrap {
    if cfg!(target_os = "macos") {
        Bootstrap::Macos
    } else if cfg!(target_os = "linux") {
        Bootstrap::Proton
    } else {
        Bootstrap::None
    }
}

pub(super) fn plan_install(env: &InstallEnvironmentSnapshot) -> Vec<InstallEffect> {
    plan_install_for_bootstrap(env, bootstrap_for_platform())
}

fn plan_install_for_bootstrap(
    env: &InstallEnvironmentSnapshot,
    bootstrap: Bootstrap,
) -> Vec<InstallEffect> {
    let payload_current = env.bepinex_installed
        && env.bpp_version.as_ref().is_some_and(|installed| {
            env.bundled_bpp_version
                .as_ref()
                .is_none_or(|bundled| installed == bundled)
        });
    let launch_options_satisfied = env.steam_launch_options == SteamLaunchOptionsState::Satisfied;
    let bootstrap_ready = match bootstrap {
        Bootstrap::None => true,
        Bootstrap::Macos => {
            env.trampoline_current
                && launch_options_satisfied
                && !env.obsolete_macos_artifacts_present
        }
        Bootstrap::Proton => launch_options_satisfied,
    };
    if payload_current && bootstrap_ready {
        return Vec::new();
    }

    let mut steps = Vec::new();
    if bootstrap != Bootstrap::None {
        steps.push(InstallEffect::EnsureGameStopped);
        if !launch_options_satisfied {
            steps.push(InstallEffect::CloseSteam);
        }
    }
    if !payload_current {
        steps.push(InstallEffect::InstallBepInEx);
    }
    match bootstrap {
        Bootstrap::Macos => {
            steps.extend([
                InstallEffect::InstallTrampoline,
                InstallEffect::RemoveObsoleteMacosArtifacts,
            ]);
            if !launch_options_satisfied {
                steps.push(InstallEffect::ClearLaunchOptions);
            }
        }
        Bootstrap::Proton => {
            if !launch_options_satisfied {
                steps.push(InstallEffect::EnsureLaunchOptions);
            }
        }
        Bootstrap::None => {}
    }
    steps
}

#[cfg(test)]
mod tests {
    use super::super::install_state_from_snapshot;
    use super::*;

    #[test]
    fn detected_facts_drive_readiness_and_effects_on_every_bootstrap() {
        use InstallEffect::*;
        use SteamLaunchOptionsState::*;

        // (bootstrap, trampoline_current, obsolete_macos_artifacts_present)
        let bootstraps = [
            (Bootstrap::None, false, false),
            (Bootstrap::Macos, true, false),
            (Bootstrap::Macos, false, false),
            (Bootstrap::Macos, true, true),
            (Bootstrap::Proton, false, false),
        ];

        for (installed, version, bundled, payload_current) in [
            (false, None, Some("2"), false),
            (false, Some("2"), Some("2"), false),
            (true, None, Some("2"), false),
            (true, None, None, false),
            (true, Some("1"), Some("2"), false),
            (true, Some("2"), Some("2"), true),
            (true, Some("2"), None, true),
        ] {
            for options in [Satisfied, Unsatisfied, Unavailable] {
                for (bootstrap, trampoline, obsolete) in bootstraps {
                    let env = InstallEnvironmentSnapshot {
                        steam_path: Some("steam".into()),
                        game_path: None,
                        game_path_valid: false,
                        bepinex_installed: installed,
                        bpp_version: version.map(str::to_owned),
                        bundled_bpp_version: bundled.map(str::to_owned),
                        steam_launch_options: options,
                        trampoline_current: trampoline,
                        obsolete_macos_artifacts_present: obsolete,
                    };
                    let launch_options_satisfied = options == Satisfied;
                    let bootstrap_ready = match bootstrap {
                        Bootstrap::None => true,
                        Bootstrap::Macos => trampoline && launch_options_satisfied && !obsolete,
                        Bootstrap::Proton => launch_options_satisfied,
                    };
                    let ready = payload_current && bootstrap_ready;
                    let steps = plan_install_for_bootstrap(&env, bootstrap);
                    assert_eq!(steps.is_empty(), ready, "{env:?}, bootstrap={bootstrap:?}");
                    assert_eq!(steps.contains(&InstallBepInEx), !payload_current);
                    assert_eq!(
                        steps.contains(&CloseSteam),
                        bootstrap != Bootstrap::None && !launch_options_satisfied
                    );
                    assert_eq!(
                        steps.contains(&ClearLaunchOptions),
                        bootstrap == Bootstrap::Macos && !launch_options_satisfied
                    );
                    assert_eq!(
                        steps.contains(&EnsureLaunchOptions),
                        bootstrap == Bootstrap::Proton && !launch_options_satisfied
                    );
                    if bootstrap == Bootstrap::Macos && !ready {
                        assert!(steps.contains(&InstallTrampoline));
                        assert!(steps.contains(&RemoveObsoleteMacosArtifacts));
                    }
                    if bootstrap != Bootstrap::None && !ready {
                        assert_eq!(steps.first(), Some(&EnsureGameStopped));
                    } else if bootstrap == Bootstrap::None && !ready {
                        assert_eq!(steps, vec![InstallBepInEx]);
                    }
                    if bootstrap == bootstrap_for_platform() {
                        assert_eq!(
                            install_state_from_snapshot(env.clone()).mod_state.ready,
                            ready,
                            "{env:?}, bootstrap={bootstrap:?}"
                        );
                    }
                }
            }
        }
    }
}

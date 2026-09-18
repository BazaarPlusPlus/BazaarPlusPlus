//! Private install planner: turns gathered facts into ordered effects.
//! macOS has one bootstrap only: trampoline plus empty Steam launch options.

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
    /// Delete installer-owned files that are not part of the canonical bootstrap.
    RemoveObsoleteMacosArtifacts,
}

pub(super) fn plan_install(env: &InstallEnvironmentSnapshot) -> Vec<InstallEffect> {
    plan_install_for_platform(env, cfg!(target_os = "macos"))
}

fn plan_install_for_platform(
    env: &InstallEnvironmentSnapshot,
    requires_macos_bootstrap: bool,
) -> Vec<InstallEffect> {
    let payload_current = env.bepinex_installed
        && env.bpp_version.as_ref().is_some_and(|installed| {
            env.bundled_bpp_version
                .as_ref()
                .is_none_or(|bundled| installed == bundled)
        });
    let launch_options_empty = env.steam_launch_options == SteamLaunchOptionsState::Empty;
    let bootstrap_ready = !requires_macos_bootstrap
        || (env.trampoline_current
            && launch_options_empty
            && !env.obsolete_macos_artifacts_present);
    if payload_current && bootstrap_ready {
        return Vec::new();
    }

    let mut steps = Vec::new();
    if requires_macos_bootstrap {
        steps.push(InstallEffect::EnsureGameStopped);
        if !launch_options_empty {
            steps.push(InstallEffect::CloseSteam);
        }
    }
    if !payload_current {
        steps.push(InstallEffect::InstallBepInEx);
    }
    if requires_macos_bootstrap {
        steps.extend([
            InstallEffect::InstallTrampoline,
            InstallEffect::RemoveObsoleteMacosArtifacts,
        ]);
        if !launch_options_empty {
            steps.push(InstallEffect::ClearLaunchOptions);
        }
    }
    steps
}

#[cfg(test)]
mod tests {
    use super::super::install_state_from_snapshot;
    use super::*;

    #[test]
    fn detected_facts_drive_readiness_and_effects_on_both_platforms() {
        use InstallEffect::*;
        use SteamLaunchOptionsState::*;

        for (installed, version, bundled, payload_current) in [
            (false, None, Some("2"), false),
            (false, Some("2"), Some("2"), false),
            (true, None, Some("2"), false),
            (true, None, None, false),
            (true, Some("1"), Some("2"), false),
            (true, Some("2"), Some("2"), true),
            (true, Some("2"), None, true),
        ] {
            for (trampoline, options, obsolete, bootstrap_ready) in [
                (true, Empty, false, true),
                (false, Empty, false, false),
                (true, NonEmpty, false, false),
                (true, Unavailable, false, false),
                (true, Empty, true, false),
            ] {
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
                for macos in [false, true] {
                    let ready = payload_current && (!macos || bootstrap_ready);
                    let steps = plan_install_for_platform(&env, macos);
                    assert_eq!(steps.is_empty(), ready, "{env:?}, macos={macos}");
                    assert_eq!(steps.contains(&InstallBepInEx), !payload_current);
                    assert_eq!(steps.contains(&InstallTrampoline), macos && !ready);
                    assert_eq!(
                        steps.contains(&RemoveObsoleteMacosArtifacts),
                        macos && !ready
                    );
                    assert_eq!(steps.contains(&CloseSteam), macos && options != Empty);
                    assert_eq!(
                        steps.contains(&ClearLaunchOptions),
                        macos && options != Empty
                    );
                    if macos && !ready {
                        assert_eq!(steps.first(), Some(&EnsureGameStopped));
                    } else if !macos && !ready {
                        assert_eq!(steps, vec![InstallBepInEx]);
                    }
                    if macos == cfg!(target_os = "macos") {
                        assert_eq!(
                            install_state_from_snapshot(env.clone()).mod_state.ready,
                            ready,
                            "{env:?}"
                        );
                    }
                }
            }
        }
    }
}

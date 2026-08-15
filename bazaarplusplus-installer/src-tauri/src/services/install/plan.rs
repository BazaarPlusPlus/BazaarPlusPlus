//! Private install planner: turns gathered facts into ordered effects.
//! macOS has one bootstrap only: trampoline plus empty Steam launch options.

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(super) enum InstallEffect {
    /// Quit Steam before changing its config or the game bundle.
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

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(super) enum PayloadState {
    Missing,
    Changed,
    Current,
}

#[derive(Debug, Clone, Copy)]
pub(super) struct InstallPlanInputs {
    pub requires_macos_bootstrap: bool,
    pub payload: PayloadState,
    pub bootstrap_satisfied: bool,
}

pub(super) fn plan_install(inputs: InstallPlanInputs) -> Vec<InstallEffect> {
    if inputs.payload == PayloadState::Current && inputs.bootstrap_satisfied {
        return Vec::new();
    }

    let mut steps = Vec::new();
    if inputs.requires_macos_bootstrap {
        steps.push(InstallEffect::CloseSteam);
    }
    if inputs.payload != PayloadState::Current {
        steps.push(InstallEffect::InstallBepInEx);
    }
    if inputs.requires_macos_bootstrap {
        steps.extend([
            InstallEffect::InstallTrampoline,
            InstallEffect::ClearLaunchOptions,
            InstallEffect::RemoveObsoleteMacosArtifacts,
        ]);
    }
    steps
}

#[cfg(test)]
mod tests {
    use super::{plan_install, InstallEffect, InstallPlanInputs, PayloadState};
    use InstallEffect::*;

    #[test]
    fn macos_install_converges_to_one_ordered_bootstrap() {
        for payload in [PayloadState::Missing, PayloadState::Changed] {
            assert_eq!(
                plan_install(InstallPlanInputs {
                    requires_macos_bootstrap: true,
                    payload,
                    bootstrap_satisfied: false,
                }),
                vec![
                    CloseSteam,
                    InstallBepInEx,
                    InstallTrampoline,
                    ClearLaunchOptions,
                    RemoveObsoleteMacosArtifacts,
                ]
            );
        }
    }

    #[test]
    fn macos_bootstrap_only_repair_preserves_current_payload() {
        assert_eq!(
            plan_install(InstallPlanInputs {
                requires_macos_bootstrap: true,
                payload: PayloadState::Current,
                bootstrap_satisfied: false,
            }),
            vec![
                CloseSteam,
                InstallTrampoline,
                ClearLaunchOptions,
                RemoveObsoleteMacosArtifacts,
            ]
        );
    }

    #[test]
    fn current_payload_and_bootstrap_is_a_no_op() {
        assert!(plan_install(InstallPlanInputs {
            requires_macos_bootstrap: true,
            payload: PayloadState::Current,
            bootstrap_satisfied: true,
        })
        .is_empty());
    }

    #[test]
    fn non_macos_install_only_updates_the_payload() {
        assert_eq!(
            plan_install(InstallPlanInputs {
                requires_macos_bootstrap: false,
                payload: PayloadState::Missing,
                bootstrap_satisfied: true,
            }),
            vec![InstallBepInEx]
        );
    }
}

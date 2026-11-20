use std::path::Path;

use crate::problem::{SemanticProblem, SemanticProblemCode};

#[cfg(target_os = "windows")]
const BAZAAR_PROCESS_NAME: &str = "TheBazaar.exe";

#[cfg(target_os = "windows")]
fn is_bazaar_running() -> anyhow::Result<bool> {
    crate::services::process_snapshot::process_is_running(BAZAAR_PROCESS_NAME)
}

/// Best-effort History diagnostic only. Destructive operations require `ensure_game_stopped`.
pub(crate) fn is_bazaar_running_best_effort() -> bool {
    #[cfg(target_os = "windows")]
    {
        is_bazaar_running().unwrap_or(false)
    }

    #[cfg(not(target_os = "windows"))]
    {
        false
    }
}

/// The game can keep the mod database open after its window closes.
pub(crate) fn terminate_game() -> anyhow::Result<bool> {
    #[cfg(target_os = "windows")]
    {
        crate::services::process_snapshot::terminate_processes(BAZAAR_PROCESS_NAME)
            .map(|terminated| terminated > 0)
    }

    #[cfg(not(target_os = "windows"))]
    {
        Err(anyhow::anyhow!(
            "Ending the game process is not supported on this platform."
        ))
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(crate) enum GameStoppedOperation {
    Install,
    Reset,
    BepinexReset,
    LegacyDelete,
}

impl GameStoppedOperation {
    #[cfg(test)]
    const ALL: [Self; 4] = [
        Self::Install,
        Self::Reset,
        Self::BepinexReset,
        Self::LegacyDelete,
    ];

    pub(crate) fn problem_code(self) -> SemanticProblemCode {
        match self {
            Self::Install => SemanticProblemCode::InstallBlockedByGame,
            Self::Reset => SemanticProblemCode::ResetBlockedByGame,
            Self::BepinexReset => SemanticProblemCode::BepinexResetBlockedByGame,
            Self::LegacyDelete => SemanticProblemCode::LegacyDeleteBlockedByGame,
        }
    }

    fn token(self) -> &'static str {
        match self {
            Self::Install => "install_blocked_by_game",
            Self::Reset => "reset_blocked_by_game",
            Self::BepinexReset => "bepinex_reset_blocked_by_game",
            Self::LegacyDelete => "legacy_delete_blocked_by_game",
        }
    }
}

/// Refusal from [`ensure_game_stopped`]: the game is running, or its processes
/// could not be inspected (`inspection_error`), which counts as running.
#[derive(Clone, Debug, PartialEq, Eq)]
pub(crate) struct GameRunningRefusal {
    pub(crate) operation: GameStoppedOperation,
    pub(crate) inspection_error: Option<String>,
}

impl GameRunningRefusal {
    pub(crate) fn into_problem(self) -> SemanticProblem {
        let problem = SemanticProblem::new(self.operation.problem_code());
        match self.inspection_error {
            Some(diagnostic) => problem.with_diagnostic(diagnostic),
            None => problem,
        }
    }
}

impl std::error::Error for GameRunningRefusal {}

impl std::fmt::Display for GameRunningRefusal {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str(self.operation.token())?;
        if let Some(error) = &self.inspection_error {
            write!(f, ": {error}")?;
        }
        Ok(())
    }
}

pub(crate) trait GameProcessProbe {
    fn is_running(&self, game_path: &Path) -> anyhow::Result<bool>;
}

impl<F> GameProcessProbe for F
where
    F: Fn(&Path) -> anyhow::Result<bool>,
{
    fn is_running(&self, game_path: &Path) -> anyhow::Result<bool> {
        self(game_path)
    }
}

#[derive(Clone, Copy, Debug, Default)]
pub(crate) struct SystemGameProcessProbe;

impl GameProcessProbe for SystemGameProcessProbe {
    fn is_running(&self, game_path: &Path) -> anyhow::Result<bool> {
        #[cfg(target_os = "macos")]
        {
            use anyhow::Context as _;

            let game_path = game_path
                .canonicalize()
                .context("Cannot resolve the game directory")?;
            let output = std::process::Command::new("/bin/ps")
                .args(["-axo", "comm="])
                .output()
                .context("Cannot inspect game processes")?;
            if !output.status.success() {
                anyhow::bail!(
                    "Cannot inspect game processes: ps exited with {}",
                    output.status
                );
            }
            let processes =
                String::from_utf8(output.stdout).context("Cannot inspect game processes")?;
            Ok(contains_game_process(&processes, &game_path))
        }
        #[cfg(target_os = "windows")]
        {
            let _ = game_path;
            is_bazaar_running()
        }
        // The game ships no build for other platforms, so no process of it can
        // run here.
        #[cfg(not(any(target_os = "macos", target_os = "windows")))]
        {
            let _ = game_path;
            Ok(false)
        }
    }
}

pub(crate) fn ensure_game_stopped(
    game_path: &Path,
    operation: GameStoppedOperation,
) -> Result<(), GameRunningRefusal> {
    ensure_game_stopped_with(&SystemGameProcessProbe, game_path, operation)
}

pub(crate) fn ensure_game_stopped_with(
    probe: &impl GameProcessProbe,
    game_path: &Path,
    operation: GameStoppedOperation,
) -> Result<(), GameRunningRefusal> {
    match probe.is_running(game_path) {
        Ok(false) => Ok(()),
        Ok(true) => Err(GameRunningRefusal {
            operation,
            inspection_error: None,
        }),
        Err(error) => Err(GameRunningRefusal {
            operation,
            inspection_error: Some(crate::problem::error_diagnostic(&error)),
        }),
    }
}

#[cfg(any(target_os = "macos", test))]
fn contains_game_process(processes: &str, game_path: &Path) -> bool {
    let bundle = game_path.join("TheBazaar.app");
    processes.lines().any(|line| {
        let process = Path::new(line.trim());
        process.starts_with(&bundle)
            && process
                .parent()
                .is_some_and(|parent| parent.ends_with("Contents/MacOS"))
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn macos_matching_finds_native_and_trampoline_processes_of_this_install_only() {
        let root = Path::new("/Games/The Bazaar");
        for executable in ["The Bazaar", "The Bazaar.orig"] {
            assert!(contains_game_process(
                &format!("/Games/The Bazaar/TheBazaar.app/Contents/MacOS/{executable}\n"),
                root
            ));
        }
        assert!(contains_game_process(
            "/usr/sbin/cfprefsd\n  /Games/The Bazaar/TheBazaar.app/Contents/MacOS/The Bazaar  \n",
            root
        ));
        assert!(!contains_game_process("/Applications/Steam.app/Contents/MacOS/steam_osx\n/Other/TheBazaar.app/Contents/MacOS/The Bazaar.orig", root));
        assert!(!contains_game_process(
            "/Games/The Bazaar/TheBazaar.app.backup/Contents/MacOS/The Bazaar",
            root
        ));
        assert!(!contains_game_process(
            "/Games/The Bazaar/TheBazaar.app/Contents/Frameworks/UnityPlayer.dylib",
            root
        ));
        assert!(!contains_game_process("", root));
    }

    #[test]
    fn each_operation_refuses_with_its_own_problem_code_while_the_game_runs() {
        let running = |_: &Path| Ok(true);
        let expected = [
            (
                GameStoppedOperation::Install,
                SemanticProblemCode::InstallBlockedByGame,
            ),
            (
                GameStoppedOperation::Reset,
                SemanticProblemCode::ResetBlockedByGame,
            ),
            (
                GameStoppedOperation::BepinexReset,
                SemanticProblemCode::BepinexResetBlockedByGame,
            ),
            (
                GameStoppedOperation::LegacyDelete,
                SemanticProblemCode::LegacyDeleteBlockedByGame,
            ),
        ];
        assert_eq!(expected.len(), GameStoppedOperation::ALL.len());

        for (operation, code) in expected {
            let refusal = ensure_game_stopped_with(&running, Path::new("/game"), operation)
                .expect_err("a running game must refuse");
            assert_eq!(refusal.operation, operation);
            let problem = refusal.into_problem();
            assert_eq!(problem.code, code);
            assert_eq!(problem.diagnostic, None);
            // The diagnostic token is the serialized code, so refusal text and
            // the frontend's code name never drift apart.
            assert_eq!(
                serde_json::to_value(code).unwrap(),
                serde_json::json!(operation.token())
            );
        }
    }

    #[test]
    fn failed_inspection_refuses_like_a_running_game() {
        let broken = |_: &Path| Err(anyhow::anyhow!("ps failed"));

        let problem =
            ensure_game_stopped_with(&broken, Path::new("/game"), GameStoppedOperation::Reset)
                .unwrap_err()
                .into_problem();

        assert_eq!(problem.code, SemanticProblemCode::ResetBlockedByGame);
        assert_eq!(problem.diagnostic.as_deref(), Some("ps failed"));
    }

    #[test]
    fn stopped_game_passes() {
        let stopped = |_: &Path| Ok(false);
        for operation in GameStoppedOperation::ALL {
            assert_eq!(
                ensure_game_stopped_with(&stopped, Path::new("/game"), operation),
                Ok(())
            );
        }
    }
}

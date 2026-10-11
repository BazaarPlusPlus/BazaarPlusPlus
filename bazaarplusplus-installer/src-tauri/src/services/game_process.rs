use std::path::Path;

use crate::problem::{SemanticProblem, SemanticProblemCode};

#[cfg(target_os = "windows")]
const BAZAAR_PROCESS_NAME: &str = "TheBazaar.exe";

#[cfg(target_os = "windows")]
fn is_bazaar_running() -> Result<bool, String> {
    crate::services::process_snapshot::process_is_running(BAZAAR_PROCESS_NAME)
}

/// Best-effort hint for History reads, which name a still-running game as the
/// likely cause of an unreadable database. It never gates a destructive
/// operation; those use [`ensure_game_stopped`]. On platforms without a
/// path-free probe (macOS today) it returns false.
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

/// Kill any leftover game process, returning whether one was actually
/// terminated. The game can keep the mod database open after its window closes;
/// the user's only other recourse is `taskkill` or a reboot.
pub(crate) fn terminate_game() -> Result<bool, String> {
    #[cfg(target_os = "windows")]
    {
        crate::services::process_snapshot::terminate_processes(BAZAAR_PROCESS_NAME)
            .map(|terminated| terminated > 0)
    }

    #[cfg(not(target_os = "windows"))]
    {
        Err("Ending the game process is not supported on this platform.".to_string())
    }
}

/// An installer operation that writes or deletes files the game or its mod may
/// hold open, and therefore refuses while The Bazaar runs. Each one reports its
/// own semantic problem code so the frontend names the blocked action.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(crate) enum GameStoppedOperation {
    Install,
    Reset,
    BepinexReset,
    /// The one-time V5 history import.
    Import,
    /// Deletion of one confirmed Legacy Root.
    LegacyDelete,
}

impl GameStoppedOperation {
    const ALL: [Self; 5] = [
        Self::Install,
        Self::Reset,
        Self::BepinexReset,
        Self::Import,
        Self::LegacyDelete,
    ];

    pub(crate) fn problem_code(self) -> SemanticProblemCode {
        match self {
            Self::Install => SemanticProblemCode::InstallBlockedByGame,
            Self::Reset => SemanticProblemCode::ResetBlockedByGame,
            Self::BepinexReset => SemanticProblemCode::BepinexResetBlockedByGame,
            Self::Import => SemanticProblemCode::ImportBlockedByGame,
            Self::LegacyDelete => SemanticProblemCode::LegacyDeleteBlockedByGame,
        }
    }

    /// The serialized problem code, used to carry a refusal through the
    /// `String` error channels of install effects and resets.
    fn token(self) -> &'static str {
        match self {
            Self::Install => "install_blocked_by_game",
            Self::Reset => "reset_blocked_by_game",
            Self::BepinexReset => "bepinex_reset_blocked_by_game",
            Self::Import => "import_blocked_by_game",
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

    /// Recovers a refusal that crossed a `String` error channel.
    pub(crate) fn from_diagnostic(diagnostic: &str) -> Option<Self> {
        GameStoppedOperation::ALL.into_iter().find_map(|operation| {
            let rest = diagnostic.strip_prefix(operation.token())?;
            let inspection_error = if rest.is_empty() {
                None
            } else {
                Some(rest.strip_prefix(": ")?.to_string())
            };
            Some(Self {
                operation,
                inspection_error,
            })
        })
    }
}

impl std::fmt::Display for GameRunningRefusal {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str(self.operation.token())?;
        if let Some(error) = &self.inspection_error {
            write!(f, ": {error}")?;
        }
        Ok(())
    }
}

impl From<GameRunningRefusal> for String {
    fn from(refusal: GameRunningRefusal) -> Self {
        refusal.to_string()
    }
}

/// Reports whether the game installed at a path is running. Destructive flows
/// take it as a parameter so tests can simulate a running game or a failed
/// inspection without touching real processes.
pub(crate) trait GameProcessProbe {
    fn is_running(&self, game_path: &Path) -> Result<bool, String>;
}

impl<F> GameProcessProbe for F
where
    F: Fn(&Path) -> Result<bool, String>,
{
    fn is_running(&self, game_path: &Path) -> Result<bool, String> {
        self(game_path)
    }
}

/// The operating system's process list.
#[derive(Clone, Copy, Debug, Default)]
pub(crate) struct SystemGameProcessProbe;

impl GameProcessProbe for SystemGameProcessProbe {
    fn is_running(&self, game_path: &Path) -> Result<bool, String> {
        #[cfg(target_os = "macos")]
        {
            let game_path = game_path
                .canonicalize()
                .map_err(|err| format!("Cannot resolve the game directory: {err}"))?;
            let output = std::process::Command::new("/bin/ps")
                .args(["-axo", "comm="])
                .output()
                .map_err(|err| format!("Cannot inspect game processes: {err}"))?;
            if !output.status.success() {
                return Err(format!(
                    "Cannot inspect game processes: ps exited with {}",
                    output.status
                ));
            }
            let processes = String::from_utf8(output.stdout)
                .map_err(|err| format!("Cannot inspect game processes: {err}"))?;
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

/// Fail-closed check run before `operation` touches files under `game_path`.
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
            inspection_error: Some(error),
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
        // Surrounding blanks in `ps` output do not hide a match.
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
                GameStoppedOperation::Import,
                SemanticProblemCode::ImportBlockedByGame,
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
            // The string token is the serialized code, so a refusal carried as a
            // diagnostic and the frontend's code name never drift apart.
            assert_eq!(
                serde_json::to_value(code).unwrap(),
                serde_json::json!(operation.token())
            );
        }
    }

    #[test]
    fn failed_inspection_refuses_like_a_running_game() {
        let broken = |_: &Path| Err("ps failed".to_string());

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

    #[test]
    fn refusals_round_trip_through_string_error_channels() {
        for operation in GameStoppedOperation::ALL {
            for inspection_error in [None, Some("cannot run ps: denied".to_string())] {
                let refusal = GameRunningRefusal {
                    operation,
                    inspection_error,
                };
                assert_eq!(
                    GameRunningRefusal::from_diagnostic(&String::from(refusal.clone())),
                    Some(refusal)
                );
            }
        }
        assert_eq!(
            GameRunningRefusal::from_diagnostic("permission denied"),
            None
        );
        assert_eq!(
            GameRunningRefusal::from_diagnostic("reset_blocked_by_gamex"),
            None
        );
    }
}

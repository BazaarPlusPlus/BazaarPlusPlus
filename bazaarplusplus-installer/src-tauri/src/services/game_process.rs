#[cfg(target_os = "windows")]
const BAZAAR_PROCESS_NAME: &str = "TheBazaar.exe";

#[cfg(target_os = "windows")]
fn is_bazaar_running() -> Result<bool, String> {
    crate::services::process_snapshot::process_is_running(BAZAAR_PROCESS_NAME)
}

/// Cross-platform best-effort check used by destructive flows that need to
/// avoid touching files the in-game mod still has open. On platforms where we
/// don't have a reliable probe (macOS today), this always returns false so the
/// caller proceeds with whatever fallback behavior it already had.
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

/// Installation must fail closed if the game process cannot be inspected.
pub(crate) fn ensure_bazaar_stopped(game_path: &std::path::Path) -> Result<(), String> {
    #[cfg(target_os = "macos")]
    let running = {
        let game_path = game_path.canonicalize().map_err(|err| err.to_string())?;
        let output = std::process::Command::new("/bin/ps")
            .args(["-axo", "comm="])
            .output()
            .map_err(|err| format!("Cannot inspect game processes: {err}"))?;
        if !output.status.success() {
            return Err("Cannot inspect game processes. Close the game and retry.".to_string());
        }
        let processes = String::from_utf8(output.stdout).map_err(|err| err.to_string())?;
        contains_game_process(&processes, &game_path)
    };
    #[cfg(target_os = "windows")]
    let running = {
        let _ = game_path;
        is_bazaar_running()?
    };
    #[cfg(not(any(target_os = "macos", target_os = "windows")))]
    let running = {
        let _ = game_path;
        false
    };
    if running {
        return Err(
            "The Bazaar is running. Close the game before installing BazaarPlusPlus.".to_string(),
        );
    }
    Ok(())
}

#[cfg(any(target_os = "macos", test))]
fn contains_game_process(processes: &str, game_path: &std::path::Path) -> bool {
    let bundle = game_path.join("TheBazaar.app");
    processes.lines().any(|line| {
        let process = std::path::Path::new(line.trim());
        process.starts_with(&bundle)
            && process
                .parent()
                .is_some_and(|parent| parent.ends_with("Contents/MacOS"))
    })
}

#[cfg(test)]
mod install_tests {
    use super::*;
    #[test]
    fn detects_native_and_trampoline_processes_without_matching_steam_or_other_installs() {
        let root = std::path::Path::new("/Games/The Bazaar");
        for executable in ["The Bazaar", "The Bazaar.orig"] {
            assert!(contains_game_process(
                &format!("/Games/The Bazaar/TheBazaar.app/Contents/MacOS/{executable}\n"),
                root
            ));
        }
        assert!(!contains_game_process("/Applications/Steam.app/Contents/MacOS/steam_osx\n/Other/TheBazaar.app/Contents/MacOS/The Bazaar.orig", root));
        assert!(!contains_game_process(
            "/Games/The Bazaar/TheBazaar.app.backup/Contents/MacOS/The Bazaar",
            root
        ));
    }
}

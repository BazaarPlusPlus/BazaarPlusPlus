use crate::commands::debug_log;
use serde::Serialize;
use std::path::Path;
use std::process::Command;
use std::time::Duration;

#[cfg(target_os = "macos")]
const STEAM_EXIT_WAIT_ATTEMPTS: usize = 60;
#[cfg(target_os = "macos")]
const STEAM_EXIT_WAIT_INTERVAL: Duration = Duration::from_millis(500);

pub fn supports_launch_option_updates(steam_path: &Path) -> bool {
    steam_path.join("userdata").is_dir()
}

#[derive(Debug, Serialize)]
pub struct SteamRunningInfo {
    pub running: bool,
}

fn steam_running_from_pgrep(
    status_code: Option<i32>,
    stdout: &[u8],
    stderr: &[u8],
) -> Result<bool, String> {
    match status_code {
        Some(0) => {
            let output = String::from_utf8_lossy(stdout);
            Ok(output.lines().any(|line| line.contains("steam_osx")))
        }
        Some(1) => Ok(false),
        _ => {
            let stderr = String::from_utf8_lossy(stderr).trim().to_string();
            if stderr.is_empty() {
                Err("Failed to inspect Steam process state.".to_string())
            } else {
                Err(format!("Failed to inspect Steam process state: {stderr}"))
            }
        }
    }
}

fn ensure_process_stopped_with<IsRunning, RequestQuit, Sleep>(
    process_name: &str,
    mut is_running: IsRunning,
    mut request_quit: RequestQuit,
    mut sleep: Sleep,
    attempts: usize,
    interval: Duration,
) -> Result<bool, String>
where
    IsRunning: FnMut() -> Result<bool, String>,
    RequestQuit: FnMut() -> Result<(), String>,
    Sleep: FnMut(Duration),
{
    if !is_running()? {
        return Ok(false);
    }

    request_quit()?;

    for _ in 0..attempts {
        sleep(interval);
        if !is_running()? {
            return Ok(true);
        }
    }

    Err(format!(
        "{process_name} did not close in time. Please quit it manually and try again."
    ))
}

#[cfg(target_os = "macos")]
fn is_steam_running() -> Result<bool, String> {
    let output = Command::new("pgrep")
        .args(["-alf", "Steam"])
        .output()
        .map_err(|err| format!("Failed to inspect Steam process state: {err}"))?;

    steam_running_from_pgrep(output.status.code(), &output.stdout, &output.stderr)
}

#[tauri::command]
pub fn detect_steam_running() -> Result<SteamRunningInfo, String> {
    #[cfg(target_os = "macos")]
    {
        return is_steam_running().map(|running| SteamRunningInfo { running });
    }

    #[cfg(not(target_os = "macos"))]
    {
        Ok(SteamRunningInfo { running: false })
    }
}

#[cfg(target_os = "macos")]
fn request_steam_quit() -> Result<(), String> {
    let output = Command::new("osascript")
        .args(["-e", "tell application \"Steam\" to quit"])
        .output()
        .map_err(|err| format!("Failed to ask Steam to quit: {err}"))?;

    if output.status.success() {
        return Ok(());
    }

    let stderr = String::from_utf8_lossy(&output.stderr).trim().to_string();
    if stderr.is_empty() {
        Err("Failed to ask Steam to quit.".to_string())
    } else {
        Err(format!("Failed to ask Steam to quit: {stderr}"))
    }
}

#[cfg(target_os = "macos")]
pub fn prepare_steam_for_launch_option_update(steam_path: &Path) -> Result<(), String> {
    if !supports_launch_option_updates(steam_path) {
        debug_log!(
            "Skipping Steam shutdown because Steam userdata was not found at {}.",
            steam_path.display()
        );
        return Ok(());
    }

    let stopped = ensure_process_stopped_with(
        "Steam",
        is_steam_running,
        request_steam_quit,
        std::thread::sleep,
        STEAM_EXIT_WAIT_ATTEMPTS,
        STEAM_EXIT_WAIT_INTERVAL,
    )?;

    if stopped {
        debug_log!("Steam was running and has been closed.");
    }

    Ok(())
}

#[cfg(not(target_os = "macos"))]
pub fn prepare_steam_for_launch_option_update(_steam_path: &Path) -> Result<(), String> {
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::{ensure_process_stopped_with, steam_running_from_pgrep};
    use std::time::Duration;

    #[test]
    fn test_steam_running_from_pgrep_detects_macos_main_process() {
        let stdout = b"1871 /Users/test/Steam.AppBundle/Steam/Contents/MacOS/steam_osx\n";

        let running = steam_running_from_pgrep(Some(0), stdout, b"").unwrap();

        assert!(running);
    }

    #[test]
    fn test_steam_running_from_pgrep_ignores_helper_only_output() {
        let stdout = b"1901 /Users/test/Steam Helper.app/Contents/MacOS/Steam Helper\n";

        let running = steam_running_from_pgrep(Some(0), stdout, b"").unwrap();

        assert!(!running);
    }

    #[test]
    fn test_ensure_process_stopped_with_skips_quit_when_process_is_not_running() {
        let mut quit_called = false;
        let result = ensure_process_stopped_with(
            "Steam",
            || Ok(false),
            || {
                quit_called = true;
                Ok(())
            },
            |_| {},
            3,
            Duration::from_millis(1),
        );

        assert_eq!(result.unwrap(), false);
        assert!(!quit_called);
    }

    #[test]
    fn test_ensure_process_stopped_with_requests_quit_and_waits_for_exit() {
        let mut running_checks = 0;
        let mut quit_called = false;

        let result = ensure_process_stopped_with(
            "Steam",
            || {
                running_checks += 1;
                Ok(running_checks < 3)
            },
            || {
                quit_called = true;
                Ok(())
            },
            |_| {},
            5,
            Duration::from_millis(1),
        );

        assert_eq!(result.unwrap(), true);
        assert!(quit_called);
    }

    #[test]
    fn test_ensure_process_stopped_with_errors_when_process_never_exits() {
        let result = ensure_process_stopped_with(
            "Steam",
            || Ok(true),
            || Ok(()),
            |_| {},
            2,
            Duration::from_millis(1),
        );

        let error = result.unwrap_err();
        assert!(error.contains("Steam"));
        assert!(error.contains("close"));
    }
}

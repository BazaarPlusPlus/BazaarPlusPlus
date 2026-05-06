use crate::commands::debug_log;
use serde::Serialize;
use std::path::Path;
use std::process::Command;
use std::time::Duration;

#[cfg_attr(not(any(target_os = "windows", test)), allow(dead_code))]
const STEAM_PROCESS_NAME: &str = "steam.exe";
#[cfg(any(target_os = "macos", target_os = "windows"))]
const STEAM_EXIT_WAIT_ATTEMPTS: usize = 60;
#[cfg(any(target_os = "macos", target_os = "windows"))]
const STEAM_EXIT_WAIT_INTERVAL: Duration = Duration::from_millis(500);

pub fn supports_launch_option_updates(steam_path: &Path) -> bool {
    steam_path.join("userdata").is_dir()
}

#[derive(Debug, Serialize, ts_rs::TS)]
#[ts(export)]
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

#[cfg_attr(not(any(target_os = "windows", test)), allow(dead_code))]
fn tasklist_output_indicates_steam_running(stdout: &[u8]) -> bool {
    let output = String::from_utf8_lossy(stdout);

    output.lines().any(|line| line.contains(STEAM_PROCESS_NAME))
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

#[cfg(target_os = "windows")]
fn is_steam_running() -> Result<bool, String> {
    let output = Command::new("tasklist")
        .args([
            "/FI",
            &format!("IMAGENAME eq {STEAM_PROCESS_NAME}"),
            "/FO",
            "CSV",
            "/NH",
        ])
        .output()
        .map_err(|err| format!("Failed to inspect Steam process state: {err}"))?;

    if output.status.success() {
        return Ok(tasklist_output_indicates_steam_running(&output.stdout));
    }

    let stderr = String::from_utf8_lossy(&output.stderr).trim().to_string();
    if stderr.is_empty() {
        Err("Failed to inspect Steam process state.".to_string())
    } else {
        Err(format!("Failed to inspect Steam process state: {stderr}"))
    }
}

#[tauri::command]
pub fn detect_steam_running() -> Result<SteamRunningInfo, String> {
    #[cfg(target_os = "macos")]
    {
        return is_steam_running().map(|running| SteamRunningInfo { running });
    }

    #[cfg(target_os = "windows")]
    {
        return is_steam_running().map(|running| SteamRunningInfo { running });
    }

    #[cfg(not(any(target_os = "macos", target_os = "windows")))]
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

#[cfg(target_os = "windows")]
fn request_steam_quit() -> Result<(), String> {
    let output = Command::new("taskkill")
        .args(["/IM", STEAM_PROCESS_NAME, "/T"])
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

#[cfg(any(target_os = "macos", target_os = "windows"))]
fn close_steam_internal() -> Result<bool, String> {
    ensure_process_stopped_with(
        "Steam",
        is_steam_running,
        request_steam_quit,
        std::thread::sleep,
        STEAM_EXIT_WAIT_ATTEMPTS,
        STEAM_EXIT_WAIT_INTERVAL,
    )
}

#[tauri::command]
pub fn close_steam() -> Result<(), String> {
    #[cfg(any(target_os = "macos", target_os = "windows"))]
    {
        let stopped = close_steam_internal()?;
        if stopped {
            debug_log!("Steam was running and has been closed.");
        }
        return Ok(());
    }

    #[cfg(not(any(target_os = "macos", target_os = "windows")))]
    {
        Ok(())
    }
}

#[cfg(any(target_os = "macos", target_os = "windows"))]
pub fn prepare_steam_for_launch_option_update(
    steam_path: &Path,
    skip_shutdown: bool,
) -> Result<(), String> {
    if !supports_launch_option_updates(steam_path) {
        debug_log!(
            "Skipping Steam shutdown because Steam userdata was not found at {}.",
            steam_path.display()
        );
        return Ok(());
    }

    if skip_shutdown {
        debug_log!("Allowing Steam to keep running during launch option update.");
        return Ok(());
    }

    let stopped = close_steam_internal()?;

    if stopped {
        debug_log!("Steam was running and has been closed.");
    }

    Ok(())
}

#[cfg(not(any(target_os = "macos", target_os = "windows")))]
pub fn prepare_steam_for_launch_option_update(
    _steam_path: &Path,
    _skip_shutdown: bool,
) -> Result<(), String> {
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::{
        ensure_process_stopped_with, steam_running_from_pgrep,
        tasklist_output_indicates_steam_running,
    };
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
    fn test_tasklist_output_indicates_steam_running_detects_steam_process() {
        let stdout = b"\"steam.exe\",\"15432\",\"Console\",\"1\",\"512,340 K\"\r\n";

        assert!(tasklist_output_indicates_steam_running(stdout));
    }

    #[test]
    fn test_tasklist_output_indicates_steam_running_returns_false_when_missing() {
        let stdout = b"INFO: No tasks are running which match the specified criteria.\r\n";

        assert!(!tasklist_output_indicates_steam_running(stdout));
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

use serde::Serialize;
#[cfg(target_os = "windows")]
use std::process::Command;

#[cfg_attr(not(any(target_os = "windows", test)), allow(dead_code))]
const BAZAAR_PROCESS_NAME: &str = "TheBazaar.exe";

#[derive(Debug, Serialize, ts_rs::TS)]
#[ts(export)]
pub struct GameRunningInfo {
    pub running: bool,
}

#[cfg_attr(not(any(target_os = "windows", test)), allow(dead_code))]
fn tasklist_output_indicates_running(stdout: &[u8]) -> bool {
    let output = String::from_utf8_lossy(stdout);

    output
        .lines()
        .any(|line| line.contains(BAZAAR_PROCESS_NAME))
}

#[cfg(target_os = "windows")]
fn is_bazaar_running() -> Result<bool, String> {
    let output = Command::new("tasklist")
        .args([
            "/FI",
            &format!("IMAGENAME eq {BAZAAR_PROCESS_NAME}"),
            "/FO",
            "CSV",
            "/NH",
        ])
        .output()
        .map_err(|err| format!("Failed to inspect The Bazaar process state: {err}"))?;

    if output.status.success() {
        return Ok(tasklist_output_indicates_running(&output.stdout));
    }

    let stderr = String::from_utf8_lossy(&output.stderr).trim().to_string();
    if stderr.is_empty() {
        Err("Failed to inspect The Bazaar process state.".to_string())
    } else {
        Err(format!(
            "Failed to inspect The Bazaar process state: {stderr}"
        ))
    }
}

/// Cross-platform best-effort check used by destructive flows that need to
/// avoid touching files the in-game mod still has open. On platforms where we
/// don't have a reliable probe (macOS today), this always returns false so the
/// caller proceeds with whatever fallback behavior it already had.
#[allow(dead_code)]
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

#[tauri::command]
pub fn detect_bazaar_running() -> Result<GameRunningInfo, String> {
    #[cfg(target_os = "windows")]
    {
        return is_bazaar_running().map(|running| GameRunningInfo { running });
    }

    #[cfg(not(target_os = "windows"))]
    {
        Ok(GameRunningInfo { running: false })
    }
}

#[cfg(test)]
mod tests {
    use super::tasklist_output_indicates_running;

    #[test]
    fn test_tasklist_output_indicates_running_detects_bazaar_process() {
        let stdout = b"\"TheBazaar.exe\",\"15432\",\"Console\",\"1\",\"512,340 K\"\r\n";

        assert!(tasklist_output_indicates_running(stdout));
    }

    #[test]
    fn test_tasklist_output_indicates_running_returns_false_when_missing() {
        let stdout = b"INFO: No tasks are running which match the specified criteria.\r\n";

        assert!(!tasklist_output_indicates_running(stdout));
    }
}

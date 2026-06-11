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

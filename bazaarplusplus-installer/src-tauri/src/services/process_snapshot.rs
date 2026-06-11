//! "Is a process named X running?" without spawning `tasklist`.
//!
//! On Windows this walks an in-process toolhelp snapshot instead of launching
//! the `tasklist` subprocess. That removes the per-call `CreateProcess` +
//! console-window flash + Defender scan of `tasklist.exe` from the flows that
//! close Steam (launch-option writes) and guard destructive resets.

/// Case-insensitive comparison of a process image name (e.g. `"Steam.exe"`)
/// against a target (e.g. `"steam.exe"`). Kept pure so the matching rule is
/// unit-testable without enumerating real processes.
#[cfg_attr(not(any(target_os = "windows", test)), allow(dead_code))]
pub(crate) fn image_name_matches(candidate: &str, target: &str) -> bool {
    candidate.eq_ignore_ascii_case(target)
}

/// Return whether any running process has the given image name. Windows-only;
/// callers on other platforms use their own probe (e.g. `pgrep`).
#[cfg(target_os = "windows")]
pub(crate) fn process_is_running(target: &str) -> Result<bool, String> {
    use windows::Win32::Foundation::CloseHandle;
    use windows::Win32::System::Diagnostics::ToolHelp::{
        CreateToolhelp32Snapshot, Process32FirstW, Process32NextW, PROCESSENTRY32W,
        TH32CS_SNAPPROCESS,
    };

    // SAFETY: a process snapshot is created, iterated with a correctly-sized
    // PROCESSENTRY32W, and the snapshot handle is always closed before return.
    unsafe {
        let snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)
            .map_err(|err| format!("Failed to inspect process state: {err}"))?;

        let mut entry = PROCESSENTRY32W {
            dwSize: std::mem::size_of::<PROCESSENTRY32W>() as u32,
            ..Default::default()
        };

        let mut found = false;
        if Process32FirstW(snapshot, &mut entry).is_ok() {
            loop {
                if image_name_matches(&image_name_from_entry(&entry), target) {
                    found = true;
                    break;
                }
                if Process32NextW(snapshot, &mut entry).is_err() {
                    break;
                }
            }
        }

        let _ = CloseHandle(snapshot);
        Ok(found)
    }
}

/// Decode the NUL-terminated UTF-16 `szExeFile` field into a Rust string.
#[cfg(target_os = "windows")]
fn image_name_from_entry(
    entry: &windows::Win32::System::Diagnostics::ToolHelp::PROCESSENTRY32W,
) -> String {
    let name = &entry.szExeFile;
    let len = name.iter().position(|&c| c == 0).unwrap_or(name.len());
    String::from_utf16_lossy(&name[..len])
}

#[cfg(test)]
mod tests {
    use super::image_name_matches;

    #[test]
    fn image_name_matches_is_case_insensitive() {
        assert!(image_name_matches("Steam.exe", "steam.exe"));
        assert!(image_name_matches("STEAM.EXE", "steam.exe"));
        assert!(image_name_matches("TheBazaar.exe", "thebazaar.exe"));
    }

    #[test]
    fn image_name_matches_rejects_different_names() {
        assert!(!image_name_matches("notsteam.exe", "steam.exe"));
        assert!(!image_name_matches("steamwebhelper.exe", "steam.exe"));
    }
}

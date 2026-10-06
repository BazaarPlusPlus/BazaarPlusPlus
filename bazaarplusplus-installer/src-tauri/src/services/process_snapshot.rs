//! "Is a process named X running?" without spawning `tasklist`.
//!
//! On Windows this walks an in-process toolhelp snapshot instead of launching
//! the `tasklist` subprocess. That removes the per-call `CreateProcess` +
//! console-window flash + Defender scan of `tasklist.exe` from the flows that
//! close Steam (launch-option writes) and guard destructive resets.

/// Case-insensitive comparison of a process image name (e.g. `"Steam.exe"`)
/// against a target (e.g. `"steam.exe"`).
#[cfg(target_os = "windows")]
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

/// Terminate every running process with the given image name and return how
/// many were killed. Windows-only. Used to clear a game process that outlived
/// its window and still holds the mod database open; a process that exits on
/// its own between the snapshot and the kill is not an error.
#[cfg(target_os = "windows")]
pub(crate) fn terminate_processes(target: &str) -> Result<u32, String> {
    use windows::Win32::Foundation::CloseHandle;
    use windows::Win32::System::Diagnostics::ToolHelp::{
        CreateToolhelp32Snapshot, Process32FirstW, Process32NextW, PROCESSENTRY32W,
        TH32CS_SNAPPROCESS,
    };
    use windows::Win32::System::Threading::{OpenProcess, TerminateProcess, PROCESS_TERMINATE};

    // SAFETY: the snapshot is iterated with a correctly-sized PROCESSENTRY32W and
    // every handle opened here is closed before the loop advances; the snapshot
    // handle is closed before return.
    unsafe {
        let snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)
            .map_err(|err| format!("Failed to inspect process state: {err}"))?;

        let mut entry = PROCESSENTRY32W {
            dwSize: std::mem::size_of::<PROCESSENTRY32W>() as u32,
            ..Default::default()
        };

        let mut terminated = 0u32;
        if Process32FirstW(snapshot, &mut entry).is_ok() {
            loop {
                if image_name_matches(&image_name_from_entry(&entry), target) {
                    if let Ok(process) = OpenProcess(PROCESS_TERMINATE, false, entry.th32ProcessID)
                    {
                        if TerminateProcess(process, 1).is_ok() {
                            terminated += 1;
                        }
                        let _ = CloseHandle(process);
                    }
                }
                if Process32NextW(snapshot, &mut entry).is_err() {
                    break;
                }
            }
        }

        let _ = CloseHandle(snapshot);
        Ok(terminated)
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

/// Return whether any running process has the given image name on Linux. Proton
/// runs the Windows build as a native process whose `/proc/<pid>/comm` carries
/// the image name (e.g. `TheBazaar.exe`), so a `/proc` scan is the equivalent of
/// the Windows toolhelp walk without spawning `pgrep`.
#[cfg(target_os = "linux")]
pub(crate) fn process_is_running(target: &str) -> Result<bool, String> {
    let entries = std::fs::read_dir("/proc")
        .map_err(|err| format!("Failed to inspect process state: {err}"))?;

    for entry in entries.flatten() {
        let name = entry.file_name();
        if !name.to_string_lossy().chars().all(|c| c.is_ascii_digit()) {
            continue;
        }

        if let Ok(comm) = std::fs::read_to_string(entry.path().join("comm")) {
            if comm.trim().eq_ignore_ascii_case(target) {
                return Ok(true);
            }
        }
    }

    Ok(false)
}

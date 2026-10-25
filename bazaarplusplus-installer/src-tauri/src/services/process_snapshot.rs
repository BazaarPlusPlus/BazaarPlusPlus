//! "Is a process named X running?" without spawning `tasklist`.
//!
//! On Windows this walks an in-process toolhelp snapshot instead of launching
//! the `tasklist` subprocess. That removes the per-call `CreateProcess` +
//! console-window flash + Defender scan of `tasklist.exe` from the flows that
//! close Steam (launch-option writes) and guard destructive resets.

#[cfg(target_os = "windows")]
use anyhow::Context as _;

/// Case-insensitive comparison of a process image name (e.g. `"Steam.exe"`)
/// against a target (e.g. `"steam.exe"`).
#[cfg(target_os = "windows")]
pub(crate) fn image_name_matches(candidate: &str, target: &str) -> bool {
    candidate.eq_ignore_ascii_case(target)
}

/// Return whether any running process has the given image name. Windows-only;
/// callers on other platforms use their own probe (e.g. `pgrep`).
#[cfg(target_os = "windows")]
pub(crate) fn process_is_running(target: &str) -> anyhow::Result<bool> {
    Ok(running_processes()?
        .iter()
        .any(|process| image_name_matches(&process.image_name, target)))
}

/// Terminate every running process with the given image name and return how
/// many were killed. Windows-only. Used to clear a game process that outlived
/// its window and still holds the mod database open; a process that exits on
/// its own between the snapshot and the kill is not an error.
#[cfg(target_os = "windows")]
pub(crate) fn terminate_processes(target: &str) -> anyhow::Result<u32> {
    use windows::Win32::System::Threading::{OpenProcess, TerminateProcess, PROCESS_TERMINATE};

    let mut terminated = 0u32;
    for process in running_processes()?
        .iter()
        .filter(|process| image_name_matches(&process.image_name, target))
    {
        // SAFETY: `OpenProcess` takes no pointers; a returned handle is owned
        // by the guard, which closes it.
        let Ok(handle) = (unsafe { OpenProcess(PROCESS_TERMINATE, false, process.id) }) else {
            continue;
        };
        let handle = OwnedHandle(handle);
        // SAFETY: `handle` is a live process handle opened with
        // `PROCESS_TERMINATE` access.
        if unsafe { TerminateProcess(handle.0, 1) }.is_ok() {
            terminated += 1;
        }
    }
    Ok(terminated)
}

/// One row of a toolhelp process snapshot.
#[cfg(target_os = "windows")]
struct ProcessEntry {
    id: u32,
    image_name: String,
}

/// Owns a Win32 handle and closes it on drop, so no return path leaks it.
#[cfg(target_os = "windows")]
struct OwnedHandle(windows::Win32::Foundation::HANDLE);

#[cfg(target_os = "windows")]
impl Drop for OwnedHandle {
    fn drop(&mut self) {
        // SAFETY: the guard is built only from a handle a successful Win32
        // call returned to us, and it is the handle's sole owner, so the
        // handle is closed exactly once. A close failure leaves nothing to do.
        let _ = unsafe { windows::Win32::Foundation::CloseHandle(self.0) };
    }
}

/// List every running process from one toolhelp snapshot.
#[cfg(target_os = "windows")]
fn running_processes() -> anyhow::Result<Vec<ProcessEntry>> {
    use windows::Win32::System::Diagnostics::ToolHelp::{
        CreateToolhelp32Snapshot, Process32FirstW, Process32NextW, PROCESSENTRY32W,
        TH32CS_SNAPPROCESS,
    };

    // SAFETY: `CreateToolhelp32Snapshot` takes no pointers; the returned
    // handle is owned by the guard, which closes it.
    let snapshot = unsafe { CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0) }
        .context("Failed to inspect process state")?;
    let snapshot = OwnedHandle(snapshot);

    let mut entry = PROCESSENTRY32W {
        dwSize: std::mem::size_of::<PROCESSENTRY32W>() as u32,
        ..Default::default()
    };
    let mut processes = Vec::new();
    // SAFETY: `snapshot` is a live process snapshot and `entry` is a writable
    // PROCESSENTRY32W whose `dwSize` holds its real size.
    let mut has_entry = unsafe { Process32FirstW(snapshot.0, &mut entry) }.is_ok();
    while has_entry {
        processes.push(ProcessEntry {
            id: entry.th32ProcessID,
            image_name: image_name_from_entry(&entry),
        });
        // SAFETY: same snapshot and entry as the `Process32FirstW` call above.
        has_entry = unsafe { Process32NextW(snapshot.0, &mut entry) }.is_ok();
    }
    Ok(processes)
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

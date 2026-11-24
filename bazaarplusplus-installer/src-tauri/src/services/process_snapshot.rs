//! Use an in-process snapshot to avoid `tasklist.exe` startup, console flashes, and Defender scans.

#[cfg(target_os = "windows")]
use anyhow::Context as _;

#[cfg(target_os = "windows")]
pub(crate) fn image_name_matches(candidate: &str, target: &str) -> bool {
    candidate.eq_ignore_ascii_case(target)
}

#[cfg(target_os = "windows")]
pub(crate) fn process_is_running(target: &str) -> anyhow::Result<bool> {
    Ok(running_processes()?
        .iter()
        .any(|process| image_name_matches(&process.image_name, target)))
}

/// Identify the selected copy while a query handle keeps the process ID alive.
#[cfg(target_os = "windows")]
pub(crate) fn inspect_game(target: &std::path::Path, terminate: bool) -> anyhow::Result<bool> {
    use std::ffi::OsString;
    use std::os::windows::ffi::OsStringExt as _;
    use std::path::PathBuf;
    use windows::core::PWSTR;
    use windows::Win32::Foundation::ERROR_INVALID_PARAMETER;
    use windows::Win32::System::Threading::{
        OpenProcess, QueryFullProcessImageNameW, TerminateProcess, PROCESS_NAME_WIN32,
        PROCESS_QUERY_LIMITED_INFORMATION, PROCESS_TERMINATE,
    };

    let target = match target.canonicalize() {
        Ok(path) => path,
        Err(err) if err.kind() == std::io::ErrorKind::NotFound => return Ok(false),
        Err(err) => return Err(err.into()),
    };
    let name = target
        .file_name()
        .context("Game executable name is missing")?
        .to_string_lossy();
    let mut matched = false;
    for process in running_processes()?
        .into_iter()
        .filter(|p| image_name_matches(&p.image_name, &name))
    {
        // SAFETY: successful calls return an owned handle, closed by OwnedHandle.
        let query =
            match unsafe { OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, process.id) } {
                Ok(handle) => OwnedHandle(handle),
                Err(err) if err.code() == ERROR_INVALID_PARAMETER.to_hresult() => continue,
                Err(err) => return Err(err.into()),
            };
        let mut buffer = vec![0u16; 32768];
        let mut size = buffer.len() as u32;
        // SAFETY: size specifies the writable buffer capacity in UTF-16 units.
        unsafe {
            QueryFullProcessImageNameW(
                query.0,
                PROCESS_NAME_WIN32,
                PWSTR(buffer.as_mut_ptr()),
                &mut size,
            )
        }?;
        let image = PathBuf::from(OsString::from_wide(&buffer[..size as usize]));
        if image.canonicalize()? != target {
            continue;
        }
        if !terminate {
            return Ok(true);
        }
        // SAFETY: query keeps this verified process object alive while opening the termination handle.
        let handle = OwnedHandle(unsafe { OpenProcess(PROCESS_TERMINATE, false, process.id) }?);
        // SAFETY: handle was opened with termination access.
        unsafe { TerminateProcess(handle.0, 1) }?;
        matched = true;
    }
    Ok(matched)
}

#[cfg(target_os = "windows")]
struct ProcessEntry {
    id: u32,
    image_name: String,
}

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

#[cfg(target_os = "windows")]
fn image_name_from_entry(
    entry: &windows::Win32::System::Diagnostics::ToolHelp::PROCESSENTRY32W,
) -> String {
    let name = &entry.szExeFile;
    let len = name.iter().position(|&c| c == 0).unwrap_or(name.len());
    String::from_utf16_lossy(&name[..len])
}

#[cfg(all(test, target_os = "windows"))]
mod tests {
    use super::inspect_game;
    use std::os::windows::process::CommandExt as _;
    use std::process::{Child, Command, Stdio};
    use std::{fs, path::Path};

    struct FixtureProcess(Child);

    impl Drop for FixtureProcess {
        fn drop(&mut self) {
            let _ = self.0.kill();
            let _ = self.0.wait();
        }
    }

    fn spawn(path: &Path) -> anyhow::Result<FixtureProcess> {
        use anyhow::Context as _;
        let ping = Path::new(&std::env::var_os("SystemRoot").context("Windows system directory")?)
            .join("System32/ping.exe");
        fs::create_dir_all(path.parent().context("fixture directory")?)?;
        fs::copy(ping, path)?;
        Ok(FixtureProcess(
            Command::new(path)
                .args(["-t", "127.0.0.1"])
                .creation_flags(windows::Win32::System::Threading::CREATE_NO_WINDOW.0)
                .stdout(Stdio::null())
                .stderr(Stdio::null())
                .spawn()?,
        ))
    }

    #[test]
    fn same_named_processes_are_inspected_and_ended_by_installation() {
        let root = tempfile::tempdir().unwrap();
        let steam = root.path().join("Steam/BppProcessFixture.exe");
        let tempo = root.path().join("Tempo/BppProcessFixture.exe");
        let mut first = spawn(&steam).unwrap();
        let mut second = spawn(&tempo).unwrap();
        assert!(inspect_game(&steam, false).unwrap());
        assert!(inspect_game(&tempo, true).unwrap());
        second.0.wait().unwrap();
        assert!(!inspect_game(&tempo, false).unwrap());
        assert!(first.0.try_wait().unwrap().is_none());
    }
}

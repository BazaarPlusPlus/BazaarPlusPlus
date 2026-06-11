use serde::{Deserialize, Serialize};
#[cfg(target_os = "windows")]
use serde_json::Value;
#[cfg(target_os = "macos")]
use std::ffi::OsStr;
use std::fs;
use std::path::{Component, Path, PathBuf};
use std::process::{Command, Stdio};
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::thread;
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};
use tauri::Emitter;

use crate::services::bepinex::remove_dir_with_retry;

const CAPTURE_TIMEOUT: Duration = Duration::from_secs(180);
const POLL_INTERVAL: Duration = Duration::from_millis(500);
const PROCESS_CLOSE_TIMEOUT: Duration = Duration::from_secs(10);
const BACKUP_PARENT_DIR: &str = "bppinstaller-tempo-backup";
const BACKUP_MANIFEST_FILE: &str = "manifest.json";

static LAUNCH_IN_FLIGHT: AtomicBool = AtomicBool::new(false);
static CANCEL_REQUESTED: AtomicBool = AtomicBool::new(false);
static BACKUP_SEQUENCE: AtomicU64 = AtomicU64::new(0);

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum ProcessMatchMode {
    AnyGame,
    ExactExecutable,
}

struct InFlightGuard;

impl Drop for InFlightGuard {
    fn drop(&mut self) {
        LAUNCH_IN_FLIGHT.store(false, Ordering::SeqCst);
    }
}

pub(crate) fn request_cancel() {
    CANCEL_REQUESTED.store(true, Ordering::SeqCst);
}

fn cancel_requested() -> bool {
    CANCEL_REQUESTED.load(Ordering::SeqCst)
}

pub(crate) fn recover_orphaned_backups_best_effort() {
    let parent = std::env::temp_dir().join(BACKUP_PARENT_DIR);
    let Ok(entries) = fs::read_dir(&parent) else {
        return;
    };
    let current_process_prefix = format!("{}-", std::process::id());

    for entry in entries.flatten() {
        let root = entry.path();
        if !root.is_dir() {
            continue;
        }
        if LAUNCH_IN_FLIGHT.load(Ordering::SeqCst)
            && entry
                .file_name()
                .to_string_lossy()
                .starts_with(&current_process_prefix)
        {
            continue;
        }
        if let Err(err) = recover_backup_root(&root) {
            crate::services::debug_error!(
                "Failed to recover orphaned Tempo backup {}: {err}",
                root.display()
            );
        }
    }
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "snake_case")]
struct TempoLaunchStatus {
    phase: &'static str,
    message: String,
}

#[derive(Clone, Debug)]
struct GameProcess {
    pid: u32,
    command_line: String,
}

#[derive(Clone, Debug)]
enum LauncherTarget {
    Executable(PathBuf),
    #[cfg(target_os = "macos")]
    AppBundle(PathBuf),
}

#[derive(Clone, Debug, Deserialize, Serialize)]
struct BackupEntry {
    original: PathBuf,
    backup: PathBuf,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
struct BackupManifest {
    entries: Vec<BackupEntry>,
}

#[derive(Debug)]
struct BackupSession {
    root: PathBuf,
    entries: Vec<BackupEntry>,
    restored: bool,
}

pub fn launch_game_via_tempo(
    app: tauri::AppHandle,
    requested_game_path: Option<String>,
    requested_launcher_path: Option<String>,
) -> Result<(), String> {
    if LAUNCH_IN_FLIGHT
        .compare_exchange(false, true, Ordering::SeqCst, Ordering::SeqCst)
        .is_err()
    {
        return Err("tempo_launch_already_in_progress".to_string());
    }
    let _in_flight = InFlightGuard;
    CANCEL_REQUESTED.store(false, Ordering::SeqCst);

    emit_status(&app, "prepare", "Preparing native Tempo Launcher flow.");

    let game_dir = resolve_game_dir(requested_game_path)?;
    let launcher = resolve_launcher_target(requested_launcher_path.as_deref(), &game_dir)?;
    let game_exe = game_executable_path(&game_dir)?;

    let existing = list_game_processes(&game_exe, ProcessMatchMode::AnyGame)?;
    if !existing.is_empty() {
        return Err(format!(
            "tempo_game_already_running: {} process(es) matched",
            existing.len()
        ));
    }

    #[cfg(target_os = "macos")]
    let mut trampoline_temporarily_removed = false;

    #[cfg(target_os = "macos")]
    {
        let installed = crate::services::detect::is_bepinex_installed(&game_dir);
        let desired = crate::services::macos_version::trampoline_forced()
            || crate::services::bepinex::read_launch_mode_marker(&game_dir)
                == Some(crate::services::bepinex::LaunchMode::Trampoline);
        let applied = crate::services::bepinex::is_trampolined(&game_dir).unwrap_or(false);
        if installed && desired != applied {
            return Err("tempo_install_needs_repair".to_string());
        }
    }

    let mut backup = BackupSession::new()?;

    #[cfg(target_os = "macos")]
    {
        if crate::services::bepinex::is_trampolined(&game_dir).unwrap_or(false) {
            emit_status(
                &app,
                "prepare",
                "Temporarily restoring the vanilla macOS app binary for Tempo integrity check.",
            );
            crate::services::bepinex::uninstall_trampoline(&game_dir)?;
            trampoline_temporarily_removed = true;
        }
    }

    let result = (|| -> Result<(), String> {
        emit_status(&app, "backup", "Backing up and removing mod payload.");
        let removal_items = removal_items();
        backup.backup_and_remove(&game_dir, &removal_items)?;

        emit_status(
            &app,
            "launcher",
            "Starting Tempo Launcher. Click PLAY in Tempo to continue.",
        );
        start_launcher(&launcher)?;

        let launched = wait_for_game_process(&game_exe, CAPTURE_TIMEOUT)?;
        emit_status(
            &app,
            "capture",
            format!("Captured native game process pid {}.", launched.pid),
        );

        let args = extract_args_after_executable(&launched.command_line, &game_exe);
        terminate_process(launched.pid)?;
        wait_for_process_exit(launched.pid, &game_exe, PROCESS_CLOSE_TIMEOUT)?;

        emit_status(&app, "restore", "Restoring mod payload.");
        backup.restore()?;

        #[cfg(target_os = "macos")]
        {
            if trampoline_temporarily_removed {
                emit_status(&app, "restore", "Reinstalling macOS launch trampoline.");
                crate::services::bepinex::install_trampoline(&app, &game_dir)?;
            }
        }

        emit_status(
            &app,
            "launch",
            "Launching modded game with captured Tempo arguments.",
        );
        #[cfg(target_os = "macos")]
        launch_modded_game(&game_dir, &args, trampoline_temporarily_removed)?;

        #[cfg(not(target_os = "macos"))]
        launch_modded_game(&game_dir, &args)?;

        emit_status(
            &app,
            "done",
            "The Bazaar launched through native Tempo flow.",
        );
        Ok(())
    })();

    if let Err(err) = result {
        let restore_error = backup.restore().err();
        #[cfg(target_os = "macos")]
        {
            if trampoline_temporarily_removed {
                let _ = crate::services::bepinex::install_trampoline(&app, &game_dir);
            }
        }
        let err = restore_error.map_or(err.clone(), |restore_err| format!("{err}; {restore_err}"));
        emit_status(&app, "error", err.clone());
        return Err(err);
    }

    Ok(())
}

fn emit_status(app: &tauri::AppHandle, phase: &'static str, message: impl Into<String>) {
    let _ = app.emit(
        "tempo-launch-status",
        TempoLaunchStatus {
            phase,
            message: message.into(),
        },
    );
}

fn resolve_game_dir(requested_game_path: Option<String>) -> Result<PathBuf, String> {
    if let Some(path) = requested_game_path.and_then(|value| {
        let trimmed = value.trim();
        (!trimmed.is_empty()).then(|| PathBuf::from(trimmed))
    }) {
        if is_valid_game_dir(&path) {
            return Ok(path);
        }
        return Err(format!(
            "Invalid The Bazaar game directory: {}",
            path.display()
        ));
    }

    crate::services::game_path::fallback_game_candidates()
        .into_iter()
        .find(|path| is_valid_game_dir(path))
        .ok_or_else(|| {
            "Could not locate The Bazaar game directory. Select the directory manually first."
                .to_string()
        })
}

fn is_valid_game_dir(path: &Path) -> bool {
    game_executable_path(path)
        .map(|exe| exe.exists())
        .unwrap_or(false)
}

fn game_executable_path(game_dir: &Path) -> Result<PathBuf, String> {
    #[cfg(target_os = "windows")]
    {
        return Ok(game_dir.join("TheBazaar.exe"));
    }

    #[cfg(target_os = "macos")]
    {
        return Ok(game_dir.join("TheBazaar.app/Contents/MacOS/TheBazaar"));
    }

    #[cfg(not(any(target_os = "windows", target_os = "macos")))]
    {
        Ok(game_dir.join("TheBazaar"))
    }
}

fn resolve_launcher_target(
    requested_launcher_path: Option<&str>,
    game_dir: &Path,
) -> Result<LauncherTarget, String> {
    if let Some(path) = requested_launcher_path
        .map(str::trim)
        .filter(|path| !path.is_empty())
        .map(PathBuf::from)
    {
        if let Some(target) = launcher_target_from_path(&path) {
            return Ok(target);
        }
        return Err(format!("Invalid Tempo Launcher path: {}", path.display()));
    }

    for candidate in launcher_candidates(game_dir) {
        if let Some(target) = launcher_target_from_path(&candidate) {
            return Ok(target);
        }
    }

    Err("tempo_launcher_not_found".to_string())
}

fn launcher_target_from_path(path: &Path) -> Option<LauncherTarget> {
    if path.is_file() {
        return Some(LauncherTarget::Executable(path.to_path_buf()));
    }

    #[cfg(target_os = "windows")]
    {
        if path.is_dir() {
            for name in [
                "Tempo Launcher.exe",
                "Tempo Launcher - Beta.exe",
                "launcher.exe",
            ] {
                let candidate = path.join(name);
                if candidate.is_file() {
                    return Some(LauncherTarget::Executable(candidate));
                }
            }
        }
    }

    #[cfg(target_os = "macos")]
    {
        if path.extension() == Some(OsStr::new("app")) && path.is_dir() {
            return Some(LauncherTarget::AppBundle(path.to_path_buf()));
        }
        if path.is_dir() {
            for name in [
                "Tempo Launcher.app",
                "Tempo Launcher - Beta.app",
                "TempoLauncher.app",
            ] {
                let candidate = path.join(name);
                if candidate.is_dir() {
                    return Some(LauncherTarget::AppBundle(candidate));
                }
            }
        }
    }

    None
}

fn launcher_candidates(game_dir: &Path) -> Vec<PathBuf> {
    let mut candidates = Vec::new();

    for ancestor in game_dir.ancestors().take(4) {
        push_unique(&mut candidates, ancestor.to_path_buf());
    }

    #[cfg(target_os = "windows")]
    {
        for var_name in ["LOCALAPPDATA", "APPDATA"] {
            if let Some(base) = std::env::var_os(var_name).map(PathBuf::from) {
                push_unique(&mut candidates, base.join("Programs/Tempo Launcher - Beta"));
                push_unique(&mut candidates, base.join("Programs/tempo-launcher-beta"));
                push_unique(&mut candidates, base.join("Tempo Launcher - Beta"));
            }
        }
    }

    #[cfg(target_os = "macos")]
    {
        push_unique(
            &mut candidates,
            PathBuf::from("/Applications/Tempo Launcher.app"),
        );
        push_unique(
            &mut candidates,
            PathBuf::from("/Applications/Tempo Launcher - Beta.app"),
        );
        if let Some(home) = dirs::home_dir() {
            push_unique(
                &mut candidates,
                home.join("Applications/Tempo Launcher.app"),
            );
            push_unique(
                &mut candidates,
                home.join("Applications/Tempo Launcher - Beta.app"),
            );
        }
    }

    candidates
}

fn push_unique(paths: &mut Vec<PathBuf>, path: PathBuf) {
    if !paths.iter().any(|existing| existing == &path) {
        paths.push(path);
    }
}

/// Everything the installer puts at the game root, plus the macOS launch-mode
/// marker. Keep the large BazaarPlusPlusV4 data dir in place unless validation
/// proves Tempo rejects unknown directories.
fn removal_items() -> Vec<String> {
    let mut items: Vec<String> = crate::services::bepinex::payload_root_relative_paths()
        .into_iter()
        .map(str::to_string)
        .collect();
    items.push(crate::services::bepinex::MARKER_FILE.to_string());
    items
}

impl BackupSession {
    fn new() -> Result<Self, String> {
        let stamp = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .map_err(|err| format!("system clock error: {err}"))?
            .as_millis();
        let sequence = BACKUP_SEQUENCE.fetch_add(1, Ordering::SeqCst);
        let root = std::env::temp_dir()
            .join(BACKUP_PARENT_DIR)
            .join(format!("{}-{stamp}-{sequence}", std::process::id()));
        fs::create_dir_all(&root)
            .map_err(|err| format!("failed to create backup folder {}: {err}", root.display()))?;
        Ok(Self {
            root,
            entries: Vec::new(),
            restored: false,
        })
    }

    fn backup_and_remove(&mut self, game_dir: &Path, items: &[String]) -> Result<(), String> {
        for item in items {
            if cancel_requested() {
                return Err("tempo_launch_cancelled".to_string());
            }
            let rel = safe_relative_item(item)?;
            let original = game_dir.join(&rel);
            if !original.exists() {
                continue;
            }

            let backup = self.root.join(&rel);
            if let Some(parent) = backup.parent() {
                fs::create_dir_all(parent).map_err(|err| {
                    format!("failed to create backup parent {}: {err}", parent.display())
                })?;
            }
            copy_path(&original, &backup)?;
            self.entries.push(BackupEntry { original, backup });
            let entry = self
                .entries
                .last()
                .ok_or_else(|| "failed to track Tempo backup entry".to_string())?;
            self.write_manifest()?;
            remove_path(&entry.original)?;
        }
        Ok(())
    }

    fn write_manifest(&self) -> Result<(), String> {
        let manifest = BackupManifest {
            entries: self.entries.clone(),
        };
        let bytes = serde_json::to_vec_pretty(&manifest)
            .map_err(|err| format!("failed to encode Tempo backup manifest: {err}"))?;
        fs::write(self.root.join(BACKUP_MANIFEST_FILE), bytes).map_err(|err| {
            format!(
                "failed to write Tempo backup manifest {}: {err}",
                self.root.join(BACKUP_MANIFEST_FILE).display()
            )
        })
    }

    fn restore(&mut self) -> Result<(), String> {
        if self.restored {
            return Ok(());
        }

        let mut failures = Vec::new();
        for entry in self.entries.iter().rev() {
            if let Some(parent) = entry.original.parent() {
                if let Err(err) = fs::create_dir_all(parent) {
                    failures.push(format!("{}: {err}", parent.display()));
                    continue;
                }
            }
            if entry.original.exists() {
                if let Err(err) = remove_path(&entry.original) {
                    failures.push(err);
                    continue;
                }
            }
            if let Err(err) = copy_path(&entry.backup, &entry.original) {
                failures.push(err);
            }
        }

        if failures.is_empty() {
            self.restored = true;
            let _ = fs::remove_dir_all(&self.root);
            Ok(())
        } else {
            Err(format!(
                "tempo_restore_failed: failed to restore mod payload from {}: {}",
                self.root.display(),
                failures.join("; ")
            ))
        }
    }
}

fn recover_backup_root(root: &Path) -> Result<(), String> {
    let manifest_path = root.join(BACKUP_MANIFEST_FILE);
    if !manifest_path.exists() {
        return Ok(());
    }
    let bytes = fs::read(&manifest_path).map_err(|err| {
        format!(
            "failed to read Tempo backup manifest {}: {err}",
            manifest_path.display()
        )
    })?;
    let manifest: BackupManifest = serde_json::from_slice(&bytes).map_err(|err| {
        format!(
            "failed to parse Tempo backup manifest {}: {err}",
            manifest_path.display()
        )
    })?;
    let mut session = BackupSession {
        root: root.to_path_buf(),
        entries: manifest.entries,
        restored: false,
    };
    session.restore()
}

impl Drop for BackupSession {
    fn drop(&mut self) {
        if !self.restored && !self.entries.is_empty() {
            let _ = self.restore();
        }
        if self.restored || self.entries.is_empty() {
            let _ = fs::remove_dir_all(&self.root);
        }
    }
}

fn safe_relative_item(item: &str) -> Result<PathBuf, String> {
    let trimmed = item.trim();
    if trimmed.is_empty() {
        return Err("mod item cannot be empty".to_string());
    }
    let rel = PathBuf::from(trimmed);
    if rel.is_absolute() {
        return Err(format!("mod item must be relative: {trimmed}"));
    }
    if rel
        .components()
        .any(|component| matches!(component, Component::ParentDir))
    {
        return Err(format!("mod item cannot contain '..': {trimmed}"));
    }
    Ok(rel)
}

fn copy_path(from: &Path, to: &Path) -> Result<(), String> {
    let metadata = fs::symlink_metadata(from)
        .map_err(|err| format!("failed to stat {}: {err}", from.display()))?;
    if metadata.file_type().is_symlink() {
        return Err(format!("cannot back up symlink {}", from.display()));
    }
    if metadata.file_type().is_dir() {
        fs::create_dir_all(to)
            .map_err(|err| format!("failed to create directory {}: {err}", to.display()))?;
        for child in fs::read_dir(from)
            .map_err(|err| format!("failed to read directory {}: {err}", from.display()))?
        {
            let child = child.map_err(|err| format!("failed to read directory entry: {err}"))?;
            copy_path(&child.path(), &to.join(child.file_name()))?;
        }
    } else {
        if let Some(parent) = to.parent() {
            fs::create_dir_all(parent)
                .map_err(|err| format!("failed to create directory {}: {err}", parent.display()))?;
        }
        fs::copy(from, to).map_err(|err| {
            format!(
                "failed to copy {} to {}: {err}",
                from.display(),
                to.display()
            )
        })?;
    }
    Ok(())
}

fn remove_path(path: &Path) -> Result<(), String> {
    let metadata = match fs::symlink_metadata(path) {
        Ok(metadata) => metadata,
        Err(err) if err.kind() == std::io::ErrorKind::NotFound => return Ok(()),
        Err(err) => return Err(format!("failed to stat {}: {err}", path.display())),
    };

    if metadata.file_type().is_dir() && !metadata.file_type().is_symlink() {
        let report = remove_dir_with_retry(path);
        if report.is_empty() {
            Ok(())
        } else {
            Err(format!(
                "failed to remove directory {}: {}",
                path.display(),
                report
                    .failed
                    .iter()
                    .map(|path| path.display().to_string())
                    .collect::<Vec<_>>()
                    .join(", ")
            ))
        }
    } else {
        fs::remove_file(path)
            .map_err(|err| format!("failed to remove file {}: {err}", path.display()))
    }
}

fn start_launcher(target: &LauncherTarget) -> Result<(), String> {
    match target {
        LauncherTarget::Executable(path) => {
            Command::new(path)
                .stdin(Stdio::null())
                .stdout(Stdio::null())
                .stderr(Stdio::null())
                .spawn()
                .map_err(|err| {
                    format!("failed to start Tempo Launcher {}: {err}", path.display())
                })?;
        }
        #[cfg(target_os = "macos")]
        LauncherTarget::AppBundle(path) => {
            Command::new("open")
                .arg(path)
                .stdin(Stdio::null())
                .stdout(Stdio::null())
                .stderr(Stdio::null())
                .spawn()
                .map_err(|err| {
                    format!("failed to open Tempo Launcher {}: {err}", path.display())
                })?;
        }
    }
    Ok(())
}

fn wait_for_game_process(game_exe: &Path, timeout: Duration) -> Result<GameProcess, String> {
    let started_at = Instant::now();
    while started_at.elapsed() < timeout {
        if cancel_requested() {
            return Err("tempo_launch_cancelled".to_string());
        }
        let processes = list_game_processes(game_exe, ProcessMatchMode::ExactExecutable)?;
        if let Some(process) = processes.into_iter().next() {
            return Ok(process);
        }
        thread::sleep(POLL_INTERVAL);
    }
    Err("tempo_capture_timeout".to_string())
}

fn wait_for_process_exit(pid: u32, game_exe: &Path, timeout: Duration) -> Result<(), String> {
    let started_at = Instant::now();
    while started_at.elapsed() < timeout {
        if cancel_requested() {
            return Err("tempo_launch_cancelled".to_string());
        }
        if !list_game_processes(game_exe, ProcessMatchMode::ExactExecutable)?
            .iter()
            .any(|process| process.pid == pid)
        {
            return Ok(());
        }
        thread::sleep(POLL_INTERVAL);
    }
    force_terminate_process(pid)
}

#[cfg(target_os = "windows")]
fn list_game_processes(
    game_exe: &Path,
    mode: ProcessMatchMode,
) -> Result<Vec<GameProcess>, String> {
    let output = quiet_command("powershell")
        .args([
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-Command",
            "Get-CimInstance Win32_Process -Filter \"Name = 'TheBazaar.exe'\" | Select-Object ProcessId,CommandLine | ConvertTo-Json -Compress",
        ])
        .output()
        .map_err(|err| format!("failed to query game process via PowerShell: {err}"))?;

    if !output.status.success() {
        return Err(format!(
            "PowerShell process query failed: {}",
            String::from_utf8_lossy(&output.stderr)
        ));
    }

    let stdout = String::from_utf8_lossy(&output.stdout).trim().to_string();
    if stdout.is_empty() {
        return Ok(Vec::new());
    }

    let value: Value = serde_json::from_str(&stdout)
        .map_err(|err| format!("failed to parse PowerShell process JSON: {err}"))?;
    let records = match value {
        Value::Array(values) => values,
        Value::Object(_) => vec![value],
        Value::Null => Vec::new(),
        _ => Vec::new(),
    };

    let expected = normalize_for_compare(game_exe);
    let mut processes = Vec::new();
    for record in records {
        let pid = record.get("ProcessId").and_then(Value::as_u64).unwrap_or(0) as u32;
        let command_line = record
            .get("CommandLine")
            .and_then(Value::as_str)
            .unwrap_or("")
            .to_string();
        if pid == 0 {
            continue;
        }
        if command_line.trim().is_empty() {
            if mode == ProcessMatchMode::AnyGame {
                processes.push(GameProcess { pid, command_line });
            }
            continue;
        }
        if process_command_line_matches(&command_line, &expected, mode) {
            processes.push(GameProcess { pid, command_line });
        }
    }
    Ok(processes)
}

#[cfg(target_os = "macos")]
fn list_game_processes(
    game_exe: &Path,
    mode: ProcessMatchMode,
) -> Result<Vec<GameProcess>, String> {
    let output = Command::new("pgrep")
        .args(["-f", "TheBazaar"])
        .output()
        .map_err(|err| format!("failed to query game process via pgrep: {err}"))?;

    if !output.status.success() && output.stdout.is_empty() {
        return Ok(Vec::new());
    }

    let expected = game_exe.to_string_lossy().to_string();
    let mut processes = Vec::new();
    for line in String::from_utf8_lossy(&output.stdout).lines() {
        let Ok(pid) = line.trim().parse::<u32>() else {
            continue;
        };
        let ps = Command::new("ps")
            .args(["-p", &pid.to_string(), "-ww", "-o", "command="])
            .output()
            .map_err(|err| format!("failed to inspect process {pid}: {err}"))?;
        if !ps.status.success() {
            continue;
        }
        let command_line = String::from_utf8_lossy(&ps.stdout).trim().to_string();
        if process_command_line_matches(&command_line, &expected, mode) {
            processes.push(GameProcess { pid, command_line });
        }
    }
    Ok(processes)
}

#[cfg(not(any(target_os = "windows", target_os = "macos")))]
fn list_game_processes(
    _game_exe: &Path,
    _mode: ProcessMatchMode,
) -> Result<Vec<GameProcess>, String> {
    Ok(Vec::new())
}

#[cfg(target_os = "windows")]
fn normalize_for_compare(path: &Path) -> String {
    path.to_string_lossy()
        .replace('/', "\\")
        .trim_matches('"')
        .to_ascii_lowercase()
}

#[cfg(target_os = "windows")]
fn first_executable_token(command_line: &str) -> String {
    let trimmed = command_line.trim_start();
    if let Some(rest) = trimmed.strip_prefix('"') {
        if let Some(index) = rest.find('"') {
            return rest[..index].to_string();
        }
    }
    trimmed
        .split_whitespace()
        .next()
        .unwrap_or_default()
        .to_string()
}

#[cfg(target_os = "windows")]
fn process_command_line_matches(
    command_line: &str,
    expected: &str,
    mode: ProcessMatchMode,
) -> bool {
    let normalized = normalize_for_compare(Path::new(&first_executable_token(command_line)));
    match mode {
        ProcessMatchMode::AnyGame => {
            command_line.to_ascii_lowercase().contains("thebazaar.exe")
                || (!expected.is_empty() && normalized == expected)
        }
        ProcessMatchMode::ExactExecutable => !expected.is_empty() && normalized == expected,
    }
}

#[cfg(target_os = "macos")]
fn process_command_line_matches(
    command_line: &str,
    expected: &str,
    mode: ProcessMatchMode,
) -> bool {
    match mode {
        ProcessMatchMode::AnyGame => {
            command_line.contains("TheBazaar.app")
                || (!expected.is_empty() && command_line.contains(expected))
        }
        ProcessMatchMode::ExactExecutable => {
            !expected.is_empty() && command_line.contains(expected)
        }
    }
}

#[cfg(target_os = "windows")]
fn quiet_command(program: &str) -> Command {
    let mut command = Command::new(program);
    use std::os::windows::process::CommandExt;
    const CREATE_NO_WINDOW: u32 = 0x0800_0000;
    command.creation_flags(CREATE_NO_WINDOW);
    command
}

fn terminate_process(pid: u32) -> Result<(), String> {
    #[cfg(target_os = "windows")]
    {
        let _ = quiet_command("taskkill")
            .args(["/PID", &pid.to_string(), "/T"])
            .output();
        return Ok(());
    }

    #[cfg(target_os = "macos")]
    {
        let _ = Command::new("kill")
            .args(["-TERM", &pid.to_string()])
            .output();
        return Ok(());
    }

    #[cfg(not(any(target_os = "windows", target_os = "macos")))]
    {
        let _ = pid;
        Ok(())
    }
}

fn force_terminate_process(pid: u32) -> Result<(), String> {
    #[cfg(target_os = "windows")]
    {
        let output = quiet_command("taskkill")
            .args(["/PID", &pid.to_string(), "/T", "/F"])
            .output()
            .map_err(|err| format!("failed to force kill game process {pid}: {err}"))?;
        if !output.status.success() {
            let stderr = String::from_utf8_lossy(&output.stderr);
            let stdout = String::from_utf8_lossy(&output.stdout);
            if is_missing_process_error(&stderr) || is_missing_process_error(&stdout) {
                return Ok(());
            }
            return Err(format!(
                "failed to force kill game process {pid}: {}",
                stderr
            ));
        }
        return Ok(());
    }

    #[cfg(target_os = "macos")]
    {
        let output = Command::new("kill")
            .args(["-KILL", &pid.to_string()])
            .output()
            .map_err(|err| format!("failed to force kill game process {pid}: {err}"))?;
        if !output.status.success() {
            let stderr = String::from_utf8_lossy(&output.stderr);
            if is_missing_process_error(&stderr) {
                return Ok(());
            }
            return Err(format!(
                "failed to force kill game process {pid}: {}",
                stderr
            ));
        }
        return Ok(());
    }

    #[cfg(not(any(target_os = "windows", target_os = "macos")))]
    {
        let _ = pid;
        Ok(())
    }
}

fn is_missing_process_error(output: &str) -> bool {
    let output = output.to_ascii_lowercase();
    output.contains("not found")
        || output.contains("no such process")
        || output.contains("no running instance")
        || output.contains("not running")
}

#[cfg(target_os = "macos")]
fn launch_modded_game(
    game_dir: &Path,
    args: &[String],
    trampoline_applied: bool,
) -> Result<(), String> {
    let script = game_dir.join("run_bepinex.sh");
    let mut command = if !trampoline_applied && script.exists() {
        if is_executable_best_effort(&script) {
            Command::new(&script)
        } else {
            let mut command = Command::new("sh");
            command.arg(&script);
            command
        }
    } else {
        Command::new(game_executable_path(game_dir)?)
    };

    command
        .current_dir(game_dir)
        .args(args)
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .spawn()
        .map_err(|err| format!("failed to launch modded game: {err}"))?;
    Ok(())
}

#[cfg(not(target_os = "macos"))]
fn launch_modded_game(game_dir: &Path, args: &[String]) -> Result<(), String> {
    Command::new(game_executable_path(game_dir)?)
        .current_dir(game_dir)
        .args(args)
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .spawn()
        .map_err(|err| format!("failed to launch modded game: {err}"))?;
    Ok(())
}

#[cfg(target_os = "macos")]
fn is_executable_best_effort(path: &Path) -> bool {
    use std::os::unix::fs::PermissionsExt;
    fs::metadata(path)
        .map(|metadata| metadata.permissions().mode() & 0o111 != 0)
        .unwrap_or(false)
}

fn extract_args_after_executable(command_line: &str, executable_path: &Path) -> Vec<String> {
    let trimmed = command_line.trim_start();
    let executable = executable_path.to_string_lossy();

    if let Some(rest) = strip_quoted_executable(trimmed, &executable) {
        return split_command_line(rest);
    }

    if starts_with_path(trimmed, &executable) {
        let rest = &trimmed[executable.len()..];
        return split_command_line(rest);
    }

    if let Some(rest) = strip_executable_by_suffix(trimmed, executable_path) {
        return split_command_line(rest);
    }

    let mut parts = split_command_line(trimmed);
    if !parts.is_empty() {
        parts.remove(0);
    }
    parts
}

fn strip_quoted_executable<'a>(command_line: &'a str, executable: &str) -> Option<&'a str> {
    let rest = command_line.strip_prefix('"')?;
    let end = rest.find('"')?;
    let quoted = &rest[..end];
    if paths_equal_str(quoted, executable)
        || quoted.ends_with("TheBazaar.exe")
        || quoted.ends_with("/TheBazaar")
    {
        Some(&rest[end + 1..])
    } else {
        None
    }
}

/// Anchor on a suffix that uniquely terminates the executable path inside a
/// possibly unquoted command line. macOS `ps` joins argv with spaces, so the
/// full path spelling may differ while the app executable suffix stays stable.
fn strip_executable_by_suffix<'a>(command_line: &'a str, game_exe: &Path) -> Option<&'a str> {
    let anchor = executable_anchor(game_exe)?;
    let lowered = command_line.to_ascii_lowercase();
    let needle = anchor.to_ascii_lowercase();
    let start = lowered.find(&needle)?;
    let rest = &command_line[start + needle.len()..];
    match rest.chars().next() {
        None => Some(rest),
        Some('"') => Some(&rest[1..]),
        Some(ch) if ch.is_whitespace() => Some(rest),
        _ => None,
    }
}

fn executable_anchor(game_exe: &Path) -> Option<String> {
    let file_name = game_exe.file_name()?.to_string_lossy().into_owned();
    #[cfg(target_os = "macos")]
    {
        Some(format!(".app/Contents/MacOS/{file_name}"))
    }
    #[cfg(not(target_os = "macos"))]
    {
        Some(file_name)
    }
}

fn starts_with_path(command_line: &str, executable: &str) -> bool {
    let Some(prefix) = command_line.get(..executable.len()) else {
        return false;
    };
    paths_equal_str(prefix, executable)
}

#[cfg(target_os = "windows")]
fn paths_equal_str(left: &str, right: &str) -> bool {
    left.replace('/', "\\")
        .eq_ignore_ascii_case(&right.replace('/', "\\"))
}

#[cfg(not(target_os = "windows"))]
fn paths_equal_str(left: &str, right: &str) -> bool {
    left == right
}

fn split_command_line(input: &str) -> Vec<String> {
    let mut args = Vec::new();
    let mut current = String::new();
    let mut in_quotes = false;
    let mut chars = input.trim().chars().peekable();

    while let Some(ch) = chars.next() {
        match ch {
            '"' => in_quotes = !in_quotes,
            '\\' => {
                if matches!(chars.peek(), Some('"')) {
                    current.push(chars.next().unwrap_or('"'));
                } else {
                    current.push(ch);
                }
            }
            ch if ch.is_whitespace() && !in_quotes => {
                if !current.is_empty() {
                    args.push(std::mem::take(&mut current));
                }
            }
            _ => current.push(ch),
        }
    }

    if !current.is_empty() {
        args.push(current);
    }

    args
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_windows_command_line_after_quoted_exe() {
        let exe = Path::new(r"C:\Users\a b\TheBazaar.exe");
        let args = extract_args_after_executable(
            r#""C:\Users\a b\TheBazaar.exe" --token abc --user "x y""#,
            exe,
        );
        assert_eq!(args, vec!["--token", "abc", "--user", "x y"]);
    }

    #[cfg(target_os = "macos")]
    #[test]
    fn parses_macos_unquoted_spaced_path_exact_match() {
        let exe = Path::new(
            "/Users/a/Library/Application Support/Tempo Launcher - Beta/game/buildx64/TheBazaar.app/Contents/MacOS/TheBazaar",
        );
        let args = extract_args_after_executable(
            "/Users/a/Library/Application Support/Tempo Launcher - Beta/game/buildx64/TheBazaar.app/Contents/MacOS/TheBazaar --token abc --user xy",
            exe,
        );
        assert_eq!(args, vec!["--token", "abc", "--user", "xy"]);
    }

    #[cfg(target_os = "macos")]
    #[test]
    fn parses_macos_path_spelling_mismatch_via_suffix_anchor() {
        let exe = Path::new(
            "/Users/a/Library/Application Support/Tempo Launcher - Beta/game/buildx64/TheBazaar.app/Contents/MacOS/TheBazaar",
        );
        let args = extract_args_after_executable(
            "/private/tmp/some copy/TheBazaar.app/Contents/MacOS/TheBazaar --token abc",
            exe,
        );
        assert_eq!(args, vec!["--token", "abc"]);
    }

    #[test]
    fn rejects_parent_dir_mod_item() {
        assert!(safe_relative_item("../BepInEx").is_err());
    }

    #[test]
    fn backup_session_restores_removed_payload() {
        let game = tempfile::tempdir().unwrap();
        let payload = game.path().join("BepInEx/config");
        fs::create_dir_all(&payload).unwrap();
        fs::write(payload.join("BazaarPlusPlus.cfg"), b"config").unwrap();

        let mut backup = BackupSession::new().unwrap();
        backup
            .backup_and_remove(game.path(), &["BepInEx".to_string()])
            .unwrap();

        assert!(!game.path().join("BepInEx").exists());
        backup.restore().unwrap();
        assert_eq!(
            fs::read(game.path().join("BepInEx/config/BazaarPlusPlus.cfg")).unwrap(),
            b"config"
        );
        assert!(!backup.root.exists());
    }

    #[test]
    fn orphaned_backup_manifest_restores_payload() {
        let game = tempfile::tempdir().unwrap();
        let payload = game.path().join("BepInEx/config");
        fs::create_dir_all(&payload).unwrap();
        fs::write(payload.join("BazaarPlusPlus.cfg"), b"config").unwrap();

        let mut backup = BackupSession::new().unwrap();
        backup
            .backup_and_remove(game.path(), &["BepInEx".to_string()])
            .unwrap();
        let root = backup.root.clone();
        std::mem::forget(backup);

        recover_backup_root(&root).unwrap();

        assert_eq!(
            fs::read(game.path().join("BepInEx/config/BazaarPlusPlus.cfg")).unwrap(),
            b"config"
        );
        assert!(!root.exists());
    }

    #[cfg(target_os = "macos")]
    #[test]
    fn exact_process_match_rejects_other_bazaar_app_bundle() {
        let expected = "/Users/me/Games/TheBazaar.app/Contents/MacOS/TheBazaar";
        let other = "/Applications/TheBazaar.app/Contents/MacOS/TheBazaar --token abc";

        assert!(process_command_line_matches(
            other,
            expected,
            ProcessMatchMode::AnyGame
        ));
        assert!(!process_command_line_matches(
            other,
            expected,
            ProcessMatchMode::ExactExecutable
        ));
    }

    #[test]
    fn missing_process_errors_are_treated_as_already_exited() {
        assert!(is_missing_process_error("kill: 123: No such process"));
        assert!(is_missing_process_error(
            "ERROR: The process \"123\" not found."
        ));
    }
}

// src-tauri/src/commands/bepinex/payload.rs
use std::path::{Path, PathBuf};
use std::thread;
use std::time::Duration;

use super::LEGACY_RECORD_DIRECTORY;

pub(super) const BPP_CONFIG_RELATIVE_PATH: &str = "BepInEx/config/BazaarPlusPlus.cfg";

/// Backoff used between retries when a file/directory removal fails. The first
/// retry runs immediately, the second after a short pause, and the last after
/// a longer pause. Windows often releases ERROR_SHARING_VIOLATION holds inside
/// 200ms once the holding process closes the handle.
const REMOVE_RETRY_DELAYS_MS: &[u64] = &[0, 50, 200];

pub(super) struct PreservedFile {
    relative_path: &'static str,
    contents: Vec<u8>,
}

/// Failure report for a per-file removal pass. `failed` lists the paths the
/// walker could not delete after retrying. The list is empty on success and on
/// "nothing to do" (path didn't exist).
#[derive(Debug, Default)]
pub(super) struct RemovalReport {
    pub(super) failed: Vec<PathBuf>,
}

impl RemovalReport {
    pub(super) fn is_empty(&self) -> bool {
        self.failed.is_empty()
    }
}

fn remove_path_if_exists(path: &Path) -> Result<(), String> {
    if !path.exists() {
        return Ok(());
    }

    if path.is_dir() {
        std::fs::remove_dir_all(path)
            .map_err(|err| format!("Cannot remove {}: {err}", path.display()))
    } else {
        std::fs::remove_file(path).map_err(|err| format!("Cannot remove {}: {err}", path.display()))
    }
}

/// Try to delete a single file or empty directory, retrying briefly to absorb
/// transient sharing violations from antivirus / explorer / a slow OS handle
/// release. Returns `true` on success or when the path didn't exist; returns
/// `false` if every retry failed.
fn try_remove_with_retry(path: &Path, is_dir: bool) -> bool {
    for (attempt, delay_ms) in REMOVE_RETRY_DELAYS_MS.iter().enumerate() {
        if *delay_ms > 0 {
            thread::sleep(Duration::from_millis(*delay_ms));
        }

        let result = if is_dir {
            std::fs::remove_dir(path)
        } else {
            std::fs::remove_file(path)
        };

        match result {
            Ok(()) => return true,
            Err(err) if err.kind() == std::io::ErrorKind::NotFound => return true,
            Err(_) if attempt + 1 < REMOVE_RETRY_DELAYS_MS.len() => continue,
            Err(_) => return false,
        }
    }

    false
}

/// Bottom-up recursive delete that retries each entry independently and
/// collects every path it could not remove. Unlike `remove_dir_all`, this does
/// not abort on the first sharing violation, so a single locked sqlite handle
/// won't leave the rest of `BazaarPlusPlus/` half-deleted.
pub(super) fn remove_dir_with_retry(root: &Path) -> RemovalReport {
    let mut report = RemovalReport::default();

    if !root.exists() {
        return report;
    }

    let metadata = match std::fs::symlink_metadata(root) {
        Ok(metadata) => metadata,
        Err(err) if err.kind() == std::io::ErrorKind::NotFound => return report,
        Err(_) => {
            report.failed.push(root.to_path_buf());
            return report;
        }
    };

    if !metadata.file_type().is_dir() || metadata.file_type().is_symlink() {
        if !try_remove_with_retry(root, false) {
            report.failed.push(root.to_path_buf());
        }
        return report;
    }

    let entries = match std::fs::read_dir(root) {
        Ok(entries) => entries,
        Err(_) => {
            report.failed.push(root.to_path_buf());
            return report;
        }
    };

    for entry in entries {
        let Ok(entry) = entry else {
            // We don't know which path this was; mark the parent as failed so
            // callers know the directory isn't empty.
            report.failed.push(root.to_path_buf());
            continue;
        };

        let entry_path = entry.path();
        let entry_metadata = match std::fs::symlink_metadata(&entry_path) {
            Ok(metadata) => metadata,
            Err(err) if err.kind() == std::io::ErrorKind::NotFound => continue,
            Err(_) => {
                report.failed.push(entry_path);
                continue;
            }
        };

        if entry_metadata.file_type().is_dir() && !entry_metadata.file_type().is_symlink() {
            let mut nested = remove_dir_with_retry(&entry_path);
            report.failed.append(&mut nested.failed);
        } else if !try_remove_with_retry(&entry_path, false) {
            report.failed.push(entry_path);
        }
    }

    // Only attempt to drop the directory itself if every child is gone.
    if report.failed.is_empty() && !try_remove_with_retry(root, true) {
        report.failed.push(root.to_path_buf());
    }

    report
}

pub(super) fn uninstall_payload(game_path: &Path) -> Result<(), String> {
    remove_path_if_exists(&game_path.join("BepInEx"))?;

    #[cfg(target_os = "macos")]
    {
        remove_path_if_exists(&game_path.join("run_bepinex.sh"))?;
        remove_path_if_exists(&game_path.join("libdoorstop.dylib"))?;
    }

    #[cfg(target_os = "windows")]
    {
        remove_path_if_exists(&game_path.join("doorstop_config.ini"))?;
        remove_path_if_exists(&game_path.join("winhttp.dll"))?;
    }

    Ok(())
}

pub(super) fn ensure_valid_game_path(game_path: &Path) -> Result<(), String> {
    if crate::commands::detect::is_valid_game_path(game_path) {
        return Ok(());
    }

    Err(format!(
        "Selected path is not a valid The Bazaar installation: {}",
        game_path.display()
    ))
}

pub(super) fn prepare_install_target(game_path: &Path) -> Result<(), String> {
    ensure_valid_game_path(game_path)?;
    uninstall_payload(game_path)
}

pub(super) fn preserve_file_if_exists(
    base_dir: &Path,
    relative_path: &'static str,
) -> Result<Option<PreservedFile>, String> {
    let path = base_dir.join(relative_path);
    if !path.exists() {
        return Ok(None);
    }

    let contents =
        std::fs::read(&path).map_err(|err| format!("Cannot preserve {}: {err}", path.display()))?;

    Ok(Some(PreservedFile {
        relative_path,
        contents,
    }))
}

pub(super) fn restore_preserved_file(
    base_dir: &Path,
    preserved: &PreservedFile,
) -> Result<(), String> {
    let path = base_dir.join(preserved.relative_path);
    if let Some(parent) = path.parent() {
        std::fs::create_dir_all(parent)
            .map_err(|err| format!("Cannot recreate {}: {err}", parent.display()))?;
    }

    std::fs::write(&path, &preserved.contents)
        .map_err(|err| format!("Cannot restore {}: {err}", path.display()))
}

pub(super) fn cleanup_legacy_record_directory(game_path: &Path) -> RemovalReport {
    remove_dir_with_retry(&game_path.join(LEGACY_RECORD_DIRECTORY))
}

pub(super) fn legacy_record_directory_size_bytes(game_path: &Path) -> Result<u64, String> {
    fn collect_size(path: &Path) -> Result<u64, String> {
        if !path.exists() {
            return Ok(0);
        }

        let metadata = std::fs::symlink_metadata(path)
            .map_err(|err| format!("Cannot read metadata for {}: {err}", path.display()))?;
        let file_type = metadata.file_type();
        if file_type.is_symlink() {
            return Ok(0);
        }
        if metadata.is_file() {
            return Ok(metadata.len());
        }
        if !metadata.is_dir() {
            return Ok(0);
        }

        let mut total = 0;
        let entries = std::fs::read_dir(path)
            .map_err(|err| format!("Cannot read directory {}: {err}", path.display()))?;
        for entry in entries {
            let entry = entry.map_err(|err| err.to_string())?;
            total += collect_size(&entry.path())?;
        }

        Ok(total)
    }

    collect_size(&game_path.join(LEGACY_RECORD_DIRECTORY))
}

#[cfg(test)]
mod tests {
    use super::LEGACY_RECORD_DIRECTORY;
    use super::{
        cleanup_legacy_record_directory, ensure_valid_game_path,
        legacy_record_directory_size_bytes, prepare_install_target, preserve_file_if_exists,
        restore_preserved_file, uninstall_payload, PreservedFile, BPP_CONFIG_RELATIVE_PATH,
    };

    #[test]
    fn test_ensure_valid_game_path_rejects_non_game_directory() {
        let tmp = tempfile::tempdir().unwrap();

        let result = ensure_valid_game_path(tmp.path());

        assert!(result.is_err());
    }

    #[test]
    fn test_prepare_install_target_cleans_previous_payload() {
        let tmp = tempfile::tempdir().unwrap();

        #[cfg(target_os = "macos")]
        {
            std::fs::create_dir_all(tmp.path().join("TheBazaar.app")).unwrap();
            std::fs::create_dir_all(tmp.path().join("BepInEx/plugins")).unwrap();
            std::fs::write(tmp.path().join("run_bepinex.sh"), b"#!/bin/sh\n").unwrap();
            std::fs::write(tmp.path().join("libdoorstop.dylib"), b"dylib").unwrap();
        }

        #[cfg(target_os = "windows")]
        {
            std::fs::write(tmp.path().join("TheBazaar.exe"), b"exe").unwrap();
            std::fs::create_dir_all(tmp.path().join("BepInEx/plugins")).unwrap();
            std::fs::write(tmp.path().join("doorstop_config.ini"), b"cfg").unwrap();
            std::fs::write(tmp.path().join("winhttp.dll"), b"dll").unwrap();
        }

        std::fs::write(tmp.path().join("BepInEx/plugins/old.dll"), b"dll").unwrap();

        prepare_install_target(tmp.path()).unwrap();

        assert!(!tmp.path().join("BepInEx").exists());
        #[cfg(target_os = "macos")]
        {
            assert!(!tmp.path().join("run_bepinex.sh").exists());
            assert!(!tmp.path().join("libdoorstop.dylib").exists());
        }
        #[cfg(target_os = "windows")]
        {
            assert!(!tmp.path().join("doorstop_config.ini").exists());
            assert!(!tmp.path().join("winhttp.dll").exists());
        }
    }

    #[test]
    fn test_prepare_install_target_keeps_legacy_directory_for_installed_v1() {
        let tmp = tempfile::tempdir().unwrap();
        let plugins_dir = tmp.path().join("BepInEx/plugins");
        let legacy_dir = tmp.path().join("BazaarPlusPlus");

        #[cfg(target_os = "macos")]
        {
            std::fs::create_dir_all(tmp.path().join("TheBazaar.app")).unwrap();
            std::fs::write(tmp.path().join("run_bepinex.sh"), b"#!/bin/sh\n").unwrap();
            std::fs::write(tmp.path().join("libdoorstop.dylib"), b"dylib").unwrap();
        }

        #[cfg(target_os = "windows")]
        {
            std::fs::write(tmp.path().join("TheBazaar.exe"), b"exe").unwrap();
            std::fs::write(tmp.path().join("doorstop_config.ini"), b"cfg").unwrap();
            std::fs::write(tmp.path().join("winhttp.dll"), b"dll").unwrap();
        }

        std::fs::create_dir_all(&plugins_dir).unwrap();
        std::fs::create_dir_all(&legacy_dir).unwrap();
        std::fs::write(plugins_dir.join("BazaarPlusPlus.version"), b"1.9.0").unwrap();
        std::fs::write(legacy_dir.join("legacy.dll"), b"dll").unwrap();

        prepare_install_target(tmp.path()).unwrap();

        assert!(legacy_dir.exists());
    }

    #[test]
    fn test_uninstall_payload_removes_platform_files() {
        let tmp = tempfile::tempdir().unwrap();
        std::fs::create_dir_all(tmp.path().join("BepInEx/plugins")).unwrap();
        std::fs::write(
            tmp.path().join("BepInEx/plugins/BazaarPlusPlus.dll"),
            b"dll",
        )
        .unwrap();

        #[cfg(target_os = "macos")]
        {
            std::fs::write(tmp.path().join("run_bepinex.sh"), b"#!/bin/sh\n").unwrap();
            std::fs::write(tmp.path().join("libdoorstop.dylib"), b"dylib").unwrap();
        }

        #[cfg(target_os = "windows")]
        {
            std::fs::write(tmp.path().join("doorstop_config.ini"), b"cfg").unwrap();
            std::fs::write(tmp.path().join("winhttp.dll"), b"dll").unwrap();
        }

        uninstall_payload(tmp.path()).unwrap();

        assert!(!tmp.path().join("BepInEx").exists());
        #[cfg(target_os = "macos")]
        {
            assert!(!tmp.path().join("run_bepinex.sh").exists());
            assert!(!tmp.path().join("libdoorstop.dylib").exists());
        }
        #[cfg(target_os = "windows")]
        {
            assert!(!tmp.path().join("doorstop_config.ini").exists());
            assert!(!tmp.path().join("winhttp.dll").exists());
        }
    }

    #[test]
    fn test_cleanup_legacy_record_directory_removes_bazaarplusplus_directory() {
        let tmp = tempfile::tempdir().unwrap();
        let legacy_dir = tmp.path().join(LEGACY_RECORD_DIRECTORY);
        std::fs::create_dir_all(&legacy_dir).unwrap();
        std::fs::write(legacy_dir.join("legacy.dll"), b"dll").unwrap();

        let report = cleanup_legacy_record_directory(tmp.path());

        assert!(report.is_empty(), "unexpected failures: {:?}", report.failed);
        assert!(!legacy_dir.exists());
    }

    #[test]
    fn test_cleanup_legacy_record_directory_is_no_op_when_directory_missing() {
        let tmp = tempfile::tempdir().unwrap();

        let report = cleanup_legacy_record_directory(tmp.path());

        assert!(report.is_empty());
    }

    #[test]
    fn test_cleanup_legacy_record_directory_walks_nested_subdirectories() {
        let tmp = tempfile::tempdir().unwrap();
        let legacy_dir = tmp.path().join(LEGACY_RECORD_DIRECTORY);
        let nested = legacy_dir.join("Identity").join("inner");
        std::fs::create_dir_all(&nested).unwrap();
        std::fs::write(legacy_dir.join("bazaarplusplus.db"), b"db").unwrap();
        std::fs::write(nested.join("auth.json"), b"auth").unwrap();

        let report = cleanup_legacy_record_directory(tmp.path());

        assert!(report.is_empty(), "unexpected failures: {:?}", report.failed);
        assert!(!legacy_dir.exists());
    }

    #[cfg(unix)]
    #[test]
    fn test_cleanup_legacy_record_directory_reports_locked_files_without_aborting_siblings() {
        use std::os::unix::fs::PermissionsExt;

        let tmp = tempfile::tempdir().unwrap();
        let legacy_dir = tmp.path().join(LEGACY_RECORD_DIRECTORY);
        let unwritable_subdir = legacy_dir.join("locked");
        std::fs::create_dir_all(&unwritable_subdir).unwrap();
        std::fs::write(legacy_dir.join("removable.bin"), b"x").unwrap();
        std::fs::write(unwritable_subdir.join("trapped.bin"), b"x").unwrap();

        // Drop write permission on the parent dir so its child can't be unlinked.
        std::fs::set_permissions(
            &unwritable_subdir,
            std::fs::Permissions::from_mode(0o500),
        )
        .unwrap();

        let report = cleanup_legacy_record_directory(tmp.path());

        // Restore permissions so the tempdir cleanup succeeds even if the test fails.
        let _ = std::fs::set_permissions(
            &unwritable_subdir,
            std::fs::Permissions::from_mode(0o700),
        );

        assert!(!report.is_empty(), "expected at least one failed path");
        assert!(!legacy_dir.join("removable.bin").exists());
    }

    #[test]
    fn test_legacy_record_directory_size_bytes_sums_nested_files() {
        let tmp = tempfile::tempdir().unwrap();
        let legacy_dir = tmp.path().join(LEGACY_RECORD_DIRECTORY);
        std::fs::create_dir_all(legacy_dir.join("nested")).unwrap();
        std::fs::write(legacy_dir.join("a.bin"), [0_u8; 3]).unwrap();
        std::fs::write(legacy_dir.join("nested").join("b.bin"), [0_u8; 5]).unwrap();

        let total = legacy_record_directory_size_bytes(tmp.path()).unwrap();

        assert_eq!(total, 8);
    }

    #[test]
    fn test_legacy_record_directory_size_bytes_returns_zero_when_missing() {
        let tmp = tempfile::tempdir().unwrap();

        let total = legacy_record_directory_size_bytes(tmp.path()).unwrap();

        assert_eq!(total, 0);
    }

    #[cfg(unix)]
    #[test]
    fn test_legacy_record_directory_size_bytes_ignores_symlink_targets() {
        let tmp = tempfile::tempdir().unwrap();
        let legacy_dir = tmp.path().join(LEGACY_RECORD_DIRECTORY);
        std::fs::create_dir_all(&legacy_dir).unwrap();
        std::fs::write(legacy_dir.join("a.bin"), [0_u8; 3]).unwrap();
        std::fs::write(tmp.path().join("outside.bin"), [0_u8; 100]).unwrap();
        std::os::unix::fs::symlink(
            tmp.path().join("outside.bin"),
            legacy_dir.join("outside-link.bin"),
        )
        .unwrap();

        let total = legacy_record_directory_size_bytes(tmp.path()).unwrap();

        assert_eq!(total, 3);
    }

    #[test]
    fn test_preserve_file_if_exists_reads_existing_file() {
        let tmp = tempfile::tempdir().unwrap();
        let config_path = tmp.path().join(BPP_CONFIG_RELATIVE_PATH);
        std::fs::create_dir_all(config_path.parent().unwrap()).unwrap();
        std::fs::write(&config_path, b"user-config").unwrap();

        let preserved = preserve_file_if_exists(tmp.path(), BPP_CONFIG_RELATIVE_PATH)
            .unwrap()
            .expect("expected preserved config");

        assert_eq!(preserved.relative_path, BPP_CONFIG_RELATIVE_PATH);
        assert_eq!(preserved.contents, b"user-config");
    }

    #[test]
    fn test_restore_preserved_file_recreates_parent_directory() {
        let tmp = tempfile::tempdir().unwrap();
        let preserved = PreservedFile {
            relative_path: BPP_CONFIG_RELATIVE_PATH,
            contents: b"user-config".to_vec(),
        };

        restore_preserved_file(tmp.path(), &preserved).unwrap();

        assert_eq!(
            std::fs::read(tmp.path().join(BPP_CONFIG_RELATIVE_PATH)).unwrap(),
            b"user-config"
        );
    }
}

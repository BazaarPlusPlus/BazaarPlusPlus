// src-tauri/src/services/bepinex/payload.rs
use std::path::{Path, PathBuf};
use std::thread;
use std::time::Duration;

use crate::config::BAZAAR_DATA_DIRECTORY;

pub(super) const BPP_CONFIG_RELATIVE_PATH: &str = "BepInEx/config/BazaarPlusPlus.cfg";

const BPP_PRIVATE_RELATIVE_PATHS: &[&str] = &[
    BPP_CONFIG_RELATIVE_PATH,
    "BepInEx/plugins/BazaarPlusPlus.dll",
    "BepInEx/plugins/BazaarPlusPlus.version",
    "BepInEx/plugins/BazaarPlusPlus.ModApi.dll",
    "BepInEx/plugins/BazaarPlusPlus.Storage.dll",
    "BepInEx/plugins/BazaarPlusPlus.Localization.dll",
    "BepInEx/plugins/libBppMacAudio.dylib",
];

const BPP_BUNDLED_DEPENDENCY_RELATIVE_PATHS: &[&str] = &[
    "BepInEx/plugins/Microsoft.Data.Sqlite.dll",
    "BepInEx/plugins/SQLitePCLRaw.batteries_v2.dll",
    "BepInEx/plugins/SQLitePCLRaw.core.dll",
    "BepInEx/plugins/SQLitePCLRaw.provider.e_sqlite3.dll",
    "BepInEx/plugins/SixLabors.ImageSharp.dll",
    "BepInEx/plugins/System.Buffers.dll",
    "BepInEx/plugins/System.Memory.dll",
    "BepInEx/plugins/System.Numerics.Vectors.dll",
    "BepInEx/plugins/System.Text.Encoding.CodePages.dll",
    "BepInEx/plugins/e_sqlite3.dll",
    "BepInEx/plugins/libe_sqlite3.dylib",
    "BepInEx/plugins/ffmpeg",
    "BepInEx/plugins/ffmpeg.exe",
    "BepInEx/plugins/ffmpeg-LICENSE.txt",
];

/// Backoff used between retries when a file/directory removal fails. The first
/// retry runs immediately, the second after a short pause, and the last after
/// a longer pause. Windows often releases ERROR_SHARING_VIOLATION holds inside
/// 200ms once the holding process closes the handle.
const REMOVE_RETRY_DELAYS_MS: &[u64] = &[0, 50, 200];

pub(super) struct PreservedFile {
    relative_path: &'static str,
    contents: Vec<u8>,
}

pub(super) struct InstallTargetBackup {
    root: tempfile::TempDir,
}

impl InstallTargetBackup {
    fn capture(game_path: &Path) -> Result<Self, String> {
        let root = tempfile::Builder::new()
            .prefix("bpp-install-backup-")
            .tempdir()
            .map_err(|err| format!("Cannot create install rollback snapshot: {err}"))?;

        for relative_path in payload_root_relative_paths() {
            let source = game_path.join(relative_path);
            if source.exists() {
                copy_path(&source, &root.path().join(relative_path))?;
            }
        }

        Ok(Self { root })
    }

    pub(super) fn restore(self, game_path: &Path) -> Result<(), String> {
        uninstall_payload(game_path)?;

        for relative_path in payload_root_relative_paths() {
            let backup_path = self.root.path().join(relative_path);
            if backup_path.exists() {
                copy_path(&backup_path, &game_path.join(relative_path))?;
            }
        }

        Ok(())
    }
}

/// Failure report for a per-file removal pass. `failed` lists the paths the
/// walker could not delete after retrying. The list is empty on success and on
/// "nothing to do" (path didn't exist).
#[derive(Debug, Default)]
pub(crate) struct RemovalReport {
    pub(crate) failed: Vec<PathBuf>,
}

impl RemovalReport {
    pub(crate) fn is_empty(&self) -> bool {
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

pub(crate) fn payload_root_relative_paths() -> Vec<&'static str> {
    let mut paths = vec!["BepInEx"];

    #[cfg(target_os = "macos")]
    {
        paths.push("run_bepinex.sh");
        paths.push("libdoorstop.dylib");
    }

    #[cfg(target_os = "windows")]
    {
        paths.push("doorstop_config.ini");
        paths.push("winhttp.dll");
    }

    paths
}

fn copy_path(source: &Path, destination: &Path) -> Result<(), String> {
    let metadata = std::fs::symlink_metadata(source)
        .map_err(|err| format!("Cannot inspect {}: {err}", source.display()))?;

    if metadata.file_type().is_dir() && !metadata.file_type().is_symlink() {
        std::fs::create_dir_all(destination)
            .map_err(|err| format!("Cannot create {}: {err}", destination.display()))?;
        for entry in std::fs::read_dir(source)
            .map_err(|err| format!("Cannot read {}: {err}", source.display()))?
        {
            let entry = entry.map_err(|err| format!("Cannot read {}: {err}", source.display()))?;
            copy_path(&entry.path(), &destination.join(entry.file_name()))?;
        }
        std::fs::set_permissions(destination, metadata.permissions())
            .map_err(|err| format!("Cannot set permissions on {}: {err}", destination.display()))?;
        return Ok(());
    }

    if let Some(parent) = destination.parent() {
        std::fs::create_dir_all(parent)
            .map_err(|err| format!("Cannot create {}: {err}", parent.display()))?;
    }
    std::fs::copy(source, destination).map_err(|err| {
        format!(
            "Cannot copy {} to {}: {err}",
            source.display(),
            destination.display()
        )
    })?;
    std::fs::set_permissions(destination, metadata.permissions())
        .map_err(|err| format!("Cannot set permissions on {}: {err}", destination.display()))
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
/// won't leave the rest of `BazaarPlusPlusV4/` half-deleted.
pub(crate) fn remove_dir_with_retry(root: &Path) -> RemovalReport {
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
    remove_bpp_files(game_path, true)
}

pub(super) fn uninstall_payload_preserving_shared_dependencies(
    game_path: &Path,
) -> Result<(), String> {
    remove_bpp_files(game_path, false)
}

fn remove_bpp_files(game_path: &Path, remove_bundled_dependencies: bool) -> Result<(), String> {
    for relative_path in BPP_PRIVATE_RELATIVE_PATHS {
        remove_path_if_exists(&game_path.join(relative_path))?;
    }

    if remove_bundled_dependencies {
        for relative_path in BPP_BUNDLED_DEPENDENCY_RELATIVE_PATHS {
            remove_path_if_exists(&game_path.join(relative_path))?;
        }
    }

    remove_empty_dir_if_exists(&game_path.join("BepInEx/config"))?;
    remove_empty_dir_if_exists(&game_path.join("BepInEx/plugins"))?;
    remove_empty_dir_if_exists(&game_path.join("BepInEx"))?;

    Ok(())
}

fn remove_empty_dir_if_exists(path: &Path) -> Result<(), String> {
    match std::fs::remove_dir(path) {
        Ok(()) => Ok(()),
        Err(err) if err.kind() == std::io::ErrorKind::NotFound => Ok(()),
        Err(err) if err.kind() == std::io::ErrorKind::DirectoryNotEmpty => Ok(()),
        Err(err) => Err(format!(
            "Cannot remove empty directory {}: {err}",
            path.display()
        )),
    }
}

pub(super) fn has_third_party_plugins(game_path: &Path) -> bool {
    let plugins_dir = game_path.join("BepInEx/plugins");
    let Ok(entries) = std::fs::read_dir(&plugins_dir) else {
        return false;
    };

    entries.filter_map(Result::ok).any(|entry| {
        let Ok(relative) = entry.path().strip_prefix(game_path).map(Path::to_path_buf) else {
            return false;
        };
        let relative = relative.to_string_lossy().replace('\\', "/");
        relative != "BepInEx/plugins/.gitkeep"
            && !BPP_PRIVATE_RELATIVE_PATHS
                .iter()
                .any(|owned| owned.eq_ignore_ascii_case(&relative))
            && !BPP_BUNDLED_DEPENDENCY_RELATIVE_PATHS
                .iter()
                .any(|owned| owned.eq_ignore_ascii_case(&relative))
    })
}

pub(super) fn ensure_valid_game_path(game_path: &Path) -> Result<(), String> {
    if crate::services::detect::is_valid_game_path(game_path) {
        return Ok(());
    }

    Err(format!(
        "Selected path is not a valid The Bazaar installation: {}",
        game_path.display()
    ))
}

pub(super) fn prepare_install_target(game_path: &Path) -> Result<InstallTargetBackup, String> {
    ensure_valid_game_path(game_path)?;
    let backup = InstallTargetBackup::capture(game_path)?;
    if let Err(uninstall_err) = uninstall_payload(game_path) {
        return match backup.restore(game_path) {
            Ok(()) => Err(uninstall_err),
            Err(restore_err) => Err(format!(
                "{uninstall_err}; additionally failed to restore previous payload: {restore_err}"
            )),
        };
    }

    Ok(backup)
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

pub(super) fn cleanup_bpp_data_directory(game_path: &Path) -> RemovalReport {
    remove_dir_with_retry(&game_path.join(BAZAAR_DATA_DIRECTORY))
}

#[cfg(test)]
mod tests {
    use crate::config::BAZAAR_DATA_DIRECTORY;

    use super::{
        cleanup_bpp_data_directory, ensure_valid_game_path, prepare_install_target,
        preserve_file_if_exists, restore_preserved_file, uninstall_payload, PreservedFile,
        BPP_CONFIG_RELATIVE_PATH,
    };

    #[test]
    fn test_ensure_valid_game_path_rejects_non_game_directory() {
        let tmp = tempfile::tempdir().unwrap();

        let result = ensure_valid_game_path(tmp.path());

        assert!(result.is_err());
    }

    #[test]
    fn test_prepare_install_target_cleans_previous_bpp_files_only() {
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
        std::fs::write(
            tmp.path().join("BepInEx/plugins/BazaarPlusPlus.dll"),
            b"bpp",
        )
        .unwrap();

        prepare_install_target(tmp.path()).unwrap();

        assert!(tmp.path().join("BepInEx/plugins/old.dll").exists());
        assert!(!tmp
            .path()
            .join("BepInEx/plugins/BazaarPlusPlus.dll")
            .exists());
        #[cfg(target_os = "macos")]
        {
            assert!(tmp.path().join("run_bepinex.sh").exists());
            assert!(tmp.path().join("libdoorstop.dylib").exists());
        }
        #[cfg(target_os = "windows")]
        {
            assert!(tmp.path().join("doorstop_config.ini").exists());
            assert!(tmp.path().join("winhttp.dll").exists());
        }
    }

    #[test]
    fn test_install_target_backup_restores_previous_payload_after_partial_install() {
        let tmp = tempfile::tempdir().unwrap();

        #[cfg(target_os = "macos")]
        {
            std::fs::create_dir_all(tmp.path().join("TheBazaar.app")).unwrap();
            std::fs::write(tmp.path().join("run_bepinex.sh"), b"old script").unwrap();
            std::fs::write(tmp.path().join("libdoorstop.dylib"), b"old dylib").unwrap();
        }

        #[cfg(target_os = "windows")]
        {
            std::fs::write(tmp.path().join("TheBazaar.exe"), b"exe").unwrap();
            std::fs::write(tmp.path().join("doorstop_config.ini"), b"old cfg").unwrap();
            std::fs::write(tmp.path().join("winhttp.dll"), b"old dll").unwrap();
        }

        std::fs::create_dir_all(tmp.path().join("BepInEx/plugins")).unwrap();
        std::fs::write(tmp.path().join("BepInEx/plugins/old.dll"), b"old").unwrap();

        let backup = prepare_install_target(tmp.path()).unwrap();
        std::fs::create_dir_all(tmp.path().join("BepInEx/plugins")).unwrap();
        std::fs::write(tmp.path().join("BepInEx/plugins/new.dll"), b"new").unwrap();
        #[cfg(target_os = "macos")]
        std::fs::write(tmp.path().join("run_bepinex.sh"), b"new script").unwrap();
        #[cfg(target_os = "windows")]
        std::fs::write(tmp.path().join("winhttp.dll"), b"new dll").unwrap();

        backup.restore(tmp.path()).unwrap();

        assert!(tmp.path().join("BepInEx/plugins/old.dll").exists());
        assert!(tmp.path().join("BepInEx/plugins/new.dll").exists());
        #[cfg(target_os = "macos")]
        assert_eq!(
            std::fs::read(tmp.path().join("run_bepinex.sh")).unwrap(),
            b"old script"
        );
        #[cfg(target_os = "windows")]
        assert_eq!(
            std::fs::read(tmp.path().join("winhttp.dll")).unwrap(),
            b"old dll"
        );
    }

    #[test]
    fn test_prepare_install_target_preserves_bpp_data_directory() {
        let tmp = tempfile::tempdir().unwrap();
        let plugins_dir = tmp.path().join("BepInEx/plugins");
        let data_dir = tmp.path().join(BAZAAR_DATA_DIRECTORY);

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
        std::fs::create_dir_all(&data_dir).unwrap();
        std::fs::write(plugins_dir.join("BazaarPlusPlus.version"), b"4.0.0").unwrap();
        std::fs::write(data_dir.join("stale.dll"), b"dll").unwrap();

        prepare_install_target(tmp.path()).unwrap();

        assert!(data_dir.exists());
        assert!(data_dir.join("stale.dll").exists());
    }

    #[test]
    fn test_uninstall_payload_removes_bpp_files_only() {
        let tmp = tempfile::tempdir().unwrap();
        std::fs::create_dir_all(tmp.path().join("BepInEx/plugins")).unwrap();
        std::fs::write(
            tmp.path().join("BepInEx/plugins/BazaarPlusPlus.dll"),
            b"dll",
        )
        .unwrap();
        std::fs::write(tmp.path().join("BepInEx/plugins/OtherMod.dll"), b"dll").unwrap();

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

        assert!(!tmp
            .path()
            .join("BepInEx/plugins/BazaarPlusPlus.dll")
            .exists());
        assert!(tmp.path().join("BepInEx/plugins/OtherMod.dll").exists());
        #[cfg(target_os = "macos")]
        {
            assert!(tmp.path().join("run_bepinex.sh").exists());
            assert!(tmp.path().join("libdoorstop.dylib").exists());
        }
        #[cfg(target_os = "windows")]
        {
            assert!(tmp.path().join("doorstop_config.ini").exists());
            assert!(tmp.path().join("winhttp.dll").exists());
        }
    }

    #[test]
    fn test_has_third_party_plugins_ignores_bpp_owned_files() {
        let tmp = tempfile::tempdir().unwrap();
        let plugins_dir = tmp.path().join("BepInEx/plugins");
        std::fs::create_dir_all(&plugins_dir).unwrap();
        std::fs::write(plugins_dir.join("BazaarPlusPlus.dll"), b"dll").unwrap();
        std::fs::write(plugins_dir.join("Microsoft.Data.Sqlite.dll"), b"dll").unwrap();

        assert!(!super::has_third_party_plugins(tmp.path()));

        std::fs::write(plugins_dir.join("OtherMod.dll"), b"dll").unwrap();

        assert!(super::has_third_party_plugins(tmp.path()));
    }

    #[test]
    fn test_cleanup_bpp_data_directory_removes_bazaarplusplus_directory() {
        let tmp = tempfile::tempdir().unwrap();
        let data_dir = tmp.path().join(BAZAAR_DATA_DIRECTORY);
        std::fs::create_dir_all(&data_dir).unwrap();
        std::fs::write(data_dir.join("stale.dll"), b"dll").unwrap();

        let report = cleanup_bpp_data_directory(tmp.path());

        assert!(
            report.is_empty(),
            "unexpected failures: {:?}",
            report.failed
        );
        assert!(!data_dir.exists());
    }

    #[test]
    fn test_cleanup_bpp_data_directory_is_no_op_when_directory_missing() {
        let tmp = tempfile::tempdir().unwrap();

        let report = cleanup_bpp_data_directory(tmp.path());

        assert!(report.is_empty());
    }

    #[test]
    fn test_cleanup_bpp_data_directory_walks_nested_subdirectories() {
        let tmp = tempfile::tempdir().unwrap();
        let data_dir = tmp.path().join(BAZAAR_DATA_DIRECTORY);
        let nested = data_dir.join("Identity").join("inner");
        std::fs::create_dir_all(&nested).unwrap();
        std::fs::write(data_dir.join("bazaarplusplus.db"), b"db").unwrap();
        std::fs::write(nested.join("auth.json"), b"auth").unwrap();

        let report = cleanup_bpp_data_directory(tmp.path());

        assert!(
            report.is_empty(),
            "unexpected failures: {:?}",
            report.failed
        );
        assert!(!data_dir.exists());
    }

    #[cfg(unix)]
    #[test]
    fn test_cleanup_bpp_data_directory_reports_locked_files_without_aborting_siblings() {
        use std::os::unix::fs::PermissionsExt;

        let tmp = tempfile::tempdir().unwrap();
        let data_dir = tmp.path().join(BAZAAR_DATA_DIRECTORY);
        let unwritable_subdir = data_dir.join("locked");
        std::fs::create_dir_all(&unwritable_subdir).unwrap();
        std::fs::write(data_dir.join("removable.bin"), b"x").unwrap();
        std::fs::write(unwritable_subdir.join("trapped.bin"), b"x").unwrap();

        // Drop write permission on the parent dir so its child can't be unlinked.
        std::fs::set_permissions(&unwritable_subdir, std::fs::Permissions::from_mode(0o500))
            .unwrap();

        let report = cleanup_bpp_data_directory(tmp.path());

        // Restore permissions so the tempdir cleanup succeeds even if the test fails.
        let _ =
            std::fs::set_permissions(&unwritable_subdir, std::fs::Permissions::from_mode(0o700));

        assert!(!report.is_empty(), "expected at least one failed path");
        assert!(!data_dir.join("removable.bin").exists());
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

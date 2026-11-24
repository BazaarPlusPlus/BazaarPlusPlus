use anyhow::{anyhow, Context as _};
use std::path::{Path, PathBuf};
use std::thread;
use std::time::Duration;

pub(super) const BPP_CONFIG_RELATIVE_PATH: &str = "BepInEx/config/BazaarPlusPlus.cfg";

#[derive(serde::Deserialize)]
struct PayloadInventory {
    #[serde(rename = "schemaVersion")]
    schema_version: u32,
    files: Vec<PayloadEntry>,
}

#[derive(serde::Deserialize)]
struct PayloadEntry {
    path: String,
    host: String,
    ownership: String,
    producer: String,
    platforms: Option<Vec<String>>,
    #[cfg(target_os = "macos")]
    #[serde(rename = "requiredEntries", default)]
    required_entries: Vec<String>,
}

fn inventory() -> &'static PayloadInventory {
    static INVENTORY: std::sync::OnceLock<PayloadInventory> = std::sync::OnceLock::new();
    INVENTORY.get_or_init(|| {
        let mut inventory: PayloadInventory = serde_json::from_str(include_str!(concat!(
            env!("CARGO_MANIFEST_DIR"),
            "/../../release/payload.json"
        )))
        .expect("the compiled Payload Inventory must be valid JSON");
        assert_eq!(inventory.schema_version, 1, "unsupported Payload Inventory");
        assert!(
            inventory
                .files
                .iter()
                .all(|entry| matches!(entry.host.as_str(), "bepinex" | "melonloader")),
            "unknown Payload Inventory host"
        );
        inventory.files.retain(|entry| entry.host == "bepinex");
        for entry in &inventory.files {
            let known_root = entry.path.starts_with("BepInEx/")
                || entry
                    .path
                    .starts_with("TheBazaar.app/Contents/Plugins/GfxPluginBpp")
                || entry
                    .path
                    .starts_with("TheBazaar_Data/Plugins/x86_64/GfxPluginBpp")
                || matches!(
                    entry.path.as_str(),
                    "winhttp.dll" | "doorstop_config.ini" | "libdoorstop.dylib"
                );
            assert!(
                known_root
                    && !entry.path.contains(['\\', ':', '\0'])
                    && entry
                        .path
                        .split('/')
                        .all(|part| !part.is_empty() && part != "." && part != ".."),
                "unsafe compiled Payload Inventory path"
            );
            assert!(matches!(
                entry.ownership.as_str(),
                "private" | "dependency" | "bootstrap"
            ));
        }
        inventory
    })
}

fn owned_paths(ownership: &str) -> impl Iterator<Item = &'static str> + '_ {
    inventory()
        .files
        .iter()
        .filter(move |entry| entry.ownership == ownership)
        .map(|entry| entry.path.as_str())
}

/// Archive and inventory paths use the Steam bundle name on macOS.
pub(super) fn installed_path(game: &Path, relative: &Path) -> PathBuf {
    #[cfg(target_os = "macos")]
    if let Ok(rest) = relative.strip_prefix("TheBazaar.app") {
        let bundle = crate::services::tempo::game_bundle(game);
        if bundle.file_name() != Some("TheBazaar.app".as_ref()) {
            return match rest.strip_prefix("Contents/Plugins") {
                Ok(plugin) => bundle.join("Contents/PlugIns").join(plugin),
                Err(_) => bundle.join(rest),
            };
        }
    }
    game.join(relative)
}

#[cfg(any(target_os = "windows", target_os = "macos"))]
pub(crate) fn native_payload_present(game: &Path) -> bool {
    inventory()
        .files
        .iter()
        .filter(|entry| {
            entry.producer == "native"
                && entry
                    .platforms
                    .as_ref()
                    .is_none_or(|platforms| platforms.iter().any(|p| p == std::env::consts::OS))
        })
        .all(|entry| {
            let path = installed_path(game, Path::new(&entry.path));
            #[cfg(target_os = "macos")]
            if !entry.required_entries.is_empty() {
                return entry
                    .required_entries
                    .iter()
                    .all(|file| path.join(file).is_file());
            }
            path.is_file()
        })
}

pub(super) fn installed_managed_manifest_path() -> &'static str {
    let mut entries = inventory()
        .files
        .iter()
        .filter(|entry| entry.producer == "managed-manifest");
    match (entries.next(), entries.next()) {
        (Some(entry), None) => entry.path.as_str(),
        _ => panic!("the Payload Inventory must name exactly one BepInEx managed-manifest entry"),
    }
}

/// A Windows sharing violation can persist briefly while the holding process exits.
const REMOVE_RETRY_DELAYS_MS: &[u64] = &[0, 50, 200];

pub(super) struct PreservedFile {
    relative_path: &'static str,
    contents: Vec<u8>,
}

pub(super) struct InstallTargetBackup {
    root: tempfile::TempDir,
    new_bootstrap: Vec<PathBuf>,
}

impl InstallTargetBackup {
    fn capture(
        game_path: &Path,
        incoming: &std::collections::HashSet<String>,
    ) -> anyhow::Result<Self> {
        let root = tempfile::Builder::new()
            .prefix("bpp-install-backup-")
            .tempdir()
            .context("Cannot create install rollback snapshot")?;

        for relative_path in payload_root_relative_paths() {
            let source = installed_path(game_path, Path::new(relative_path));
            if source.exists() {
                copy_path(&source, &root.path().join(relative_path))?;
            }
        }

        let mut new_bootstrap = Vec::new();
        for relative in owned_paths("bootstrap") {
            if incoming
                .iter()
                .any(|file| Path::new(file).starts_with(relative))
            {
                let path = installed_path(game_path, Path::new(relative));
                match std::fs::symlink_metadata(&path) {
                    Err(e) if e.kind() == std::io::ErrorKind::NotFound => new_bootstrap.push(path),
                    Err(e) => {
                        return Err(e).with_context(|| format!("Cannot inspect {}", path.display()))
                    }
                    Ok(_) => {}
                }
            }
        }
        Ok(Self {
            root,
            new_bootstrap,
        })
    }

    pub(super) fn restore(self, game_path: &Path) -> anyhow::Result<()> {
        for path in &self.new_bootstrap {
            remove_path_if_exists(path)?;
        }
        uninstall_payload(game_path)?;

        for relative_path in payload_root_relative_paths() {
            let backup_path = self.root.path().join(relative_path);
            if backup_path.exists() {
                copy_path(
                    &backup_path,
                    &installed_path(game_path, Path::new(relative_path)),
                )?;
            }
        }

        Ok(())
    }
}

#[derive(Debug, Default)]
pub(crate) struct RemovalReport {
    pub(crate) failed: Vec<PathBuf>,
}

impl RemovalReport {
    pub(crate) fn is_empty(&self) -> bool {
        self.failed.is_empty()
    }
}

fn remove_path_if_exists(path: &Path) -> anyhow::Result<()> {
    if !path.exists() {
        return Ok(());
    }

    if path.is_dir() {
        std::fs::remove_dir_all(path).with_context(|| format!("Cannot remove {}", path.display()))
    } else {
        std::fs::remove_file(path).with_context(|| format!("Cannot remove {}", path.display()))
    }
}

pub(crate) fn payload_root_relative_paths() -> Vec<&'static str> {
    let mut paths = vec!["BepInEx"];
    let platform = std::env::consts::OS;
    paths.extend(
        inventory()
            .files
            .iter()
            .filter(|entry| {
                !entry.path.starts_with("BepInEx/")
                    && entry
                        .platforms
                        .as_ref()
                        .is_none_or(|platforms| platforms.iter().any(|name| name == platform))
            })
            .map(|entry| entry.path.as_str()),
    );
    paths
}

fn copy_path(source: &Path, destination: &Path) -> anyhow::Result<()> {
    let metadata = std::fs::symlink_metadata(source)
        .with_context(|| format!("Cannot inspect {}", source.display()))?;

    if metadata.file_type().is_dir() && !metadata.file_type().is_symlink() {
        std::fs::create_dir_all(destination)
            .with_context(|| format!("Cannot create {}", destination.display()))?;
        for entry in std::fs::read_dir(source)
            .with_context(|| format!("Cannot read {}", source.display()))?
        {
            let entry = entry.with_context(|| format!("Cannot read {}", source.display()))?;
            copy_path(&entry.path(), &destination.join(entry.file_name()))?;
        }
        std::fs::set_permissions(destination, metadata.permissions())
            .with_context(|| format!("Cannot set permissions on {}", destination.display()))?;
        return Ok(());
    }

    if let Some(parent) = destination.parent() {
        std::fs::create_dir_all(parent)
            .with_context(|| format!("Cannot create {}", parent.display()))?;
    }
    std::fs::copy(source, destination).with_context(|| {
        format!(
            "Cannot copy {} to {}",
            source.display(),
            destination.display()
        )
    })?;
    std::fs::set_permissions(destination, metadata.permissions())
        .with_context(|| format!("Cannot set permissions on {}", destination.display()))
}

/// Retry transient sharing violations from antivirus, Explorer, or delayed handle release.
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

/// Continue after a sharing violation so one locked SQLite handle cannot block removal of its siblings.
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

    let Ok(entries) = std::fs::read_dir(root) else {
        report.failed.push(root.to_path_buf());
        return report;
    };

    for entry in entries {
        let Ok(entry) = entry else {
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

    if report.failed.is_empty() && !try_remove_with_retry(root, true) {
        report.failed.push(root.to_path_buf());
    }

    report
}

pub(super) fn uninstall_payload(game_path: &Path) -> anyhow::Result<()> {
    remove_bpp_files(game_path, true)
}

pub(super) fn uninstall_payload_preserving_shared_dependencies(
    game_path: &Path,
) -> anyhow::Result<()> {
    remove_bpp_files(game_path, false)
}

fn remove_bpp_files(game_path: &Path, remove_bundled_dependencies: bool) -> anyhow::Result<()> {
    for relative_path in owned_paths("private") {
        remove_path_if_exists(&installed_path(game_path, Path::new(relative_path)))?;
    }

    if remove_bundled_dependencies {
        for relative_path in owned_paths("dependency") {
            remove_path_if_exists(&installed_path(game_path, Path::new(relative_path)))?;
        }
    }

    remove_empty_dir_if_exists(&game_path.join("BepInEx/config"))?;
    remove_empty_dir_if_exists(&game_path.join("BepInEx/plugins"))?;
    remove_empty_dir_if_exists(&game_path.join("BepInEx/patchers"))?;
    remove_empty_dir_if_exists(&game_path.join("BepInEx"))?;

    Ok(())
}

fn remove_empty_dir_if_exists(path: &Path) -> anyhow::Result<()> {
    match std::fs::remove_dir(path) {
        Ok(()) => Ok(()),
        Err(err) if err.kind() == std::io::ErrorKind::NotFound => Ok(()),
        Err(err) if err.kind() == std::io::ErrorKind::DirectoryNotEmpty => Ok(()),
        Err(err) => Err(anyhow!(
            "Cannot remove empty directory {}: {err}",
            path.display()
        )),
    }
}

pub(super) fn remove_bootstrap_files(game_path: &Path) -> anyhow::Result<()> {
    for relative_path in owned_paths("bootstrap") {
        remove_path_if_exists(&installed_path(game_path, Path::new(relative_path)))?;
    }

    remove_empty_dir_if_exists(&game_path.join("BepInEx/config"))?;
    remove_empty_dir_if_exists(&game_path.join("BepInEx/plugins"))?;
    remove_empty_dir_if_exists(&game_path.join("BepInEx/patchers"))?;
    remove_empty_dir_if_exists(&game_path.join("BepInEx"))?;

    Ok(())
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
            && !owned_paths("private").any(|owned| owned.eq_ignore_ascii_case(&relative))
            && !owned_paths("dependency").any(|owned| owned.eq_ignore_ascii_case(&relative))
    })
}

/// A third-party patcher can depend on BepInEx even when `BepInEx/plugins` is empty.
pub(super) fn has_third_party_patchers(game_path: &Path) -> bool {
    dir_contains_unowned_file(game_path, &game_path.join("BepInEx/patchers"))
}

fn dir_contains_unowned_file(game_path: &Path, dir: &Path) -> bool {
    let Ok(entries) = std::fs::read_dir(dir) else {
        return false;
    };

    entries.filter_map(Result::ok).any(|entry| {
        let path = entry.path();
        if path.is_dir() {
            return dir_contains_unowned_file(game_path, &path);
        }
        let Ok(relative) = path.strip_prefix(game_path) else {
            return true;
        };
        let relative = relative.to_string_lossy().replace('\\', "/");
        !owned_paths("private").any(|owned| owned.eq_ignore_ascii_case(&relative))
    })
}

pub(crate) fn ensure_valid_game_path(game_path: &Path) -> anyhow::Result<()> {
    if crate::services::detect::is_valid_game_path(game_path) {
        return Ok(());
    }

    Err(anyhow!(
        "Selected path is not a valid The Bazaar installation: {}",
        game_path.display()
    ))
}

pub(super) fn prepare_install_target(
    game_path: &Path,
    incoming_relative_paths: &std::collections::HashSet<String>,
) -> anyhow::Result<InstallTargetBackup> {
    ensure_valid_game_path(game_path)?;
    let backup = InstallTargetBackup::capture(game_path, incoming_relative_paths)?;
    if let Err(cleanup_err) = remove_stale_bpp_files(game_path, incoming_relative_paths) {
        return match backup.restore(game_path) {
            Ok(()) => Err(cleanup_err),
            Err(restore_err) => Err(anyhow!(
                "{cleanup_err:#}; additionally failed to restore previous payload: {restore_err:#}"
            )),
        };
    }

    Ok(backup)
}

fn remove_stale_bpp_files(
    game_path: &Path,
    incoming_relative_paths: &std::collections::HashSet<String>,
) -> anyhow::Result<()> {
    for relative_path in owned_paths("private").chain(owned_paths("dependency")) {
        if incoming_relative_paths.contains(relative_path) {
            continue;
        }
        remove_path_if_exists(&installed_path(game_path, Path::new(relative_path)))?;
    }

    Ok(())
}

pub(super) fn preserve_file_if_exists(
    base_dir: &Path,
    relative_path: &'static str,
) -> anyhow::Result<Option<PreservedFile>> {
    let path = base_dir.join(relative_path);
    if !path.exists() {
        return Ok(None);
    }

    let contents =
        std::fs::read(&path).with_context(|| format!("Cannot preserve {}", path.display()))?;

    Ok(Some(PreservedFile {
        relative_path,
        contents,
    }))
}

pub(super) fn restore_preserved_file(
    base_dir: &Path,
    preserved: &PreservedFile,
) -> anyhow::Result<()> {
    let path = base_dir.join(preserved.relative_path);
    if let Some(parent) = path.parent() {
        std::fs::create_dir_all(parent)
            .with_context(|| format!("Cannot recreate {}", parent.display()))?;
    }

    std::fs::write(&path, &preserved.contents)
        .with_context(|| format!("Cannot restore {}", path.display()))
}

/// Both loaders fall through to the real game when the target assembly is absent, so keep the bootstrap.
pub(super) fn reset_bepinex_directory(game_path: &Path) -> RemovalReport {
    remove_dir_with_retry(&game_path.join("BepInEx"))
}

#[cfg(test)]
mod tests {
    use crate::config::{BAZAAR_DATA_DIRECTORY, DATABASE_FILE_NAME};

    #[cfg(any(target_os = "macos", target_os = "windows"))]
    use super::prepare_install_target;
    use super::{
        ensure_valid_game_path, remove_bootstrap_files, restore_preserved_file, uninstall_payload,
        PreservedFile, BPP_CONFIG_RELATIVE_PATH,
    };

    #[test]
    fn test_ensure_valid_game_path_rejects_non_game_directory() {
        let tmp = tempfile::tempdir().unwrap();

        let result = ensure_valid_game_path(tmp.path());

        assert!(result.is_err());
    }

    #[cfg(any(target_os = "macos", target_os = "windows"))]
    #[test]
    fn test_prepare_install_target_removes_only_stale_bpp_files() {
        let tmp = tempfile::tempdir().unwrap();

        #[cfg(target_os = "macos")]
        {
            std::fs::create_dir_all(tmp.path().join("TheBazaar.app")).unwrap();
            std::fs::create_dir_all(tmp.path().join("BepInEx/plugins")).unwrap();
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
        std::fs::write(
            tmp.path().join("BepInEx/plugins/Microsoft.Data.Sqlite.dll"),
            b"stale dep",
        )
        .unwrap();

        let incoming: std::collections::HashSet<String> =
            ["BepInEx/plugins/BazaarPlusPlus.dll".to_string()]
                .into_iter()
                .collect();

        prepare_install_target(tmp.path(), &incoming).unwrap();

        assert!(tmp.path().join("BepInEx/plugins/old.dll").exists());
        assert!(tmp
            .path()
            .join("BepInEx/plugins/BazaarPlusPlus.dll")
            .exists());
        assert!(!tmp
            .path()
            .join("BepInEx/plugins/Microsoft.Data.Sqlite.dll")
            .exists());
        #[cfg(target_os = "macos")]
        {
            assert!(tmp.path().join("libdoorstop.dylib").exists());
        }
        #[cfg(target_os = "windows")]
        {
            assert!(tmp.path().join("doorstop_config.ini").exists());
            assert!(tmp.path().join("winhttp.dll").exists());
        }
    }

    #[cfg(any(target_os = "macos", target_os = "windows"))]
    #[test]
    fn test_install_target_backup_restores_previous_payload_after_partial_install() {
        let tmp = tempfile::tempdir().unwrap();

        #[cfg(target_os = "macos")]
        {
            std::fs::create_dir_all(tmp.path().join("TheBazaar.app")).unwrap();
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

        let preloader = "BepInEx/core/BepInEx.Preloader.dll";
        let backup = prepare_install_target(tmp.path(), &[preloader.to_owned()].into()).unwrap();
        std::fs::create_dir(tmp.path().join("BepInEx/core")).unwrap();
        std::fs::write(tmp.path().join(preloader), b"partial install").unwrap();
        std::fs::create_dir_all(tmp.path().join("BepInEx/plugins")).unwrap();
        std::fs::write(tmp.path().join("BepInEx/plugins/new.dll"), b"new").unwrap();
        #[cfg(target_os = "macos")]
        std::fs::write(tmp.path().join("libdoorstop.dylib"), b"new dylib").unwrap();
        #[cfg(target_os = "windows")]
        std::fs::write(tmp.path().join("winhttp.dll"), b"new dll").unwrap();

        backup.restore(tmp.path()).unwrap();

        assert!(!tmp.path().join("BepInEx/core").exists());
        assert!(tmp.path().join("BepInEx/plugins/old.dll").exists());
        assert!(tmp.path().join("BepInEx/plugins/new.dll").exists());
        #[cfg(target_os = "macos")]
        assert_eq!(
            std::fs::read(tmp.path().join("libdoorstop.dylib")).unwrap(),
            b"old dylib"
        );
        #[cfg(target_os = "windows")]
        assert_eq!(
            std::fs::read(tmp.path().join("winhttp.dll")).unwrap(),
            b"old dll"
        );
    }

    #[cfg(any(target_os = "macos", target_os = "windows"))]
    #[test]
    fn test_prepare_install_target_preserves_bpp_data_directory() {
        let tmp = tempfile::tempdir().unwrap();
        let plugins_dir = tmp.path().join("BepInEx/plugins");
        let data_dir = tmp.path().join(BAZAAR_DATA_DIRECTORY);

        #[cfg(target_os = "macos")]
        {
            std::fs::create_dir_all(tmp.path().join("TheBazaar.app")).unwrap();
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
        std::fs::write(plugins_dir.join("BazaarPlusPlus.version"), b"4.7.0.prod").unwrap();
        std::fs::write(data_dir.join("stale.dll"), b"dll").unwrap();

        prepare_install_target(tmp.path(), &std::collections::HashSet::new()).unwrap();

        assert!(data_dir.exists());
        assert!(data_dir.join("stale.dll").exists());
    }

    #[test]
    fn uninstall_preserves_every_data_root() {
        for keep_shared_dependencies in [false, true] {
            let tmp = tempfile::tempdir().unwrap();
            std::fs::create_dir_all(tmp.path().join("BepInEx/plugins")).unwrap();
            std::fs::write(
                tmp.path().join("BepInEx/plugins/BazaarPlusPlus.dll"),
                b"dll",
            )
            .unwrap();
            let roots: Vec<_> = std::iter::once(BAZAAR_DATA_DIRECTORY)
                .chain(
                    crate::services::legacy_data::LegacyRoot::ALL
                        .into_iter()
                        .map(crate::services::legacy_data::LegacyRoot::directory_name),
                )
                .map(|name| tmp.path().join(name).join(DATABASE_FILE_NAME))
                .collect();
            for database in &roots {
                std::fs::create_dir_all(database.parent().unwrap()).unwrap();
                std::fs::write(database, b"db").unwrap();
            }

            if keep_shared_dependencies {
                super::uninstall_payload_preserving_shared_dependencies(tmp.path()).unwrap();
            } else {
                uninstall_payload(tmp.path()).unwrap();
                remove_bootstrap_files(tmp.path()).unwrap();
            }

            assert!(!tmp
                .path()
                .join("BepInEx/plugins/BazaarPlusPlus.dll")
                .exists());
            for database in &roots {
                assert!(database.is_file(), "{} was removed", database.display());
            }
        }
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
        std::fs::create_dir_all(
            tmp.path()
                .join("BepInEx/plugins/BppReplayRecorder.app/Contents/MacOS"),
        )
        .unwrap();
        std::fs::write(
            tmp.path()
                .join("BepInEx/plugins/BppReplayRecorder.app/Contents/MacOS/BppReplayRecorder"),
            b"helper",
        )
        .unwrap();
        std::fs::create_dir_all(tmp.path().join(
            "TheBazaar.app/Contents/Plugins/GfxPluginBppReplayVideoToolbox.bundle/Contents/MacOS",
        ))
        .unwrap();
        std::fs::write(
            tmp.path().join(
                "TheBazaar.app/Contents/Plugins/GfxPluginBppReplayVideoToolbox.bundle/Contents/MacOS/GfxPluginBppReplayVideoToolbox",
            ),
            b"plugin",
        )
        .unwrap();
        std::fs::create_dir_all(tmp.path().join("TheBazaar_Data/Plugins/x86_64")).unwrap();
        std::fs::write(
            tmp.path()
                .join("TheBazaar_Data/Plugins/x86_64/GfxPluginBppReplayMediaFoundation.dll"),
            b"plugin",
        )
        .unwrap();
        std::fs::write(tmp.path().join("BepInEx/plugins/OtherMod.dll"), b"dll").unwrap();

        #[cfg(target_os = "macos")]
        {
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
        assert!(!tmp
            .path()
            .join("BepInEx/plugins/BppReplayRecorder.app")
            .exists());
        assert!(!tmp
            .path()
            .join("TheBazaar.app/Contents/Plugins/GfxPluginBppReplayVideoToolbox.bundle")
            .exists());
        assert!(!tmp
            .path()
            .join("TheBazaar_Data/Plugins/x86_64/GfxPluginBppReplayMediaFoundation.dll")
            .exists());
        assert!(tmp.path().join("BepInEx/plugins/OtherMod.dll").exists());
        #[cfg(target_os = "macos")]
        {
            assert!(tmp.path().join("libdoorstop.dylib").exists());
        }
        #[cfg(target_os = "windows")]
        {
            assert!(tmp.path().join("doorstop_config.ini").exists());
            assert!(tmp.path().join("winhttp.dll").exists());
        }
    }

    #[test]
    fn test_remove_bootstrap_files_restores_vanilla_game_root() {
        let tmp = tempfile::tempdir().unwrap();
        std::fs::create_dir_all(tmp.path().join("BepInEx/core")).unwrap();
        std::fs::create_dir_all(tmp.path().join("BepInEx/cache")).unwrap();
        std::fs::create_dir_all(tmp.path().join("BepInEx/config")).unwrap();
        std::fs::create_dir_all(tmp.path().join("BepInEx/plugins")).unwrap();
        std::fs::write(
            tmp.path().join("BepInEx/core/BepInEx.Preloader.dll"),
            b"dll",
        )
        .unwrap();
        std::fs::write(tmp.path().join("BepInEx/cache/typeloader.dat"), b"cache").unwrap();
        std::fs::write(tmp.path().join("BepInEx/config/BepInEx.cfg"), b"cfg").unwrap();
        std::fs::write(tmp.path().join("BepInEx/LogOutput.log"), b"log").unwrap();
        std::fs::write(tmp.path().join("BepInEx/plugins/.gitkeep"), b"").unwrap();
        std::fs::write(tmp.path().join("winhttp.dll"), b"dll").unwrap();
        std::fs::write(tmp.path().join("doorstop_config.ini"), b"cfg").unwrap();
        std::fs::write(tmp.path().join("libdoorstop.dylib"), b"dylib").unwrap();

        remove_bootstrap_files(tmp.path()).unwrap();

        assert!(!tmp.path().join("BepInEx").exists());
        assert!(!tmp.path().join("winhttp.dll").exists());
        assert!(!tmp.path().join("doorstop_config.ini").exists());
        assert!(!tmp.path().join("libdoorstop.dylib").exists());
    }

    #[test]
    fn test_remove_bootstrap_files_keeps_unknown_foreign_files() {
        let tmp = tempfile::tempdir().unwrap();
        std::fs::create_dir_all(tmp.path().join("BepInEx/core")).unwrap();
        std::fs::create_dir_all(tmp.path().join("BepInEx/config")).unwrap();
        std::fs::write(
            tmp.path().join("BepInEx/core/BepInEx.Preloader.dll"),
            b"dll",
        )
        .unwrap();
        std::fs::write(tmp.path().join("BepInEx/config/OtherMod.cfg"), b"cfg").unwrap();

        remove_bootstrap_files(tmp.path()).unwrap();

        assert!(!tmp.path().join("BepInEx/core").exists());
        assert!(tmp.path().join("BepInEx/config/OtherMod.cfg").exists());
        assert!(tmp.path().join("BepInEx").exists());
    }

    #[test]
    fn test_has_third_party_plugins_ignores_bpp_owned_files() {
        let tmp = tempfile::tempdir().unwrap();
        let plugins_dir = tmp.path().join("BepInEx/plugins");
        std::fs::create_dir_all(&plugins_dir).unwrap();
        std::fs::write(plugins_dir.join("BazaarPlusPlus.dll"), b"dll").unwrap();
        std::fs::write(plugins_dir.join("Microsoft.Data.Sqlite.dll"), b"dll").unwrap();
        std::fs::create_dir_all(plugins_dir.join("BppReplayRecorder.app/Contents/MacOS")).unwrap();
        std::fs::write(
            plugins_dir.join("BppReplayRecorder.app/Contents/MacOS/BppReplayRecorder"),
            b"helper",
        )
        .unwrap();

        assert!(!super::has_third_party_plugins(tmp.path()));

        std::fs::write(plugins_dir.join("OtherMod.dll"), b"dll").unwrap();

        assert!(super::has_third_party_plugins(tmp.path()));
    }

    #[test]
    fn test_has_third_party_patchers_requires_a_file_not_just_directories() {
        let tmp = tempfile::tempdir().unwrap();
        assert!(!super::has_third_party_patchers(tmp.path()));

        std::fs::create_dir_all(tmp.path().join("BepInEx/patchers/SomeMod")).unwrap();
        assert!(!super::has_third_party_patchers(tmp.path()));

        std::fs::write(
            tmp.path().join("BepInEx/patchers/SomeMod/Patcher.dll"),
            b"dll",
        )
        .unwrap();
        assert!(super::has_third_party_patchers(tmp.path()));
    }

    #[test]
    fn bpp_updater_patcher_is_not_a_third_party_patcher_and_uninstall_removes_it() {
        let tmp = tempfile::tempdir().unwrap();
        let patchers = tmp.path().join("BepInEx/patchers");
        std::fs::create_dir_all(&patchers).unwrap();
        std::fs::write(patchers.join("BazaarPlusPlus.Updater.dll"), b"dll").unwrap();
        assert!(!super::has_third_party_patchers(tmp.path()));

        super::uninstall_payload(tmp.path()).unwrap();
        assert!(!patchers.exists());
        assert!(!tmp.path().join("BepInEx").exists());
    }

    #[test]
    fn inventory_owns_only_bepinex_host_paths() {
        assert!(!super::inventory().files.is_empty());
        assert!(super::inventory()
            .files
            .iter()
            .all(|entry| entry.host == "bepinex"));
        for relative_path in super::payload_root_relative_paths() {
            assert!(
                !relative_path.starts_with("MelonLoader/")
                    && !relative_path.starts_with("lib/")
                    && !relative_path.starts_with("assets/"),
                "{relative_path}"
            );
        }
    }

    #[test]
    fn inventory_drives_private_cleanup_and_shared_dependency_preservation() {
        let tmp = tempfile::tempdir().unwrap();
        for entry in &super::inventory().files {
            if entry.ownership == "bootstrap" {
                continue;
            }
            let target = tmp.path().join(&entry.path);
            std::fs::create_dir_all(target.parent().unwrap()).unwrap();
            std::fs::write(target, b"payload").unwrap();
        }
        assert!(!super::has_third_party_plugins(tmp.path()));
        let foreign = tmp.path().join("BepInEx/plugins/OtherMod.dll");
        std::fs::write(&foreign, b"third party").unwrap();
        assert!(super::has_third_party_plugins(tmp.path()));

        super::uninstall_payload_preserving_shared_dependencies(tmp.path()).unwrap();
        for entry in &super::inventory().files {
            if entry.ownership == "bootstrap" {
                continue;
            }
            assert_eq!(
                tmp.path().join(&entry.path).exists(),
                entry.ownership == "dependency",
                "{}",
                entry.path
            );
        }
        assert!(foreign.exists());
        super::uninstall_payload(tmp.path()).unwrap();
        for relative_path in super::owned_paths("dependency") {
            assert!(!tmp.path().join(relative_path).exists());
        }
        assert!(foreign.exists());
    }

    #[test]
    fn test_cleanup_bpp_data_directory_walks_nested_subdirectories() {
        let tmp = tempfile::tempdir().unwrap();
        let data_dir = tmp.path().join(BAZAAR_DATA_DIRECTORY);
        let nested = data_dir.join("Identity").join("inner");
        std::fs::create_dir_all(&nested).unwrap();
        std::fs::write(data_dir.join(DATABASE_FILE_NAME), b"db").unwrap();
        std::fs::write(nested.join("auth.json"), b"auth").unwrap();

        let report =
            super::remove_dir_with_retry(&crate::services::paths::bpp_data_dir(tmp.path()));

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
        if crate::test_support::permission_bits_are_bypassed() {
            return;
        }

        let tmp = tempfile::tempdir().unwrap();
        let data_dir = tmp.path().join(BAZAAR_DATA_DIRECTORY);
        let unwritable_subdir = data_dir.join("locked");
        std::fs::create_dir_all(&unwritable_subdir).unwrap();
        std::fs::write(data_dir.join("removable.bin"), b"x").unwrap();
        std::fs::write(unwritable_subdir.join("trapped.bin"), b"x").unwrap();

        std::fs::set_permissions(&unwritable_subdir, std::fs::Permissions::from_mode(0o500))
            .unwrap();

        let report =
            super::remove_dir_with_retry(&crate::services::paths::bpp_data_dir(tmp.path()));

        // Restore permissions so the tempdir cleanup succeeds even if the test fails.
        let _ =
            std::fs::set_permissions(&unwritable_subdir, std::fs::Permissions::from_mode(0o700));

        assert!(!report.is_empty(), "expected at least one failed path");
        assert!(!data_dir.join("removable.bin").exists());
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

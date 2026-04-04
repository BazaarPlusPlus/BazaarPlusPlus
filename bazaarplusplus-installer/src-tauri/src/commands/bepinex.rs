use serde::Serialize;
use std::io::{Cursor, Read};
use std::path::Path;
use tauri::Manager;

macro_rules! debug_log {
    ($($arg:tt)*) => {
        #[cfg(debug_assertions)]
        println!($($arg)*);
    };
}

macro_rules! debug_error {
    ($($arg:tt)*) => {
        #[cfg(debug_assertions)]
        eprintln!($($arg)*);
    };
}

pub fn bundled_zip_relative_path() -> &'static str {
    "BepInExSource/BepInEx.zip"
}

const BPP_CONFIG_RELATIVE_PATH: &str = "BepInEx/config/BazaarPlusPlus.cfg";
const LEGACY_RECORD_DIRECTORY: &str = "BazaarPlusPlus";

#[derive(Debug, Serialize)]
pub struct LegacyRecordDirectoryInfo {
    pub total_bytes: u64,
}

struct PreservedFile {
    relative_path: &'static str,
    contents: Vec<u8>,
}

pub fn read_bundled_bpp_version(app: &tauri::AppHandle) -> Result<Option<String>, String> {
    let resource_path = app
        .path()
        .resource_dir()
        .map_err(|err| err.to_string())?
        .join(bundled_zip_relative_path());
    let zip_bytes = std::fs::read(&resource_path)
        .map_err(|err| format!("Cannot read bundled BepInEx.zip: {err}"))?;

    let reader = Cursor::new(zip_bytes);
    let mut archive = zip::ZipArchive::new(reader).map_err(|err| err.to_string())?;

    for index in 0..archive.len() {
        let mut file = archive.by_index(index).map_err(|err| err.to_string())?;
        if file.name().ends_with("BazaarPlusPlus.version") {
            let mut version = String::new();
            file.read_to_string(&mut version)
                .map_err(|err| err.to_string())?;
            let version = version.trim();
            return Ok((!version.is_empty()).then(|| version.to_string()));
        }
    }

    Ok(None)
}

pub fn extract_zip(zip_bytes: &[u8], dest_dir: &Path) -> Result<Vec<String>, String> {
    let reader = Cursor::new(zip_bytes);
    let mut archive = zip::ZipArchive::new(reader).map_err(|err| err.to_string())?;
    let mut extracted = Vec::new();

    for index in 0..archive.len() {
        let mut file = archive.by_index(index).map_err(|err| err.to_string())?;
        let Some(relative_path) = file.enclosed_name().map(|path| path.to_path_buf()) else {
            return Err(format!("Zip entry has unsafe path: {}", file.name()));
        };
        let output_path = dest_dir.join(relative_path);

        if file.is_dir() {
            std::fs::create_dir_all(&output_path).map_err(|err| err.to_string())?;
            continue;
        }

        if let Some(parent) = output_path.parent() {
            std::fs::create_dir_all(parent).map_err(|err| err.to_string())?;
        }

        let mut contents = Vec::new();
        file.read_to_end(&mut contents)
            .map_err(|err| err.to_string())?;
        std::fs::write(&output_path, contents).map_err(|err| err.to_string())?;
        extracted.push(output_path.to_string_lossy().into_owned());
    }

    Ok(extracted)
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

fn uninstall_payload(game_path: &Path) -> Result<(), String> {
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

fn ensure_valid_game_path(game_path: &Path) -> Result<(), String> {
    if crate::commands::detect::is_valid_game_path(game_path) {
        return Ok(());
    }

    Err(format!(
        "Selected path is not a valid The Bazaar installation: {}",
        game_path.display()
    ))
}

fn prepare_install_target(game_path: &Path) -> Result<(), String> {
    ensure_valid_game_path(game_path)?;
    uninstall_payload(game_path)
}

fn preserve_file_if_exists(
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

fn restore_preserved_file(base_dir: &Path, preserved: &PreservedFile) -> Result<(), String> {
    let path = base_dir.join(preserved.relative_path);
    if let Some(parent) = path.parent() {
        std::fs::create_dir_all(parent)
            .map_err(|err| format!("Cannot recreate {}: {err}", parent.display()))?;
    }

    std::fs::write(&path, &preserved.contents)
        .map_err(|err| format!("Cannot restore {}: {err}", path.display()))
}

fn cleanup_legacy_record_directory(game_path: &Path) -> Result<(), String> {
    remove_path_if_exists(&game_path.join(LEGACY_RECORD_DIRECTORY))
}

fn legacy_record_directory_size_bytes(game_path: &Path) -> Result<u64, String> {
    fn collect_size(path: &Path) -> Result<u64, String> {
        if !path.exists() {
            return Ok(0);
        }

        let metadata = std::fs::metadata(path)
            .map_err(|err| format!("Cannot read metadata for {}: {err}", path.display()))?;
        if metadata.is_file() {
            return Ok(metadata.len());
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

#[tauri::command]
pub fn repair_bpp(game_path: String) -> Result<(), String> {
    let game_path = Path::new(&game_path);
    ensure_valid_game_path(game_path)?;

    cleanup_legacy_record_directory(game_path)?;

    debug_log!("Repaired BazaarPlusPlus payload at {}", game_path.display());
    Ok(())
}

#[tauri::command]
pub fn get_legacy_record_directory_info(
    game_path: String,
) -> Result<LegacyRecordDirectoryInfo, String> {
    let game_path = Path::new(&game_path);
    ensure_valid_game_path(game_path)?;

    Ok(LegacyRecordDirectoryInfo {
        total_bytes: legacy_record_directory_size_bytes(game_path)?,
    })
}

#[tauri::command]
pub fn install_bepinex(
    app: tauri::AppHandle,
    steam_path: String,
    game_path: String,
) -> Result<(), String> {
    let game_path = Path::new(&game_path);
    let preserved_bpp_config = preserve_file_if_exists(game_path, BPP_CONFIG_RELATIVE_PATH)?;
    #[cfg(not(target_os = "macos"))]
    let _ = &steam_path;
    #[cfg(target_os = "macos")]
    crate::commands::steam::prepare_steam_for_launch_option_update(Path::new(&steam_path))?;
    prepare_install_target(game_path)?;

    let install_result = (|| -> Result<(), String> {
        debug_log!("Reading bundled BepInEx.zip...");
        let relative_zip_path = bundled_zip_relative_path();
        let resource_path = app
            .path()
            .resource_dir()
            .map_err(|err| err.to_string())?
            .join(relative_zip_path);
        let zip_bytes = std::fs::read(&resource_path).map_err(|err| {
            debug_error!("Cannot read bundled BepInEx.zip: {err}");
            format!("Cannot read bundled BepInEx.zip: {err}")
        })?;

        debug_log!("Extracting BepInEx...");
        let extracted = extract_zip(&zip_bytes, game_path)?;
        debug_log!("Extracted {} files.", extracted.len());

        Ok(())
    })();

    let restore_result = preserved_bpp_config
        .as_ref()
        .map(|preserved| restore_preserved_file(game_path, preserved))
        .transpose();

    match (install_result, restore_result) {
        (Ok(()), Ok(_)) => Ok(()),
        (Err(install_err), Ok(_)) => Err(install_err),
        (Ok(()), Err(restore_err)) => Err(restore_err),
        (Err(install_err), Err(restore_err)) => Err(format!(
            "{install_err}; additionally failed to restore preserved config: {restore_err}"
        )),
    }
}

#[tauri::command]
pub fn uninstall_bpp(
    _app: tauri::AppHandle,
    _steam_path: String,
    game_path: String,
) -> Result<(), String> {
    let game_path = Path::new(&game_path);
    ensure_valid_game_path(game_path)?;

    #[cfg(target_os = "macos")]
    crate::commands::steam::prepare_steam_for_launch_option_update(Path::new(&_steam_path))?;

    uninstall_payload(game_path)?;

    #[cfg(target_os = "macos")]
    {
        crate::commands::vdf::clear_launch_options_for_steam(Path::new(&_steam_path))?;
    }

    debug_log!(
        "Uninstalled BazaarPlusPlus payload from {}",
        game_path.display()
    );
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Write;

    fn make_test_zip() -> Vec<u8> {
        let buffer = Cursor::new(Vec::new());
        let mut zip = zip::ZipWriter::new(buffer);
        let options = zip::write::SimpleFileOptions::default();

        zip.add_directory("BepInEx/", options).unwrap();
        zip.start_file("BepInEx/core/BepInEx.Core.dll", options)
            .unwrap();
        zip.write_all(b"fake dll content").unwrap();

        zip.finish().unwrap().into_inner()
    }

    #[test]
    fn test_extract_zip_creates_files() {
        let zip_bytes = make_test_zip();
        let tmp = tempfile::tempdir().unwrap();

        let extracted = extract_zip(&zip_bytes, tmp.path()).unwrap();
        assert!(!extracted.is_empty());
        assert!(tmp.path().join("BepInEx/core/BepInEx.Core.dll").exists());
    }

    #[test]
    fn test_bundled_zip_relative_path_matches_supported_targets() {
        assert_eq!(bundled_zip_relative_path(), "BepInExSource/BepInEx.zip");
    }

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

        cleanup_legacy_record_directory(tmp.path()).unwrap();

        assert!(!legacy_dir.exists());
    }

    #[test]
    fn test_repair_bpp_removes_legacy_directory_for_installed_v1() {
        let tmp = tempfile::tempdir().unwrap();
        let legacy_dir = tmp.path().join(LEGACY_RECORD_DIRECTORY);

        #[cfg(target_os = "macos")]
        {
            std::fs::create_dir_all(tmp.path().join("TheBazaar.app")).unwrap();
        }

        #[cfg(target_os = "windows")]
        {
            std::fs::write(tmp.path().join("TheBazaar.exe"), b"exe").unwrap();
        }

        std::fs::create_dir_all(&legacy_dir).unwrap();
        std::fs::write(legacy_dir.join("legacy.dll"), b"dll").unwrap();

        repair_bpp(tmp.path().to_string_lossy().into_owned()).unwrap();

        assert!(!legacy_dir.exists());
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

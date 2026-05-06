use std::path::{Path, PathBuf};

pub(super) fn normalize_game_path(game_path: Option<String>) -> Option<PathBuf> {
    game_path
        .as_deref()
        .map(str::trim)
        .filter(|path| !path.is_empty())
        .map(PathBuf::from)
}

pub(super) fn is_bepinex_installed(game_path: &Path) -> bool {
    if !game_path
        .join("BepInEx/core/BepInEx.Preloader.dll")
        .exists()
    {
        return false;
    }

    #[cfg(target_os = "macos")]
    return game_path.join("run_bepinex.sh").exists()
        && game_path.join("libdoorstop.dylib").exists();

    #[cfg(target_os = "windows")]
    return game_path.join("doorstop_config.ini").exists()
        && game_path.join("winhttp.dll").exists();

    #[cfg(not(any(target_os = "macos", target_os = "windows")))]
    return true;
}

pub(super) fn read_installed_bpp_version(game_path: &Path) -> Option<String> {
    let version_path = game_path.join("BepInEx/plugins/BazaarPlusPlus.version");
    let version = std::fs::read_to_string(version_path).ok()?;
    let version = version.trim();
    (!version.is_empty()).then(|| version.to_string())
}

pub(crate) fn is_valid_game_path(base: &Path) -> bool {
    #[cfg(target_os = "macos")]
    return base.join("TheBazaar.app").exists();

    #[cfg(target_os = "windows")]
    return base.join("TheBazaar.exe").exists();

    #[cfg(not(any(target_os = "macos", target_os = "windows")))]
    return base.join("TheBazaar").exists();
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_read_installed_bpp_version_trims_contents() {
        let temp_root = std::env::temp_dir().join(format!(
            "bppinstaller-version-test-{}-{}",
            std::process::id(),
            std::time::SystemTime::now()
                .duration_since(std::time::UNIX_EPOCH)
                .expect("system time before epoch")
                .as_nanos()
        ));
        let plugins_dir = temp_root.join("BepInEx/plugins");
        std::fs::create_dir_all(&plugins_dir).expect("create plugins dir");
        std::fs::write(
            plugins_dir.join("BazaarPlusPlus.version"),
            "1.2.3+2026-03-10 12:34:56\n",
        )
        .expect("write version file");

        let version = read_installed_bpp_version(&temp_root);

        std::fs::remove_dir_all(&temp_root).expect("cleanup temp dir");

        assert_eq!(version.as_deref(), Some("1.2.3+2026-03-10 12:34:56"));
    }

    #[test]
    fn test_is_bepinex_installed_detects_core_payload_without_version_file() {
        let tmp = tempfile::tempdir().unwrap();
        std::fs::create_dir_all(tmp.path().join("BepInEx/core")).unwrap();
        std::fs::write(
            tmp.path().join("BepInEx/core/BepInEx.Preloader.dll"),
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

        assert!(is_bepinex_installed(tmp.path()));
    }

    #[test]
    fn test_normalize_game_path_trims_whitespace() {
        let game_path = normalize_game_path(Some("  C:\\Games\\The Bazaar  ".to_string()));
        assert_eq!(game_path, Some(PathBuf::from("C:\\Games\\The Bazaar")));
    }
}

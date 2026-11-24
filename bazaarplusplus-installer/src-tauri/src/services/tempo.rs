use std::path::{Path, PathBuf};

use anyhow::Context as _;
use winreg::enums::{
    HKEY_CURRENT_USER, HKEY_LOCAL_MACHINE, KEY_READ, KEY_WOW64_32KEY, KEY_WOW64_64KEY,
};
use winreg::RegKey;

const LAUNCHER_NAME: &str = "Tempo Launcher - Beta";

pub(super) fn default_game_path() -> Option<PathBuf> {
    Some(dirs::data_dir()?.join(LAUNCHER_NAME).join("game/buildx64"))
}

/// Tempo creates game/buildx64 under both its default and user-chosen library.
pub(super) fn library(game: &Path) -> Option<&Path> {
    let parent = game.parent()?;
    (game.file_name()?.eq_ignore_ascii_case("buildx64")
        && parent.file_name()?.eq_ignore_ascii_case("game"))
    .then_some(parent)
}

fn launcher_path() -> Option<PathBuf> {
    const KEY: &str =
        r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\df68daa9-c4d4-5ddb-a82e-498d1a4dd9c3";
    let exe = format!("{LAUNCHER_NAME}.exe");
    for hive in [HKEY_CURRENT_USER, HKEY_LOCAL_MACHINE] {
        for view in [KEY_WOW64_64KEY, KEY_WOW64_32KEY] {
            let Ok(key) = RegKey::predef(hive).open_subkey_with_flags(KEY, KEY_READ | view) else {
                continue;
            };
            if let Ok(icon) = key.get_value::<String, _>("DisplayIcon") {
                if let Some(dir) = Path::new(icon.trim_matches('"')).parent() {
                    let launcher = dir.join(&exe);
                    if launcher.is_absolute() && launcher.is_file() {
                        return Some(launcher);
                    }
                }
            }
        }
    }
    let launcher = PathBuf::from(std::env::var_os("ProgramFiles")?)
        .join(LAUNCHER_NAME)
        .join(exe);
    launcher.is_file().then_some(launcher)
}

pub(super) fn open_launcher() -> anyhow::Result<()> {
    let launcher = launcher_path().context("Tempo Launcher is missing")?;
    // The launcher requests highestAvailable. Let the Windows shell handle UAC.
    tauri_plugin_opener::open_path(launcher, None::<&str>).context("Failed to open Tempo Launcher")
}

#[cfg(test)]
mod tests {
    #[test]
    fn custom_library_uses_the_stable_parent_across_game_updates() {
        let root = tempfile::tempdir().unwrap();
        let library = root.path().join("custom location/Game");
        let game = library.join("BuildX64");
        let data = crate::services::paths::bpp_data_dir(&game);
        assert_eq!(data, library.join(crate::config::BAZAAR_DATA_DIRECTORY));
        assert!(super::library(&root.path().join("steamapps/common/The Bazaar")).is_none());
        assert!(super::library(&root.path().join("BuildX64")).is_none());
    }

    #[test]
    fn restored_version_marker_requires_the_native_recorder_for_readiness() {
        let root = tempfile::tempdir().unwrap();
        let game = root.path().join("game/buildx64");
        let plugins = game.join("BepInEx/plugins");
        std::fs::create_dir_all(&plugins).unwrap();
        std::fs::write(plugins.join("BazaarPlusPlus.version"), "6.4.5").unwrap();
        let version = crate::services::detect::read_installed_bpp_version;
        assert_eq!(version(&game), None);
        let recorder =
            game.join("TheBazaar_Data/Plugins/x86_64/GfxPluginBppReplayMediaFoundation.dll");
        std::fs::create_dir_all(recorder.parent().unwrap()).unwrap();
        std::fs::write(recorder, []).unwrap();
        assert_eq!(version(&game).as_deref(), Some("6.4.5"));
    }
}

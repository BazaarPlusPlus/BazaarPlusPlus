#[cfg(any(target_os = "windows", target_os = "macos"))]
use anyhow::Context as _;
use std::path::{Path, PathBuf};

use crate::config::{
    BAZAAR_DATA_DIRECTORY, COMBAT_REPLAYS_DIRECTORY, COMBAT_REPLAY_VIDEOS_DIRECTORY,
    DATABASE_FILE_NAME, INSTALLER_STATE_DIRECTORY, SCREENSHOTS_DIRECTORY,
};

const OVERLAY_CACHE_DIRECTORY: &str = "stream-overlay-cache";
const OVERLAY_SETTINGS_FILE_NAME: &str = "stream-overlay-crop.json";

pub fn bpp_data_dir(game_path: &Path) -> PathBuf {
    #[cfg(any(target_os = "windows", target_os = "macos"))]
    if let Some(library) = super::tempo::library(game_path) {
        let stable = library.join(BAZAAR_DATA_DIRECTORY);
        let old = game_path.join(BAZAAR_DATA_DIRECTORY);
        if !stable.is_dir() && (old.is_dir() || stable.is_file()) {
            return old;
        }
        return stable;
    }
    game_path.join(BAZAAR_DATA_DIRECTORY)
}

fn existing_directory(path: &Path) -> anyhow::Result<bool> {
    let entry = match std::fs::symlink_metadata(path) {
        Ok(entry) => entry,
        Err(err) if err.kind() == std::io::ErrorKind::NotFound => return Ok(false),
        Err(err) => return Err(err.into()),
    };
    anyhow::ensure!(
        entry.is_dir() && !entry.file_type().is_symlink(),
        "Expected a data directory at {}",
        path.display()
    );
    Ok(true)
}

/// Verify the physical target again immediately before migration or Reset.
pub(crate) fn checked_data_root(game: &Path) -> anyhow::Result<PathBuf> {
    let original = game.join(BAZAAR_DATA_DIRECTORY);
    let original_exists = existing_directory(&original)?;
    #[cfg(any(target_os = "windows", target_os = "macos"))]
    if let Some(library) = super::tempo::library(game) {
        let stable = library.join(BAZAAR_DATA_DIRECTORY);
        let stable_exists = existing_directory(&stable)?;
        anyhow::ensure!(!(stable_exists && original_exists),
            "Both {} and {} exist. Back up and consolidate the two Tempo data directories before retrying.",
            stable.display(), original.display());
        if !original_exists {
            return Ok(stable);
        }
    }
    let _ = original_exists;
    Ok(original)
}

pub(crate) fn tempo_migration_pending(game: &Path) -> bool {
    #[cfg(any(target_os = "windows", target_os = "macos"))]
    if let Some(library) = super::tempo::library(game) {
        return game
            .join(BAZAAR_DATA_DIRECTORY)
            .try_exists()
            .unwrap_or(true)
            || existing_directory(&library.join(BAZAAR_DATA_DIRECTORY)).is_err();
    }
    let _ = game;
    false
}

pub(crate) fn migrate_tempo_data(game: &Path) -> anyhow::Result<()> {
    #[cfg(any(target_os = "windows", target_os = "macos"))]
    if let Some(library) = super::tempo::library(game) {
        let source = checked_data_root(game)?;
        let stable = library.join(BAZAAR_DATA_DIRECTORY);
        if source != stable {
            std::fs::rename(&source, &stable).with_context(|| {
                format!(
                    "Could not move {} to {}",
                    source.display(),
                    stable.display()
                )
            })?;
        }
    }
    let _ = game;
    Ok(())
}

pub fn database_path(game_path: &Path) -> PathBuf {
    bpp_data_dir(game_path).join(DATABASE_FILE_NAME)
}

pub fn screenshots_dir(game_path: &Path) -> PathBuf {
    screenshots_dir_in(&bpp_data_dir(game_path))
}

pub fn combat_replay_videos_dir(game_path: &Path) -> PathBuf {
    combat_replay_videos_dir_in(&bpp_data_dir(game_path))
}

/// Legacy Roots and the current Data Root use the same relative layout.
pub fn screenshots_dir_in(data_root: &Path) -> PathBuf {
    data_root.join(SCREENSHOTS_DIRECTORY)
}

pub fn combat_replay_videos_dir_in(data_root: &Path) -> PathBuf {
    data_root.join(COMBAT_REPLAY_VIDEOS_DIRECTORY)
}

pub fn combat_replays_dir_in(data_root: &Path) -> PathBuf {
    data_root.join(COMBAT_REPLAYS_DIRECTORY)
}

pub fn installer_cache_dir() -> PathBuf {
    let base = dirs::cache_dir()
        .or_else(dirs::config_dir)
        .or_else(dirs::data_local_dir)
        .unwrap_or_else(std::env::temp_dir);
    base.join(INSTALLER_STATE_DIRECTORY)
}

pub fn overlay_cache_dir() -> PathBuf {
    installer_cache_dir().join(OVERLAY_CACHE_DIRECTORY)
}

pub fn card_catalog_dir() -> PathBuf {
    installer_cache_dir().join("card-catalog")
}

pub fn game_data_dir(game: &Path) -> PathBuf {
    #[cfg(target_os = "macos")]
    return crate::services::tempo::game_bundle(game).join("Contents/Resources/Data");
    #[cfg(not(target_os = "macos"))]
    return game.join("TheBazaar_Data");
}

pub fn game_cache_dir() -> Option<PathBuf> {
    let home = dirs::home_dir()?;
    if cfg!(target_os = "macos") {
        Some(home.join("Library/Application Support/com.TempoStorm.TheBazaar/prod/cache"))
    } else if cfg!(target_os = "windows") {
        // Unity's persistentDataPath: LocalLow\<company>\<product>.
        Some(home.join("AppData/LocalLow/Tempo Storm/The Bazaar/prod/cache"))
    } else {
        None
    }
}

fn overlay_settings_base_dir() -> PathBuf {
    dirs::config_dir()
        .or_else(dirs::data_local_dir)
        .unwrap_or_else(std::env::temp_dir)
}

pub fn overlay_settings_path() -> PathBuf {
    overlay_settings_base_dir()
        .join(INSTALLER_STATE_DIRECTORY)
        .join(OVERLAY_SETTINGS_FILE_NAME)
}

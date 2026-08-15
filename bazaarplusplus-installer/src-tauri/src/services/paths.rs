use std::path::{Path, PathBuf};

use crate::config::{
    BAZAAR_DATA_DIRECTORY, COMBAT_REPLAYS_DIRECTORY, COMBAT_REPLAY_VIDEOS_DIRECTORY,
    DATABASE_FILE_NAME, INSTALLER_STATE_DIRECTORY, SCREENSHOTS_DIRECTORY,
};

const LEGACY_OVERLAY_SETTINGS_DIRECTORY: &str = "BazaarPlusPlusV4";
const OVERLAY_CACHE_DIRECTORY: &str = "stream-overlay-cache";
const OVERLAY_SETTINGS_FILE_NAME: &str = "stream-overlay-crop.json";

pub fn bpp_data_dir(game_path: &Path) -> PathBuf {
    game_path.join(BAZAAR_DATA_DIRECTORY)
}

pub fn database_path(game_path: &Path) -> PathBuf {
    bpp_data_dir(game_path).join(DATABASE_FILE_NAME)
}

pub fn screenshots_dir(game_path: &Path) -> PathBuf {
    bpp_data_dir(game_path).join(SCREENSHOTS_DIRECTORY)
}

pub fn combat_replay_videos_dir(game_path: &Path) -> PathBuf {
    bpp_data_dir(game_path).join(COMBAT_REPLAY_VIDEOS_DIRECTORY)
}

pub fn combat_replays_dir(game_path: &Path) -> PathBuf {
    bpp_data_dir(game_path).join(COMBAT_REPLAYS_DIRECTORY)
}

pub fn overlay_cache_dir() -> PathBuf {
    let base = dirs::cache_dir()
        .or_else(dirs::config_dir)
        .or_else(dirs::data_local_dir)
        .unwrap_or_else(std::env::temp_dir);
    base.join(INSTALLER_STATE_DIRECTORY)
        .join(OVERLAY_CACHE_DIRECTORY)
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

pub fn legacy_overlay_settings_path() -> PathBuf {
    overlay_settings_base_dir()
        .join(LEGACY_OVERLAY_SETTINGS_DIRECTORY)
        .join(OVERLAY_SETTINGS_FILE_NAME)
}

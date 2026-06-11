use std::path::{Path, PathBuf};

use crate::config::{
    BAZAAR_DATA_DIRECTORY, COMBAT_REPLAY_VIDEOS_DIRECTORY, DATABASE_FILE_NAME,
    SCREENSHOTS_DIRECTORY,
};

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

pub fn overlay_cache_dir() -> PathBuf {
    let base = dirs::cache_dir()
        .or_else(dirs::config_dir)
        .or_else(dirs::data_local_dir)
        .unwrap_or_else(std::env::temp_dir);
    base.join(BAZAAR_DATA_DIRECTORY)
        .join(OVERLAY_CACHE_DIRECTORY)
}

pub fn overlay_settings_path() -> PathBuf {
    let base = dirs::config_dir()
        .or_else(dirs::data_local_dir)
        .unwrap_or_else(std::env::temp_dir);
    base.join(BAZAAR_DATA_DIRECTORY)
        .join(OVERLAY_SETTINGS_FILE_NAME)
}

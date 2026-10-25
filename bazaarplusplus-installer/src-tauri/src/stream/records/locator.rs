use anyhow::{anyhow, bail};
use std::path::{Path, PathBuf};

use crate::config::DATABASE_FILE_NAME;
use crate::services::game_path::find_fallback_game_path_with_database;
use crate::services::paths;

pub fn resolve_database_path(game_path: &Path) -> anyhow::Result<PathBuf> {
    let data_dir = paths::bpp_data_dir(game_path);
    if !data_dir.exists() {
        bail!(
            "BazaarPlusPlus data directory not found: {}",
            data_dir.display()
        );
    }

    let candidate = paths::database_path(game_path);
    if candidate.exists() {
        return Ok(candidate);
    }

    Err(anyhow!(
        "Expected stream database at {}, but {DATABASE_FILE_NAME} was not found.",
        candidate.display()
    ))
}

pub fn find_database_path_anywhere() -> anyhow::Result<PathBuf> {
    if let Some(game_path) = find_fallback_game_path_with_database() {
        return Ok(paths::database_path(&game_path));
    }
    Err(anyhow!(
        "{DATABASE_FILE_NAME} not found: game path is not configured and no known Steam library path contains it."
    ))
}

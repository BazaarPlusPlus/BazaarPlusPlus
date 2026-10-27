use anyhow::{anyhow, bail};
use std::path::{Path, PathBuf};

use crate::config::DATABASE_FILE_NAME;
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

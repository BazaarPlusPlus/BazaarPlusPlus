use std::path::{Path, PathBuf};

use crate::services::game_path::fallback_game_candidates;
use crate::services::paths;

pub fn resolve_database_path(game_path: &Path) -> Result<PathBuf, String> {
    let data_dir = paths::bpp_data_dir(game_path);
    if !data_dir.exists() {
        return Err(format!(
            "BazaarPlusPlus data directory not found: {}",
            data_dir.display()
        ));
    }

    let candidate = paths::database_path(game_path);
    if candidate.exists() {
        return Ok(candidate);
    }

    Err(format!(
        "Expected stream database at {}, but bazaarplusplus.db was not found.",
        candidate.display()
    ))
}

pub fn find_database_path_anywhere() -> Result<PathBuf, String> {
    for candidate in fallback_game_candidates() {
        let db = paths::database_path(&candidate);
        if db.exists() {
            return Ok(db);
        }
    }
    Err(
        "bazaarplusplus.db not found: game path is not configured and no known Steam library path contains it."
            .to_string(),
    )
}

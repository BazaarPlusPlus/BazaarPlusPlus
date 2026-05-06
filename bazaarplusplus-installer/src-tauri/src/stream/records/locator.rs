use std::path::{Path, PathBuf};

#[cfg(target_os = "windows")]
use crate::config::STEAM_LIBRARY_FALLBACK_CANDIDATES;
pub(crate) use crate::config::{
    BAZAAR_DATA_DIRECTORY as DATA_DIRECTORY, DATABASE_FILE_NAME, SCREENSHOTS_DIRECTORY,
};

pub fn resolve_database_path(game_path: &Path) -> Result<PathBuf, String> {
    let data_dir = game_path.join(DATA_DIRECTORY);
    if !data_dir.exists() {
        return Err(format!(
            "BazaarPlusPlus data directory not found: {}",
            data_dir.display()
        ));
    }

    let candidate = data_dir.join(DATABASE_FILE_NAME);
    if candidate.exists() {
        return Ok(candidate);
    }

    Err(format!(
        "Expected stream database at {}, but bazaarplusplus.db was not found.",
        candidate.display()
    ))
}

pub(super) fn find_database_path_anywhere() -> Result<PathBuf, String> {
    #[cfg(target_os = "windows")]
    {
        for candidate in STEAM_LIBRARY_FALLBACK_CANDIDATES {
            let db = PathBuf::from(candidate)
                .join(DATA_DIRECTORY)
                .join(DATABASE_FILE_NAME);
            if db.exists() {
                return Ok(db);
            }
        }
    }
    Err(
        "bazaarplusplus.db not found: game path is not configured and no known Steam library path contains it."
            .to_string(),
    )
}

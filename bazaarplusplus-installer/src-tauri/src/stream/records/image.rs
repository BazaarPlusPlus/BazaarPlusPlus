use std::path::PathBuf;

use crate::history::files::resolve_stored_path;
use crate::services::paths;

pub fn resolve_overlay_image_path(
    game_path: Option<PathBuf>,
    raw_path: Option<&str>,
) -> Option<PathBuf> {
    resolve_stored_path(&paths::screenshots_dir(&game_path?), raw_path?)
}

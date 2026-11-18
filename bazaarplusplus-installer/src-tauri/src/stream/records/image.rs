use std::path::PathBuf;

use crate::history::files::resolve_stored_path;
use crate::services::paths;

/// The screenshot a row names under the installation's Screenshots directory,
/// by the one stored-path rule of `StoredRelativePath`.
pub fn resolve_overlay_image_path(
    game_path: Option<PathBuf>,
    raw_path: Option<&str>,
) -> Option<PathBuf> {
    // Callers that need the file check it exists; this only builds the path.
    resolve_stored_path(&paths::screenshots_dir(&game_path?), raw_path?)
}

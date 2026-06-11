use std::path::{Path, PathBuf};

pub fn remove_video_file(root_dir: &Path, relative_path: &str) -> Result<(), String> {
    let Some(path) = resolve_data_file_path(root_dir, relative_path) else {
        return Ok(());
    };
    match std::fs::remove_file(&path) {
        Ok(()) => Ok(()),
        Err(err) if err.kind() == std::io::ErrorKind::NotFound => Ok(()),
        Err(err) => Err(format!("failed to remove video {}: {err}", path.display())),
    }
}

pub fn resolve_data_file_path(root_dir: &Path, raw_path: &str) -> Option<PathBuf> {
    let raw_path = raw_path.trim();
    if raw_path.is_empty() {
        return None;
    }

    let candidate = PathBuf::from(raw_path);
    if candidate.is_absolute() {
        return Some(candidate);
    }

    let mut normalized = PathBuf::new();
    for segment in raw_path.split(['/', '\\']) {
        let trimmed = segment.trim();
        if !trimmed.is_empty() && trimmed != "." && trimmed != ".." {
            normalized.push(trimmed);
        }
    }
    Some(root_dir.join(normalized))
}

pub fn resolve_screenshot_path(game_path: &Path, raw_path: &str) -> Option<PathBuf> {
    let raw_path = raw_path.trim();
    if raw_path.is_empty() {
        return None;
    }
    let candidate = PathBuf::from(raw_path);
    if candidate.is_absolute() {
        return Some(candidate);
    }
    resolve_data_file_path(
        &crate::services::paths::screenshots_dir(game_path),
        raw_path,
    )
}

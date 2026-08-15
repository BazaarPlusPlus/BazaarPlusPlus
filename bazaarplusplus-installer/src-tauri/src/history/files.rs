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

/// Resolver for delete operations: unlike `resolve_screenshot_path`, absolute
/// paths stored in the DB are refused so a malformed row can never point a
/// deletion outside the data directory.
pub fn resolve_cleanup_file_path(root_dir: &Path, raw_path: &str) -> Option<PathBuf> {
    let trimmed = raw_path.trim();
    if trimmed.is_empty() || PathBuf::from(trimmed).is_absolute() {
        return None;
    }
    let resolved = resolve_data_file_path(root_dir, trimmed)?;
    // Defense in depth: a Windows drive-relative segment like `C:evil.png` is
    // not `is_absolute()`, yet `PathBuf::join` treats its drive prefix as
    // replacing the root, so the resolved path can escape `root_dir`. Confirm
    // containment before any delete ever touches this path.
    resolved.starts_with(root_dir).then_some(resolved)
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

#[cfg(test)]
mod tests {
    use super::resolve_cleanup_file_path;

    #[test]
    fn cleanup_path_rejects_absolute_paths() {
        let root = std::path::Path::new("root");
        // is_absolute() is platform-specific: "/x" is not absolute on Windows.
        let absolute = if cfg!(windows) {
            r"C:\evil\shot.png"
        } else {
            "/evil/shot.png"
        };
        assert_eq!(resolve_cleanup_file_path(root, absolute), None);
        assert_eq!(resolve_cleanup_file_path(root, ""), None);
        assert_eq!(resolve_cleanup_file_path(root, "   "), None);
    }

    #[test]
    fn cleanup_path_never_resolves_outside_root() {
        // Whatever survives resolution must stay under root_dir. On Windows a
        // drive-relative segment like `C:evil.png` would otherwise escape via
        // join(); on Unix it is an ordinary filename that stays contained. The
        // invariant (contained or refused) must hold on every platform.
        let root = std::path::Path::new("root");
        for raw in [
            "C:evil.png",
            r"C:\evil.png",
            r"..\..\evil.png",
            "2026-06-15/shot.png",
        ] {
            if let Some(resolved) = resolve_cleanup_file_path(root, raw) {
                assert!(
                    resolved.starts_with(root),
                    "`{raw}` resolved outside root: {resolved:?}"
                );
            }
        }
        // On Windows the drive-relative form is a real escape and must be refused.
        #[cfg(windows)]
        assert_eq!(resolve_cleanup_file_path(root, "C:evil.png"), None);
    }

    #[test]
    fn cleanup_path_normalizes_separators_and_strips_traversal() {
        let root = std::path::Path::new("root");
        assert_eq!(
            resolve_cleanup_file_path(root, "2026-06-15\\shot.png"),
            Some(root.join("2026-06-15").join("shot.png"))
        );
        assert_eq!(
            resolve_cleanup_file_path(root, "../../2026-06-15/shot.png"),
            Some(root.join("2026-06-15").join("shot.png"))
        );
    }
}

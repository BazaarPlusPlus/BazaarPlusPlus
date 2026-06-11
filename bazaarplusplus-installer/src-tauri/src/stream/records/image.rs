use std::path::{Component, PathBuf};

use crate::services::paths;

pub fn resolve_overlay_image_path(
    game_path: Option<PathBuf>,
    raw_path: Option<&str>,
) -> Option<PathBuf> {
    let raw_path = raw_path?.trim();
    if raw_path.is_empty() {
        return None;
    }

    let candidate = PathBuf::from(raw_path);
    if candidate.is_absolute() {
        return None;
    }

    let game_path = game_path?;
    let screenshots_directory = paths::screenshots_dir(&game_path);
    let normalized_relative_path = normalized_relative_image_path(raw_path)?;
    let from_screenshots = Some(screenshots_directory.join(normalized_relative_path));
    if let Some(path) = from_screenshots.as_ref().filter(|path| path.exists()) {
        return Some(path.clone());
    }

    from_screenshots
}

fn normalized_relative_image_path(raw_path: &str) -> Option<PathBuf> {
    let mut normalized = PathBuf::new();
    for segment in raw_path.split(['/', '\\']) {
        let trimmed = segment.trim();
        if trimmed.is_empty() || trimmed == "." {
            continue;
        }
        if trimmed == ".." || PathBuf::from(trimmed).components().any(is_unsafe_component) {
            return None;
        }
        normalized.push(trimmed);
    }

    (!normalized.as_os_str().is_empty()).then_some(normalized)
}

fn is_unsafe_component(component: Component<'_>) -> bool {
    matches!(
        component,
        Component::ParentDir | Component::RootDir | Component::Prefix(_)
    )
}

#[cfg(test)]
mod tests {
    use super::resolve_overlay_image_path;

    #[test]
    fn resolve_overlay_image_path_rejects_absolute_inputs() {
        let temp_dir = tempfile::tempdir().unwrap();
        let game_path = temp_dir.path().join("TheBazaar");
        let absolute_input = temp_dir
            .path()
            .join("BazaarPlusPlusV4")
            .join("Screenshots")
            .join("match-2.png");

        let absolute =
            resolve_overlay_image_path(Some(game_path), Some(absolute_input.to_str().unwrap()));

        assert!(absolute.is_none());
    }

    #[test]
    fn resolve_overlay_image_path_supports_bazaarplusplus_screenshots_directory() {
        let temp_dir = tempfile::tempdir().unwrap();
        let game_path = temp_dir.path().join("TheBazaar");
        let screenshots_dir = game_path.join("BazaarPlusPlusV4").join("Screenshots");
        std::fs::create_dir_all(&screenshots_dir).unwrap();
        std::fs::write(screenshots_dir.join("match-1.png"), b"png").unwrap();

        let resolved = resolve_overlay_image_path(Some(game_path), Some("match-1.png")).unwrap();

        assert_eq!(resolved, screenshots_dir.join("match-1.png"));
    }

    #[test]
    fn resolve_overlay_image_path_normalizes_nested_backslash_relative_paths() {
        let temp_dir = tempfile::tempdir().unwrap();
        let game_path = temp_dir.path().join("TheBazaar");
        let screenshots_dir = game_path.join("BazaarPlusPlusV4").join("Screenshots");
        let dated_dir = screenshots_dir.join("2026-04-16");
        std::fs::create_dir_all(&dated_dir).unwrap();
        std::fs::write(dated_dir.join("match-1.png"), b"png").unwrap();

        let resolved =
            resolve_overlay_image_path(Some(game_path), Some(r"2026-04-16\match-1.png")).unwrap();

        assert_eq!(resolved, dated_dir.join("match-1.png"));
    }

    #[test]
    fn resolve_overlay_image_path_rejects_path_traversal() {
        let temp_dir = tempfile::tempdir().unwrap();
        let game_path = temp_dir.path().join("TheBazaar");

        let resolved =
            resolve_overlay_image_path(Some(game_path), Some(r"2026-04-16\..\secret.txt"));

        assert!(resolved.is_none());
    }
}

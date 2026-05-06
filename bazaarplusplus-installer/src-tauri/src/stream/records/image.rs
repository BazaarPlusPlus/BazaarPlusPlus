use std::path::PathBuf;

use super::locator::{DATA_DIRECTORY, SCREENSHOTS_DIRECTORY};

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
        return Some(candidate);
    }

    let game_path = game_path?;
    let screenshots_directory = game_path.join(DATA_DIRECTORY).join(SCREENSHOTS_DIRECTORY);
    let normalized_relative_path = normalized_relative_image_path(raw_path);
    let from_screenshots = Some(screenshots_directory.join(normalized_relative_path));
    if let Some(path) = from_screenshots.as_ref().filter(|path| path.exists()) {
        return Some(path.clone());
    }

    from_screenshots
}

fn normalized_relative_image_path(raw_path: &str) -> PathBuf {
    let mut normalized = PathBuf::new();
    for segment in raw_path.split(['/', '\\']) {
        let trimmed = segment.trim();
        if !trimmed.is_empty() {
            normalized.push(trimmed);
        }
    }

    normalized
}

#[cfg(test)]
mod tests {
    use super::resolve_overlay_image_path;
    use std::path::PathBuf;

    #[test]
    fn resolve_overlay_image_path_supports_relative_and_absolute_inputs() {
        let game_path = Some(PathBuf::from("/tmp/TheBazaar"));
        let relative = resolve_overlay_image_path(game_path.clone(), Some("match-1.png")).unwrap();
        let absolute = resolve_overlay_image_path(
            game_path,
            Some("/tmp/BazaarPlusPlus/Screenshots/match-2.png"),
        )
        .unwrap();

        assert_eq!(
            relative,
            PathBuf::from("/tmp/TheBazaar/BazaarPlusPlus/Screenshots/match-1.png")
        );
        assert_eq!(
            absolute,
            PathBuf::from("/tmp/BazaarPlusPlus/Screenshots/match-2.png")
        );
    }

    #[test]
    fn resolve_overlay_image_path_supports_bazaarplusplus_screenshots_directory() {
        let temp_dir = tempfile::tempdir().unwrap();
        let game_path = temp_dir.path().join("TheBazaar");
        let screenshots_dir = game_path.join("BazaarPlusPlus").join("Screenshots");
        std::fs::create_dir_all(&screenshots_dir).unwrap();
        std::fs::write(screenshots_dir.join("match-1.png"), b"png").unwrap();

        let resolved = resolve_overlay_image_path(Some(game_path), Some("match-1.png")).unwrap();

        assert_eq!(resolved, screenshots_dir.join("match-1.png"));
    }

    #[test]
    fn resolve_overlay_image_path_normalizes_nested_backslash_relative_paths() {
        let temp_dir = tempfile::tempdir().unwrap();
        let game_path = temp_dir.path().join("TheBazaar");
        let screenshots_dir = game_path.join("BazaarPlusPlus").join("Screenshots");
        let dated_dir = screenshots_dir.join("2026-04-16");
        std::fs::create_dir_all(&dated_dir).unwrap();
        std::fs::write(dated_dir.join("match-1.png"), b"png").unwrap();

        let resolved =
            resolve_overlay_image_path(Some(game_path), Some(r"2026-04-16\match-1.png")).unwrap();

        assert_eq!(resolved, dated_dir.join("match-1.png"));
    }
}

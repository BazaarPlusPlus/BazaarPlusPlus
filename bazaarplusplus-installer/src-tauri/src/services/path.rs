use std::path::PathBuf;

pub fn normalize_requested_game_path(game_path: Option<String>) -> Option<PathBuf> {
    game_path
        .as_deref()
        .map(str::trim)
        .filter(|path| !path.is_empty())
        .map(PathBuf::from)
}

#[cfg(test)]
mod tests {
    use super::normalize_requested_game_path;
    use std::path::PathBuf;

    #[test]
    fn normalize_requested_game_path_trims_blank_input() {
        assert_eq!(
            normalize_requested_game_path(Some("  D:\\Games\\The Bazaar  ".to_string())),
            Some(PathBuf::from("D:\\Games\\The Bazaar"))
        );
        assert_eq!(normalize_requested_game_path(Some("   ".to_string())), None);
        assert_eq!(normalize_requested_game_path(None), None);
    }
}

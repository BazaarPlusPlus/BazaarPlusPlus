use crate::commands::startup::InstallerContextState;
#[cfg(target_os = "windows")]
use crate::config::STEAM_LIBRARY_FALLBACK_CANDIDATES;
#[cfg(target_os = "windows")]
use crate::config::{BAZAAR_DATA_DIRECTORY, DATABASE_FILE_NAME};
use crate::stream::state::StreamRuntimeState;
use std::path::PathBuf;
use tauri::Manager;

pub fn normalize_requested_game_path(game_path: Option<String>) -> Option<PathBuf> {
    game_path
        .as_deref()
        .map(str::trim)
        .filter(|path| !path.is_empty())
        .map(PathBuf::from)
}

/// Resolve the game directory using the standard fallback chain.
///
/// 1. Full Steam-aware environment detection. Detect errors are swallowed and
///    treated as "not detected" so we still try the remaining fallbacks.
/// 2. The user's manually-supplied path (if any).
/// 3. The path cached when the stream service last started — only consulted
///    when `state` is provided (the server itself doesn't have one yet at start).
/// 4. Well-known Windows Steam library paths that contain the BPP database.
pub fn resolve_game_path_with_fallback(
    app: &tauri::AppHandle,
    state: Option<&StreamRuntimeState>,
    requested_game_path: Option<String>,
) -> Option<PathBuf> {
    let context_state = app.state::<InstallerContextState>();
    if let Ok(env) = crate::commands::detect::detect_environment(
        app.clone(),
        context_state,
        requested_game_path.clone(),
    ) {
        if let Some(path) = env.game_path.map(PathBuf::from) {
            return Some(path);
        }
    }

    if let Some(path) = normalize_requested_game_path(requested_game_path) {
        return Some(path);
    }

    if let Some(state) = state {
        if let Some(path) = state.get_game_path() {
            return Some(path);
        }
    }

    #[cfg(target_os = "windows")]
    {
        for candidate in STEAM_LIBRARY_FALLBACK_CANDIDATES {
            let path = PathBuf::from(candidate);
            let db = path.join(BAZAAR_DATA_DIRECTORY).join(DATABASE_FILE_NAME);
            if db.exists() {
                return Some(path);
            }
        }
    }

    None
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

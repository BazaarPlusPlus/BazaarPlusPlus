#[cfg(target_os = "windows")]
use crate::config::STEAM_LIBRARY_FALLBACK_CANDIDATES;
use crate::services::path::normalize_requested_game_path;
use crate::services::paths;
use crate::services::startup::InstallerContextState;
use std::path::PathBuf;
use tauri::Manager;

#[derive(Clone, Debug, PartialEq, Eq)]
pub enum GamePathSource {
    Detection,
    Requested,
    Session,
    Fallback,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct GamePathResolution {
    pub game_path: PathBuf,
    pub database_path: Option<PathBuf>,
    pub source: GamePathSource,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum GamePathRequirement {
    Any,
    DatabaseExists,
}

/// Resolve the game directory using the standard fallback chain.
///
/// 1. Full Steam-aware environment detection (errors are swallowed).
/// 2. The user's manually-supplied path (if any).
/// 3. Optional stream session path hint from the last started overlay service.
/// 4. Well-known Steam library paths that contain the BPP database.
pub fn resolve_game_path_with_source(
    app: &tauri::AppHandle,
    requested_game_path: Option<String>,
    session_game_path: Option<PathBuf>,
) -> Option<GamePathResolution> {
    resolve_game_path_matching(
        app,
        requested_game_path,
        session_game_path,
        GamePathRequirement::Any,
    )
}

pub fn resolve_game_path_with_database(
    app: &tauri::AppHandle,
    requested_game_path: Option<String>,
    session_game_path: Option<PathBuf>,
) -> Option<GamePathResolution> {
    resolve_game_path_matching(
        app,
        requested_game_path,
        session_game_path,
        GamePathRequirement::DatabaseExists,
    )
}

fn resolve_game_path_matching(
    app: &tauri::AppHandle,
    requested_game_path: Option<String>,
    session_game_path: Option<PathBuf>,
    requirement: GamePathRequirement,
) -> Option<GamePathResolution> {
    let context_state = app.state::<InstallerContextState>();
    if let Ok(env) = crate::services::detect::detect_environment(
        app.clone(),
        context_state,
        requested_game_path.clone(),
    ) {
        if let Some(path) = env.game_path.map(PathBuf::from) {
            if let Some(value) = resolution(path, GamePathSource::Detection, requirement) {
                return Some(value);
            }
        }
    }

    if let Some(path) = normalize_requested_game_path(requested_game_path) {
        if let Some(value) = resolution(path, GamePathSource::Requested, requirement) {
            return Some(value);
        }
    }

    if let Some(path) = session_game_path {
        if let Some(value) = resolution(path, GamePathSource::Session, requirement) {
            return Some(value);
        }
    }

    for path in fallback_game_candidates() {
        let db = paths::database_path(&path);
        if db.exists() {
            return Some(GamePathResolution {
                game_path: path,
                database_path: Some(db),
                source: GamePathSource::Fallback,
            });
        }
    }

    None
}

fn resolution(
    game_path: PathBuf,
    source: GamePathSource,
    requirement: GamePathRequirement,
) -> Option<GamePathResolution> {
    let database_path = paths::database_path(&game_path);
    let database_path = database_path.exists().then_some(database_path);
    if requirement == GamePathRequirement::DatabaseExists && database_path.is_none() {
        return None;
    }

    Some(GamePathResolution {
        game_path,
        database_path,
        source,
    })
}

pub(crate) fn fallback_game_candidates() -> Vec<PathBuf> {
    let mut candidates = Vec::new();

    #[cfg(target_os = "macos")]
    {
        if let Some(home) = dirs::home_dir() {
            push_unique(
                &mut candidates,
                home.join("Library/Application Support/Tempo Launcher - Beta/game/buildx64"),
            );
            push_unique(
                &mut candidates,
                home.join("Library/Application Support/Tempo Launcher - Beta/game"),
            );
        }

        if let Some(path) = dirs::home_dir()
            .map(|home| home.join("Library/Application Support/Steam/steamapps/common/The Bazaar"))
        {
            push_unique(&mut candidates, path);
        }
    }

    #[cfg(target_os = "windows")]
    {
        for var_name in ["APPDATA", "LOCALAPPDATA"] {
            if let Some(base) = std::env::var_os(var_name).map(PathBuf::from) {
                push_unique(
                    &mut candidates,
                    base.join("Tempo Launcher - Beta")
                        .join("game")
                        .join("buildx64"),
                );
            }
        }

        use windows::Win32::Storage::FileSystem::GetLogicalDrives;

        // Only probe drive letters that actually exist. The previous
        // unconditional C..=Z scan issued ~48 `stat`s, and probing a
        // disconnected/removable drive can stall for seconds on Windows.
        let present = present_drive_letters(unsafe { GetLogicalDrives() });
        for drive in present.into_iter().filter(|letter| *letter >= 'C') {
            for root in ["Steam", "SteamLibrary"] {
                push_unique(
                    &mut candidates,
                    PathBuf::from(format!("{drive}:\\{root}\\steamapps\\common\\The Bazaar")),
                );
            }
        }

        for candidate in STEAM_LIBRARY_FALLBACK_CANDIDATES {
            push_unique(&mut candidates, PathBuf::from(candidate));
        }
    }

    candidates
}

fn push_unique(paths: &mut Vec<PathBuf>, path: PathBuf) {
    if !paths.iter().any(|existing| existing == &path) {
        paths.push(path);
    }
}

/// Parse the bitmask returned by `GetLogicalDrives` (bit 0 = `A:`, ...,
/// bit 25 = `Z:`) into the drive letters that currently exist. Kept pure so
/// the bit math is unit-testable without the Win32 call.
#[cfg_attr(not(any(target_os = "windows", test)), allow(dead_code))]
fn present_drive_letters(bitmask: u32) -> Vec<char> {
    ('A'..='Z')
        .enumerate()
        .filter(|(index, _)| bitmask & (1 << index) != 0)
        .map(|(_, letter)| letter)
        .collect()
}

#[cfg(test)]
mod tests {
    use super::present_drive_letters;

    #[test]
    fn present_drive_letters_parses_c_and_d() {
        // bit2 (C:) + bit3 (D:)
        assert_eq!(present_drive_letters(0b1100), vec!['C', 'D']);
    }

    #[test]
    fn present_drive_letters_is_empty_when_no_bits_set() {
        assert!(present_drive_letters(0).is_empty());
    }

    #[test]
    fn present_drive_letters_parses_a_and_z_extremes() {
        // bit0 (A:) + bit25 (Z:) guards the enumerate/shift bounds.
        assert_eq!(present_drive_letters(0b1 | (1 << 25)), vec!['A', 'Z']);
    }
}

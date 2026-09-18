use crate::services::vdf::THE_BAZAAR_APP_ID;
use keyvalues_parser::{Obj, Parser, Value};
use std::path::{Path, PathBuf};

#[derive(Debug, Clone)]
pub(crate) struct SteamInstallPaths {
    pub(crate) steam_path: Option<PathBuf>,
    pub(crate) game_path: Option<PathBuf>,
}

#[cfg(debug_assertions)]
fn debug_paths_label(paths: &[PathBuf]) -> Vec<String> {
    paths
        .iter()
        .map(|path| path.display().to_string())
        .collect()
}

fn first_obj<'a, 'text>(values: &'a [Value<'text>]) -> Option<&'a Obj<'text>>
where
    'a: 'text,
{
    values.first()?.get_obj()
}

fn first_str<'a, 'text>(values: &'a [Value<'text>]) -> Option<&'a str>
where
    'a: 'text,
{
    values.first()?.get_str()
}

fn library_has_app(folder: &Obj<'_>, app_id: &str) -> bool {
    folder
        .get("apps")
        .and_then(|values| first_obj(values))
        .map(|apps| apps.contains_key(app_id))
        .unwrap_or(false)
}

fn parse_library_folders(vdf_content: &str, app_id: &str) -> Option<Vec<(String, bool)>> {
    let parsed = Parser::new()
        .literal_special_chars(true)
        .parse(vdf_content)
        .ok()?;
    let libraries = parsed.value.get_obj()?;
    let mut folders = Vec::new();

    for values in libraries.values() {
        let Some(folder) = first_obj(values) else {
            continue;
        };
        let Some(library_path) = folder.get("path").and_then(|values| first_str(values)) else {
            continue;
        };

        folders.push((library_path.to_string(), library_has_app(folder, app_id)));
    }

    (!folders.is_empty()).then_some(folders)
}

fn candidate_steam_paths() -> Vec<PathBuf> {
    let mut candidates = Vec::new();

    #[cfg(target_os = "macos")]
    {
        if let Some(path) =
            dirs::home_dir().map(|home| home.join("Library/Application Support/Steam"))
        {
            if path.exists() {
                candidates.push(path);
            }
        }
    }

    #[cfg(target_os = "windows")]
    {
        use std::collections::HashSet;
        use winreg::enums::{HKEY_CURRENT_USER, HKEY_LOCAL_MACHINE};
        use winreg::RegKey;

        let mut seen = HashSet::new();
        let registry_candidates = [
            (HKEY_CURRENT_USER, r"Software\Valve\Steam", "SteamPath"),
            (HKEY_CURRENT_USER, r"Software\Valve\Steam", "InstallPath"),
            (HKEY_LOCAL_MACHINE, r"Software\Valve\Steam", "InstallPath"),
            (HKEY_LOCAL_MACHINE, r"Software\Valve\Steam", "SteamPath"),
            (
                HKEY_LOCAL_MACHINE,
                r"Software\WOW6432Node\Valve\Steam",
                "InstallPath",
            ),
            (
                HKEY_LOCAL_MACHINE,
                r"Software\WOW6432Node\Valve\Steam",
                "SteamPath",
            ),
        ];

        for (root, key_path, value_name) in registry_candidates {
            let root_key = RegKey::predef(root);
            if let Ok(key) = root_key.open_subkey(key_path) {
                if let Ok(path) = key.get_value::<String, _>(value_name) {
                    let path = PathBuf::from(path.trim());
                    if path.exists() && seen.insert(path.clone()) {
                        candidates.push(path);
                    }
                }
            }
        }

        let default_candidates = [
            std::env::var_os("ProgramFiles(x86)")
                .map(PathBuf::from)
                .map(|path| path.join("Steam")),
            std::env::var_os("ProgramFiles")
                .map(PathBuf::from)
                .map(|path| path.join("Steam")),
            Some(PathBuf::from(r"C:\Program Files (x86)\Steam")),
            Some(PathBuf::from(r"C:\Program Files\Steam")),
        ];

        for candidate in default_candidates.into_iter().flatten() {
            if candidate.exists() && seen.insert(candidate.clone()) {
                candidates.push(candidate);
            }
        }
    }

    crate::services::debug_log!(
        "[detect::steam] steam root candidates={:?}",
        debug_paths_label(&candidates)
    );

    candidates
}

fn get_game_path_from_single_steam_root(steam_path: &Path) -> Option<PathBuf> {
    crate::services::debug_log!(
        "[detect::steam] probing steam root={}",
        steam_path.display()
    );

    if let Some(path) = get_game_path_from_vdf(steam_path) {
        crate::services::debug_log!(
            "[detect::steam] hit from libraryfolders.vdf root={} game_path={}",
            steam_path.display(),
            path.display()
        );
        return Some(path);
    }

    let candidate = steam_path.join("steamapps/common/The Bazaar");
    if candidate.exists() {
        crate::services::debug_log!(
            "[detect::steam] hit from default steam library root={} game_path={}",
            steam_path.display(),
            candidate.display()
        );
        return Some(candidate);
    }

    crate::services::debug_log!(
        "[detect::steam] miss for steam root={}",
        steam_path.display()
    );
    None
}

fn get_game_path_from_detected_steam_roots(
    primary_steam_root: &Path,
    candidate_roots: &[PathBuf],
) -> Option<PathBuf> {
    let mut steam_roots = vec![primary_steam_root.to_path_buf()];
    for candidate in candidate_roots {
        if !steam_roots.contains(candidate) {
            steam_roots.push(candidate.clone());
        }
    }

    crate::services::debug_log!(
        "[detect::steam] ordered steam roots for game lookup={:?}",
        debug_paths_label(&steam_roots)
    );

    if let Some(path) = steam_roots
        .iter()
        .find_map(|root| get_game_path_from_single_steam_root(root))
    {
        return Some(path);
    }

    #[cfg(target_os = "windows")]
    {
        if let Some(path) = crate::services::game_path::find_existing_fallback_game_path() {
            crate::services::debug_log!(
                "[detect::steam] hit from common Windows candidate game_path={}",
                path.display()
            );
            return Some(path);
        }
    }

    crate::services::debug_log!("[detect::steam] failed to resolve game path");
    None
}

pub(crate) fn detect_installation_paths() -> SteamInstallPaths {
    let steam_roots = candidate_steam_paths();
    let steam_path = steam_roots.first().cloned();
    let game_path = steam_path
        .as_deref()
        .and_then(|path| get_game_path_from_detected_steam_roots(path, &steam_roots));
    crate::services::debug_log!(
        "[detect::steam] detected startup paths steam_path={:?} game_path={:?}",
        steam_path.as_ref().map(|path| path.display().to_string()),
        game_path.as_ref().map(|path| path.display().to_string())
    );

    SteamInstallPaths {
        steam_path,
        game_path,
    }
}

fn get_game_path_from_vdf(steam_path: &Path) -> Option<PathBuf> {
    let library_vdf_path = steam_path.join("steamapps/libraryfolders.vdf");
    let library_vdf = match std::fs::read_to_string(&library_vdf_path) {
        Ok(content) => content,
        Err(_error) => {
            crate::services::debug_log!(
                "[detect::steam] cannot read libraryfolders.vdf path={} error={}",
                library_vdf_path.display(),
                _error
            );
            return None;
        }
    };

    let parsed_folders = match parse_library_folders(&library_vdf, THE_BAZAAR_APP_ID) {
        Some(folders) => folders,
        None => {
            crate::services::debug_log!(
                "[detect::steam] failed to parse libraryfolders.vdf path={}",
                library_vdf_path.display()
            );
            return None;
        }
    };

    crate::services::debug_log!(
        "[detect::steam] parsed libraryfolders path={} folders={:?}",
        library_vdf_path.display(),
        parsed_folders
            .iter()
            .map(|(path, has_app)| format!("{path} [has_app={has_app}]"))
            .collect::<Vec<_>>()
    );

    // The library that declares the app wins; every other parsed library stays a
    // fallback for installs Steam has not recorded under `apps`.
    parsed_folders
        .iter()
        .filter(|(_, has_app)| *has_app)
        .chain(parsed_folders.iter().filter(|(_, has_app)| !*has_app))
        .map(|(library_root, _)| PathBuf::from(library_root).join("steamapps/common/The Bazaar"))
        .find(|candidate| candidate.exists())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_parse_library_folders_marks_the_library_declaring_the_app() {
        let vdf = r#"
"libraryfolders"
{
    "0"
    {
        "path"      "C:\Program Files (x86)\Steam"
        "apps"
        {
            "730"   "1"
        }
    }
    "1"
    {
        "path"      "D:\SteamLibrary"
        "apps"
        {
            "1617400"   "1"
        }
    }
}"#;

        let folders = parse_library_folders(vdf, THE_BAZAAR_APP_ID).expect("parsed folders");

        assert_eq!(
            folders,
            vec![
                (r"C:\Program Files (x86)\Steam".to_string(), false),
                (r"D:\SteamLibrary".to_string(), true)
            ]
        );
    }

    #[test]
    fn test_parse_library_folders_reports_no_app_when_the_id_is_missing() {
        let vdf = r#"
"libraryfolders"
{
    "0"
    {
        "path"      "C:\Program Files (x86)\Steam"
        "apps"
        {
            "730"   "1"
        }
    }
}"#;

        let folders = parse_library_folders(vdf, THE_BAZAAR_APP_ID).expect("parsed folders");

        assert!(folders.iter().all(|(_, has_app)| !has_app));
    }

    #[test]
    fn test_parse_library_folders_keeps_paths_without_app_listing() {
        let vdf = r#"
"libraryfolders"
{
    "0"
    {
        "path"      "C:\Program Files (x86)\Steam"
    }
    "1"
    {
        "path"      "D:\SteamLibrary"
        "apps"
        {
            "1617400"   "1"
        }
    }
}"#;

        let folders = parse_library_folders(vdf, THE_BAZAAR_APP_ID).expect("parsed folders");

        assert_eq!(
            folders,
            vec![
                (r"C:\Program Files (x86)\Steam".to_string(), false),
                (r"D:\SteamLibrary".to_string(), true)
            ]
        );
    }

    #[test]
    fn test_get_game_path_from_vdf_falls_back_to_existing_library_folder() {
        let tmp = tempfile::tempdir().unwrap();
        let steam_root = tmp.path().join("Steam");
        let library_root = tmp.path().join("Library");
        let steamapps_dir = steam_root.join("steamapps");
        let game_dir = library_root.join("steamapps/common/The Bazaar");

        std::fs::create_dir_all(&steamapps_dir).unwrap();
        std::fs::create_dir_all(&game_dir).unwrap();

        let library_root_string = library_root.to_string_lossy().replace('\\', "\\\\");
        let vdf = format!(
            "\"libraryfolders\"\n{{\n    \"0\"\n    {{\n        \"path\"      \"{library_root_string}\"\n    }}\n}}"
        );
        std::fs::write(steamapps_dir.join("libraryfolders.vdf"), vdf).unwrap();

        let path = get_game_path_from_vdf(&steam_root);

        assert_eq!(path, Some(game_dir));
    }

    #[test]
    fn test_get_game_path_from_vdf_prefers_the_library_declaring_the_app() {
        let tmp = tempfile::tempdir().unwrap();
        let steam_root = tmp.path().join("Steam");
        let listed_first = tmp.path().join("ListedFirst");
        let declaring = tmp.path().join("Declaring");
        let steamapps_dir = steam_root.join("steamapps");
        let declared_game_dir = declaring.join("steamapps/common/The Bazaar");

        std::fs::create_dir_all(&steamapps_dir).unwrap();
        std::fs::create_dir_all(listed_first.join("steamapps/common/The Bazaar")).unwrap();
        std::fs::create_dir_all(&declared_game_dir).unwrap();

        let listed_first_string = listed_first.to_string_lossy().replace('\\', "\\\\");
        let declaring_string = declaring.to_string_lossy().replace('\\', "\\\\");
        let vdf = format!(
            "\"libraryfolders\"\n{{\n    \"0\"\n    {{\n        \"path\"      \"{listed_first_string}\"\n    }}\n    \"1\"\n    {{\n        \"path\"      \"{declaring_string}\"\n        \"apps\"\n        {{\n            \"{THE_BAZAAR_APP_ID}\"   \"1\"\n        }}\n    }}\n}}"
        );
        std::fs::write(steamapps_dir.join("libraryfolders.vdf"), vdf).unwrap();

        let path = get_game_path_from_vdf(&steam_root);

        assert_eq!(path, Some(declared_game_dir));
    }

    #[test]
    fn test_detected_steam_roots_try_the_secondary_root() {
        let tmp = tempfile::tempdir().unwrap();
        let primary_root = tmp.path().join("PrimarySteam");
        let secondary_root = tmp.path().join("SecondarySteam");
        let game_dir = secondary_root.join("steamapps/common/The Bazaar");

        std::fs::create_dir_all(primary_root.join("steamapps")).unwrap();
        std::fs::create_dir_all(&game_dir).unwrap();

        let path = get_game_path_from_detected_steam_roots(
            &primary_root,
            &[primary_root.clone(), secondary_root],
        );

        assert_eq!(path, Some(game_dir));
    }
}

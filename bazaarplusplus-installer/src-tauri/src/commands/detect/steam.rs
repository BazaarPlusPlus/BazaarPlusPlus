use keyvalues_parser::{Obj, Parser, Value};
use std::path::{Path, PathBuf};

#[derive(Debug, Clone)]
pub(crate) struct SteamInstallPaths {
    pub(crate) steam_path: Option<PathBuf>,
    pub(crate) game_path: Option<PathBuf>,
    pub(crate) steam_launch_options_supported: bool,
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

fn bazaar_app_id() -> &'static str {
    "1617400"
}

fn find_game_in_library_vdf(vdf_content: &str, app_id: &str) -> Option<String> {
    parse_library_folders(vdf_content, app_id)?
        .into_iter()
        .find_map(|(library_path, has_app)| has_app.then_some(library_path))
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

    crate::commands::debug_log!(
        "[detect::steam] steam root candidates={:?}",
        debug_paths_label(&candidates)
    );

    candidates
}

fn get_game_path_from_single_steam_root(steam_path: &Path) -> Option<PathBuf> {
    crate::commands::debug_log!(
        "[detect::steam] probing steam root={}",
        steam_path.display()
    );

    if let Some(path) = get_game_path_from_vdf(steam_path) {
        crate::commands::debug_log!(
            "[detect::steam] hit from libraryfolders.vdf root={} game_path={}",
            steam_path.display(),
            path.display()
        );
        return Some(path);
    }

    let candidate = steam_path.join("steamapps/common/The Bazaar");
    if candidate.exists() {
        crate::commands::debug_log!(
            "[detect::steam] hit from default steam library root={} game_path={}",
            steam_path.display(),
            candidate.display()
        );
        return Some(candidate);
    }

    crate::commands::debug_log!(
        "[detect::steam] miss for steam root={}",
        steam_path.display()
    );
    None
}

#[cfg(target_os = "windows")]
fn windows_common_game_candidates() -> Vec<PathBuf> {
    let mut candidates = Vec::new();

    for drive in 'C'..='Z' {
        for root in ["Steam", "SteamLibrary"] {
            candidates.push(PathBuf::from(format!(
                "{drive}:\\{root}\\steamapps\\common\\The Bazaar"
            )));
        }
    }

    for candidate in crate::config::STEAM_LIBRARY_FALLBACK_CANDIDATES {
        candidates.push(PathBuf::from(candidate));
    }

    candidates
}

fn get_game_path_from_steam_roots<I>(steam_roots: I) -> Option<PathBuf>
where
    I: IntoIterator<Item = PathBuf>,
{
    for root in steam_roots {
        if let Some(path) = get_game_path_from_single_steam_root(&root) {
            return Some(path);
        }
    }

    None
}

fn ordered_steam_roots(primary_steam_root: &Path, candidate_roots: &[PathBuf]) -> Vec<PathBuf> {
    let mut steam_roots = vec![primary_steam_root.to_path_buf()];
    for candidate in candidate_roots {
        if !steam_roots.iter().any(|existing| existing == candidate) {
            steam_roots.push(candidate.clone());
        }
    }

    steam_roots
}

fn get_game_path_from_detected_steam_roots(
    primary_steam_root: &Path,
    candidate_roots: &[PathBuf],
) -> Option<PathBuf> {
    let steam_roots = ordered_steam_roots(primary_steam_root, candidate_roots);

    crate::commands::debug_log!(
        "[detect::steam] ordered steam roots for game lookup={:?}",
        debug_paths_label(&steam_roots)
    );

    if let Some(path) = get_game_path_from_steam_roots(steam_roots) {
        return Some(path);
    }

    #[cfg(target_os = "windows")]
    {
        crate::commands::debug_log!(
            "[detect::steam] probing common Windows candidates count={}",
            windows_common_game_candidates().len()
        );
        for path in windows_common_game_candidates() {
            if path.exists() {
                crate::commands::debug_log!(
                    "[detect::steam] hit from common Windows candidate game_path={}",
                    path.display()
                );
                return Some(path);
            }
        }
    }

    crate::commands::debug_log!("[detect::steam] failed to resolve game path");
    None
}

pub(crate) fn detect_installation_paths() -> SteamInstallPaths {
    let steam_roots = candidate_steam_paths();
    let steam_path = steam_roots.first().cloned();
    let game_path = steam_path
        .as_deref()
        .and_then(|path| get_game_path_from_detected_steam_roots(path, &steam_roots));
    let steam_launch_options_supported = steam_path
        .as_deref()
        .map(crate::commands::steam::supports_launch_option_updates)
        .unwrap_or(false);

    crate::commands::debug_log!(
        "[detect::steam] detected startup paths steam_path={:?} game_path={:?} launch_options_supported={}",
        steam_path.as_ref().map(|path| path.display().to_string()),
        game_path.as_ref().map(|path| path.display().to_string()),
        steam_launch_options_supported
    );

    SteamInstallPaths {
        steam_path,
        game_path,
        steam_launch_options_supported,
    }
}

fn get_game_path_from_vdf(steam_path: &Path) -> Option<PathBuf> {
    let library_vdf_path = steam_path.join("steamapps/libraryfolders.vdf");
    let library_vdf = match std::fs::read_to_string(&library_vdf_path) {
        Ok(content) => content,
        Err(_error) => {
            crate::commands::debug_log!(
                "[detect::steam] cannot read libraryfolders.vdf path={} error={}",
                library_vdf_path.display(),
                _error
            );
            return None;
        }
    };

    let parsed_folders = match parse_library_folders(&library_vdf, bazaar_app_id()) {
        Some(folders) => folders,
        None => {
            crate::commands::debug_log!(
                "[detect::steam] failed to parse libraryfolders.vdf path={}",
                library_vdf_path.display()
            );
            return None;
        }
    };

    crate::commands::debug_log!(
        "[detect::steam] parsed libraryfolders path={} folders={:?}",
        library_vdf_path.display(),
        parsed_folders
            .iter()
            .map(|(path, has_app)| format!("{path} [has_app={has_app}]"))
            .collect::<Vec<_>>()
    );

    if let Some(library_root) = find_game_in_library_vdf(&library_vdf, bazaar_app_id()) {
        let candidate = PathBuf::from(&library_root).join("steamapps/common/The Bazaar");
        if candidate.exists() {
            return Some(candidate);
        }
    }

    for (library_root, _has_app) in parsed_folders {
        let candidate = PathBuf::from(library_root).join("steamapps/common/The Bazaar");
        if candidate.exists() {
            return Some(candidate);
        }
    }

    None
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_find_game_in_library_vdf_returns_matching_library_path() {
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

        let path = find_game_in_library_vdf(vdf, "1617400");

        assert_eq!(path.as_deref(), Some(r"D:\SteamLibrary"));
    }

    #[test]
    fn test_find_game_in_library_vdf_returns_none_when_app_missing() {
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

        let path = find_game_in_library_vdf(vdf, "1617400");

        assert_eq!(path, None);
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

        let folders = parse_library_folders(vdf, bazaar_app_id()).expect("parsed folders");

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
    fn test_get_game_path_from_steam_roots_tries_secondary_root() {
        let tmp = tempfile::tempdir().unwrap();
        let primary_root = tmp.path().join("PrimarySteam");
        let secondary_root = tmp.path().join("SecondarySteam");
        let game_dir = secondary_root.join("steamapps/common/The Bazaar");

        std::fs::create_dir_all(primary_root.join("steamapps")).unwrap();
        std::fs::create_dir_all(&game_dir).unwrap();

        let path = get_game_path_from_steam_roots(vec![primary_root, secondary_root]);

        assert_eq!(path, Some(game_dir));
    }
}

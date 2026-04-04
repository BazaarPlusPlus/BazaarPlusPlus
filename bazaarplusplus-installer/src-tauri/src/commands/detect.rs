use keyvalues_parser::{Obj, Parser, Value};
use serde::{Deserialize, Serialize};
use std::path::{Path, PathBuf};
use std::process::Command;
use tauri::AppHandle;

#[cfg(target_os = "windows")]
use std::os::windows::process::CommandExt;

#[cfg(target_os = "windows")]
const CREATE_NO_WINDOW: u32 = 0x08000000;

#[derive(Debug, Serialize, Deserialize)]
pub struct EnvironmentInfo {
    pub steam_path: Option<String>,
    pub steam_launch_options_supported: bool,
    pub game_path: Option<String>,
    pub dotnet_version: Option<String>,
    pub dotnet_ok: bool,
    pub bepinex_installed: bool,
    pub bpp_version: Option<String>,
    pub bundled_bpp_version: Option<String>,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct DotnetInfo {
    pub dotnet_version: Option<String>,
    pub dotnet_ok: bool,
}

#[tauri::command]
pub fn detect_environment(
    app: AppHandle,
    game_path: Option<String>,
) -> Result<EnvironmentInfo, String> {
    let steam_path = get_steam_path();
    let requested_game_path = normalize_game_path(game_path);
    let game_path = resolve_game_path(steam_path.as_deref(), requested_game_path.as_deref());
    let steam_launch_options_supported = steam_path
        .as_deref()
        .map(crate::commands::steam::supports_launch_option_updates)
        .unwrap_or(false);
    let bpp_version = game_path
        .as_ref()
        .and_then(|path| read_installed_bpp_version(path));
    let bundled_bpp_version = crate::commands::bepinex::read_bundled_bpp_version(&app)
        .ok()
        .flatten();
    let bepinex_installed = game_path
        .as_ref()
        .map(|path| is_bepinex_installed(path))
        .unwrap_or(false);

    Ok(EnvironmentInfo {
        steam_path: steam_path.map(|path| path.to_string_lossy().into_owned()),
        steam_launch_options_supported,
        game_path: game_path.map(|path| path.to_string_lossy().into_owned()),
        dotnet_version: None,
        dotnet_ok: false,
        bepinex_installed,
        bpp_version,
        bundled_bpp_version,
    })
}

fn normalize_game_path(game_path: Option<String>) -> Option<PathBuf> {
    game_path
        .as_deref()
        .map(str::trim)
        .filter(|path| !path.is_empty())
        .map(PathBuf::from)
}

fn resolve_game_path(
    steam_path: Option<&Path>,
    requested_game_path: Option<&Path>,
) -> Option<PathBuf> {
    requested_game_path
        .map(Path::to_path_buf)
        .or_else(|| steam_path.and_then(get_game_path))
}

#[tauri::command]
pub async fn detect_dotnet_runtime() -> Result<DotnetInfo, String> {
    tauri::async_runtime::spawn_blocking(|| {
        let (dotnet_version, dotnet_ok) = detect_dotnet();
        DotnetInfo {
            dotnet_version,
            dotnet_ok,
        }
    })
    .await
    .map_err(|err| format!("failed to detect .NET runtime: {err}"))
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

pub fn find_game_in_library_vdf(vdf_content: &str, app_id: &str) -> Option<String> {
    let parsed = Parser::new()
        .literal_special_chars(true)
        .parse(vdf_content)
        .ok()?;
    let libraries = parsed.value.get_obj()?;

    for values in libraries.values() {
        let Some(folder) = first_obj(values) else {
            continue;
        };
        let Some(library_path) = folder.get("path").and_then(|values| first_str(values)) else {
            continue;
        };

        if library_has_app(folder, app_id) {
            return Some(library_path.to_string());
        }
    }

    None
}

pub fn parse_dotnet_runtimes(output: &str) -> Option<String> {
    output
        .lines()
        .filter(|line| line.starts_with("Microsoft.NETCore.App "))
        .filter_map(|line| line.split_whitespace().nth(1))
        .filter(|version| is_supported_dotnet_version(version))
        .map(str::to_string)
        .max_by(|a, b| parse_version_tuple(a).cmp(&parse_version_tuple(b)))
}

fn parse_version_tuple(v: &str) -> (u32, u32, u32) {
    let mut parts = v.split('.').filter_map(|p| p.parse::<u32>().ok());
    (
        parts.next().unwrap_or(0),
        parts.next().unwrap_or(0),
        parts.next().unwrap_or(0),
    )
}

fn is_supported_dotnet_version(version: &str) -> bool {
    version
        .split('.')
        .next()
        .and_then(|major| major.parse::<u32>().ok())
        .map(|major| major >= 6)
        .unwrap_or(false)
}

fn get_steam_path() -> Option<PathBuf> {
    #[cfg(target_os = "macos")]
    {
        let path = dirs::home_dir()?.join("Library/Application Support/Steam");
        if path.exists() {
            return Some(path);
        }
    }

    #[cfg(target_os = "windows")]
    {
        use winreg::enums::HKEY_CURRENT_USER;
        use winreg::RegKey;

        let hkcu = RegKey::predef(HKEY_CURRENT_USER);
        if let Ok(key) = hkcu.open_subkey(r"Software\Valve\Steam") {
            if let Ok(path) = key.get_value::<String, _>("SteamPath") {
                let path = PathBuf::from(path);
                if path.exists() {
                    return Some(path);
                }
            }
        }
    }

    None
}

fn get_game_path(steam_path: &Path) -> Option<PathBuf> {
    let library_vdf =
        std::fs::read_to_string(steam_path.join("steamapps/libraryfolders.vdf")).ok()?;
    let library_root = find_game_in_library_vdf(&library_vdf, "1617400")?;
    let candidate = PathBuf::from(library_root).join("steamapps/common/The Bazaar");
    candidate.exists().then_some(candidate)
}

pub(crate) fn is_bepinex_installed(game_path: &Path) -> bool {
    if !game_path
        .join("BepInEx/core/BepInEx.Preloader.dll")
        .exists()
    {
        return false;
    }

    #[cfg(target_os = "macos")]
    return game_path.join("run_bepinex.sh").exists()
        && game_path.join("libdoorstop.dylib").exists();

    #[cfg(target_os = "windows")]
    return game_path.join("doorstop_config.ini").exists()
        && game_path.join("winhttp.dll").exists();

    #[cfg(not(any(target_os = "macos", target_os = "windows")))]
    return true;
}

pub(crate) fn read_installed_bpp_version(game_path: &Path) -> Option<String> {
    let version_path = game_path.join("BepInEx/plugins/BazaarPlusPlus.version");
    let version = std::fs::read_to_string(version_path).ok()?;
    let version = version.trim();
    (!version.is_empty()).then(|| version.to_string())
}

fn detect_dotnet() -> (Option<String>, bool) {
    #[cfg(target_os = "windows")]
    let candidates = {
        let mut candidates = vec!["dotnet".to_string()];
        if let Ok(program_files) = std::env::var("PROGRAMFILES") {
            candidates.push(format!(r"{}\dotnet\dotnet.exe", program_files));
        }
        candidates
    };

    #[cfg(not(target_os = "windows"))]
    let candidates = {
        let mut candidates = vec![
            "dotnet".to_string(),
            "/usr/local/bin/dotnet".to_string(),
            "/usr/local/share/dotnet/dotnet".to_string(),
            "/opt/homebrew/bin/dotnet".to_string(),
        ];
        if let Some(home) = dirs::home_dir() {
            candidates.push(home.join(".dotnet/dotnet").to_string_lossy().into_owned());
        }
        candidates
    };

    for candidate in candidates {
        let mut command = Command::new(&candidate);
        command.arg("--list-runtimes");

        #[cfg(target_os = "windows")]
        command.creation_flags(CREATE_NO_WINDOW);

        let Ok(output) = command.output() else {
            continue;
        };
        let stdout = String::from_utf8_lossy(&output.stdout);
        if let Some(version) =
            parse_dotnet_runtimes(&stdout).filter(|version| is_supported_dotnet_version(version))
        {
            return (Some(version), true);
        }
    }

    (None, false)
}

/// Returns true if the game installation is found at the given path.
#[tauri::command]
pub fn verify_game_path(path: String) -> bool {
    let base = PathBuf::from(&path);
    is_valid_game_path(&base)
}

pub(crate) fn is_valid_game_path(base: &Path) -> bool {
    #[cfg(target_os = "macos")]
    return base.join("TheBazaar.app").exists();

    #[cfg(target_os = "windows")]
    return base.join("TheBazaar.exe").exists();

    #[cfg(not(any(target_os = "macos", target_os = "windows")))]
    return base.join("TheBazaar").exists();
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
    fn test_parse_dotnet_runtimes_found() {
        let output = "Microsoft.NETCore.App 6.0.25 [/usr/share/dotnet/shared/Microsoft.NETCore.App]\nMicrosoft.NETCore.App 8.0.1 [/usr/share/dotnet/shared/Microsoft.NETCore.App]";
        let result = parse_dotnet_runtimes(output);
        assert_eq!(result.as_deref(), Some("8.0.1"));
    }

    #[test]
    fn test_parse_dotnet_runtimes_too_old() {
        let output = "Microsoft.NETCore.App 5.0.0 [/usr/share/dotnet]";
        let result = parse_dotnet_runtimes(output);
        assert_eq!(result, None);
    }

    #[test]
    fn test_parse_dotnet_runtimes_empty_output() {
        let result = parse_dotnet_runtimes("");
        assert_eq!(result, None);
    }

    #[test]
    fn test_is_supported_dotnet_version_requires_major_6_or_higher() {
        assert!(!is_supported_dotnet_version("5.0.17"));
        assert!(is_supported_dotnet_version("6.0.0"));
        assert!(is_supported_dotnet_version("8.0.1"));
    }

    #[test]
    fn test_read_installed_bpp_version_trims_contents() {
        let temp_root = std::env::temp_dir().join(format!(
            "bppinstaller-version-test-{}-{}",
            std::process::id(),
            std::time::SystemTime::now()
                .duration_since(std::time::UNIX_EPOCH)
                .expect("system time before epoch")
                .as_nanos()
        ));
        let plugins_dir = temp_root.join("BepInEx/plugins");
        std::fs::create_dir_all(&plugins_dir).expect("create plugins dir");
        std::fs::write(
            plugins_dir.join("BazaarPlusPlus.version"),
            "1.2.3+2026-03-10 12:34:56\n",
        )
        .expect("write version file");

        let version = read_installed_bpp_version(&temp_root);

        std::fs::remove_dir_all(&temp_root).expect("cleanup temp dir");

        assert_eq!(version.as_deref(), Some("1.2.3+2026-03-10 12:34:56"));
    }

    #[test]
    fn test_is_bepinex_installed_detects_core_payload_without_version_file() {
        let tmp = tempfile::tempdir().unwrap();
        std::fs::create_dir_all(tmp.path().join("BepInEx/core")).unwrap();
        std::fs::write(
            tmp.path().join("BepInEx/core/BepInEx.Preloader.dll"),
            b"dll",
        )
        .unwrap();

        #[cfg(target_os = "macos")]
        {
            std::fs::write(tmp.path().join("run_bepinex.sh"), b"#!/bin/sh\n").unwrap();
            std::fs::write(tmp.path().join("libdoorstop.dylib"), b"dylib").unwrap();
        }

        #[cfg(target_os = "windows")]
        {
            std::fs::write(tmp.path().join("doorstop_config.ini"), b"cfg").unwrap();
            std::fs::write(tmp.path().join("winhttp.dll"), b"dll").unwrap();
        }

        assert!(is_bepinex_installed(tmp.path()));
    }

    #[test]
    fn test_normalize_game_path_trims_whitespace() {
        let game_path = normalize_game_path(Some("  C:\\Games\\The Bazaar  ".to_string()));
        assert_eq!(game_path, Some(PathBuf::from("C:\\Games\\The Bazaar")));
    }

    #[test]
    fn test_resolve_game_path_prefers_requested_path() {
        let requested = PathBuf::from("D:\\Games\\The Bazaar");
        let steam_path = Path::new("C:\\Program Files (x86)\\Steam");

        let game_path = resolve_game_path(Some(steam_path), Some(requested.as_path()));

        assert_eq!(game_path, Some(requested));
    }
}

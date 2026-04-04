use std::path::{Path, PathBuf};

#[cfg(test)]
use keyvalues_parser::{Obj, Parser, Value};
use serde::Serialize;

use super::{debug_error, debug_log};

const THE_BAZAAR_APP_ID: &str = "1617400";
const LAUNCH_OPTIONS_KEY: &str = "LaunchOptions";

#[derive(Debug, Serialize)]
pub struct LaunchOptionsPatchResult {
    pub verified: bool,
}

#[cfg(test)]
fn first_obj<'a, 'text>(values: &'a [Value<'text>]) -> Option<&'a Obj<'text>>
where
    'a: 'text,
{
    values.first()?.get_obj()
}

#[cfg(test)]
fn get_app_obj<'a, 'text>(root: &'a Obj<'text>, app_id: &str) -> Option<&'a Obj<'text>>
where
    'a: 'text,
{
    root.get("Software")
        .and_then(|values| first_obj(values))
        .and_then(|software| software.get("Valve").and_then(|values| first_obj(values)))
        .and_then(|valve| valve.get("Steam").and_then(|values| first_obj(values)))
        .and_then(|steam| steam.get("apps").and_then(|values| first_obj(values)))
        .and_then(|apps| apps.get(app_id).and_then(|values| first_obj(values)))
}

struct LocalconfigUpdate {
    path: PathBuf,
    original_content: String,
    new_content: String,
}

fn escape_vdf_string(value: &str) -> String {
    value.replace('\\', "\\\\").replace('"', "\\\"")
}

fn verify_launch_options_in_content(
    vdf_content: &str,
    expected: &str,
) -> Result<Option<bool>, String> {
    let lines = vdf_content.lines().map(str::to_string).collect::<Vec<_>>();
    let Some((apps_open, apps_close)) = find_apps_block(&lines) else {
        return Err("Malformed VDF: could not locate Steam/apps object".to_string());
    };
    let Some((app_open, app_close)) =
        find_named_block(&lines, apps_open..=apps_close, THE_BAZAAR_APP_ID)
    else {
        return Ok(None);
    };

    for idx in app_open + 1..app_close {
        if let Some((_indent, key, value)) = parse_line_pair(&lines[idx]) {
            if key == LAUNCH_OPTIONS_KEY {
                return Ok(Some(value == escape_vdf_string(expected)));
            }
        }
    }

    Ok(None)
}

fn parse_line_pair(line: &str) -> Option<(&str, &str, &str)> {
    let indent_len = line.find('"')?;
    let indent = &line[..indent_len];
    let trimmed = &line[indent_len..];

    fn parse_quoted(input: &str) -> Option<(&str, &str)> {
        let mut escaped = false;
        let mut end = None;
        let chars = input.char_indices();

        for (idx, ch) in chars.skip(1) {
            if escaped {
                escaped = false;
                continue;
            }

            if ch == '\\' {
                escaped = true;
                continue;
            }

            if ch == '"' {
                end = Some(idx);
                break;
            }
        }

        let end = end?;
        Some((&input[1..end], &input[end + 1..]))
    }

    let (key, rest) = parse_quoted(trimmed)?;
    let rest = rest.trim_start();
    let (value, remainder) = parse_quoted(rest)?;
    if !remainder.trim().is_empty() {
        return None;
    }

    Some((indent, key, value))
}

fn join_lines(lines: &[String]) -> String {
    lines.join("\n")
}

fn find_apps_block(lines: &[String]) -> Option<(usize, usize)> {
    let start = lines.iter().position(|line| line.trim() == "\"apps\"")?;
    let open = (start + 1..lines.len()).find(|&idx| lines[idx].trim() == "{")?;
    let mut depth = 0usize;

    for idx in open..lines.len() {
        match lines[idx].trim() {
            "{" => depth += 1,
            "}" => {
                depth = depth.saturating_sub(1);
                if depth == 0 {
                    return Some((open, idx));
                }
            }
            _ => {}
        }
    }

    None
}

fn find_named_block(
    lines: &[String],
    range: std::ops::RangeInclusive<usize>,
    key: &str,
) -> Option<(usize, usize)> {
    let mut idx = *range.start();
    while idx <= *range.end() {
        if lines[idx].trim() == format!("\"{key}\"") {
            let open = (idx + 1..=*range.end()).find(|&line_idx| lines[line_idx].trim() == "{")?;
            let mut depth = 0usize;
            for line_idx in open..=*range.end() {
                match lines[line_idx].trim() {
                    "{" => depth += 1,
                    "}" => {
                        depth = depth.saturating_sub(1);
                        if depth == 0 {
                            return Some((open, line_idx));
                        }
                    }
                    _ => {}
                }
            }
        }
        idx += 1;
    }
    None
}

fn malformed_launch_option_fragment_count(lines: &[String], start: usize) -> usize {
    let mut consumed = 0usize;
    let mut saw_command = false;

    for line in lines.iter().skip(start) {
        let Some((_indent, key, value)) = parse_line_pair(line) else {
            break;
        };

        if consumed == 0 && !key.contains('/') && !value.contains("%command%") {
            break;
        }

        consumed += 1;
        if key.contains("%command%") || value.contains("%command%") {
            saw_command = true;
            break;
        }
    }

    if saw_command {
        consumed
    } else {
        0
    }
}

fn collect_fragment_text(lines: &[String], start: usize, count: usize) -> String {
    let mut parts = Vec::new();
    for line in lines.iter().skip(start).take(count) {
        if let Some((_indent, key, value)) = parse_line_pair(line) {
            parts.push(key.trim().to_string());
            if !value.trim().is_empty() {
                parts.push(value.trim().to_string());
            }
        }
    }
    parts.join(" ")
}

fn cleanup_malformed_bpp_launch_options(lines: &mut Vec<String>) {
    let mut idx = 0usize;
    while idx < lines.len() {
        let Some((_indent, key, value)) = parse_line_pair(&lines[idx]) else {
            idx += 1;
            continue;
        };

        if key != LAUNCH_OPTIONS_KEY || !value.is_empty() {
            idx += 1;
            continue;
        }

        let fragment_count = malformed_launch_option_fragment_count(lines, idx + 1);
        if fragment_count == 0 {
            idx += 1;
            continue;
        }

        let fragment_text = collect_fragment_text(lines, idx + 1, fragment_count);
        if !fragment_text.contains("run_bepinex.sh") {
            idx += 1;
            continue;
        }

        lines.drain(idx..idx + 1 + fragment_count);
    }
}

fn upsert_launch_options_text(vdf_content: &str, args: &str) -> Result<Option<String>, String> {
    let mut lines = vdf_content.lines().map(str::to_string).collect::<Vec<_>>();
    let Some((apps_open, apps_close)) = find_apps_block(&lines) else {
        return Err("Malformed VDF: could not locate Steam/apps object".to_string());
    };
    let Some((app_open, app_close)) =
        find_named_block(&lines, apps_open..=apps_close, THE_BAZAAR_APP_ID)
    else {
        return Ok(None);
    };

    let mut launch_line_idx = None;
    for idx in app_open + 1..app_close {
        if let Some((_indent, key, _value)) = parse_line_pair(&lines[idx]) {
            if key == LAUNCH_OPTIONS_KEY {
                launch_line_idx = Some(idx);
                break;
            }
        }
    }

    let escape_args = escape_vdf_string(args);
    let property_indent = (app_open + 1..app_close)
        .find_map(|idx| parse_line_pair(&lines[idx]).map(|(indent, _, _)| indent.to_string()))
        .unwrap_or_else(|| format!("{}\t", lines[app_close].split('"').next().unwrap_or("")));
    let launch_line = format!("{property_indent}\"{LAUNCH_OPTIONS_KEY}\"\t\t\"{escape_args}\"");

    if let Some(idx) = launch_line_idx {
        lines[idx] = launch_line;
        let malformed_count = malformed_launch_option_fragment_count(&lines, idx + 1);
        if malformed_count > 0 {
            lines.drain(idx + 1..idx + 1 + malformed_count);
        }
    } else {
        lines.insert(app_close, launch_line);
    }

    cleanup_malformed_bpp_launch_options(&mut lines);

    Ok(Some(join_lines(&lines)))
}

fn remove_launch_options_text(vdf_content: &str) -> Result<Option<String>, String> {
    let mut lines = vdf_content.lines().map(str::to_string).collect::<Vec<_>>();
    let Some((apps_open, apps_close)) = find_apps_block(&lines) else {
        return Err("Malformed VDF: could not locate Steam/apps object".to_string());
    };
    let Some((app_open, app_close)) =
        find_named_block(&lines, apps_open..=apps_close, THE_BAZAAR_APP_ID)
    else {
        return Ok(None);
    };

    let mut launch_line_idx = None;
    for idx in app_open + 1..app_close {
        if let Some((_indent, key, _value)) = parse_line_pair(&lines[idx]) {
            if key == LAUNCH_OPTIONS_KEY {
                launch_line_idx = Some(idx);
                break;
            }
        }
    }

    let Some(idx) = launch_line_idx else {
        return Ok(None);
    };

    let malformed_count = malformed_launch_option_fragment_count(&lines, idx + 1);
    lines.remove(idx);
    if malformed_count > 0 {
        lines.drain(idx..idx + malformed_count);
    }

    cleanup_malformed_bpp_launch_options(&mut lines);

    Ok(Some(join_lines(&lines)))
}

pub fn inject_launch_options(vdf_content: &str, args: &str) -> Result<Option<String>, String> {
    upsert_launch_options_text(vdf_content, args)
}

pub fn clear_launch_options(vdf_content: &str) -> Result<Option<String>, String> {
    remove_launch_options_text(vdf_content)
}

#[cfg(target_os = "macos")]
fn launch_options_args(game_path: &Path) -> String {
    format!(
        "\"{}\" %command%",
        game_path.join("run_bepinex.sh").display()
    )
}

#[cfg(target_os = "windows")]
fn launch_options_args(_game_path: &Path) -> String {
    String::new()
}

#[cfg(not(any(target_os = "macos", target_os = "windows")))]
fn launch_options_args(_game_path: &Path) -> String {
    String::new()
}

#[cfg(target_os = "macos")]
fn ensure_launcher_executable(script_path: &Path) -> Result<(), String> {
    use std::os::unix::fs::PermissionsExt;

    let metadata = std::fs::metadata(script_path)
        .map_err(|err| format!("Cannot access {}: {err}", script_path.display()))?;
    let mut permissions = metadata.permissions();
    permissions.set_mode(permissions.mode() | 0o111);
    std::fs::set_permissions(script_path, permissions).map_err(|err| {
        format!(
            "Cannot set executable permission on {}: {err}",
            script_path.display()
        )
    })
}

#[cfg(not(target_os = "macos"))]
fn ensure_launcher_executable(_script_path: &Path) -> Result<(), String> {
    Ok(())
}

pub fn find_localconfig_paths(steam_path: &Path) -> Vec<PathBuf> {
    let Ok(entries) = std::fs::read_dir(steam_path.join("userdata")) else {
        return Vec::new();
    };

    let mut paths = entries
        .filter_map(|entry| entry.ok())
        .filter_map(|entry| {
            let user_name = entry.file_name();
            let user_name = user_name.to_str()?;
            if !user_name.chars().all(|ch| ch.is_ascii_digit()) {
                return None;
            }

            let localconfig = entry.path().join("config/localconfig.vdf");
            localconfig.exists().then_some(localconfig)
        })
        .collect::<Vec<_>>();
    paths.sort();
    paths
}

fn backup_localconfig_once(localconfig: &Path) -> Result<(), String> {
    let backup = localconfig.with_extension("vdf.bak");
    if backup.exists() {
        return Ok(());
    }

    std::fs::copy(localconfig, &backup).map_err(|err| err.to_string())?;
    Ok(())
}

fn write_localconfig(localconfig: &Path, content: &str) -> Result<(), String> {
    let tmp = localconfig.with_extension("vdf.tmp");
    std::fs::write(&tmp, content).map_err(|err| err.to_string())?;
    std::fs::rename(&tmp, localconfig).map_err(|err| err.to_string())
}

fn plan_localconfig_updates<F>(
    steam_path: &Path,
    mut transform: F,
) -> Result<Vec<LocalconfigUpdate>, String>
where
    F: FnMut(&str) -> Result<Option<String>, String>,
{
    let localconfigs = find_localconfig_paths(steam_path);
    if localconfigs.is_empty() {
        return Err("Could not find any localconfig.vdf under Steam/userdata".to_string());
    }

    let mut planned = Vec::new();
    for localconfig in localconfigs {
        let content = std::fs::read_to_string(&localconfig).map_err(|err| err.to_string())?;
        let Some(new_content) = transform(&content)? else {
            debug_log!(
                "Skipped {} because app {} is not present.",
                localconfig.display(),
                THE_BAZAAR_APP_ID
            );
            continue;
        };

        planned.push(LocalconfigUpdate {
            path: localconfig,
            original_content: content,
            new_content,
        });
    }

    Ok(planned)
}

fn apply_localconfig_updates(updates: Vec<LocalconfigUpdate>) -> Result<usize, String> {
    let mut applied: Vec<LocalconfigUpdate> = Vec::new();

    for update in &updates {
        backup_localconfig_once(&update.path)?;
        debug_log!("Backed up {}", update.path.display());
    }

    for update in updates {
        if let Err(err) = write_localconfig(&update.path, &update.new_content) {
            for applied_update in &applied {
                let _ = write_localconfig(&applied_update.path, &applied_update.original_content);
            }
            return Err(format!(
                "Failed updating {}: {err}. Rolled back {} file(s).",
                update.path.display(),
                applied.len()
            ));
        }

        debug_log!("Updated {}", update.path.display());
        applied.push(update);
    }

    Ok(applied.len())
}

fn patch_localconfigs(steam_path: &Path, args: &str) -> Result<usize, String> {
    let planned =
        plan_localconfig_updates(steam_path, |content| inject_launch_options(content, args))?;
    if planned.is_empty() {
        return Err(format!(
            "Could not find app {} in any localconfig.vdf under Steam/userdata",
            THE_BAZAAR_APP_ID
        ));
    }

    apply_localconfig_updates(planned)
}

fn verify_patched_localconfigs(steam_path: &Path, expected_args: &str) -> Result<bool, String> {
    let localconfigs = find_localconfig_paths(steam_path);
    let mut checked = false;

    for localconfig in localconfigs {
        let content = std::fs::read_to_string(&localconfig).map_err(|err| err.to_string())?;
        match verify_launch_options_in_content(&content, expected_args)? {
            Some(true) => {
                checked = true;
            }
            Some(false) => {
                debug_error!(
                    "Launch option verification mismatch in {}.",
                    localconfig.display()
                );
                return Ok(false);
            }
            None => {}
        }
    }

    Ok(checked)
}

pub fn clear_launch_options_for_steam(steam_path: &Path) -> Result<(), String> {
    if !crate::commands::steam::supports_launch_option_updates(steam_path) {
        debug_log!(
            "Skipping launch option cleanup because Steam userdata was not found at {}.",
            steam_path.display()
        );
        return Ok(());
    }

    let planned = plan_localconfig_updates(steam_path, clear_launch_options)?;
    if planned.is_empty() {
        return Ok(());
    }

    apply_localconfig_updates(planned)?;
    Ok(())
}

#[tauri::command]
pub fn patch_launch_options(
    _app: tauri::AppHandle,
    _steam_path: String,
    _game_path: String,
) -> Result<LaunchOptionsPatchResult, String> {
    let game_path = PathBuf::from(&_game_path);
    let args = launch_options_args(&game_path);

    if args.is_empty() {
        debug_log!("Skipping launch option patch for this platform.");
        return Ok(LaunchOptionsPatchResult { verified: true });
    }

    let steam_path = Path::new(&_steam_path);
    if !crate::commands::steam::supports_launch_option_updates(steam_path) {
        debug_log!(
            "Skipping launch option patch because Steam userdata was not found at {}.",
            steam_path.display()
        );
        return Ok(LaunchOptionsPatchResult { verified: true });
    }

    crate::commands::steam::prepare_steam_for_launch_option_update(steam_path)?;

    #[cfg(target_os = "macos")]
    {
        let script_path = game_path.join("run_bepinex.sh");
        ensure_launcher_executable(&script_path)?;
        debug_log!("Marked {} as executable.", script_path.display());
    }

    debug_log!("Locating localconfig.vdf files...");
    let updated = patch_localconfigs(steam_path, &args).map_err(|err| {
        debug_error!("{err}");
        err
    })?;
    if updated > 0 {
        debug_log!("Updated {} localconfig.vdf file(s).", updated);
    }

    let verified = verify_patched_localconfigs(steam_path, &args)?;
    if !verified {
        debug_error!("Launch option verification failed after write.");
    }

    Ok(LaunchOptionsPatchResult { verified })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[cfg(target_os = "macos")]
    use std::os::unix::fs::PermissionsExt;

    fn fixture_vdf() -> &'static str {
        r#"
"UserLocalConfigStore"
{
    "Software"
    {
        "Valve"
        {
            "Steam"
            {
                "apps"
                {
                    "1617400"
                    {
                        "LastPlayed"    "1700000000"
                    }
                }
            }
        }
    }
}"#
    }

    #[test]
    fn test_inject_launch_options_inserts_when_missing() {
        let result = inject_launch_options(fixture_vdf(), "MY_ARGS")
            .unwrap()
            .unwrap();
        assert!(result.contains("LaunchOptions"));
        assert!(result.contains("MY_ARGS"));
    }

    #[test]
    fn test_inject_launch_options_replaces_existing() {
        let vdf_with_lo = fixture_vdf().replace(
            "\"LastPlayed\"",
            "\"LaunchOptions\"\t\t\"OLD_ARGS\"\n\t\t\t\t\t\"LastPlayed\"",
        );

        let result = inject_launch_options(&vdf_with_lo, "NEW_ARGS")
            .unwrap()
            .unwrap();
        assert!(result.contains("NEW_ARGS"));
        assert!(!result.contains("OLD_ARGS"));
    }

    #[test]
    fn test_clear_launch_options_removes_existing_line() {
        let vdf_with_lo = fixture_vdf().replace(
            "\"LastPlayed\"",
            "\"LaunchOptions\"\t\t\"OLD_ARGS\"\n\t\t\t\t\t\"LastPlayed\"",
        );
        let result = clear_launch_options(&vdf_with_lo).unwrap().unwrap();
        assert!(!result.contains("LaunchOptions"));
        assert!(result.contains("LastPlayed"));
    }

    #[test]
    fn test_inject_skips_missing_app_id() {
        let vdf = r#"
"UserLocalConfigStore"
{
    "Software"
    {
        "Valve"
        {
            "Steam"
            {
                "apps"
                {
                    "730"
                    {
                        "LastPlayed"    "1700000000"
                    }
                }
            }
        }
    }
}"#;
        let result = inject_launch_options(vdf, "args");
        assert_eq!(result.unwrap(), None);
    }

    #[test]
    fn test_inject_launch_options_escapes_quoted_args() {
        let args = "\"/Applications/The Bazaar/run_bepinex.sh\" %command%";
        let rendered = inject_launch_options(fixture_vdf(), args).unwrap().unwrap();
        assert!(rendered.contains(
            "\"LaunchOptions\"\t\t\"\\\"/Applications/The Bazaar/run_bepinex.sh\\\" %command%\""
        ));
        assert!(!rendered.contains("\"LaunchOptions\"\t\t\"\"\n"));
        let parsed = Parser::new().parse(&rendered).unwrap();
        let root = parsed.value.get_obj().unwrap();
        let app = get_app_obj(root, THE_BAZAAR_APP_ID).unwrap();
        let launch_options = app
            .get(LAUNCH_OPTIONS_KEY)
            .and_then(|values| values.first())
            .and_then(Value::get_str)
            .unwrap();

        assert_eq!(launch_options, args);
        assert!(rendered.contains("\\\"/Applications/The Bazaar/run_bepinex.sh\\\" %command%"));
    }

    #[test]
    fn test_inject_launch_options_removes_malformed_stray_fragments() {
        let args = "\"/Applications/The Bazaar/run_bepinex.sh\" %command%";
        let broken = format!(
            "{fixture}\n\t\"LaunchOptions\"\t\t\"\"\n\t\"/Applications/The\"\t\t\"Bazaar/run_bepinex.sh\"\n\t\"%command%\"\t\t\"\"\n",
            fixture = fixture_vdf()
        );

        let rendered = inject_launch_options(&broken, args).unwrap().unwrap();

        assert!(!rendered.contains("\"/Applications/The\""));
        assert!(!rendered.contains("\"%command%\"\t\t\"\""));
        assert!(rendered.contains(
            "\"LaunchOptions\"\t\t\"\\\"/Applications/The Bazaar/run_bepinex.sh\\\" %command%\""
        ));
    }

    #[test]
    fn test_verify_launch_options_in_content_reports_mismatch() {
        let vdf_with_lo = fixture_vdf().replace(
            "\"LastPlayed\"",
            "\"LaunchOptions\"\t\t\"OLD_ARGS\"\n\t\t\t\t\t\"LastPlayed\"",
        );

        let verified = verify_launch_options_in_content(&vdf_with_lo, "EXPECTED_ARGS").unwrap();

        assert_eq!(verified, Some(false));
    }

    #[test]
    fn test_find_localconfig_paths_returns_only_numeric_userdata_entries() {
        let tmp = tempfile::tempdir().unwrap();
        let valid = tmp.path().join("userdata/123456/config");
        let invalid = tmp.path().join("userdata/not-a-user/config");

        std::fs::create_dir_all(&valid).unwrap();
        std::fs::create_dir_all(&invalid).unwrap();
        std::fs::write(valid.join("localconfig.vdf"), fixture_vdf()).unwrap();
        std::fs::write(invalid.join("localconfig.vdf"), fixture_vdf()).unwrap();

        let paths = find_localconfig_paths(tmp.path());
        assert_eq!(paths, vec![valid.join("localconfig.vdf")]);
    }

    #[test]
    fn test_patch_localconfigs_updates_matching_accounts_and_skips_others() {
        let tmp = tempfile::tempdir().unwrap();
        let with_app = tmp.path().join("userdata/123456/config");
        let without_app = tmp.path().join("userdata/234567/config");

        std::fs::create_dir_all(&with_app).unwrap();
        std::fs::create_dir_all(&without_app).unwrap();
        std::fs::write(with_app.join("localconfig.vdf"), fixture_vdf()).unwrap();
        std::fs::write(
            without_app.join("localconfig.vdf"),
            r#"
"UserLocalConfigStore"
{
    "Software"
    {
        "Valve"
        {
            "Steam"
            {
                "apps"
                {
                    "730"
                    {
                        "LastPlayed"    "1700000000"
                    }
                }
            }
        }
    }
}"#,
        )
        .unwrap();

        let updated = patch_localconfigs(tmp.path(), "MY_ARGS").unwrap();

        assert_eq!(updated, 1);
        let patched = std::fs::read_to_string(with_app.join("localconfig.vdf")).unwrap();
        assert!(patched.contains("LaunchOptions"));
        let untouched = std::fs::read_to_string(without_app.join("localconfig.vdf")).unwrap();
        assert!(!untouched.contains("LaunchOptions"));
    }

    #[test]
    fn test_clear_launch_options_for_steam_ignores_missing_steam_directory() {
        let tmp = tempfile::tempdir().unwrap();
        let missing = tmp.path().join("missing-steam");

        let result = clear_launch_options_for_steam(&missing);

        assert!(result.is_ok());
    }

    #[cfg(target_os = "macos")]
    #[test]
    fn test_launch_options_args_uses_run_script_with_quoted_game_path() {
        let args = launch_options_args(Path::new("/Applications/The Bazaar"));
        assert_eq!(
            args,
            "\"/Applications/The Bazaar/run_bepinex.sh\" %command%"
        );
    }

    #[cfg(target_os = "macos")]
    #[test]
    fn test_ensure_launcher_executable_sets_execute_bits() {
        let tmp = tempfile::tempdir().unwrap();
        let script = tmp.path().join("run_bepinex.sh");
        std::fs::write(&script, "#!/bin/sh\n").unwrap();
        std::fs::set_permissions(&script, std::fs::Permissions::from_mode(0o644)).unwrap();

        ensure_launcher_executable(&script).unwrap();

        let mode = std::fs::metadata(&script).unwrap().permissions().mode();
        assert_eq!(mode & 0o111, 0o111);
    }

    #[cfg(target_os = "windows")]
    #[test]
    fn test_launch_options_args_is_empty_on_windows() {
        let args = launch_options_args(Path::new("C:\\Games\\The Bazaar"));
        assert!(args.is_empty());
    }
}

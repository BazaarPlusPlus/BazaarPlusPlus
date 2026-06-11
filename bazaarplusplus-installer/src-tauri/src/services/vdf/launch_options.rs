use std::path::{Path, PathBuf};

use crate::services::{debug_error, debug_log};
use serde::Serialize;

use super::parse::{
    clear_launch_options, inject_launch_options, verify_launch_options_in_content,
    THE_BAZAAR_APP_ID,
};

#[derive(Debug, Serialize)]
pub struct LaunchOptionsPatchResult {
    pub verified: bool,
}

struct LocalconfigUpdate {
    path: PathBuf,
    original_content: String,
    new_content: String,
}

#[cfg(target_os = "macos")]
pub(crate) fn launch_options_args(game_path: &Path) -> String {
    format!(
        "\"{}\" %command%",
        game_path.join("run_bepinex.sh").display()
    )
}

#[cfg(target_os = "windows")]
pub(crate) fn launch_options_args(_game_path: &Path) -> String {
    String::new()
}

#[cfg(not(any(target_os = "macos", target_os = "windows")))]
pub(crate) fn launch_options_args(_game_path: &Path) -> String {
    String::new()
}

#[cfg(target_os = "macos")]
pub(crate) fn ensure_launcher_executable(script_path: &Path) -> Result<(), String> {
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

pub(crate) fn patch_localconfigs(steam_path: &Path, args: &str) -> Result<usize, String> {
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
    if !crate::services::steam::supports_launch_option_updates(steam_path) {
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
    if !crate::services::steam::supports_launch_option_updates(steam_path) {
        debug_log!(
            "Skipping launch option patch because Steam userdata was not found at {}.",
            steam_path.display()
        );
        return Ok(LaunchOptionsPatchResult { verified: true });
    }

    crate::services::steam::prepare_steam_for_launch_option_update(steam_path, true)?;

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

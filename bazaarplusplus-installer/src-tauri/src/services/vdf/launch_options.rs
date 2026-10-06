use std::path::{Path, PathBuf};

use crate::services::debug_log;

use super::parse::{
    clear_launch_options, launch_options_value_in_content, set_launch_options,
    PROTON_LAUNCH_OPTIONS, THE_BAZAAR_APP_ID,
};

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum SteamLaunchOptionsState {
    /// The platform's required LaunchOptions value is in place (or the platform
    /// needs none at all).
    Satisfied,
    /// A required value is missing or different.
    Unsatisfied,
    Unavailable,
}

/// The value The Bazaar's Steam LaunchOptions must carry for the platform's
/// bootstrap, or `None` when the platform needs no launch option. macOS's
/// trampoline requires an empty value; Linux runs the Windows build under Proton
/// and requires Doorstop's DLL override.
pub(crate) fn required_launch_options() -> Option<&'static str> {
    if cfg!(target_os = "macos") {
        Some("")
    } else if cfg!(target_os = "linux") {
        Some(PROTON_LAUNCH_OPTIONS)
    } else {
        None
    }
}

fn satisfies_required(value: &str, required: &str) -> bool {
    if required.is_empty() {
        value.is_empty()
    } else {
        value == required
    }
}

struct LocalconfigUpdate {
    path: PathBuf,
    original_content: String,
    new_content: String,
}

pub fn find_localconfig_paths(steam_path: &Path) -> Vec<PathBuf> {
    let Ok(entries) = std::fs::read_dir(steam_path.join("userdata")) else {
        return Vec::new();
    };

    let mut paths = entries
        .filter_map(std::result::Result::ok)
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

fn inspect_launch_options_for_steam_raw(
    steam_path: &Path,
) -> Result<SteamLaunchOptionsState, String> {
    let Some(required) = required_launch_options() else {
        return Ok(SteamLaunchOptionsState::Satisfied);
    };

    let localconfigs = find_localconfig_paths(steam_path);
    if localconfigs.is_empty() {
        return Err(format!(
            "Could not find any localconfig.vdf under {}",
            steam_path.join("userdata").display()
        ));
    }

    for localconfig in localconfigs {
        let content = std::fs::read_to_string(&localconfig).map_err(|err| err.to_string())?;
        if let Some(value) = launch_options_value_in_content(&content)? {
            if !satisfies_required(&value, required) {
                return Ok(SteamLaunchOptionsState::Unsatisfied);
            }
        }
    }

    Ok(SteamLaunchOptionsState::Satisfied)
}

pub(crate) fn inspect_launch_options_for_steam(steam_path: &Path) -> SteamLaunchOptionsState {
    match inspect_launch_options_for_steam_raw(steam_path) {
        Ok(state) => state,
        Err(_err) => {
            debug_log!("Cannot inspect Steam launch options: {_err}");
            SteamLaunchOptionsState::Unavailable
        }
    }
}

pub fn clear_launch_options_for_steam(steam_path: &Path) -> Result<(), String> {
    let planned = plan_localconfig_updates(steam_path, clear_launch_options)?;
    if !planned.is_empty() {
        apply_localconfig_updates(planned)?;
    }

    // Verify the cleanup itself (the value is gone), independent of whatever
    // value the current platform's bootstrap requires.
    for localconfig in find_localconfig_paths(steam_path) {
        let content = std::fs::read_to_string(&localconfig).map_err(|err| err.to_string())?;
        if launch_options_value_in_content(&content)?.is_some_and(|value| !value.is_empty()) {
            return Err(format!(
                "Steam launch options for app {THE_BAZAAR_APP_ID} are still non-empty after cleanup"
            ));
        }
    }
    Ok(())
}

/// Write the platform's required LaunchOptions value and verify the result.
/// Used by the Linux Proton bootstrap; macOS clears instead (see
/// `clear_launch_options_for_steam`).
pub fn ensure_launch_options_for_steam(steam_path: &Path) -> Result<(), String> {
    let Some(required) = required_launch_options() else {
        return Ok(());
    };
    debug_assert!(
        !required.is_empty(),
        "ensure_launch_options_for_steam is the Linux (non-empty) bootstrap; macOS clears instead"
    );

    let planned =
        plan_localconfig_updates(steam_path, |content| set_launch_options(content, required))?;
    if !planned.is_empty() {
        apply_localconfig_updates(planned)?;
    }

    match inspect_launch_options_for_steam_raw(steam_path)? {
        SteamLaunchOptionsState::Satisfied => Ok(()),
        SteamLaunchOptionsState::Unsatisfied => Err(format!(
            "Steam launch options for app {THE_BAZAAR_APP_ID} are not the required value after update"
        )),
        SteamLaunchOptionsState::Unavailable => {
            unreachable!("raw inspection never returns unavailable")
        }
    }
}

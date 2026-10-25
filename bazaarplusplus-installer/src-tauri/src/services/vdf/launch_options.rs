use anyhow::{anyhow, bail};
use std::path::{Path, PathBuf};

use crate::services::debug_log;

use super::parse::{clear_launch_options, launch_options_empty_in_content, THE_BAZAAR_APP_ID};

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum SteamLaunchOptionsState {
    Empty,
    NonEmpty,
    Unavailable,
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

fn backup_localconfig_once(localconfig: &Path) -> anyhow::Result<()> {
    let backup = localconfig.with_extension("vdf.bak");
    if backup.exists() {
        return Ok(());
    }

    std::fs::copy(localconfig, &backup)?;
    Ok(())
}

fn write_localconfig(localconfig: &Path, content: &str) -> anyhow::Result<()> {
    let tmp = localconfig.with_extension("vdf.tmp");
    std::fs::write(&tmp, content)?;
    std::fs::rename(&tmp, localconfig).map_err(anyhow::Error::from)
}

fn plan_localconfig_updates<F>(
    steam_path: &Path,
    mut transform: F,
) -> anyhow::Result<Vec<LocalconfigUpdate>>
where
    F: FnMut(&str) -> anyhow::Result<Option<String>>,
{
    let localconfigs = find_localconfig_paths(steam_path);
    if localconfigs.is_empty() {
        bail!("Could not find any localconfig.vdf under Steam/userdata");
    }

    let mut planned = Vec::new();
    for localconfig in localconfigs {
        let content = std::fs::read_to_string(&localconfig)?;
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

fn apply_localconfig_updates(updates: Vec<LocalconfigUpdate>) -> anyhow::Result<usize> {
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
            bail!(
                "Failed updating {}: {err:#}. Rolled back {} file(s).",
                update.path.display(),
                applied.len()
            );
        }

        debug_log!("Updated {}", update.path.display());
        applied.push(update);
    }

    Ok(applied.len())
}

fn inspect_launch_options_for_steam_raw(
    steam_path: &Path,
) -> anyhow::Result<SteamLaunchOptionsState> {
    let localconfigs = find_localconfig_paths(steam_path);
    if localconfigs.is_empty() {
        bail!(
            "Could not find any localconfig.vdf under {}",
            steam_path.join("userdata").display()
        );
    }

    for localconfig in localconfigs {
        let content = std::fs::read_to_string(&localconfig)?;
        if launch_options_empty_in_content(&content)? == Some(false) {
            return Ok(SteamLaunchOptionsState::NonEmpty);
        }
    }

    Ok(SteamLaunchOptionsState::Empty)
}

pub(crate) fn inspect_launch_options_for_steam(steam_path: &Path) -> SteamLaunchOptionsState {
    match inspect_launch_options_for_steam_raw(steam_path) {
        Ok(state) => state,
        Err(_err) => {
            debug_log!("Cannot inspect Steam launch options: {_err:#}");
            SteamLaunchOptionsState::Unavailable
        }
    }
}

pub fn clear_launch_options_for_steam(steam_path: &Path) -> anyhow::Result<()> {
    let planned = plan_localconfig_updates(steam_path, clear_launch_options)?;
    if !planned.is_empty() {
        apply_localconfig_updates(planned)?;
    }
    match inspect_launch_options_for_steam_raw(steam_path)? {
        SteamLaunchOptionsState::Empty => Ok(()),
        SteamLaunchOptionsState::NonEmpty => Err(anyhow!(
            "Steam launch options for app {THE_BAZAAR_APP_ID} are still non-empty after cleanup"
        )),
        SteamLaunchOptionsState::Unavailable => {
            unreachable!("raw inspection never returns unavailable")
        }
    }
}

mod payload;
mod trampoline;
mod zip_archive;

pub(crate) use payload::{payload_root_relative_paths, remove_dir_with_retry};
pub(crate) use trampoline::{
    install_trampoline, is_trampolined, read_launch_mode_marker, uninstall_trampoline,
    write_launch_mode_marker, LaunchMode, MARKER_FILE,
};
pub(crate) use zip_archive::read_bundled_bpp_version;

use std::path::{Path, PathBuf};
use tauri::Manager;

use crate::stream::state::StreamRuntimeState;

use super::{debug_error, debug_log};

/// Stable error-code prefixes returned when a BPP-data reset is blocked or
/// partially fails. Kept stable so the frontend can pattern-match the prefix;
/// the current UI (`useInstallPage.ts`) maps these to localized messages.
pub(crate) const RESET_BPP_DATA_ERR_GAME_RUNNING: &str = "bpp_data_reset_blocked_by_game";
pub(crate) const RESET_BPP_DATA_ERR_PARTIAL_FAILURE: &str = "bpp_data_reset_partial_failure";

pub async fn reset_bpp_data(
    stream_state: tauri::State<'_, StreamRuntimeState>,
    game_path: String,
) -> Result<bool, String> {
    // Drop our own SQLite connections before touching the data directory.
    // Without this, OBS overlay polling keeps `bazaarplusplus.db` open and
    // Windows refuses to delete it (the headline customer complaint).
    let _ = crate::stream::server::stop(stream_state.inner()).await;

    tauri::async_runtime::spawn_blocking(move || reset_bpp_data_blocking(Path::new(&game_path)))
        .await
        .map_err(|err| format!("failed to reset BazaarPlusPlus data: {err}"))?
}

fn reset_bpp_data_blocking(game_path: &Path) -> Result<bool, String> {
    payload::ensure_valid_game_path(game_path)?;

    if crate::services::game_process::is_bazaar_running_best_effort() {
        return Err(RESET_BPP_DATA_ERR_GAME_RUNNING.to_string());
    }

    let data_dir = crate::services::paths::bpp_data_dir(game_path);
    let had_resettable_data = data_dir.exists();
    let report = payload::cleanup_bpp_data_directory(game_path);
    if !report.is_empty() {
        return Err(format_partial_failure(&report.failed));
    }

    debug_log!(
        "Reset BazaarPlusPlus data directory at {}",
        game_path.display()
    );
    Ok(had_resettable_data)
}

fn format_partial_failure(paths: &[PathBuf]) -> String {
    // Use a delimiter that won't collide with Windows drive letters or POSIX
    // separators. The frontend splits on `\u{1f}` to recover the list.
    let joined = paths
        .iter()
        .map(|path| path.display().to_string())
        .collect::<Vec<_>>()
        .join("\u{1f}");
    format!("{RESET_BPP_DATA_ERR_PARTIAL_FAILURE}:{joined}")
}

pub fn install_bepinex(
    app: tauri::AppHandle,
    steam_path: String,
    game_path: String,
) -> Result<(), String> {
    let game_path = Path::new(&game_path);
    let preserved_bpp_config =
        payload::preserve_file_if_exists(game_path, payload::BPP_CONFIG_RELATIVE_PATH)?;
    #[cfg(not(target_os = "macos"))]
    let _ = &steam_path;
    #[cfg(target_os = "macos")]
    if !steam_path.trim().is_empty() {
        crate::services::steam::prepare_steam_for_launch_option_update(
            Path::new(&steam_path),
            true,
        )?;
    }
    let install_backup = payload::prepare_install_target(game_path)?;

    let install_result = (|| -> Result<(), String> {
        debug_log!("Reading bundled BepInEx.zip...");
        let relative_zip_path = zip_archive::bundled_zip_relative_path();
        let resource_path = app
            .path()
            .resource_dir()
            .map_err(|err| err.to_string())?
            .join(relative_zip_path);
        let zip_bytes = std::fs::read(&resource_path).map_err(|err| {
            debug_error!("Cannot read bundled BepInEx.zip: {err}");
            format!("Cannot read bundled BepInEx.zip: {err}")
        })?;

        debug_log!("Extracting BepInEx...");
        let _extracted = zip_archive::extract_zip(&zip_bytes, game_path)?;
        debug_log!("Extracted {} files.", _extracted.len());

        #[cfg(target_os = "macos")]
        {
            let script_path = game_path.join("run_bepinex.sh");
            crate::services::vdf::ensure_launcher_executable(&script_path)?;
            debug_log!("Marked {} as executable.", script_path.display());
        }

        Ok(())
    })();

    let restore_result = preserved_bpp_config
        .as_ref()
        .map(|preserved| payload::restore_preserved_file(game_path, preserved))
        .transpose();

    let final_result = match (install_result, restore_result) {
        (Ok(()), Ok(_)) => Ok(()),
        (Err(install_err), Ok(_)) => Err(install_err),
        (Ok(()), Err(restore_err)) => Err(restore_err),
        (Err(install_err), Err(restore_err)) => Err(format!(
            "{install_err}; additionally failed to restore preserved config: {restore_err}"
        )),
    };

    match final_result {
        Ok(()) => Ok(()),
        Err(err) => match install_backup.restore(game_path) {
            Ok(()) => Err(err),
            Err(rollback_err) => Err(format!(
                "{err}; additionally failed to restore previous payload: {rollback_err}"
            )),
        },
    }
}

pub fn uninstall_bpp(
    _app: tauri::AppHandle,
    _steam_path: String,
    game_path: String,
) -> Result<(), String> {
    let game_path = Path::new(&game_path);
    payload::ensure_valid_game_path(game_path)?;

    #[cfg(target_os = "macos")]
    if !_steam_path.trim().is_empty() {
        crate::services::steam::prepare_steam_for_launch_option_update(
            Path::new(&_steam_path),
            false,
        )?;
    }

    let keep_shared_bootstrap = payload::has_third_party_plugins(game_path);

    // Restore the vanilla bundle only when BPP is the last installed plugin. If
    // another mod remains, the trampoline / launch options are shared BepInEx
    // bootstrap state and removing them would disable that mod.
    if !keep_shared_bootstrap {
        // Call uninstall_trampoline UNCONDITIONALLY (not gated on is_trampolined):
        // it self-classifies — a no-op when already vanilla, a restore when a
        // `.orig` exists, and a hard error in the broken stub-without-backup state.
        // If this fails, abort so we never strand a stubbed bundle whose `.orig`
        // we then can't recover. No-op off macOS.
        trampoline::uninstall_trampoline(game_path)?;
    }

    if keep_shared_bootstrap {
        payload::uninstall_payload_preserving_shared_dependencies(game_path)?;
    } else {
        payload::uninstall_payload(game_path)?;
    }

    if !keep_shared_bootstrap {
        trampoline::remove_launch_mode_marker(game_path)?;

        #[cfg(target_os = "macos")]
        {
            if !_steam_path.trim().is_empty() {
                crate::services::vdf::clear_launch_options_for_steam(Path::new(&_steam_path))?;
            }
        }
    }

    debug_log!(
        "Uninstalled BazaarPlusPlus payload from {}",
        game_path.display()
    );
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn make_valid_game_dir() -> tempfile::TempDir {
        let tmp = tempfile::tempdir().unwrap();

        #[cfg(target_os = "macos")]
        {
            std::fs::create_dir_all(tmp.path().join("TheBazaar.app")).unwrap();
        }

        #[cfg(target_os = "windows")]
        {
            std::fs::write(tmp.path().join("TheBazaar.exe"), b"exe").unwrap();
        }

        #[cfg(not(any(target_os = "macos", target_os = "windows")))]
        {
            std::fs::write(tmp.path().join("TheBazaar"), b"exe").unwrap();
        }

        tmp
    }

    #[test]
    fn test_reset_bpp_data_removes_bpp_data_directory() {
        let tmp = make_valid_game_dir();
        let data_dir = tmp.path().join(crate::config::BAZAAR_DATA_DIRECTORY);

        std::fs::create_dir_all(&data_dir).unwrap();
        std::fs::write(data_dir.join("stale.dll"), b"dll").unwrap();

        let removed_data = reset_bpp_data_blocking(tmp.path()).unwrap();

        assert!(removed_data);
        assert!(!data_dir.exists());
    }

    #[test]
    fn test_reset_bpp_data_is_noop_when_directory_missing() {
        let tmp = make_valid_game_dir();
        let data_dir = tmp.path().join(crate::config::BAZAAR_DATA_DIRECTORY);
        assert!(!data_dir.exists());

        let removed_data = reset_bpp_data_blocking(tmp.path()).unwrap();

        assert!(!removed_data);
        assert!(!data_dir.exists());
    }

    #[test]
    fn test_reset_bpp_data_is_idempotent_when_run_twice() {
        let tmp = make_valid_game_dir();
        let data_dir = tmp.path().join(crate::config::BAZAAR_DATA_DIRECTORY);
        std::fs::create_dir_all(&data_dir).unwrap();

        let removed_data = reset_bpp_data_blocking(tmp.path()).unwrap();
        let removed_data_again = reset_bpp_data_blocking(tmp.path()).unwrap();

        assert!(removed_data);
        assert!(!removed_data_again);
        assert!(!data_dir.exists());
    }

    #[test]
    fn test_format_partial_failure_uses_unit_separator() {
        let formatted = format_partial_failure(&[
            PathBuf::from("C:/Games/The Bazaar/BazaarPlusPlusV4/bazaarplusplus.db"),
            PathBuf::from("C:/Games/The Bazaar/BazaarPlusPlusV4/Identity/observation.json"),
        ]);

        assert!(formatted.starts_with(RESET_BPP_DATA_ERR_PARTIAL_FAILURE));
        assert!(formatted.contains('\u{1f}'));
        assert!(formatted.ends_with("observation.json"));
    }
}

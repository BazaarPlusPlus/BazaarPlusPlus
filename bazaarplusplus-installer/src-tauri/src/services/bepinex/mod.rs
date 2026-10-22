#[cfg(target_os = "macos")]
mod bundle_root;
mod managed_payload;
mod payload;
mod trampoline;
mod zip_archive;

pub(crate) use managed_payload::installed_version_is_current;
pub(crate) use payload::{ensure_valid_game_path, remove_dir_with_retry};
pub(crate) use trampoline::{
    install_trampoline, is_current_trampoline, obsolete_macos_artifacts_present,
    remove_obsolete_macos_artifacts,
};
pub(crate) use zip_archive::read_bundled_bpp_version;

/// Repairs developer deployments without extracting the installer's bundled Payload.
pub(crate) fn repair_deployed_game(game_path: &Path, stub: &Path) -> Result<(), String> {
    #[cfg(target_os = "macos")]
    {
        payload::ensure_valid_game_path(game_path)?;
        if !game_path.join("libdoorstop.dylib").is_file() {
            return Err(
                "libdoorstop.dylib is missing; install BepInEx with the installer first".into(),
            );
        }
        trampoline::repair_with_stub(game_path, stub)
    }
    #[cfg(not(target_os = "macos"))]
    {
        let _ = (game_path, stub);
        Err("Launch trampoline repair is supported only on macOS".into())
    }
}

use std::path::Path;

use crate::services::action_error::ActionError;
use crate::services::data_maintenance::DataMaintenanceLock;
use crate::services::game_process::{
    ensure_game_stopped_with, GameProcessProbe, GameStoppedOperation, SystemGameProcessProbe,
};
use crate::stream::runtime::StreamRuntime;

use super::{debug_error, debug_log};

pub async fn reset_bpp_data(
    maintenance: &DataMaintenanceLock,
    stream_runtime: &StreamRuntime,
    game_path: String,
) -> Result<bool, ActionError> {
    reset_bpp_data_with(
        maintenance,
        stream_runtime,
        game_path,
        SystemGameProcessProbe,
    )
    .await
}

/// Holds the data maintenance lock and stops the OBS overlay, which reads the
/// database this deletes, for the whole reset.
pub(crate) async fn reset_bpp_data_with(
    maintenance: &DataMaintenanceLock,
    stream_runtime: &StreamRuntime,
    game_path: String,
    probe: impl GameProcessProbe + Send + 'static,
) -> Result<bool, ActionError> {
    maintenance
        .run_blocking_with_overlay_stopped(stream_runtime, move || {
            reset_bpp_data_blocking_with(Path::new(&game_path), &probe)
        })
        .await?
}

fn reset_bpp_data_blocking_with(
    game_path: &Path,
    probe: &impl GameProcessProbe,
) -> Result<bool, ActionError> {
    payload::ensure_valid_game_path(game_path)?;
    ensure_game_stopped_with(probe, game_path, GameStoppedOperation::Reset)?;

    let data_dir = crate::services::paths::bpp_data_dir(game_path);
    let had_resettable_data = data_dir.exists();
    let report = payload::cleanup_bpp_data_directory(game_path);
    if !report.is_empty() {
        return Err(ActionError::PartialFailure(report.failed));
    }

    debug_log!(
        "Reset BazaarPlusPlus data directory at {}",
        game_path.display()
    );
    Ok(had_resettable_data)
}

/// Blunt "wipe the whole BepInEx folder" repair. Deletes only `<game>/BepInEx`
/// (including any third-party mod under it) and leaves the doorstop/trampoline
/// bootstrap untouched, so the game stays launchable and the user reinstalls
/// manually afterward. It holds the data maintenance lock; unlike
/// [`reset_bpp_data`] it touches no SQLite database, so the overlay keeps
/// running.
pub async fn reset_bepinex_folder(
    maintenance: &DataMaintenanceLock,
    game_path: String,
) -> Result<bool, ActionError> {
    reset_bepinex_folder_with(maintenance, game_path, SystemGameProcessProbe).await
}

pub(crate) async fn reset_bepinex_folder_with(
    maintenance: &DataMaintenanceLock,
    game_path: String,
    probe: impl GameProcessProbe + Send + 'static,
) -> Result<bool, ActionError> {
    maintenance
        .run_blocking(move || reset_bepinex_folder_blocking_with(Path::new(&game_path), &probe))
        .await?
}

fn reset_bepinex_folder_blocking_with(
    game_path: &Path,
    probe: &impl GameProcessProbe,
) -> Result<bool, ActionError> {
    payload::ensure_valid_game_path(game_path)?;
    ensure_game_stopped_with(probe, game_path, GameStoppedOperation::BepinexReset)?;

    let had_bepinex = game_path.join("BepInEx").exists();
    let report = payload::reset_bepinex_directory(game_path);
    if !report.is_empty() {
        return Err(ActionError::PartialFailure(report.failed));
    }

    debug_log!("Reset BepInEx folder at {}", game_path.display());
    Ok(had_bepinex)
}

pub fn install_bepinex(resource_dir: &Path, game_path: &Path) -> Result<(), String> {
    let preserved_bpp_config =
        payload::preserve_file_if_exists(game_path, payload::BPP_CONFIG_RELATIVE_PATH)?;
    debug_log!("Reading bundled BepInEx.zip...");
    let relative_zip_path = zip_archive::bundled_zip_relative_path();
    let resource_path = resource_dir.join(relative_zip_path);
    let zip_bytes = std::fs::read(&resource_path).map_err(|err| {
        debug_error!("Cannot read bundled BepInEx.zip: {err}");
        format!("Cannot read bundled BepInEx.zip: {err}")
    })?;
    let mut incoming_relative_paths = zip_archive::zip_entry_paths(&zip_bytes)?;

    // Repair never writes an older Managed Payload over a newer one the mod's
    // self-update installed (ADR-0009); the bundled zip still supplies every
    // non-managed file.
    let manifest_path = payload::installed_managed_manifest_path();
    let bundled_manifest = zip_archive::zip_entry_bytes(&zip_bytes, manifest_path)?;
    let skip = match managed_payload::choose_managed_payload_source(
        game_path,
        manifest_path,
        bundled_manifest.as_deref(),
    ) {
        managed_payload::ManagedPayloadSource::Bundled => Default::default(),
        managed_payload::ManagedPayloadSource::Installed {
            version: _version,
            skip,
            keep,
        } => {
            debug_log!("Keeping the installed Managed Payload {_version}");
            incoming_relative_paths.retain(|path| !skip.contains(path));
            incoming_relative_paths.extend(keep);
            skip
        }
    };

    let install_backup = payload::prepare_install_target(game_path, &incoming_relative_paths)?;

    let install_result = (|| -> Result<(), String> {
        debug_log!("Extracting BepInEx...");
        let _report = zip_archive::extract_zip(&zip_bytes, game_path, &skip)?;
        debug_log!(
            "Extracted {} files, skipped {} identical.",
            _report.written.len(),
            _report.skipped_identical.len()
        );

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
        crate::services::steam::prepare_steam_for_config_update(Path::new(&_steam_path))?;
    }

    let keep_shared_bootstrap =
        payload::has_third_party_plugins(game_path) || payload::has_third_party_patchers(game_path);

    // Payload removal can fail after restoring the executable or deleting a
    // native plugin. Always seal the resulting bundle before returning an error.
    trampoline::with_finalized_bundle(game_path, || {
        // Restore the vanilla bundle only when BPP is the last installed mod. If
        // another mod remains — a plugin in BepInEx/plugins or a patcher-only mod
        // under BepInEx/patchers — the trampoline / launch options are shared
        // BepInEx bootstrap state and removing them would disable that mod.
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
            // Last plugin standing: also tear down the BepInEx bootstrap so the
            // Windows doorstop stops injecting and detection reports uninstalled.
            payload::remove_bootstrap_files(game_path)?;
        }

        if !keep_shared_bootstrap {
            #[cfg(target_os = "macos")]
            {
                if !_steam_path.trim().is_empty() {
                    crate::services::vdf::clear_launch_options_for_steam(Path::new(&_steam_path))?;
                }
            }
        }

        trampoline::remove_obsolete_macos_artifacts(game_path)?;
        Ok(())
    })?;

    debug_log!(
        "Uninstalled BazaarPlusPlus payload from {}",
        game_path.display()
    );
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::problem::SemanticProblemCode;

    fn stopped(_: &Path) -> Result<bool, String> {
        Ok(false)
    }

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

        let removed_data = reset_bpp_data_blocking_with(tmp.path(), &stopped).unwrap();

        assert!(removed_data);
        assert!(!data_dir.exists());
    }

    #[test]
    fn test_reset_bpp_data_is_noop_when_directory_missing() {
        let tmp = make_valid_game_dir();
        let data_dir = tmp.path().join(crate::config::BAZAAR_DATA_DIRECTORY);
        assert!(!data_dir.exists());

        let removed_data = reset_bpp_data_blocking_with(tmp.path(), &stopped).unwrap();

        assert!(!removed_data);
        assert!(!data_dir.exists());
    }

    #[test]
    fn test_reset_bpp_data_is_idempotent_when_run_twice() {
        let tmp = make_valid_game_dir();
        let data_dir = tmp.path().join(crate::config::BAZAAR_DATA_DIRECTORY);
        std::fs::create_dir_all(&data_dir).unwrap();

        let removed_data = reset_bpp_data_blocking_with(tmp.path(), &stopped).unwrap();
        let removed_data_again = reset_bpp_data_blocking_with(tmp.path(), &stopped).unwrap();

        assert!(removed_data);
        assert!(!removed_data_again);
        assert!(!data_dir.exists());
    }

    #[test]
    fn test_reset_bepinex_folder_removes_bepinex_directory_and_foreign_mods() {
        let tmp = make_valid_game_dir();
        let bepinex = tmp.path().join("BepInEx");
        std::fs::create_dir_all(bepinex.join("plugins")).unwrap();
        std::fs::write(bepinex.join("plugins/BazaarPlusPlus.dll"), b"bpp").unwrap();
        // A third-party mod under BepInEx is deliberately wiped too (blunt reset).
        std::fs::write(bepinex.join("plugins/OtherMod.dll"), b"foreign").unwrap();

        let removed = reset_bepinex_folder_blocking_with(tmp.path(), &stopped).unwrap();

        assert!(removed);
        assert!(!bepinex.exists());
    }

    #[test]
    fn test_reset_bepinex_folder_leaves_bootstrap_and_game_intact() {
        let tmp = make_valid_game_dir();
        std::fs::create_dir_all(tmp.path().join("BepInEx/core")).unwrap();
        // Doorstop/trampoline bootstrap lives OUTSIDE BepInEx and must survive so
        // the bundle stays launchable; only BepInEx itself is removed.
        std::fs::write(tmp.path().join("winhttp.dll"), b"doorstop").unwrap();
        std::fs::write(tmp.path().join("libdoorstop.dylib"), b"doorstop").unwrap();

        let removed = reset_bepinex_folder_blocking_with(tmp.path(), &stopped).unwrap();

        assert!(removed);
        assert!(!tmp.path().join("BepInEx").exists());
        assert!(tmp.path().join("winhttp.dll").exists());
        assert!(tmp.path().join("libdoorstop.dylib").exists());
    }

    #[test]
    fn test_reset_bepinex_folder_is_noop_when_directory_missing() {
        let tmp = make_valid_game_dir();
        assert!(!tmp.path().join("BepInEx").exists());

        let removed = reset_bepinex_folder_blocking_with(tmp.path(), &stopped).unwrap();

        assert!(!removed);
    }

    #[test]
    fn resets_refuse_with_typed_problems_and_delete_nothing_while_the_game_runs() {
        fn running(_: &Path) -> Result<bool, String> {
            Ok(true)
        }
        fn unknown(_: &Path) -> Result<bool, String> {
            Err("cannot inspect processes".to_string())
        }
        type Probe = fn(&Path) -> Result<bool, String>;
        let probes: [Probe; 2] = [running, unknown];
        for refusing in probes {
            let tmp = make_valid_game_dir();
            let data_dir = tmp.path().join(crate::config::BAZAAR_DATA_DIRECTORY);
            let plugins = tmp.path().join("BepInEx/plugins");
            std::fs::create_dir_all(&data_dir).unwrap();
            std::fs::write(data_dir.join("history.db"), b"db").unwrap();
            std::fs::create_dir_all(&plugins).unwrap();
            std::fs::write(plugins.join("BazaarPlusPlus.dll"), b"bpp").unwrap();

            let reset = reset_bpp_data_blocking_with(tmp.path(), &refusing).unwrap_err();
            let bepinex_reset =
                reset_bepinex_folder_blocking_with(tmp.path(), &refusing).unwrap_err();

            assert_eq!(
                reset.into_problem("reset_bpp_data").code,
                SemanticProblemCode::ResetBlockedByGame
            );
            assert_eq!(
                bepinex_reset.into_problem("reset_bepinex").code,
                SemanticProblemCode::BepinexResetBlockedByGame
            );
            assert!(data_dir.join("history.db").is_file());
            assert!(plugins.join("BazaarPlusPlus.dll").is_file());
        }
    }

    #[tokio::test]
    async fn locked_resets_refuse_while_the_game_runs() {
        let tmp = make_valid_game_dir();
        let data_dir = tmp.path().join(crate::config::BAZAAR_DATA_DIRECTORY);
        std::fs::create_dir_all(&data_dir).unwrap();
        std::fs::create_dir_all(tmp.path().join("BepInEx")).unwrap();
        let lock = DataMaintenanceLock::default();
        let game_path = tmp.path().to_string_lossy().into_owned();

        let reset = reset_bpp_data_with(
            &lock,
            &StreamRuntime::default(),
            game_path.clone(),
            |_: &Path| Ok(true),
        )
        .await
        .unwrap_err();
        let bepinex_reset = reset_bepinex_folder_with(&lock, game_path, |_: &Path| Ok(true))
            .await
            .unwrap_err();

        assert_eq!(
            reset.into_problem("reset_bpp_data").code,
            SemanticProblemCode::ResetBlockedByGame
        );
        assert_eq!(
            bepinex_reset.into_problem("reset_bepinex").code,
            SemanticProblemCode::BepinexResetBlockedByGame
        );
        assert!(data_dir.is_dir());
        assert!(tmp.path().join("BepInEx").is_dir());
    }

    /// A bundled resource directory whose zip carries `files` plus their
    /// Managed Payload Manifest at version `version`.
    fn bundled_resources(
        version: &str,
        managed: &[(&str, &[u8])],
        other: &[(&str, &[u8])],
    ) -> tempfile::TempDir {
        use std::io::Write;
        let manifest = managed_payload::tests::render(version, 1, &"e".repeat(64), managed);
        let resources = tempfile::tempdir().unwrap();
        let zip_path = resources
            .path()
            .join(zip_archive::bundled_zip_relative_path());
        std::fs::create_dir_all(zip_path.parent().unwrap()).unwrap();
        let mut zip = zip::ZipWriter::new(std::fs::File::create(zip_path).unwrap());
        let options = zip::write::SimpleFileOptions::default();
        for (path, bytes) in managed
            .iter()
            .chain(other)
            .chain(&[(managed_payload::tests::MANIFEST_PATH, manifest.as_bytes())])
        {
            zip.start_file(*path, options).unwrap();
            zip.write_all(bytes).unwrap();
        }
        zip.finish().unwrap();
        resources
    }

    #[test]
    fn install_keeps_a_newer_self_updated_managed_payload_and_writes_everything_else() {
        const PLUGINS: &str = "BepInEx/plugins/";
        let bundled_managed: &[(&str, &[u8])] = &[
            ("BepInEx/plugins/BazaarPlusPlus.dll", b"bundled mod"),
            (
                "BepInEx/plugins/BazaarPlusPlus.Storage.dll",
                b"bundled storage",
            ),
            ("BepInEx/plugins/BazaarPlusPlus.version", b"6.0.0.prod\n"),
        ];
        let non_managed: &[(&str, &[u8])] = &[
            ("BepInEx/core/BepInEx.Preloader.dll", b"preloader"),
            (
                "BepInEx/patchers/BazaarPlusPlus.Updater.dll",
                b"bundled updater",
            ),
        ];
        let installed_managed: &[(&str, &[u8])] = &[
            (
                "BepInEx/plugins/BazaarPlusPlus.Core.dll",
                b"self-updated core",
            ),
            ("BepInEx/plugins/BazaarPlusPlus.dll", b"self-updated mod"),
            ("BepInEx/plugins/BazaarPlusPlus.version", b"6.1.0.prod\n"),
        ];
        let installed_manifest =
            managed_payload::tests::render("6.1.0", 1, &"e".repeat(64), installed_managed);
        let resources = bundled_resources("6.0.0", bundled_managed, non_managed);
        let read = |root: &Path, path: &str| std::fs::read(root.join(path)).ok();

        for corrupt in [false, true] {
            let game = make_valid_game_dir();
            for (path, bytes) in installed_managed {
                let path = game.path().join(path);
                std::fs::create_dir_all(path.parent().unwrap()).unwrap();
                std::fs::write(path, bytes).unwrap();
            }
            std::fs::write(
                game.path().join(managed_payload::tests::MANIFEST_PATH),
                &installed_manifest,
            )
            .unwrap();
            std::fs::create_dir_all(game.path().join("BepInEx/patchers")).unwrap();
            std::fs::write(
                game.path()
                    .join("BepInEx/patchers/BazaarPlusPlus.Updater.dll"),
                b"stale updater",
            )
            .unwrap();
            std::fs::create_dir_all(game.path().join("BepInEx/config")).unwrap();
            std::fs::write(
                game.path().join(payload::BPP_CONFIG_RELATIVE_PATH),
                b"user config",
            )
            .unwrap();
            if corrupt {
                std::fs::write(
                    game.path().join(format!("{PLUGINS}BazaarPlusPlus.dll")),
                    b"torn",
                )
                .unwrap();
            }

            install_bepinex(resources.path(), game.path()).unwrap();

            for (path, bytes) in non_managed {
                assert_eq!(read(game.path(), path).as_deref(), Some(*bytes), "{path}");
            }
            assert_eq!(
                read(game.path(), payload::BPP_CONFIG_RELATIVE_PATH).as_deref(),
                Some(b"user config".as_slice())
            );
            let (expected_managed, absent) = if corrupt {
                (bundled_managed, "BepInEx/plugins/BazaarPlusPlus.Core.dll")
            } else {
                (
                    installed_managed,
                    "BepInEx/plugins/BazaarPlusPlus.Storage.dll",
                )
            };
            for (path, bytes) in expected_managed {
                assert_eq!(read(game.path(), path).as_deref(), Some(*bytes), "{path}");
            }
            assert!(!game.path().join(absent).exists(), "{absent}");
            let version = String::from_utf8(
                read(game.path(), "BepInEx/plugins/BazaarPlusPlus.version").unwrap(),
            )
            .unwrap();
            assert!(installed_version_is_current(&version, "6.0.0.prod"));
            assert_eq!(
                version.trim(),
                if corrupt { "6.0.0.prod" } else { "6.1.0.prod" }
            );
        }
    }
}

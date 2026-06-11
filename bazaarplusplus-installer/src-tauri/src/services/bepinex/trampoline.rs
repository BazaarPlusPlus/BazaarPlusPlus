//! macOS in-bundle Mach-O launch trampoline.
//!
//! On macOS 27+ Steam no longer spawns a prefix executable before `%command%`,
//! so the `run_bepinex.sh` launch path is dead and injection must move inside the
//! `.app`. This module installs a tiny arm64 stub (`bpp_launcher.c`, compiled at
//! build time) as the bundle's `CFBundleExecutable`, renames the real Unity
//! bootstrap to `<exe>.orig`, and re-signs the real binary with the JIT
//! entitlements so Harmony can write executable memory. See
//! `docs/macos27-bepinex-launch-trampoline.md`.
//!
//! Every behaviour here is macOS-only; the public API has no-op / `false` stubs on
//! other platforms so the install orchestrator can call it unconditionally.

use std::path::Path;

#[cfg(target_os = "macos")]
use std::path::PathBuf;

use tauri::AppHandle;

/// Game-dir sibling (OUTSIDE the `.app`) recording the chosen launch mode, so the
/// installer still knows the desired mode after a Steam "Verify integrity" / game
/// update reverts the bundle. Removed on uninstall.
pub(crate) const MARKER_FILE: &str = ".bpp-launch-mode";

/// Which launch mechanism an install applied. Persisted in [`MARKER_FILE`].
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum LaunchMode {
    /// In-bundle Mach-O trampoline (macOS 27+ forced, or <= 26 opt-in).
    Trampoline,
    /// `run_bepinex.sh` prefix launcher (macOS <= 26 default).
    Prefix,
}

impl LaunchMode {
    fn as_marker(self) -> &'static str {
        match self {
            LaunchMode::Trampoline => "trampoline",
            LaunchMode::Prefix => "prefix",
        }
    }

    fn from_marker(value: &str) -> Option<Self> {
        match value {
            "trampoline" => Some(LaunchMode::Trampoline),
            "prefix" => Some(LaunchMode::Prefix),
            _ => None,
        }
    }
}

// ---------------------------------------------------------------------------
// Launch-mode marker (macOS only; no-ops elsewhere keep Windows byte-identical)
// ---------------------------------------------------------------------------

#[cfg(target_os = "macos")]
pub(crate) fn write_launch_mode_marker(game_path: &Path, mode: LaunchMode) -> Result<(), String> {
    let path = game_path.join(MARKER_FILE);
    std::fs::write(&path, mode.as_marker())
        .map_err(|err| format!("Cannot write launch-mode marker {}: {err}", path.display()))
}

#[cfg(target_os = "macos")]
pub(crate) fn read_launch_mode_marker(game_path: &Path) -> Option<LaunchMode> {
    let content = std::fs::read_to_string(game_path.join(MARKER_FILE)).ok()?;
    LaunchMode::from_marker(content.trim())
}

#[cfg(target_os = "macos")]
pub(crate) fn remove_launch_mode_marker(game_path: &Path) -> Result<(), String> {
    let path = game_path.join(MARKER_FILE);
    match std::fs::remove_file(&path) {
        Ok(()) => Ok(()),
        Err(err) if err.kind() == std::io::ErrorKind::NotFound => Ok(()),
        Err(err) => Err(format!(
            "Cannot remove launch-mode marker {}: {err}",
            path.display()
        )),
    }
}

#[cfg(not(target_os = "macos"))]
pub(crate) fn write_launch_mode_marker(_game_path: &Path, _mode: LaunchMode) -> Result<(), String> {
    Ok(())
}

#[cfg(not(target_os = "macos"))]
pub(crate) fn read_launch_mode_marker(_game_path: &Path) -> Option<LaunchMode> {
    None
}

#[cfg(not(target_os = "macos"))]
pub(crate) fn remove_launch_mode_marker(_game_path: &Path) -> Result<(), String> {
    Ok(())
}

// ---------------------------------------------------------------------------
// Trampoline install / uninstall (macOS)
// ---------------------------------------------------------------------------

#[cfg(target_os = "macos")]
mod imp {
    use super::*;
    use std::process::Command;
    use tauri::Manager;

    /// The 3 JIT/library-validation entitlements that must live on the REAL binary
    /// (the process that runs Harmony / `mprotect` W+X). Byte-identical to the keys
    /// in `run_bepinex.sh`; a parity test guards against drift.
    pub(super) const TRAMPOLINE_ENTITLEMENTS: &str = r#"<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>com.apple.security.cs.allow-jit</key><true/>
    <key>com.apple.security.cs.allow-unsigned-executable-memory</key><true/>
    <key>com.apple.security.cs.disable-library-validation</key><true/>
</dict>
</plist>
"#;

    /// Resolved bundle paths derived from the bundle's current `CFBundleExecutable`.
    pub(super) struct BundleLayout {
        pub(super) app_path: PathBuf,
        pub(super) exe_path: PathBuf,
        pub(super) orig_path: PathBuf,
    }

    /// Read `CFBundleExecutable` and derive the stub / real-binary paths.
    pub(super) fn bundle_paths(game_path: &Path) -> Result<BundleLayout, String> {
        let app_path = game_path.join("TheBazaar.app");
        if !app_path.is_dir() {
            return Err(format!(
                "{} is not a valid game bundle (TheBazaar.app missing)",
                game_path.display()
            ));
        }
        let info_plist = app_path.join("Contents/Info.plist");
        let exe_name = read_cf_bundle_executable(&info_plist)?;
        let macos_dir = app_path.join("Contents/MacOS");
        let exe_path = macos_dir.join(&exe_name);
        let orig_path = macos_dir.join(format!("{exe_name}.orig"));
        Ok(BundleLayout {
            app_path,
            exe_path,
            orig_path,
        })
    }

    fn read_cf_bundle_executable(info_plist: &Path) -> Result<String, String> {
        let output = Command::new("plutil")
            .args(["-extract", "CFBundleExecutable", "raw", "-o", "-"])
            .arg(info_plist)
            .output()
            .map_err(|err| format!("Cannot run plutil on {}: {err}", info_plist.display()))?;
        if !output.status.success() {
            return Err(format!(
                "Cannot read CFBundleExecutable from {}: {}",
                info_plist.display(),
                String::from_utf8_lossy(&output.stderr).trim()
            ));
        }
        let name = String::from_utf8_lossy(&output.stdout).trim().to_string();
        if name.is_empty() {
            return Err(format!(
                "CFBundleExecutable is empty in {}",
                info_plist.display()
            ));
        }
        Ok(name)
    }

    /// The real Unity bootstrap links `UnityPlayer.dylib`; our tiny stub does not.
    /// Used to guard the rename (never rename a stray stub as if it were the game)
    /// and to detect the trampolined state.
    pub(super) fn links_unity(path: &Path) -> bool {
        if !path.exists() {
            return false;
        }
        let Ok(output) = Command::new("otool").arg("-L").arg(path).output() else {
            return false;
        };
        output.status.success()
            && String::from_utf8_lossy(&output.stdout).contains("UnityPlayer.dylib")
    }

    pub(super) fn command_available(command: &str, args: &[&str]) -> bool {
        Command::new(command).args(args).output().is_ok()
    }

    fn codesign_available() -> bool {
        // macOS `codesign` does not support `--version`; availability only means
        // the executable can be spawned. Real signing errors are reported later.
        command_available("codesign", &["-h"])
    }

    fn stub_resource_path(app: &AppHandle) -> Result<PathBuf, String> {
        let stub = app
            .path()
            .resource_dir()
            .map_err(|err| err.to_string())?
            .join("Trampoline/bpp_launcher");
        if !stub.exists() {
            return Err(format!(
                "Bundled trampoline stub is missing at {}",
                stub.display()
            ));
        }
        Ok(stub)
    }

    #[derive(Debug, Clone, Copy, PartialEq, Eq)]
    pub(super) enum RealBinarySource {
        CurrentExe,
        ExistingOrig,
    }

    /// Decide which file holds the real Unity binary so [`install_trampoline`]
    /// preserves exactly it as `.orig`. A live `<exe>` that links Unity always
    /// wins — this covers a fresh install AND a Steam update/Verify that wrote a
    /// NEW real binary at `<exe>` on top of a STALE `<exe>.orig` from a prior
    /// trampoline (where re-running Repair must keep the fresh binary, never
    /// re-stub over it and exec the stale one). Only when `<exe>` is not the real
    /// binary do we fall back to an existing, genuinely-real `.orig`; anything
    /// else is corrupt and only Steam can re-supply the binary.
    pub(super) fn classify_real_binary(
        exe_is_real: bool,
        orig_exists: bool,
        orig_is_real: bool,
    ) -> Result<RealBinarySource, String> {
        if exe_is_real {
            Ok(RealBinarySource::CurrentExe)
        } else if orig_exists && orig_is_real {
            Ok(RealBinarySource::ExistingOrig)
        } else if orig_exists {
            Err(
                "The game binary backup (.orig) exists but is not the real Unity binary. Run Steam \"Verify integrity of game files\" and reinstall."
                    .to_string(),
            )
        } else {
            Err(
                "The game's main executable is not the real Unity binary and no backup exists. Run Steam \"Verify integrity of game files\"."
                    .to_string(),
            )
        }
    }

    /// Mechanical filesystem swap (no signing, no Unity check — callers guard
    /// first): preserve the real binary as `.orig` once, then drop the stub in
    /// its place. Idempotent — never clobbers an existing `.orig`.
    pub(super) fn swap_in_stub(layout: &BundleLayout, stub_src: &Path) -> Result<(), String> {
        if !layout.orig_path.exists() {
            std::fs::rename(&layout.exe_path, &layout.orig_path).map_err(|err| {
                format!(
                    "Cannot rename {} to {}: {err}",
                    layout.exe_path.display(),
                    layout.orig_path.display()
                )
            })?;
        }
        install_stub(stub_src, &layout.exe_path)
    }

    /// Inverse of [`swap_in_stub`]: drop the stub and move the real binary back.
    pub(super) fn restore_vanilla_layout(layout: &BundleLayout) -> Result<(), String> {
        if layout.exe_path.exists() && layout.orig_path.exists() {
            // exe is the stub copy; remove it so the rename can take its place.
            let _ = std::fs::remove_file(&layout.exe_path);
        }
        if layout.orig_path.exists() {
            std::fs::rename(&layout.orig_path, &layout.exe_path).map_err(|err| {
                format!(
                    "Cannot restore {} from {}: {err}",
                    layout.exe_path.display(),
                    layout.orig_path.display()
                )
            })?;
        }
        Ok(())
    }

    fn install_stub(stub_src: &Path, exe_dst: &Path) -> Result<(), String> {
        use std::os::unix::fs::PermissionsExt;

        std::fs::copy(stub_src, exe_dst).map_err(|err| {
            format!(
                "Cannot install trampoline stub to {}: {err}",
                exe_dst.display()
            )
        })?;
        std::fs::set_permissions(exe_dst, std::fs::Permissions::from_mode(0o755))
            .map_err(|err| format!("Cannot set permissions on {}: {err}", exe_dst.display()))?;
        // Best-effort: an AMFI-relevant quarantine on the bundle main could block
        // launch on macOS 27. The installer is notarized, so this is defensive.
        let _ = Command::new("xattr")
            .args(["-d", "com.apple.quarantine"])
            .arg(exe_dst)
            .output();
        Ok(())
    }

    /// Clear the exec bits on `run_bepinex.sh` so its `--deep` re-sign (which would
    /// strip the `.orig`'s entitlements) can never run while trampolined.
    pub(super) fn disable_prefix_launcher(game_path: &Path) {
        use std::os::unix::fs::PermissionsExt;

        let script = game_path.join("run_bepinex.sh");
        if let Ok(metadata) = std::fs::metadata(&script) {
            let mut permissions = metadata.permissions();
            permissions.set_mode(permissions.mode() & !0o111);
            let _ = std::fs::set_permissions(&script, permissions);
        }
    }

    fn sign_real_binary(orig: &Path) -> Result<(), String> {
        let entitlements = tempfile::Builder::new()
            .prefix("bpp-ents-")
            .suffix(".plist")
            .tempfile()
            .map_err(|err| format!("Cannot create entitlements temp file: {err}"))?;
        std::fs::write(entitlements.path(), TRAMPOLINE_ENTITLEMENTS)
            .map_err(|err| format!("Cannot write entitlements: {err}"))?;
        run_codesign(
            &[
                "--force".as_ref(),
                "--sign".as_ref(),
                "-".as_ref(),
                "--entitlements".as_ref(),
                entitlements.path().as_os_str(),
                orig.as_os_str(),
            ],
            &format!("sign {} with JIT entitlements", orig.display()),
        )
    }

    fn seal_bundle(app: &Path) -> Result<(), String> {
        // NO --deep: re-signing the bundle main + sealing resources, without
        // re-signing the nested `.orig` (which would strip its entitlements).
        run_codesign(
            &[
                "--force".as_ref(),
                "--sign".as_ref(),
                "-".as_ref(),
                app.as_os_str(),
            ],
            &format!("seal {}", app.display()),
        )
    }

    fn verify_bundle(app: &Path) -> Result<(), String> {
        run_codesign(
            &[
                "--verify".as_ref(),
                "--deep".as_ref(),
                "--strict".as_ref(),
                app.as_os_str(),
            ],
            &format!("verify {}", app.display()),
        )
    }

    fn run_codesign(args: &[&std::ffi::OsStr], context: &str) -> Result<(), String> {
        let output = Command::new("codesign")
            .args(args)
            .output()
            .map_err(|err| format!("Cannot run codesign to {context}: {err}"))?;
        if output.status.success() {
            Ok(())
        } else {
            Err(format!(
                "codesign failed to {context}: {}",
                String::from_utf8_lossy(&output.stderr).trim()
            ))
        }
    }

    pub(super) fn is_trampolined(game_path: &Path) -> Result<bool, String> {
        let layout = bundle_paths(game_path)?;
        if !layout.orig_path.exists() {
            return Ok(false);
        }
        Ok(layout.exe_path.exists()
            && !links_unity(&layout.exe_path)
            && links_unity(&layout.orig_path))
    }

    pub(super) fn install_trampoline(app: &AppHandle, game_path: &Path) -> Result<(), String> {
        // Step 0: absolute preconditions BEFORE any filesystem mutation — a
        // modified-but-unsigned bundle is AMFI-killed on Apple Silicon.
        if !codesign_available() {
            return Err(
                "codesign is unavailable; cannot install the macOS launch trampoline. Install the Xcode command line tools and retry."
                    .to_string(),
            );
        }
        // Best-effort guard. Currently a no-op on macOS (is_bazaar_running_best_effort
        // returns false there); real protection comes from the orchestrator closing
        // Steam first, which takes down a Steam-launched Bazaar. Kept so a future
        // macOS process probe activates it automatically.
        if crate::services::game_process::is_bazaar_running_best_effort() {
            return Err(
                "The Bazaar is running. Close the game before installing BazaarPlusPlus."
                    .to_string(),
            );
        }

        let layout = bundle_paths(game_path)?;
        let stub = stub_resource_path(app)?;

        // Step 1: already fully trampolined -> re-seal + verify only (idempotent).
        if is_trampolined(game_path)? {
            disable_prefix_launcher(game_path);
            sign_real_binary(&layout.orig_path)?;
            seal_bundle(&layout.app_path)?;
            verify_bundle(&layout.app_path)?;
            return Ok(());
        }

        // Step 2: identify the real Unity binary and preserve exactly it as
        // `.orig`. Critically, a Steam update/Verify can leave a FRESH real binary
        // at <exe> on top of a STALE <exe>.orig; we keep the fresh one and discard
        // the stale backup, never the reverse (which would re-stub over the updated
        // binary and silently exec the old one).
        let exe_is_real = links_unity(&layout.exe_path);
        let orig_exists = layout.orig_path.exists();
        let orig_is_real = orig_exists && links_unity(&layout.orig_path);
        match classify_real_binary(exe_is_real, orig_exists, orig_is_real)? {
            RealBinarySource::CurrentExe => {
                // <exe> is the real binary (fresh install, or Steam-updated over a
                // stale backup). Drop any stale `.orig` so the swap renames the
                // CURRENT binary into `.orig` instead of clobbering it.
                if orig_exists {
                    std::fs::remove_file(&layout.orig_path).map_err(|err| {
                        format!("Cannot remove stale {}: {err}", layout.orig_path.display())
                    })?;
                }
            }
            RealBinarySource::ExistingOrig => {
                // <exe> is our stub / a partial copy; the real binary is already
                // safely preserved as `.orig` (recover a prior interrupted run).
            }
        }

        // Steps 3-7 with rollback on any failure.
        let result = (|| -> Result<(), String> {
            swap_in_stub(&layout, &stub)?; // rename real -> .orig (if needed) + drop stub
            disable_prefix_launcher(game_path);
            sign_real_binary(&layout.orig_path)?;
            seal_bundle(&layout.app_path)?;
            verify_bundle(&layout.app_path)?;
            Ok(())
        })();

        match result {
            Ok(()) => Ok(()),
            Err(err) => match restore_vanilla_layout(&layout) {
                Ok(()) => {
                    // Re-seal so a rollback that runs after sign/seal still leaves a
                    // self-consistent (codesign --verify-clean) vanilla bundle.
                    // Best-effort: codesign was proven available at step 0.
                    let _ = seal_bundle(&layout.app_path);
                    Err(err)
                }
                Err(_) if layout.exe_path.exists() => Err(err),
                Err(restore_err) => Err(format!(
                    "{err}; additionally could not restore the original game binary: {restore_err}. Run Steam \"Verify integrity of game files\" to repair the bundle."
                )),
            },
        }
    }

    pub(super) fn uninstall_trampoline(game_path: &Path) -> Result<(), String> {
        let layout = bundle_paths(game_path)?;

        if layout.orig_path.exists() {
            if !codesign_available() {
                return Err(
                    "codesign is unavailable; cannot restore the vanilla game bundle. Install the Xcode command line tools and retry."
                        .to_string(),
                );
            }
            restore_vanilla_layout(&layout)?;
            // Re-seal so the bundle signature matches its now-real main executable.
            seal_bundle(&layout.app_path)?;
            return Ok(());
        }

        // .orig missing: either already vanilla (Steam verify reverted) or broken.
        if links_unity(&layout.exe_path) {
            return Ok(());
        }
        Err(format!(
            "The game binary backup is missing ({} not found) and {} is still the trampoline stub. Run Steam \"Verify integrity of game files\" to restore the game.",
            layout.orig_path.display(),
            layout.exe_path.display()
        ))
    }
}

#[cfg(target_os = "macos")]
pub(crate) fn is_trampolined(game_path: &Path) -> Result<bool, String> {
    imp::is_trampolined(game_path)
}

#[cfg(target_os = "macos")]
pub(crate) fn install_trampoline(app: &AppHandle, game_path: &Path) -> Result<(), String> {
    imp::install_trampoline(app, game_path)
}

#[cfg(target_os = "macos")]
pub(crate) fn uninstall_trampoline(game_path: &Path) -> Result<(), String> {
    imp::uninstall_trampoline(game_path)
}

#[cfg(not(target_os = "macos"))]
pub(crate) fn is_trampolined(_game_path: &Path) -> Result<bool, String> {
    Ok(false)
}

#[cfg(not(target_os = "macos"))]
pub(crate) fn install_trampoline(_app: &AppHandle, _game_path: &Path) -> Result<(), String> {
    Ok(())
}

#[cfg(not(target_os = "macos"))]
pub(crate) fn uninstall_trampoline(_game_path: &Path) -> Result<(), String> {
    Ok(())
}

#[cfg(test)]
#[cfg(target_os = "macos")]
mod tests {
    use super::imp::{
        bundle_paths, classify_real_binary, command_available, is_trampolined,
        restore_vanilla_layout, swap_in_stub, RealBinarySource, TRAMPOLINE_ENTITLEMENTS,
    };
    use super::*;

    /// Build a minimal `TheBazaar.app` fixture with a fake main executable.
    fn make_bundle(real_contents: &[u8]) -> tempfile::TempDir {
        let tmp = tempfile::tempdir().unwrap();
        let macos_dir = tmp.path().join("TheBazaar.app/Contents/MacOS");
        std::fs::create_dir_all(&macos_dir).unwrap();
        std::fs::write(
            tmp.path().join("TheBazaar.app/Contents/Info.plist"),
            r#"<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key><string>The Bazaar</string>
</dict>
</plist>
"#,
        )
        .unwrap();
        std::fs::write(macos_dir.join("The Bazaar"), real_contents).unwrap();
        tmp
    }

    #[test]
    fn test_bundle_paths_reads_cf_bundle_executable() {
        let tmp = make_bundle(b"real");
        let layout = bundle_paths(tmp.path()).unwrap();
        assert!(layout.exe_path.ends_with("Contents/MacOS/The Bazaar"));
        assert!(layout.orig_path.ends_with("Contents/MacOS/The Bazaar.orig"));
    }

    #[test]
    fn test_is_trampolined_is_false_for_vanilla_bundle() {
        let tmp = make_bundle(b"real");
        // No `.orig` -> not trampolined; short-circuits before any otool call.
        assert!(!is_trampolined(tmp.path()).unwrap());
    }

    #[test]
    fn test_classify_real_binary_prefers_live_exe_over_stale_orig() {
        // Fresh install: exe is real, no backup.
        assert_eq!(
            classify_real_binary(true, false, false).unwrap(),
            RealBinarySource::CurrentExe
        );
        // Steam-updated binary at <exe> on top of a STALE real .orig — keep <exe>.
        assert_eq!(
            classify_real_binary(true, true, true).unwrap(),
            RealBinarySource::CurrentExe
        );
        // Interrupted prior run: exe is the stub, .orig is the preserved real binary.
        assert_eq!(
            classify_real_binary(false, true, true).unwrap(),
            RealBinarySource::ExistingOrig
        );
        // Corrupt: neither side is the real binary.
        assert!(classify_real_binary(false, true, false).is_err());
        assert!(classify_real_binary(false, false, false).is_err());
    }

    #[test]
    fn test_command_available_only_requires_spawn_success() {
        assert!(command_available("/bin/sh", &["-c", "exit 7"]));
        assert!(!command_available(
            "/definitely/not/a/bpp-installer-command",
            &[]
        ));
    }

    #[test]
    fn test_swap_in_stub_preserves_real_binary_and_is_idempotent() {
        let tmp = make_bundle(b"REAL-UNITY");
        let layout = bundle_paths(tmp.path()).unwrap();
        let stub = tmp.path().join("stub");
        std::fs::write(&stub, b"STUB").unwrap();

        swap_in_stub(&layout, &stub).unwrap();
        assert_eq!(std::fs::read(&layout.exe_path).unwrap(), b"STUB");
        assert_eq!(std::fs::read(&layout.orig_path).unwrap(), b"REAL-UNITY");

        // Second call must NOT clobber the preserved real binary.
        std::fs::write(&stub, b"STUB2").unwrap();
        swap_in_stub(&layout, &stub).unwrap();
        assert_eq!(std::fs::read(&layout.exe_path).unwrap(), b"STUB2");
        assert_eq!(std::fs::read(&layout.orig_path).unwrap(), b"REAL-UNITY");
    }

    #[test]
    fn test_restore_vanilla_layout_moves_real_binary_back() {
        let tmp = make_bundle(b"REAL-UNITY");
        let layout = bundle_paths(tmp.path()).unwrap();
        let stub = tmp.path().join("stub");
        std::fs::write(&stub, b"STUB").unwrap();

        swap_in_stub(&layout, &stub).unwrap();
        restore_vanilla_layout(&layout).unwrap();

        assert_eq!(std::fs::read(&layout.exe_path).unwrap(), b"REAL-UNITY");
        assert!(!layout.orig_path.exists());
    }

    #[test]
    fn test_launch_mode_marker_round_trip() {
        let tmp = tempfile::tempdir().unwrap();
        assert_eq!(read_launch_mode_marker(tmp.path()), None);

        write_launch_mode_marker(tmp.path(), LaunchMode::Trampoline).unwrap();
        assert_eq!(
            read_launch_mode_marker(tmp.path()),
            Some(LaunchMode::Trampoline)
        );

        write_launch_mode_marker(tmp.path(), LaunchMode::Prefix).unwrap();
        assert_eq!(
            read_launch_mode_marker(tmp.path()),
            Some(LaunchMode::Prefix)
        );

        remove_launch_mode_marker(tmp.path()).unwrap();
        assert_eq!(read_launch_mode_marker(tmp.path()), None);
        // Removing a missing marker is a no-op success.
        remove_launch_mode_marker(tmp.path()).unwrap();
    }

    #[test]
    fn test_entitlements_match_run_bepinex_script() {
        // Keep the trampoline's entitlements byte-aligned with the prefix path so
        // the two launch modes grant the real binary the SAME capabilities.
        let script = include_str!("../../../resources/SourceForBuild/macos/run_bepinex.sh");
        for key in [
            "com.apple.security.cs.allow-jit",
            "com.apple.security.cs.allow-unsigned-executable-memory",
            "com.apple.security.cs.disable-library-validation",
        ] {
            assert!(
                TRAMPOLINE_ENTITLEMENTS.contains(key),
                "trampoline entitlements missing {key}"
            );
            assert!(script.contains(key), "run_bepinex.sh missing {key}");
        }
        // Also assert COUNT parity so a 4th capability added to only one side is
        // caught (containment alone would miss it).
        let needle = "com.apple.security.cs.";
        assert_eq!(
            TRAMPOLINE_ENTITLEMENTS.matches(needle).count(),
            script.matches(needle).count(),
            "trampoline entitlements and run_bepinex.sh have a different number of capability keys"
        );
    }
}

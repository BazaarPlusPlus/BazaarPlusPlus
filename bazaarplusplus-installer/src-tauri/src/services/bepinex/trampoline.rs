//! macOS in-bundle Mach-O launch trampoline.
//!
//! The trampoline is the only macOS launch bootstrap. This module installs a
//! tiny arm64 stub as the bundle's `CFBundleExecutable`, renames the real Unity
//! bootstrap to `<exe>.orig`, and re-signs the real binary with the JIT
//! entitlements so Harmony can write executable memory.
//!
//! Every behaviour here is macOS-only; the public API has no-op / `false` stubs on
//! other platforms so the install orchestrator can call it unconditionally.

use std::path::Path;

#[cfg(target_os = "macos")]
use std::path::PathBuf;

use tauri::AppHandle;

#[cfg(target_os = "macos")]
const OBSOLETE_MACOS_ARTIFACTS: &[&str] = &["run_bepinex.sh", "bpp_launcher.c", ".bpp-launch-mode"];

// ---------------------------------------------------------------------------
// Trampoline install / uninstall (macOS)
// ---------------------------------------------------------------------------

#[cfg(target_os = "macos")]
mod imp {
    use super::*;
    use std::process::Command;
    use tauri::Manager;

    /// The 3 JIT/library-validation entitlements that must live on the REAL binary
    /// (the process that runs Harmony / `mprotect` W+X).
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

    pub(super) fn stub_resource_path(app: &AppHandle) -> Result<PathBuf, String> {
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

        // Step 1: already structurally trampolined -> refresh the stub from the
        // current installer, then re-sign and verify. A differing stub is not a
        // valid final state.
        if is_trampolined(game_path)? {
            install_stub(&stub, &layout.exe_path)?;
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
pub(crate) fn is_current_trampoline(app: &AppHandle, game_path: &Path) -> Result<bool, String> {
    if !imp::is_trampolined(game_path)? {
        return Ok(false);
    }
    let layout = imp::bundle_paths(game_path)?;
    let bundled = imp::stub_resource_path(app)?;
    trampoline_builds_match(&layout.exe_path, &bundled)
}

#[cfg(target_os = "macos")]
pub(crate) fn install_trampoline(app: &AppHandle, game_path: &Path) -> Result<(), String> {
    imp::install_trampoline(app, game_path)
}

#[cfg(target_os = "macos")]
pub(crate) fn uninstall_trampoline(game_path: &Path) -> Result<(), String> {
    imp::uninstall_trampoline(game_path)
}

#[cfg(target_os = "macos")]
pub(crate) fn obsolete_macos_artifacts_present(game_path: &Path) -> bool {
    OBSOLETE_MACOS_ARTIFACTS
        .iter()
        .any(|relative| game_path.join(relative).exists())
}

#[cfg(target_os = "macos")]
pub(crate) fn remove_obsolete_macos_artifacts(game_path: &Path) -> Result<(), String> {
    for relative in OBSOLETE_MACOS_ARTIFACTS {
        let path = game_path.join(relative);
        match std::fs::remove_file(&path) {
            Ok(()) => {}
            Err(err) if err.kind() == std::io::ErrorKind::NotFound => {}
            Err(err) => return Err(format!("Cannot remove obsolete {}: {err}", path.display())),
        }
    }
    Ok(())
}

#[cfg(target_os = "macos")]
fn trampoline_builds_match(left: &Path, right: &Path) -> Result<bool, String> {
    Ok(read_macho_uuid(left)? == read_macho_uuid(right)?)
}

#[cfg(target_os = "macos")]
fn read_macho_uuid(path: &Path) -> Result<[u8; 16], String> {
    const MACH_HEADER_64_SIZE: usize = 32;
    const MH_MAGIC_64: u32 = 0xfeedfacf;
    const LC_UUID: u32 = 0x1b;
    const LC_UUID_SIZE: usize = 24;

    fn read_u32(bytes: &[u8], offset: usize) -> Option<u32> {
        let value = bytes.get(offset..offset.checked_add(4)?)?;
        Some(u32::from_le_bytes(value.try_into().ok()?))
    }

    let bytes = std::fs::read(path)
        .map_err(|err| format!("Cannot read trampoline {}: {err}", path.display()))?;
    if read_u32(&bytes, 0) != Some(MH_MAGIC_64) {
        return Err(format!(
            "Trampoline {} is not a 64-bit little-endian Mach-O image",
            path.display()
        ));
    }

    let command_count = read_u32(&bytes, 16).ok_or_else(|| {
        format!(
            "Trampoline {} has an incomplete Mach-O header",
            path.display()
        )
    })?;
    let command_bytes = read_u32(&bytes, 20).ok_or_else(|| {
        format!(
            "Trampoline {} has an incomplete Mach-O header",
            path.display()
        )
    })? as usize;
    let commands_end = MACH_HEADER_64_SIZE
        .checked_add(command_bytes)
        .filter(|end| *end <= bytes.len())
        .ok_or_else(|| {
            format!(
                "Trampoline {} has invalid Mach-O load commands",
                path.display()
            )
        })?;

    let mut offset = MACH_HEADER_64_SIZE;
    for _ in 0..command_count {
        let command = read_u32(&bytes, offset).ok_or_else(|| {
            format!(
                "Trampoline {} has an incomplete Mach-O load command",
                path.display()
            )
        })?;
        let command_size = read_u32(&bytes, offset + 4).ok_or_else(|| {
            format!(
                "Trampoline {} has an incomplete Mach-O load command",
                path.display()
            )
        })? as usize;
        let next_offset = offset
            .checked_add(command_size)
            .filter(|next| command_size >= 8 && *next <= commands_end)
            .ok_or_else(|| {
                format!(
                    "Trampoline {} has an invalid Mach-O load command",
                    path.display()
                )
            })?;

        if command == LC_UUID {
            if command_size < LC_UUID_SIZE {
                return Err(format!(
                    "Trampoline {} has an invalid Mach-O UUID command",
                    path.display()
                ));
            }
            return bytes[offset + 8..offset + LC_UUID_SIZE]
                .try_into()
                .map_err(|_| format!("Cannot read Mach-O UUID from {}", path.display()));
        }

        offset = next_offset;
    }

    Err(format!(
        "Trampoline {} has no Mach-O build UUID",
        path.display()
    ))
}

#[cfg(not(target_os = "macos"))]
pub(crate) fn is_current_trampoline(_app: &AppHandle, _game_path: &Path) -> Result<bool, String> {
    Ok(true)
}

#[cfg(not(target_os = "macos"))]
pub(crate) fn install_trampoline(_app: &AppHandle, _game_path: &Path) -> Result<(), String> {
    Ok(())
}

#[cfg(not(target_os = "macos"))]
pub(crate) fn uninstall_trampoline(_game_path: &Path) -> Result<(), String> {
    Ok(())
}

#[cfg(not(target_os = "macos"))]
pub(crate) fn obsolete_macos_artifacts_present(_game_path: &Path) -> bool {
    false
}

#[cfg(not(target_os = "macos"))]
pub(crate) fn remove_obsolete_macos_artifacts(_game_path: &Path) -> Result<(), String> {
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

    fn write_macho_stub(path: &Path, uuid: [u8; 16], signature: &[u8]) {
        const MACH_HEADER_64_SIZE: usize = 32;
        const LC_UUID: u32 = 0x1b;
        const LC_UUID_SIZE: u32 = 24;

        let mut bytes = vec![0; MACH_HEADER_64_SIZE];
        bytes[0..4].copy_from_slice(&0xfeedfacfu32.to_le_bytes());
        bytes[16..20].copy_from_slice(&1u32.to_le_bytes());
        bytes[20..24].copy_from_slice(&LC_UUID_SIZE.to_le_bytes());
        bytes.extend_from_slice(&LC_UUID.to_le_bytes());
        bytes.extend_from_slice(&LC_UUID_SIZE.to_le_bytes());
        bytes.extend_from_slice(&uuid);
        bytes.extend_from_slice(signature);
        std::fs::write(path, bytes).unwrap();
    }

    #[test]
    fn trampoline_build_identity_ignores_signatures_but_rejects_different_builds() {
        let tmp = tempfile::tempdir().unwrap();
        let bundled = tmp.path().join("bundled");
        let installed = tmp.path().join("installed");
        let outdated = tmp.path().join("outdated");

        write_macho_stub(&bundled, [7; 16], b"developer-id-signature");
        write_macho_stub(&installed, [7; 16], b"adhoc-bundle-signature");
        write_macho_stub(&outdated, [8; 16], b"adhoc-bundle-signature");

        assert!(trampoline_builds_match(&installed, &bundled).unwrap());
        assert!(!trampoline_builds_match(&outdated, &bundled).unwrap());
    }

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
    fn test_obsolete_macos_artifacts_are_detected_and_removed_without_parsing() {
        let tmp = tempfile::tempdir().unwrap();
        for relative in OBSOLETE_MACOS_ARTIFACTS {
            std::fs::write(tmp.path().join(relative), b"arbitrary old content").unwrap();
        }

        assert!(obsolete_macos_artifacts_present(tmp.path()));
        remove_obsolete_macos_artifacts(tmp.path()).unwrap();
        assert!(!obsolete_macos_artifacts_present(tmp.path()));
        remove_obsolete_macos_artifacts(tmp.path()).unwrap();
    }

    #[test]
    fn test_trampoline_entitlements_are_complete() {
        for key in [
            "com.apple.security.cs.allow-jit",
            "com.apple.security.cs.allow-unsigned-executable-memory",
            "com.apple.security.cs.disable-library-validation",
        ] {
            assert!(
                TRAMPOLINE_ENTITLEMENTS.contains(key),
                "trampoline entitlements missing {key}"
            );
        }
        assert_eq!(
            TRAMPOLINE_ENTITLEMENTS
                .matches("com.apple.security.cs.")
                .count(),
            3
        );
    }
}

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
    /// and to detect the trampolined state. The Mach-O is parsed in-process:
    /// `otool` is an Xcode shim that fails until the Xcode license is accepted,
    /// and a probe that cannot run must never read as "not the real binary".
    pub(super) fn links_unity(path: &Path) -> Result<bool, String> {
        let bytes = match std::fs::read(path) {
            Ok(bytes) => bytes,
            Err(err) if err.kind() == std::io::ErrorKind::NotFound => return Ok(false),
            Err(err) => {
                return Err(format!(
                    "Cannot read game executable {}: {err}",
                    path.display()
                ))
            }
        };
        Ok(macho_images(&bytes)
            .into_iter()
            .any(|(_, image)| image_links_unity(image)))
    }

    /// Whether the executable carries an arm64 image, thin or as a universal slice.
    /// Parsed in-process for the same reason as [`links_unity`]: `lipo` is an Xcode shim.
    pub(super) fn has_arm64(path: &Path) -> Result<bool, String> {
        let bytes = std::fs::read(path)
            .map_err(|err| format!("Cannot inspect game architecture: {err}"))?;
        Ok(macho_images(&bytes)
            .into_iter()
            .any(|(cpu_type, _)| cpu_type == CPU_TYPE_ARM64))
    }

    fn command_available(command: &str, args: &[&str]) -> bool {
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
        let game_root = exe_dst
            .ancestors()
            .nth(4)
            .ok_or_else(|| format!("Invalid game executable path: {}", exe_dst.display()))?;
        let staged = tempfile::Builder::new()
            .prefix(".bpp-stub-")
            .tempfile_in(game_root)
            .map_err(|err| format!("Cannot stage game executable: {err}"))?;
        std::fs::copy(stub_src, staged.path()).map_err(|err| {
            format!(
                "Cannot install trampoline stub to {}: {err}",
                exe_dst.display()
            )
        })?;
        std::fs::set_permissions(staged.path(), std::fs::Permissions::from_mode(0o755))
            .map_err(|err| format!("Cannot set permissions on {}: {err}", exe_dst.display()))?;
        // Best-effort: an AMFI-relevant quarantine on the bundle main could block
        // launch on macOS 27. The installer is notarized, so this is defensive.
        let _ = Command::new("xattr")
            .args(["-d", "com.apple.quarantine"])
            .arg(staged.path())
            .output();
        staged
            .persist(exe_dst)
            .map_err(|err| format!("Cannot replace {}: {err}", exe_dst.display()))?;
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

    pub(super) fn seal_bundle(app: &Path) -> Result<(), String> {
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

    pub(super) fn verify_bundle(app: &Path) -> Result<(), String> {
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
            && !links_unity(&layout.exe_path)?
            && links_unity(&layout.orig_path)?)
    }

    pub(super) fn install_trampoline(resource_dir: &Path, game_path: &Path) -> Result<(), String> {
        let stub = resource_dir.join("Trampoline/bpp_launcher");
        install_with_stub(game_path, &stub)
    }

    pub(super) fn install_with_stub(game_path: &Path, stub: &Path) -> Result<(), String> {
        install_with_finalizer(game_path, stub, |layout| {
            sign_real_binary(&layout.orig_path)?;
            seal_bundle(&layout.app_path)?;
            verify_bundle(&layout.app_path)
        })
    }

    pub(super) fn install_with_finalizer(
        game_path: &Path,
        stub: &Path,
        finalize: impl FnOnce(&BundleLayout) -> Result<(), String>,
    ) -> Result<(), String> {
        // Step 0: absolute preconditions BEFORE any filesystem mutation — a
        // modified-but-unsigned bundle is AMFI-killed on Apple Silicon.
        if !codesign_available() {
            return Err(
                "codesign is unavailable; cannot install the macOS launch trampoline. Install the Xcode command line tools and retry."
                    .to_string(),
            );
        }
        crate::services::game_process::ensure_game_stopped(
            game_path,
            crate::services::game_process::GameStoppedOperation::Install,
        )?;

        let layout = bundle_paths(game_path)?;

        // Step 2: identify the real Unity binary and preserve exactly it as
        // `.orig`. Critically, a Steam update/Verify can leave a FRESH real binary
        // at <exe> on top of a STALE <exe>.orig; we keep the fresh one and discard
        // the stale backup, never the reverse (which would re-stub over the updated
        // binary and silently exec the old one).
        let exe_is_real = links_unity(&layout.exe_path)?;
        let orig_exists = layout.orig_path.exists();
        let orig_is_real = orig_exists && links_unity(&layout.orig_path)?;
        let source = classify_real_binary(exe_is_real, orig_exists, orig_is_real)?;
        let real = match source {
            RealBinarySource::CurrentExe => &layout.exe_path,
            RealBinarySource::ExistingOrig => &layout.orig_path,
        };
        if !has_arm64(real)? {
            return Err("The game executable has no arm64 architecture; restore the Steam game before repairing.".to_string());
        }
        if !stub.is_file() {
            return Err(format!(
                "Bundled trampoline stub is missing at {}",
                stub.display()
            ));
        }
        read_macho_uuid(stub)?;
        super::super::bundle_root::normalize(game_path)?;
        // Preserve the selected current game binary before codesign can rewrite it.
        // Initial installs and already-installed repairs share this recovery path.
        let recovery = tempfile::tempdir_in(game_path)
            .map_err(|err| format!("Cannot prepare game binary recovery: {err}"))?;
        let recovery_exe = recovery.path().join("game-executable");
        std::fs::copy(real, &recovery_exe)
            .map_err(|err| format!("Cannot back up game executable: {err}"))?;
        match source {
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
            swap_in_stub(&layout, stub)?; // rename real -> .orig (if needed) + drop stub
            finalize(&layout)
        })();

        match result {
            Ok(()) => Ok(()),
            Err(err) => {
                let rollback = (|| {
                    install_stub(&recovery_exe, &layout.exe_path)?;
                    if layout.orig_path.exists() {
                        std::fs::remove_file(&layout.orig_path)
                            .map_err(|error| error.to_string())?;
                    }
                    seal_bundle(&layout.app_path)?;
                    verify_bundle(&layout.app_path)
                })();
                match rollback {
                    Ok(()) => Err(err),
                    Err(restore_err) => {
                        let retained = recovery.keep();
                        Err(format!("{err}; game recovery also failed: {restore_err}. The executable backup is retained at {}. Run Steam \"Verify integrity of game files\".", retained.display()))
                    }
                }
            }
        }
    }

    pub(super) fn uninstall_trampoline(game_path: &Path) -> Result<(), String> {
        let layout = bundle_paths(game_path)?;
        super::super::bundle_root::normalize(game_path)?;

        if layout.orig_path.exists() {
            if !codesign_available() {
                return Err(
                    "codesign is unavailable; cannot restore the vanilla game bundle. Install the Xcode command line tools and retry."
                        .to_string(),
                );
            }
            restore_vanilla_layout(&layout)?;
            // The caller seals after removing all owned in-bundle payload files.
            return Ok(());
        }

        // .orig missing: either already vanilla (Steam verify reverted) or broken.
        if links_unity(&layout.exe_path)? {
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
    is_current_with_stub(game_path, &imp::stub_resource_path(app)?)
}

#[cfg(target_os = "macos")]
fn is_current_with_stub(game_path: &Path, bundled: &Path) -> Result<bool, String> {
    if !imp::is_trampolined(game_path)? {
        return Ok(false);
    }
    let layout = imp::bundle_paths(game_path)?;
    Ok(trampoline_builds_match(&layout.exe_path, bundled)?
        && imp::verify_bundle(&layout.app_path).is_ok())
}

#[cfg(target_os = "macos")]
pub(crate) fn finalize_game_bundle(game_path: &Path) -> Result<(), String> {
    super::bundle_root::normalize(game_path)?;
    let layout = imp::bundle_paths(game_path)?;
    imp::seal_bundle(&layout.app_path)?;
    imp::verify_bundle(&layout.app_path)
}

#[cfg(not(target_os = "macos"))]
pub(crate) fn finalize_game_bundle(_game_path: &Path) -> Result<(), String> {
    Ok(())
}

pub(super) fn with_finalized_bundle(
    game_path: &Path,
    operation: impl FnOnce() -> Result<(), String>,
) -> Result<(), String> {
    let result = operation();
    match (result, finalize_game_bundle(game_path)) {
        (result, Ok(())) => result,
        (Ok(()), Err(error)) => Err(error),
        (Err(error), Err(final_error)) => Err(format!(
            "{error}; final game bundle signing also failed: {final_error}"
        )),
    }
}

#[cfg(target_os = "macos")]
pub(crate) fn install_trampoline(resource_dir: &Path, game_path: &Path) -> Result<(), String> {
    imp::install_trampoline(resource_dir, game_path)
}

#[cfg(target_os = "macos")]
pub(super) fn repair_with_stub(game_path: &Path, stub: &Path) -> Result<(), String> {
    imp::install_with_stub(game_path, stub)
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
const MH_MAGIC_64: u32 = 0xfeedfacf;
#[cfg(target_os = "macos")]
const CPU_TYPE_ARM64: u32 = 0x0100_000c;

#[cfg(target_os = "macos")]
fn read_u32_le(bytes: &[u8], offset: usize) -> Option<u32> {
    let value = bytes.get(offset..offset.checked_add(4)?)?;
    Some(u32::from_le_bytes(value.try_into().ok()?))
}

#[cfg(target_os = "macos")]
fn read_u32_be(bytes: &[u8], offset: usize) -> Option<u32> {
    let value = bytes.get(offset..offset.checked_add(4)?)?;
    Some(u32::from_be_bytes(value.try_into().ok()?))
}

#[cfg(target_os = "macos")]
fn read_u64_be(bytes: &[u8], offset: usize) -> Option<u64> {
    let value = bytes.get(offset..offset.checked_add(8)?)?;
    Some(u64::from_be_bytes(value.try_into().ok()?))
}

/// Every 64-bit little-endian Mach-O image in `bytes` as `(cputype, image)`: the
/// file itself when thin, or each slice of a universal binary. Anything else,
/// including a truncated or malformed header, yields no images.
#[cfg(target_os = "macos")]
fn macho_images(bytes: &[u8]) -> Vec<(u32, &[u8])> {
    const FAT_MAGIC: u32 = 0xcafebabe;
    const FAT_MAGIC_64: u32 = 0xcafebabf;
    const FAT_HEADER_SIZE: usize = 8;

    fn thin(image: &[u8]) -> Option<(u32, &[u8])> {
        (read_u32_le(image, 0)? == MH_MAGIC_64).then_some((read_u32_le(image, 4)?, image))
    }

    let wide = match read_u32_be(bytes, 0) {
        Some(FAT_MAGIC) => false,
        Some(FAT_MAGIC_64) => true,
        _ => return thin(bytes).into_iter().collect(),
    };
    let entry_size = if wide { 32 } else { 20 };
    let declared = read_u32_be(bytes, 4).unwrap_or(0) as usize;
    let present = bytes.len().saturating_sub(FAT_HEADER_SIZE) / entry_size;
    (0..declared.min(present))
        .filter_map(|index| {
            let entry = FAT_HEADER_SIZE + index * entry_size;
            let (offset, size) = if wide {
                (
                    read_u64_be(bytes, entry + 8)?,
                    read_u64_be(bytes, entry + 16)?,
                )
            } else {
                (
                    u64::from(read_u32_be(bytes, entry + 8)?),
                    u64::from(read_u32_be(bytes, entry + 12)?),
                )
            };
            let start = usize::try_from(offset).ok()?;
            let end = start.checked_add(usize::try_from(size).ok()?)?;
            thin(bytes.get(start..end)?)
        })
        .collect()
}

/// The load commands of a thin 64-bit Mach-O image as `(cmd, command bytes)`.
#[cfg(target_os = "macos")]
fn load_commands(image: &[u8]) -> Result<Vec<(u32, &[u8])>, &'static str> {
    const MACH_HEADER_64_SIZE: usize = 32;

    let command_count = read_u32_le(image, 16).ok_or("an incomplete Mach-O header")?;
    let command_bytes = read_u32_le(image, 20).ok_or("an incomplete Mach-O header")? as usize;
    let commands_end = MACH_HEADER_64_SIZE
        .checked_add(command_bytes)
        .filter(|end| *end <= image.len())
        .ok_or("invalid Mach-O load commands")?;

    let mut commands = Vec::new();
    let mut offset = MACH_HEADER_64_SIZE;
    for _ in 0..command_count {
        let command = read_u32_le(image, offset).ok_or("an incomplete Mach-O load command")?;
        let command_size =
            read_u32_le(image, offset + 4).ok_or("an incomplete Mach-O load command")? as usize;
        let next_offset = offset
            .checked_add(command_size)
            .filter(|next| command_size >= 8 && *next <= commands_end)
            .ok_or("an invalid Mach-O load command")?;
        commands.push((command, &image[offset..next_offset]));
        offset = next_offset;
    }
    Ok(commands)
}

/// Whether a thin image loads `UnityPlayer.dylib` through any dylib load command.
#[cfg(target_os = "macos")]
fn image_links_unity(image: &[u8]) -> bool {
    const LC_REQ_DYLD: u32 = 0x8000_0000;
    const DYLIB_LOAD_COMMANDS: [u32; 5] = [
        0xc,                // LC_LOAD_DYLIB
        0x18 | LC_REQ_DYLD, // LC_LOAD_WEAK_DYLIB
        0x1f | LC_REQ_DYLD, // LC_REEXPORT_DYLIB
        0x20,               // LC_LAZY_LOAD_DYLIB
        0x23 | LC_REQ_DYLD, // LC_LOAD_UPWARD_DYLIB
    ];

    let Ok(commands) = load_commands(image) else {
        return false;
    };
    commands.into_iter().any(|(command, bytes)| {
        DYLIB_LOAD_COMMANDS.contains(&command)
            && read_u32_le(bytes, 8)
                .and_then(|name_offset| bytes.get(name_offset as usize..))
                .and_then(|name| name.split(|byte| *byte == 0).next())
                .is_some_and(|name| name.ends_with(b"UnityPlayer.dylib"))
    })
}

#[cfg(target_os = "macos")]
fn read_macho_uuid(path: &Path) -> Result<[u8; 16], String> {
    const LC_UUID: u32 = 0x1b;
    const LC_UUID_SIZE: usize = 24;

    let bytes = std::fs::read(path)
        .map_err(|err| format!("Cannot read trampoline {}: {err}", path.display()))?;
    if read_u32_le(&bytes, 0) != Some(MH_MAGIC_64) {
        return Err(format!(
            "Trampoline {} is not a 64-bit little-endian Mach-O image",
            path.display()
        ));
    }

    let commands = load_commands(&bytes)
        .map_err(|problem| format!("Trampoline {} has {problem}", path.display()))?;
    let (_, command) = commands
        .into_iter()
        .find(|(command, _)| *command == LC_UUID)
        .ok_or_else(|| format!("Trampoline {} has no Mach-O build UUID", path.display()))?;
    command
        .get(8..LC_UUID_SIZE)
        .and_then(|uuid| uuid.try_into().ok())
        .ok_or_else(|| {
            format!(
                "Trampoline {} has an invalid Mach-O UUID command",
                path.display()
            )
        })
}

#[cfg(not(target_os = "macos"))]
pub(crate) fn is_current_trampoline(_app: &AppHandle, _game_path: &Path) -> Result<bool, String> {
    Ok(true)
}

#[cfg(not(target_os = "macos"))]
pub(crate) fn install_trampoline(_resource_dir: &Path, _game_path: &Path) -> Result<(), String> {
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
        bundle_paths, classify_real_binary, is_trampolined, restore_vanilla_layout, swap_in_stub,
        RealBinarySource, TRAMPOLINE_ENTITLEMENTS,
    };
    use super::*;

    fn command(program: &str, args: &[&std::ffi::OsStr]) {
        let mut command = std::process::Command::new(program);
        if program == "clang" {
            let sdk = std::process::Command::new("xcrun")
                .args(["--sdk", "macosx", "--show-sdk-path"])
                .output()
                .unwrap();
            assert!(sdk.status.success());
            command.args(["-isysroot", String::from_utf8_lossy(&sdk.stdout).trim()]);
        }
        let output = command.args(args).output().unwrap();
        assert!(
            output.status.success(),
            "{program}: {}",
            String::from_utf8_lossy(&output.stderr)
        );
    }

    fn native_bundle() -> (tempfile::TempDir, PathBuf) {
        use super::super::bundle_root::tests::make_bundle;
        let game = tempfile::tempdir().unwrap();
        let app = make_bundle(game.path());
        let source = game.path().join("engine.c");
        std::fs::write(&source, "int unity_probe(void) { return 0; }").unwrap();
        let engine = app.join("Contents/Frameworks/UnityPlayer.dylib");
        command(
            "clang",
            &[
                "-arch".as_ref(),
                "arm64".as_ref(),
                "-dynamiclib".as_ref(),
                "-install_name".as_ref(),
                "@rpath/UnityPlayer.dylib".as_ref(),
                source.as_os_str(),
                "-o".as_ref(),
                engine.as_os_str(),
            ],
        );
        let mono = app.join("Contents/Frameworks/libmonobdwgc-2.0.dylib");
        std::fs::copy(&engine, &mono).unwrap();
        std::fs::write(
            &source,
            "extern int unity_probe(void); int main(void) { return unity_probe(); }",
        )
        .unwrap();
        let executable = app.join("Contents/MacOS/The Bazaar");
        command(
            "clang",
            &[
                "-arch".as_ref(),
                "arm64".as_ref(),
                source.as_os_str(),
                engine.as_os_str(),
                "-Wl,-rpath,@executable_path/../Frameworks".as_ref(),
                "-o".as_ref(),
                executable.as_os_str(),
            ],
        );
        imp::seal_bundle(&app).unwrap();
        imp::verify_bundle(&app).unwrap();
        let stub = game.path().join("stub");
        std::fs::write(&source, "int main(void) { return 0; }").unwrap();
        command(
            "clang",
            &[
                "-arch".as_ref(),
                "arm64".as_ref(),
                source.as_os_str(),
                "-o".as_ref(),
                stub.as_os_str(),
            ],
        );
        (game, stub)
    }

    #[test]
    fn native_repair_converges_after_repetition_steam_updates_and_resource_changes() {
        use super::super::bundle_root::tests::copy_tree;
        let (game, stub) = native_bundle();
        let layout = bundle_paths(game.path()).unwrap();
        std::fs::write(layout.app_path.join(".DS_Store"), b"Finder metadata").unwrap();
        let backups = game.path().join(".bpp-bundle-root-stash");
        std::fs::create_dir(&backups).unwrap();
        std::fs::write(backups.join(".DS_Store"), b"Legacy Finder metadata").unwrap();
        let clean = game.path().join("steam.app");
        copy_tree(&layout.app_path, &clean);
        copy_tree(&clean, &layout.app_path.join("TheBazaar_ARM64.app"));
        assert!(imp::verify_bundle(&layout.app_path)
            .unwrap_err()
            .contains("unsealed contents"));
        for _ in 0..3 {
            imp::install_with_stub(game.path(), &stub).unwrap();
            assert!(is_current_with_stub(game.path(), &stub).unwrap());
            assert!(imp::links_unity(&layout.orig_path).unwrap());
            assert!(!layout.app_path.join("TheBazaar_ARM64.app").exists());
        }
        let entitlements = std::process::Command::new("codesign")
            .args(["-d", "--entitlements", ":-"])
            .arg(&layout.orig_path)
            .output()
            .unwrap();
        let entitlements = format!(
            "{}{}",
            String::from_utf8_lossy(&entitlements.stdout),
            String::from_utf8_lossy(&entitlements.stderr)
        );
        assert!(entitlements.contains("com.apple.security.cs.allow-jit"));

        // Steam restores its executable and duplicate over the existing .orig.
        std::fs::copy(clean.join("Contents/MacOS/The Bazaar"), &layout.exe_path).unwrap();
        copy_tree(&clean, &layout.app_path.join("TheBazaar_ARM64.app"));
        assert!(!is_current_with_stub(game.path(), &stub).unwrap());
        imp::install_with_stub(game.path(), &stub).unwrap();
        assert!(is_current_with_stub(game.path(), &stub).unwrap());

        // Resource damage must change readiness even when the stub UUID matches.
        let resource = layout.app_path.join("Contents/Resources/Data/boot.config");
        std::fs::write(&resource, b"changed").unwrap();
        assert!(!is_current_with_stub(game.path(), &stub).unwrap());
        imp::install_with_stub(game.path(), &stub).unwrap();
        assert!(is_current_with_stub(game.path(), &stub).unwrap());

        // A shared-bootstrap uninstall removes an in-bundle plugin, then seals.
        std::fs::remove_file(&resource).unwrap();
        finalize_game_bundle(game.path()).unwrap();
        assert!(is_current_with_stub(game.path(), &stub).unwrap());
        let error = with_finalized_bundle(game.path(), || {
            imp::uninstall_trampoline(game.path())?;
            Err("simulated payload removal failure".into())
        })
        .unwrap_err();
        assert_eq!(error, "simulated payload removal failure");
        assert!(imp::links_unity(&layout.exe_path).unwrap());
        assert!(!layout.orig_path.exists());
        imp::verify_bundle(&layout.app_path).unwrap();
        assert_eq!(
            std::fs::read(layout.app_path.join(".DS_Store")).unwrap(),
            b"Finder metadata"
        );
        assert_eq!(
            std::fs::read(backups.join(".DS_Store")).unwrap(),
            b"Legacy Finder metadata"
        );
    }

    #[test]
    fn native_failed_repair_recovers_the_real_binary_and_can_be_retried() {
        let (game, stub) = native_bundle();
        imp::install_with_stub(game.path(), &stub).unwrap();
        let error = imp::install_with_finalizer(game.path(), &stub, |layout| {
            // Simulate a signing failure after the real binary has been rewritten.
            std::fs::write(&layout.orig_path, b"partly rewritten signature").unwrap();
            Err("injected signing failure".into())
        })
        .unwrap_err();
        assert_eq!(error, "injected signing failure");
        let layout = bundle_paths(game.path()).unwrap();
        assert!(imp::links_unity(&layout.exe_path).unwrap());
        assert!(!layout.orig_path.exists());
        imp::verify_bundle(&layout.app_path).unwrap();
        imp::install_with_stub(game.path(), &stub).unwrap();
        assert!(is_current_with_stub(game.path(), &stub).unwrap());
    }

    #[test]
    #[ignore = "macOS acceptance: run through `just installer::acceptance`; copies BPP_TEST_GAME_ROOT and never mutates it"]
    fn copied_steam_bundle_repair_acceptance() {
        let source =
            PathBuf::from(std::env::var_os("BPP_TEST_GAME_ROOT").expect("BPP_TEST_GAME_ROOT"));
        let resources = PathBuf::from(
            std::env::var_os("BPP_ACCEPTANCE_RESOURCE_DIR").expect("BPP_ACCEPTANCE_RESOURCE_DIR"),
        );
        let game = tempfile::tempdir().unwrap();
        let app = game.path().join("TheBazaar.app");
        command(
            "/bin/cp",
            &[
                "-cR".as_ref(),
                source.join("TheBazaar.app").as_os_str(),
                app.as_os_str(),
            ],
        );
        std::fs::copy(
            source.join("libdoorstop.dylib"),
            game.path().join("libdoorstop.dylib"),
        )
        .unwrap();
        std::fs::create_dir_all(game.path().join("BepInEx/plugins")).unwrap();
        std::fs::write(
            game.path().join("BepInEx/plugins/BazaarPlusPlus.dll"),
            b"developer-payload",
        )
        .unwrap();
        std::fs::create_dir_all(game.path().join("BazaarPlusPlusV5")).unwrap();
        std::fs::write(
            game.path().join("BazaarPlusPlusV5/history-sentinel"),
            b"user-data",
        )
        .unwrap();
        let stub = resources.join("Trampoline/bpp_launcher");
        for iteration in 1..=3 {
            imp::install_with_stub(game.path(), &stub).unwrap();
            assert!(is_current_with_stub(game.path(), &stub).unwrap());
            assert!(!app.join("TheBazaar_ARM64.app").exists());
            eprintln!("Copied Steam bundle repair {iteration}: strict signature and current stub verified");
        }

        // The game's own files differ between machines and game versions, so
        // the committed golden holds only the Payload Inventory paths present
        // after repair; the per-run manifest holds every file and its hash.
        let manifest = crate::goldens::tree_manifest(game.path());
        crate::goldens::write_run_manifest("copied_steam_bundle_repair_acceptance", &manifest);
        let inventory: serde_json::Value = serde_json::from_str(
            &std::fs::read_to_string(
                Path::new(env!("CARGO_MANIFEST_DIR")).join("../../release/payload.json"),
            )
            .unwrap(),
        )
        .unwrap();
        let mut bpp_paths: Vec<&str> = inventory["files"]
            .as_array()
            .unwrap()
            .iter()
            .filter(|file| {
                file["platforms"]
                    .as_array()
                    .is_none_or(|platforms| platforms.iter().any(|value| value == "macos"))
            })
            .filter_map(|file| file["path"].as_str())
            .filter(|path| std::fs::symlink_metadata(game.path().join(path)).is_ok())
            .collect();
        bpp_paths.sort_unstable();
        let layout = imp::bundle_paths(game.path()).unwrap();
        let tree = serde_json::json!({
            "bpp_paths": bpp_paths,
            "trampoline_uuid_matches_bundled": trampoline_builds_match(&layout.exe_path, &stub).unwrap(),
            "codesign_verified": imp::verify_bundle(&app).is_ok(),
        });
        crate::goldens::assert_golden(
            "acceptance/repaired-tree.json",
            &crate::goldens::json(&tree, &[]),
        );
        // The CLI is the developer deployment entry. Its manifest must match the
        // service outcome, and repeated repairs must preserve deployed DLLs/data.
        let cli = std::env::var_os("BPP_TEST_INSTALLER_CLI").expect("BPP_TEST_INSTALLER_CLI");
        let evidence = tempfile::tempdir().unwrap();
        let manifest_path = evidence.path().join("repair.json");
        for _ in 0..2 {
            let output = std::process::Command::new(&cli)
                .args(["repair", "--game"])
                .arg(game.path())
                .arg("--stub")
                .arg(&stub)
                .arg("--manifest")
                .arg(&manifest_path)
                .output()
                .unwrap();
            assert!(
                output.status.success(),
                "{}{}",
                String::from_utf8_lossy(&output.stdout),
                String::from_utf8_lossy(&output.stderr)
            );
            assert_eq!(
                std::fs::read_to_string(&manifest_path).unwrap(),
                crate::services::file_manifest::json(&crate::goldens::tree_manifest(game.path()))
                    .unwrap()
            );
            assert!(is_current_with_stub(game.path(), &stub).unwrap());
            assert_eq!(
                std::fs::read(game.path().join("BepInEx/plugins/BazaarPlusPlus.dll")).unwrap(),
                b"developer-payload"
            );
            assert_eq!(
                std::fs::read(game.path().join("BazaarPlusPlusV5/history-sentinel")).unwrap(),
                b"user-data"
            );
        }
        imp::uninstall_trampoline(game.path()).unwrap();
        finalize_game_bundle(game.path()).unwrap();
        imp::verify_bundle(&app).unwrap();
        eprintln!("Copied Steam bundle uninstall: strict signature verified");
    }

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

    /// A thin arm64/x86_64 Mach-O image whose only load command is an optional dylib.
    fn macho_image(cpu_type: u32, dylib: Option<&str>) -> Vec<u8> {
        const LC_LOAD_DYLIB: u32 = 0xc;
        const DYLIB_NAME_OFFSET: u32 = 24;

        let mut command = Vec::new();
        if let Some(name) = dylib {
            let mut name = name.as_bytes().to_vec();
            name.push(0);
            name.resize(name.len().next_multiple_of(8), 0);
            command.extend_from_slice(&LC_LOAD_DYLIB.to_le_bytes());
            command.extend_from_slice(&(DYLIB_NAME_OFFSET + name.len() as u32).to_le_bytes());
            command.extend_from_slice(&DYLIB_NAME_OFFSET.to_le_bytes());
            command.extend_from_slice(&[0; 12]);
            command.extend_from_slice(&name);
        }
        let mut bytes = vec![0; 32];
        bytes[0..4].copy_from_slice(&0xfeedfacfu32.to_le_bytes());
        bytes[4..8].copy_from_slice(&cpu_type.to_le_bytes());
        bytes[16..20].copy_from_slice(&u32::from(dylib.is_some()).to_le_bytes());
        bytes[20..24].copy_from_slice(&(command.len() as u32).to_le_bytes());
        bytes.extend_from_slice(&command);
        bytes
    }

    /// A universal binary wrapping `slices`, with 32-bit or 64-bit fat arch entries.
    fn universal(slices: &[Vec<u8>], wide: bool) -> Vec<u8> {
        let entry_size = if wide { 32 } else { 20 };
        let magic: u32 = if wide { 0xcafebabf } else { 0xcafebabe };
        let mut header = magic.to_be_bytes().to_vec();
        header.extend_from_slice(&(slices.len() as u32).to_be_bytes());
        let mut offset = (8 + slices.len() * entry_size).next_multiple_of(16);
        let mut body = Vec::new();
        for slice in slices {
            header.extend_from_slice(&slice[4..8].iter().rev().copied().collect::<Vec<_>>());
            header.extend_from_slice(&[0; 4]);
            if wide {
                header.extend_from_slice(&(offset as u64).to_be_bytes());
                header.extend_from_slice(&(slice.len() as u64).to_be_bytes());
                header.extend_from_slice(&[0; 8]);
            } else {
                header.extend_from_slice(&(offset as u32).to_be_bytes());
                header.extend_from_slice(&(slice.len() as u32).to_be_bytes());
                header.extend_from_slice(&[0; 4]);
            }
            body.push((offset, slice));
            offset = (offset + slice.len()).next_multiple_of(16);
        }
        let mut bytes = header;
        for (start, slice) in body {
            bytes.resize(start, 0);
            bytes.extend_from_slice(slice);
        }
        bytes
    }

    #[test]
    fn unity_linkage_and_arm64_are_read_without_xcode_tools() {
        const ARM64: u32 = 0x0100_000c;
        const X86_64: u32 = 0x0100_0007;
        const UNITY: &str = "@executable_path/../Frameworks/UnityPlayer.dylib";
        let tmp = tempfile::tempdir().unwrap();
        let probe = |name: &str, bytes: &[u8]| {
            let path = tmp.path().join(name);
            std::fs::write(&path, bytes).unwrap();
            (
                imp::links_unity(&path).unwrap(),
                imp::has_arm64(&path).unwrap(),
            )
        };

        assert_eq!(
            probe("thin-unity", &macho_image(ARM64, Some(UNITY))),
            (true, true)
        );
        assert_eq!(
            probe(
                "thin-stub",
                &macho_image(ARM64, Some("/usr/lib/libSystem.B.dylib"))
            ),
            (false, true)
        );
        assert_eq!(
            probe("intel-unity", &macho_image(X86_64, Some(UNITY))),
            (true, false)
        );
        // The Steam build ships as a universal x86_64 + arm64 binary.
        for wide in [false, true] {
            let slices = [
                macho_image(X86_64, Some(UNITY)),
                macho_image(ARM64, Some(UNITY)),
            ];
            assert_eq!(probe("universal", &universal(&slices, wide)), (true, true));
        }
        assert_eq!(probe("not-macho", b"STUB"), (false, false));
        assert!(!imp::links_unity(&tmp.path().join("missing")).unwrap());
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
        // No `.orig` -> not trampolined; short-circuits before inspecting the executable.
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

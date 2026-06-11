//! macOS major-version probe and launch-mode decision.
//!
//! macOS 27 changed the Steam client so it no longer spawns a prefix executable
//! before `%command%` (even `/bin/sh` fails), which kills the `run_bepinex.sh`
//! launch path BepInEx depends on. On 27+ we must inject from inside the `.app`
//! via the Mach-O trampoline (`services::bepinex::trampoline`). The probe is
//! cached process-wide because the OS version cannot change without a reboot.
//!
//! `use_trampoline` is the single boolean that selects the launch mode:
//! `macos_major() >= 27 || compat_opt_in`. On non-macOS it is always `false`.

#[cfg(target_os = "macos")]
use std::sync::OnceLock;

/// The macOS major version (e.g. `27` for `27.0`). Forced to the trampoline path
/// at or above this. Pure version gate — matches the proven root cause; a future
/// Steam fix that restores prefix-launch would be handled by revisiting this.
#[cfg(target_os = "macos")]
pub const TRAMPOLINE_FORCED_MAJOR: u32 = 27;

#[cfg(target_os = "macos")]
fn detect_macos_major() -> Option<u32> {
    use std::process::Command;

    let output = Command::new("sw_vers")
        .arg("-productVersion")
        .output()
        .ok()?;
    if !output.status.success() {
        return None;
    }
    let version = String::from_utf8_lossy(&output.stdout);
    parse_major(version.trim())
}

/// Parse the leading major component of a `productVersion` string ("27.0.1" -> 27).
#[cfg(target_os = "macos")]
fn parse_major(version: &str) -> Option<u32> {
    version.split('.').next()?.trim().parse::<u32>().ok()
}

/// Cached macOS major version. Returns `None` only if `sw_vers` is unavailable or
/// unparseable, in which case callers treat the platform as not-forced (the safe,
/// zero-regression default that preserves the prefix path).
#[cfg(target_os = "macos")]
pub fn macos_major() -> Option<u32> {
    static MAJOR: OnceLock<Option<u32>> = OnceLock::new();
    *MAJOR.get_or_init(detect_macos_major)
}

/// True when the running macOS forces trampoline mode (no working prefix path).
#[cfg(target_os = "macos")]
pub fn trampoline_forced() -> bool {
    macos_major().is_some_and(|major| major >= TRAMPOLINE_FORCED_MAJOR)
}

#[cfg(not(target_os = "macos"))]
pub fn trampoline_forced() -> bool {
    false
}

/// True when the install should use the in-bundle trampoline instead of the
/// prefix script: version-forced on macOS 27+, or opted into on macOS <= 26.
/// Always `false` off macOS, regardless of `compat_opt_in`.
#[cfg(target_os = "macos")]
pub fn use_trampoline(compat_opt_in: bool) -> bool {
    trampoline_forced() || compat_opt_in
}

#[cfg(not(target_os = "macos"))]
pub fn use_trampoline(_compat_opt_in: bool) -> bool {
    false
}

/// True when the compatibility-mode checkbox should be offered as an opt-in
/// (macOS <= 26, where the prefix path still works and trampoline is experimental).
/// On 27+ the mode is forced (shown checked + locked), so it is not "available"
/// to toggle; off macOS it is never shown.
pub fn compat_mode_available() -> bool {
    cfg!(target_os = "macos") && !trampoline_forced()
}

#[cfg(test)]
#[cfg(target_os = "macos")]
mod tests {
    use super::{parse_major, TRAMPOLINE_FORCED_MAJOR};

    #[test]
    fn test_parse_major_reads_leading_component() {
        assert_eq!(parse_major("27.0"), Some(27));
        assert_eq!(parse_major("26.4.1"), Some(26));
        assert_eq!(parse_major("27"), Some(27));
    }

    #[test]
    fn test_parse_major_rejects_garbage() {
        assert_eq!(parse_major(""), None);
        assert_eq!(parse_major("sonoma"), None);
    }

    #[test]
    fn test_forced_threshold_is_27() {
        assert_eq!(TRAMPOLINE_FORCED_MAJOR, 27);
        assert!(parse_major("27.0").unwrap() >= TRAMPOLINE_FORCED_MAJOR);
        assert!(parse_major("26.9").unwrap() < TRAMPOLINE_FORCED_MAJOR);
    }
}

use std::time::SystemTime;

pub const MACOS_TRAMPOLINE_DEPLOYMENT_TARGET: &str = "12.0";

pub fn should_compile_trampoline(
    source_modified: SystemTime,
    output_modified: Option<SystemTime>,
) -> bool {
    match output_modified {
        Some(output_modified) => source_modified > output_modified,
        None => true,
    }
}

/// Return whether `otool -l` reports the required minimum macOS version for a
/// Mach-O image. Modern Apple toolchains emit `LC_BUILD_VERSION`; accepting the
/// legacy `LC_VERSION_MIN_MACOSX` form keeps the check valid with older clangs.
pub fn has_macos_trampoline_deployment_target(otool_output: &str, expected_version: &str) -> bool {
    let mut load_command = None;
    let mut is_macos_platform = false;

    for raw_line in otool_output.lines() {
        let line = raw_line.trim();

        if let Some(command) = line.strip_prefix("cmd ") {
            load_command = Some(command);
            is_macos_platform = false;
            continue;
        }

        match load_command {
            Some("LC_BUILD_VERSION") => {
                if line == "platform 1" {
                    is_macos_platform = true;
                } else if is_macos_platform && line.strip_prefix("minos ") == Some(expected_version)
                {
                    return true;
                }
            }
            Some("LC_VERSION_MIN_MACOSX")
                if line.strip_prefix("version ") == Some(expected_version) =>
            {
                return true;
            }
            _ => {}
        }
    }

    false
}

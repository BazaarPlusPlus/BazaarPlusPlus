#[path = "../build_support.rs"]
mod build_support;

use std::time::{Duration, SystemTime};

#[test]
fn missing_trampoline_requires_compilation() {
    let source_modified = SystemTime::UNIX_EPOCH + Duration::from_secs(20);

    assert!(build_support::should_compile_trampoline(
        source_modified,
        None
    ));
}

#[test]
fn newer_trampoline_source_requires_compilation() {
    let output_modified = SystemTime::UNIX_EPOCH + Duration::from_secs(20);
    let source_modified = output_modified + Duration::from_secs(1);

    assert!(build_support::should_compile_trampoline(
        source_modified,
        Some(output_modified)
    ));
}

#[test]
fn fresh_trampoline_output_is_reused() {
    let source_modified = SystemTime::UNIX_EPOCH + Duration::from_secs(20);
    let output_modified = source_modified + Duration::from_secs(1);

    assert!(!build_support::should_compile_trampoline(
        source_modified,
        Some(output_modified)
    ));
}

#[test]
fn macos_build_version_with_required_minimum_is_accepted() {
    let output = r#"
Load command 10
      cmd LC_BUILD_VERSION
  cmdsize 32
 platform 1
    minos 12.0
      sdk 27.0
"#;

    assert!(build_support::has_macos_trampoline_deployment_target(
        output,
        build_support::MACOS_TRAMPOLINE_DEPLOYMENT_TARGET
    ));
}

#[test]
fn newer_macos_build_version_is_rejected() {
    let output = r#"
Load command 10
      cmd LC_BUILD_VERSION
  cmdsize 32
 platform 1
    minos 27.0
      sdk 27.0
"#;

    assert!(!build_support::has_macos_trampoline_deployment_target(
        output,
        build_support::MACOS_TRAMPOLINE_DEPLOYMENT_TARGET
    ));
}

#[test]
fn legacy_macos_version_load_command_is_supported() {
    let output = r#"
Load command 8
      cmd LC_VERSION_MIN_MACOSX
  cmdsize 16
  version 12.0
      sdk 14.0
"#;

    assert!(build_support::has_macos_trampoline_deployment_target(
        output,
        build_support::MACOS_TRAMPOLINE_DEPLOYMENT_TARGET
    ));
}

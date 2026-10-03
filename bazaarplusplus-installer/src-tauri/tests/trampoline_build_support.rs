// `build.rs` is the only caller of the build-time freshness check.
#[allow(dead_code)]
#[path = "../build_support.rs"]
mod build_support;

#[test]
fn macos_build_version_with_required_minimum_is_accepted() {
    let output = r"
Load command 10
      cmd LC_BUILD_VERSION
  cmdsize 32
 platform 1
    minos 12.0
      sdk 27.0
";

    assert!(build_support::has_macos_trampoline_deployment_target(
        output,
        build_support::MACOS_TRAMPOLINE_DEPLOYMENT_TARGET
    ));
}

#[test]
fn newer_macos_build_version_is_rejected() {
    let output = r"
Load command 10
      cmd LC_BUILD_VERSION
  cmdsize 32
 platform 1
    minos 27.0
      sdk 27.0
";

    assert!(!build_support::has_macos_trampoline_deployment_target(
        output,
        build_support::MACOS_TRAMPOLINE_DEPLOYMENT_TARGET
    ));
}

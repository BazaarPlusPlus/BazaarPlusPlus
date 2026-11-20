mod build_support;

use std::path::Path;
use std::process::Command;

fn main() {
    compile_macos_trampoline_stub();
    tauri_build::build();
    expose_windows_resources_to_test_harnesses();
}

/// Tauri only links the Common Controls v6 resource into binaries. The test harness needs it to resolve `TaskDialogIndirect`.
fn expose_windows_resources_to_test_harnesses() {
    if std::env::var("CARGO_CFG_TARGET_OS").as_deref() != Ok("windows") {
        return;
    }

    let output_dir = std::env::var_os("OUT_DIR").expect("Cargo must provide OUT_DIR");
    println!(
        "cargo:rustc-link-search=native={}",
        Path::new(&output_dir).display()
    );
}

/// The stub must exist before `tauri_build` validates the configured resources.
fn compile_macos_trampoline_stub() {
    if std::env::var("CARGO_CFG_TARGET_OS").as_deref() != Ok("macos") {
        return;
    }

    let source = "trampoline/bpp_launcher.c";
    let output = "resources/Trampoline/macos/bpp_launcher";
    // Rewriting an unchanged watched output makes Cargo mark the package dirty on the next command.
    println!("cargo:rerun-if-changed={source}");
    println!("cargo:rerun-if-changed={output}");

    let source_modified = std::fs::metadata(source)
        .and_then(|metadata| metadata.modified())
        .unwrap_or_else(|err| panic!("cannot inspect trampoline source {source}: {err}"));
    let output_modified = match std::fs::metadata(output) {
        Ok(metadata) => Some(
            metadata
                .modified()
                .unwrap_or_else(|err| panic!("cannot inspect trampoline output {output}: {err}")),
        ),
        Err(err) if err.kind() == std::io::ErrorKind::NotFound => None,
        Err(err) => panic!("cannot inspect trampoline output {output}: {err}"),
    };

    if !build_support::should_compile_trampoline(source_modified, output_modified)
        && has_required_macos_deployment_target(output)
    {
        return;
    }

    if let Some(parent) = Path::new(output).parent() {
        std::fs::create_dir_all(parent)
            .unwrap_or_else(|err| panic!("cannot create {}: {err}", parent.display()));
    }

    let deployment_target_arg = format!(
        "-mmacosx-version-min={}",
        build_support::MACOS_TRAMPOLINE_DEPLOYMENT_TARGET
    );
    let status = Command::new("clang")
        .args([
            "-arch",
            "arm64",
            deployment_target_arg.as_str(),
            "-O2",
            "-o",
            output,
            source,
        ])
        .status()
        .unwrap_or_else(|err| panic!("failed to run clang for trampoline stub: {err}"));
    if !status.success() {
        panic!("clang failed to compile the macOS trampoline stub ({source})");
    }

    if !has_required_macos_deployment_target(output) {
        panic!(
            "macOS trampoline stub has no {} deployment target: {output}",
            build_support::MACOS_TRAMPOLINE_DEPLOYMENT_TARGET
        );
    }
}

fn has_required_macos_deployment_target(output: &str) -> bool {
    let otool_output = Command::new("otool")
        .args(["-l", output])
        .output()
        .unwrap_or_else(|err| panic!("failed to run otool for trampoline stub: {err}"));
    if !otool_output.status.success() {
        panic!(
            "otool failed to inspect the macOS trampoline stub ({output}): {}",
            String::from_utf8_lossy(&otool_output.stderr).trim()
        );
    }

    build_support::has_macos_trampoline_deployment_target(
        &String::from_utf8_lossy(&otool_output.stdout),
        build_support::MACOS_TRAMPOLINE_DEPLOYMENT_TARGET,
    )
}

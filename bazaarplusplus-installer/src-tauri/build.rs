mod build_support;

use std::path::Path;
use std::process::Command;

fn main() {
    compile_macos_trampoline_stub();
    tauri_build::build();
    expose_windows_resources_to_test_harnesses();
}

/// `tauri-build` links `resource.lib` (including the Common Controls v6
/// manifest) into application binaries only. The library test harness links
/// Tauri's Windows dialog code too, so it needs the same manifest before the
/// Windows loader can resolve `TaskDialogIndirect`.
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

/// Compile the sole macOS launch trampoline stub (arm64) from its committed C source so
/// the bundled resource declared in `tauri.macos.conf.json` exists before
/// `tauri_build` validates resource paths. The generated binary is gitignored and
/// is replaced when it is missing, older than its source, or built against a
/// deployment target other than the supported baseline. No-op when not targeting
/// macOS.
fn compile_macos_trampoline_stub() {
    if std::env::var("CARGO_CFG_TARGET_OS").as_deref() != Ok("macos") {
        return;
    }

    let source = "trampoline/bpp_launcher.c";
    let output = "resources/Trampoline/macos/bpp_launcher";
    // Re-run when the source changes or the generated output disappears. Merely
    // running the build script must not rewrite the watched output: doing so makes
    // the next Cargo command consider this package dirty again.
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

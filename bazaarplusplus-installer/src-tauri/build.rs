use std::path::Path;
use std::process::Command;

fn main() {
    compile_macos_trampoline_stub();
    tauri_build::build()
}

/// Compile the macOS launch trampoline stub (arm64) from its committed C source so
/// the bundled resource declared in `tauri.macos.conf.json` exists before
/// `tauri_build` validates resource paths. Runs on every macOS cargo build (dev,
/// `npm run check`, bindings, release) so no entry point can ship a missing stub;
/// the compiled binary is gitignored. No-op when not targeting macOS.
fn compile_macos_trampoline_stub() {
    if std::env::var("CARGO_CFG_TARGET_OS").as_deref() != Ok("macos") {
        return;
    }

    let source = "resources/SourceForBuild/macos/bpp_launcher.c";
    let output = "resources/Trampoline/macos/bpp_launcher";
    // Recompile when the source changes; rerun (and thus recreate) if the output
    // is ever deleted — a missing rerun-if-changed path counts as "changed".
    println!("cargo:rerun-if-changed={source}");
    println!("cargo:rerun-if-changed={output}");

    if let Some(parent) = Path::new(output).parent() {
        std::fs::create_dir_all(parent)
            .unwrap_or_else(|err| panic!("cannot create {}: {err}", parent.display()));
    }

    let status = Command::new("clang")
        .args(["-arch", "arm64", "-O2", "-o", output, source])
        .status()
        .unwrap_or_else(|err| panic!("failed to run clang for trampoline stub: {err}"));
    if !status.success() {
        panic!("clang failed to compile the macOS trampoline stub ({source})");
    }
}

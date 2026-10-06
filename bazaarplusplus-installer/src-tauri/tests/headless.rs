use std::process::Command;

#[test]
fn headless_help_exits_without_starting_the_desktop_shell() {
    let output = Command::new(env!("CARGO_BIN_EXE_bppinstaller"))
        .arg("--help")
        .output()
        .unwrap();
    assert!(output.status.success());
    assert!(String::from_utf8_lossy(&output.stdout).contains("repair --game"));
}

#[test]
fn invalid_repair_leaves_the_target_untouched_and_returns_failure() {
    let game = tempfile::tempdir().unwrap();
    let sentinel = game.path().join("user-history");
    std::fs::write(&sentinel, b"keep").unwrap();
    let output = Command::new(env!("CARGO_BIN_EXE_bppinstaller"))
        .args(["repair", "--game"])
        .arg(game.path())
        .args(["--stub", "missing-stub"])
        .output()
        .unwrap();
    assert!(!output.status.success());
    assert_eq!(std::fs::read(&sentinel).unwrap(), b"keep");
    assert_eq!(std::fs::read_dir(game.path()).unwrap().count(), 1);
}

#[cfg(unix)]
#[test]
fn manifest_symlink_cannot_overwrite_game_files() {
    let game = tempfile::tempdir().unwrap();
    let evidence = tempfile::tempdir().unwrap();
    let target = game.path().join("libdoorstop.dylib");
    std::fs::write(&target, b"doorstop").unwrap();
    let manifest = evidence.path().join("manifest.json");
    std::os::unix::fs::symlink(&target, &manifest).unwrap();
    let output = Command::new(env!("CARGO_BIN_EXE_bppinstaller"))
        .args(["repair", "--game"])
        .arg(game.path())
        .arg("--manifest")
        .arg(&manifest)
        .output()
        .unwrap();
    assert!(!output.status.success());
    assert!(String::from_utf8_lossy(&output.stderr).contains("regular file"));
    assert_eq!(std::fs::read(&target).unwrap(), b"doorstop");
}

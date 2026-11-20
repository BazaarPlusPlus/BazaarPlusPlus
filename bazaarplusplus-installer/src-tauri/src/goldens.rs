use std::path::{Path, PathBuf};

use serde::Serialize;

pub(crate) fn golden_path(relative: &str) -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("tests/goldens")
        .join(relative)
}

pub(crate) fn assert_golden(relative: &str, actual: &str) {
    let path = golden_path(relative);
    if std::env::var("BPP_UPDATE_GOLDENS").as_deref() == Ok("1") {
        std::fs::create_dir_all(path.parent().expect("golden has a directory")).unwrap();
        std::fs::write(&path, actual).unwrap();
        return;
    }
    let expected = std::fs::read_to_string(&path).unwrap_or_else(|error| {
        panic!(
            "read golden {}: {error}; regenerate with BPP_UPDATE_GOLDENS=1",
            path.display()
        )
    });
    assert!(
        expected == actual,
        "golden {} differs; review and regenerate with BPP_UPDATE_GOLDENS=1\n--- expected\n{expected}\n--- actual\n{actual}",
        path.display()
    );
}

/// Normalize both spellings because JSON escapes Windows path separators.
pub(crate) fn json(value: &impl Serialize, roots: &[(&Path, &str)]) -> String {
    let mut text = serde_json::to_string_pretty(value).unwrap();
    for (root, placeholder) in roots {
        let raw = root.to_string_lossy();
        let escaped = serde_json::to_string(raw.as_ref()).unwrap();
        text = text
            .replace(&escaped[1..escaped.len() - 1], placeholder)
            .replace(raw.as_ref(), placeholder);
    }
    text.push('\n');
    text
}

#[cfg(target_os = "macos")]
pub(crate) fn tree_manifest(root: &Path) -> Vec<(String, String)> {
    crate::services::file_manifest::tree_manifest(root).unwrap()
}

/// Signatures and game files vary across machines, so compare full manifests only across runs with the same inputs.
#[cfg(target_os = "macos")]
pub(crate) fn write_run_manifest(name: &str, entries: &[(String, String)]) {
    let path = Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("target/acceptance")
        .join(format!("{name}.manifest.json"));
    std::fs::create_dir_all(path.parent().expect("manifest has a directory")).unwrap();
    let entries: Vec<_> = entries
        .iter()
        .map(|(path, sha256)| serde_json::json!({ "path": path, "sha256": sha256 }))
        .collect();
    std::fs::write(&path, json(&entries, &[])).unwrap();
    eprintln!("Wrote the run manifest to {}", path.display());
}

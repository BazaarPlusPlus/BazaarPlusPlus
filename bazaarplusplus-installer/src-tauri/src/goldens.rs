//! Golden files committed under `tests/goldens/`. A test renders its output to
//! text and compares it with the committed file byte for byte; with
//! `BPP_UPDATE_GOLDENS=1` it rewrites the file instead, and the diff is
//! reviewed in the pull request.

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

/// Pretty JSON with every fixture root replaced by its placeholder, in both the
/// raw and the JSON-escaped spelling (Windows paths contain `\`).
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

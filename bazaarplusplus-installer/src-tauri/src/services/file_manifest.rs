//! Deterministic filesystem evidence shared by headless repair and acceptance.

use std::io::Read;
use std::path::Path;

use sha2::{Digest, Sha256};

pub(crate) fn tree_manifest(root: &Path) -> Result<Vec<(String, String)>, String> {
    fn walk(
        root: &Path,
        directory: &Path,
        entries: &mut Vec<(String, String)>,
    ) -> std::io::Result<()> {
        for entry in std::fs::read_dir(directory)? {
            let path = entry?.path();
            let relative = path
                .strip_prefix(root)
                .expect("walk stays under root")
                .to_string_lossy()
                .replace('\\', "/");
            let kind = std::fs::symlink_metadata(&path)?.file_type();
            if kind.is_symlink() {
                entries.push((
                    relative,
                    format!("symlink:{}", std::fs::read_link(&path)?.display()),
                ));
            } else if kind.is_dir() {
                walk(root, &path, entries)?;
            } else if kind.is_file() {
                let mut file = std::fs::File::open(&path)?;
                let mut digest = Sha256::new();
                let mut buffer = [0; 64 * 1024];
                loop {
                    let count = file.read(&mut buffer)?;
                    if count == 0 {
                        break;
                    }
                    digest.update(&buffer[..count]);
                }
                let hash = digest
                    .finalize()
                    .iter()
                    .map(|byte| format!("{byte:02x}"))
                    .collect();
                entries.push((relative, hash));
            } else {
                return Err(std::io::Error::other(format!(
                    "Unsupported file: {}",
                    path.display()
                )));
            }
        }
        Ok(())
    }
    let mut entries = Vec::new();
    walk(root, root, &mut entries)
        .map_err(|error| format!("Cannot inspect {}: {error}", root.display()))?;
    entries.sort();
    Ok(entries)
}

pub(crate) fn json(entries: &[(String, String)]) -> Result<String, String> {
    let entries: Vec<_> = entries
        .iter()
        .map(|(path, sha256)| serde_json::json!({"path": path, "sha256": sha256}))
        .collect();
    serde_json::to_string_pretty(&entries)
        .map(|text| text + "\n")
        .map_err(|error| error.to_string())
}

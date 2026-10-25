use anyhow::{bail, Context as _};
use std::fs::File;
use std::io::{Cursor, Read};
use std::path::Path;
use tauri::Manager;

pub(super) fn bundled_zip_relative_path() -> &'static str {
    "BepInExSource/BepInEx.zip"
}

pub(crate) fn read_bundled_bpp_version(app: &tauri::AppHandle) -> anyhow::Result<Option<String>> {
    let resource_path = app.path().resource_dir()?.join(bundled_zip_relative_path());
    let file = File::open(&resource_path).context("Cannot read bundled BepInEx.zip")?;
    let mut archive = zip::ZipArchive::new(file)?;

    for index in 0..archive.len() {
        let mut file = archive.by_index(index)?;
        let is_version_file = file.name()?.ends_with("BazaarPlusPlus.version");
        if is_version_file {
            let mut version = String::new();
            file.read_to_string(&mut version)?;
            let version = version.trim();
            return Ok((!version.is_empty()).then(|| version.to_string()));
        }
    }

    Ok(None)
}

pub(super) struct ExtractReport {
    pub(super) written: Vec<String>,
    pub(super) skipped_identical: Vec<String>,
}

/// Extracts every file entry except the install-relative paths in `skip`.
pub(super) fn extract_zip(
    zip_bytes: &[u8],
    dest_dir: &Path,
    skip: &std::collections::HashSet<String>,
) -> anyhow::Result<ExtractReport> {
    let reader = Cursor::new(zip_bytes);
    let mut archive = zip::ZipArchive::new(reader)?;
    let mut report = ExtractReport {
        written: Vec::new(),
        skipped_identical: Vec::new(),
    };

    for index in 0..archive.len() {
        let mut file = archive.by_index(index)?;
        let Some(relative_path) = file.enclosed_name() else {
            bail!(
                "Zip entry has unsafe path: {}",
                String::from_utf8_lossy(file.name_raw())
            );
        };
        if !file.is_dir() && skip.contains(&relative_path.to_string_lossy().replace('\\', "/")) {
            continue;
        }
        let output_path = dest_dir.join(relative_path);

        if file.is_dir() {
            std::fs::create_dir_all(&output_path)?;
            continue;
        }

        if let Some(parent) = output_path.parent() {
            std::fs::create_dir_all(parent)?;
        }

        let mut contents = Vec::new();
        file.read_to_end(&mut contents)?;

        if existing_file_is_identical(&output_path, &contents) {
            report
                .skipped_identical
                .push(output_path.to_string_lossy().into_owned());
            continue;
        }

        std::fs::write(&output_path, contents)?;
        report
            .written
            .push(output_path.to_string_lossy().into_owned());
    }

    Ok(report)
}

pub(super) fn zip_entry_paths(
    zip_bytes: &[u8],
) -> anyhow::Result<std::collections::HashSet<String>> {
    let reader = Cursor::new(zip_bytes);
    let archive = zip::ZipArchive::new(reader)?;
    archive
        .file_names()
        .filter_map(|name| match name {
            Ok(name) if name.ends_with('/') => None,
            Ok(name) => Some(Ok(name.replace('\\', "/"))),
            Err(err) => Some(Err(err.into())),
        })
        .collect()
}

/// The bytes of one file entry, or `None` when the zip has no such entry.
pub(super) fn zip_entry_bytes(zip_bytes: &[u8], name: &str) -> anyhow::Result<Option<Vec<u8>>> {
    let mut archive = zip::ZipArchive::new(Cursor::new(zip_bytes))?;
    let mut file = match archive.by_name(name) {
        Ok(file) => file,
        Err(zip::result::ZipError::FileNotFound) => return Ok(None),
        Err(err) => return Err(err.into()),
    };
    let mut contents = Vec::new();
    file.read_to_end(&mut contents)?;
    Ok(Some(contents))
}

/// A shared dll another mod ships at the same version must not be rewritten
/// on every BPP install; the length gate keeps the multi-MB payload entries
/// from being read unless they could actually match.
fn existing_file_is_identical(path: &Path, contents: &[u8]) -> bool {
    match std::fs::metadata(path) {
        Ok(metadata) if metadata.is_file() && metadata.len() == contents.len() as u64 => {
            std::fs::read(path)
                .map(|existing| existing == contents)
                .unwrap_or(false)
        }
        _ => false,
    }
}

#[cfg(test)]
mod tests {
    use super::{bundled_zip_relative_path, extract_zip, zip_entry_paths};
    use std::io::{Cursor, Write};

    fn make_test_zip() -> Vec<u8> {
        let buffer = Cursor::new(Vec::new());
        let mut zip = zip::ZipWriter::new(buffer);
        let options = zip::write::SimpleFileOptions::default();

        zip.add_directory("BepInEx/", options).unwrap();
        zip.start_file("BepInEx/core/BepInEx.Core.dll", options)
            .unwrap();
        zip.write_all(b"fake dll content").unwrap();

        zip.finish().unwrap().into_inner()
    }

    #[test]
    fn test_extract_zip_creates_files() {
        let zip_bytes = make_test_zip();
        let tmp = tempfile::tempdir().unwrap();

        let report = extract_zip(&zip_bytes, tmp.path(), &Default::default()).unwrap();
        assert!(!report.written.is_empty());
        assert!(report.skipped_identical.is_empty());
        assert!(tmp.path().join("BepInEx/core/BepInEx.Core.dll").exists());
    }

    #[test]
    fn test_extract_zip_skips_byte_identical_existing_files() {
        let zip_bytes = make_test_zip();
        let tmp = tempfile::tempdir().unwrap();
        let target = tmp.path().join("BepInEx/core/BepInEx.Core.dll");
        std::fs::create_dir_all(target.parent().unwrap()).unwrap();
        std::fs::write(&target, b"fake dll content").unwrap();

        let report = extract_zip(&zip_bytes, tmp.path(), &Default::default()).unwrap();

        assert!(report.written.is_empty());
        assert_eq!(report.skipped_identical.len(), 1);
        assert_eq!(std::fs::read(&target).unwrap(), b"fake dll content");
    }

    #[test]
    fn test_extract_zip_overwrites_differing_existing_files() {
        let zip_bytes = make_test_zip();
        let tmp = tempfile::tempdir().unwrap();
        let target = tmp.path().join("BepInEx/core/BepInEx.Core.dll");
        std::fs::create_dir_all(target.parent().unwrap()).unwrap();
        std::fs::write(&target, b"other mod's different version").unwrap();

        let report = extract_zip(&zip_bytes, tmp.path(), &Default::default()).unwrap();

        assert_eq!(report.written.len(), 1);
        assert!(report.skipped_identical.is_empty());
        assert_eq!(std::fs::read(&target).unwrap(), b"fake dll content");
    }

    #[test]
    fn test_zip_entry_paths_lists_files_not_directories() {
        let zip_bytes = make_test_zip();

        let paths = zip_entry_paths(&zip_bytes).unwrap();

        assert!(paths.contains("BepInEx/core/BepInEx.Core.dll"));
        assert!(!paths.iter().any(|path| path.ends_with('/')));
    }

    #[test]
    fn test_bundled_zip_relative_path_matches_supported_targets() {
        assert_eq!(bundled_zip_relative_path(), "BepInExSource/BepInEx.zip");
    }
}

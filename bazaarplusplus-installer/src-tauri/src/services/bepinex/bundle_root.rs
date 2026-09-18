//! Preserve the duplicate application shipped at the root of some Steam builds.
//! Content-addressed backups make interrupted and repeated repairs resumable.
//! Only the latest backup is retained; unknown or modified backups are never removed.

use sha2::{Digest, Sha256};
use std::fs;
use std::io::Read;
use std::path::{Path, PathBuf};

const DUPLICATE: &str = "TheBazaar_ARM64.app";
const BACKUPS: &str = ".bpp-bundle-root-stash";

fn io<T>(path: &Path, result: std::io::Result<T>) -> Result<T, String> {
    result.map_err(|error| format!("Cannot access {}: {error}", path.display()))
}

fn directory(path: &Path) -> Result<(), String> {
    if !io(path, fs::symlink_metadata(path))?.file_type().is_dir() {
        return Err(format!("Expected a real directory at {}", path.display()));
    }
    Ok(())
}

fn children(path: &Path) -> Result<Vec<PathBuf>, String> {
    io(path, fs::read_dir(path))?
        .map(|entry| io(path, entry).map(|entry| entry.path()))
        .collect()
}

fn file_digest(path: &Path) -> Result<String, String> {
    let mut file = io(path, fs::File::open(path))?;
    let mut digest = Sha256::new();
    let mut buffer = [0; 64 * 1024];
    loop {
        let count = io(path, file.read(&mut buffer))?;
        if count == 0 {
            break;
        }
        digest.update(&buffer[..count]);
    }
    Ok(format!("{:x}", digest.finalize()))
}

// This manifest format is also used by the developer repair script. Reject
// ambiguous names and links instead of following them outside the owned tree.
fn fingerprint(root: &Path) -> Result<String, String> {
    fn visit(root: &Path, path: &Path, records: &mut Vec<String>) -> Result<(), String> {
        let relative = path.strip_prefix(root).map_err(|error| error.to_string())?;
        let relative = relative
            .to_str()
            .filter(|name| !name.contains(['\n', '\r', '\\']))
            .ok_or_else(|| format!("Unsupported backup path: {}", path.display()))?;
        let name = if relative.is_empty() {
            ".".into()
        } else {
            format!("./{relative}")
        };
        let kind = io(path, fs::symlink_metadata(path))?.file_type();
        if kind.is_dir() {
            records.push(format!("D {name}\n"));
            for child in children(path)? {
                visit(root, &child, records)?;
            }
        } else if kind.is_file() {
            records.push(format!("F {}  {name}\n", file_digest(path)?));
        } else {
            return Err(format!(
                "Unsupported file or symbolic link: {}",
                path.display()
            ));
        }
        Ok(())
    }
    let mut records = Vec::new();
    visit(root, root, &mut records)?;
    records.sort();
    Ok(format!("{:x}", Sha256::digest(records.concat().as_bytes())))
}

fn read_plist(app: &Path, key: &str) -> Result<String, String> {
    let path = app.join("Contents/Info.plist");
    let output = std::process::Command::new("/usr/bin/plutil")
        .args(["-extract", key, "raw", "-o", "-"])
        .arg(&path)
        .output()
        .map_err(|error| format!("Cannot read {}: {error}", path.display()))?;
    if !output.status.success() {
        return Err(format!("Cannot read {key} from {}", path.display()));
    }
    Ok(String::from_utf8_lossy(&output.stdout).trim().to_string())
}

fn validate_duplicate(app: &Path, duplicate: &Path) -> Result<(), String> {
    directory(duplicate)?;
    for key in [
        "CFBundleIdentifier",
        "CFBundleExecutable",
        "CFBundleShortVersionString",
    ] {
        let value = read_plist(app, key)?;
        if value.is_empty() || value != read_plist(duplicate, key)? {
            return Err(format!(
                "Unexpected {key} in {}; no files were moved",
                duplicate.display()
            ));
        }
        if key == "CFBundleIdentifier" && value != "com.TempoStorm.TheBazaar" {
            return Err(format!("Unrecognized game bundle: {}", app.display()));
        }
    }
    // Signatures and the main executable may already have been changed by BPP.
    // The engine, Mono runtime and boot configuration must still match Steam's copy.
    for relative in [
        "Contents/Frameworks/UnityPlayer.dylib",
        "Contents/Frameworks/libmonobdwgc-2.0.dylib",
        "Contents/Resources/Data/boot.config",
    ] {
        if file_digest(&app.join(relative))? != file_digest(&duplicate.join(relative))? {
            return Err(format!(
                "Duplicate game bundle differs at {relative}; no files were moved"
            ));
        }
    }
    Ok(())
}

fn valid_digest(value: &str) -> bool {
    value.len() == 64
        && value
            .bytes()
            .all(|byte| byte.is_ascii_digit() || (b'a'..=b'f').contains(&byte))
}

fn write_current(backups: &Path, digest: &str) -> Result<(), String> {
    let staged = backups.join("current.tmp");
    io(&staged, fs::write(&staged, format!("{digest}\n")))?;
    io(&staged, fs::rename(&staged, backups.join("current")))
}

fn retire(backups: &Path, path: &Path, digest: &str) -> Result<(), String> {
    // Renaming is the commit point: a partly removed directory must never be
    // mistaken for a user-modified backup or an incomplete Steam application.
    let tombstone = backups.join(format!(".delete-{digest}.app"));
    io(path, fs::rename(path, &tombstone))?;
    io(&tombstone, fs::remove_dir_all(&tombstone))
}

pub(super) fn normalize(game: &Path) -> Result<(), String> {
    let app = game.join("TheBazaar.app");
    directory(&app)?;
    directory(&app.join("Contents"))?;
    for entry in children(&app)? {
        if entry
            .file_name()
            .is_some_and(|name| name == "Contents" || name == DUPLICATE)
        {
            continue;
        }
        // Finder metadata is explicitly permitted by macOS code signing.
        if entry.file_name().is_some_and(|name| name == ".DS_Store")
            && io(&entry, fs::symlink_metadata(&entry))?
                .file_type()
                .is_file()
        {
            continue;
        }
        return Err(format!(
            "Unexpected application root entry {}; move it outside TheBazaar.app before repairing",
            entry.display()
        ));
    }
    let source = app.join(DUPLICATE);
    let source_exists = source.try_exists().map_err(|error| error.to_string())?
        || fs::symlink_metadata(&source).is_ok();
    let backups = game.join(BACKUPS);
    if !source_exists && !backups.exists() {
        return Ok(());
    }
    let incoming = if source_exists {
        validate_duplicate(&app, &source)?;
        Some(fingerprint(&source)?)
    } else {
        None
    };
    if !backups.exists() {
        io(&backups, fs::create_dir(&backups))?;
    }
    directory(&backups)?;

    // Inspect every existing backup BEFORE deleting or moving anything. A legacy
    // stash is adopted without changing its contents, including after an update.
    let mut snapshots = Vec::new();
    let mut tombstones = Vec::new();
    let mut legacy = None;
    for entry in children(&backups)? {
        let name = entry
            .file_name()
            .and_then(|name| name.to_str())
            .unwrap_or("");
        if name == ".DS_Store" {
            if !io(&entry, fs::symlink_metadata(&entry))?
                .file_type()
                .is_file()
            {
                return Err(format!("Invalid Finder metadata: {}", entry.display()));
            }
        } else if name == "current" || name == "current.tmp" {
            if !io(&entry, fs::symlink_metadata(&entry))?
                .file_type()
                .is_file()
            {
                return Err(format!("Invalid backup record: {}", entry.display()));
            }
        } else if name
            .strip_prefix(".delete-")
            .and_then(|name| name.strip_suffix(".app"))
            .is_some_and(valid_digest)
        {
            directory(&entry)?;
            tombstones.push(entry);
        } else if name == DUPLICATE {
            directory(&entry)?;
            if read_plist(&entry, "CFBundleIdentifier")? != "com.TempoStorm.TheBazaar" {
                return Err(format!("Unrecognized legacy backup: {}", entry.display()));
            }
            legacy = Some((entry.clone(), fingerprint(&entry)?));
        } else if name.strip_suffix(".app").is_some_and(valid_digest) {
            let digest = fingerprint(&entry)?;
            if name != format!("{digest}.app") {
                return Err(format!(
                    "Backup was modified; preserve or move {} before repairing",
                    entry.display()
                ));
            }
            snapshots.push((entry.clone(), digest));
        } else {
            return Err(format!("Unexpected backup entry: {}", entry.display()));
        }
    }
    if !tombstones.is_empty() && snapshots.is_empty() {
        return Err(format!(
            "No verified backup remains in {}; pending deletion was preserved",
            backups.display()
        ));
    }
    for path in tombstones {
        io(&path, fs::remove_dir_all(&path))?;
    }
    if let Some((path, digest)) = legacy {
        let target = backups.join(format!("{digest}.app"));
        if incoming.is_none() && !backups.join("current").exists() {
            write_current(&backups, &digest)?;
        }
        if target.exists() {
            retire(&backups, &path, &digest)?;
        } else {
            io(&path, fs::rename(&path, &target))?;
            snapshots.push((target, digest.clone()));
        }
    }
    let current = if let Some(digest) = incoming {
        write_current(&backups, &digest)?;
        let target = backups.join(format!("{digest}.app"));
        if target.exists() {
            retire(&backups, &source, &digest)?;
        } else {
            io(&source, fs::rename(&source, &target))?;
        }
        digest
    } else if snapshots.is_empty() {
        return Ok(());
    } else if snapshots.len() == 1 && !backups.join("current").exists() {
        let digest = snapshots[0].1.clone();
        write_current(&backups, &digest)?;
        digest
    } else {
        io(&backups, fs::read_to_string(backups.join("current")))?
            .trim()
            .to_string()
    };
    if !valid_digest(&current) || !backups.join(format!("{current}.app")).is_dir() {
        return Err(format!(
            "Incomplete backup record in {}; no backups were removed",
            backups.display()
        ));
    }
    // The current contents are now preserved outside the app, even if signing or
    // cleanup is interrupted. Re-entry completes cleanup without restoring bad layout.
    for (path, digest) in snapshots {
        if digest != current {
            retire(&backups, &path, &digest)?;
        }
    }
    Ok(())
}

#[cfg(test)]
pub(super) mod tests {
    use super::*;

    pub(crate) fn copy_tree(source: &Path, target: &Path) {
        fs::create_dir_all(target).unwrap();
        for entry in fs::read_dir(source).unwrap() {
            let entry = entry.unwrap();
            let destination = target.join(entry.file_name());
            if entry.file_type().unwrap().is_dir() {
                copy_tree(&entry.path(), &destination);
            } else {
                fs::copy(entry.path(), destination).unwrap();
            }
        }
    }

    pub(crate) fn make_bundle(game: &Path) -> PathBuf {
        let app = game.join("TheBazaar.app");
        for directory in [
            "Contents/MacOS",
            "Contents/Frameworks",
            "Contents/Resources/Data",
        ] {
            fs::create_dir_all(app.join(directory)).unwrap();
        }
        fs::write(
            app.join("Contents/Info.plist"),
            r#"<?xml version="1.0" encoding="UTF-8"?>
<plist version="1.0"><dict>
<key>CFBundleExecutable</key><string>The Bazaar</string>
<key>CFBundleIdentifier</key><string>com.TempoStorm.TheBazaar</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>test</string>
</dict></plist>"#,
        )
        .unwrap();
        fs::write(app.join("Contents/MacOS/The Bazaar"), b"game").unwrap();
        for relative in [
            "Contents/Frameworks/UnityPlayer.dylib",
            "Contents/Frameworks/libmonobdwgc-2.0.dylib",
            "Contents/Resources/Data/boot.config",
        ] {
            fs::write(app.join(relative), b"original").unwrap();
        }
        app
    }

    fn add_duplicate(game: &Path, app: &Path) {
        let staged = game.join("staged.app");
        copy_tree(app, &staged);
        fs::rename(staged, app.join(DUPLICATE)).unwrap();
    }

    #[test]
    fn repeated_repairs_and_new_steam_contents_keep_one_verified_backup() {
        let game = tempfile::tempdir().unwrap();
        let app = make_bundle(game.path());
        add_duplicate(game.path(), &app);
        let original = fingerprint(&app.join(DUPLICATE)).unwrap();
        normalize(game.path()).unwrap();
        for _ in 0..3 {
            normalize(game.path()).unwrap();
            add_duplicate(game.path(), &app);
            normalize(game.path()).unwrap();
            assert!(!app.join(DUPLICATE).exists());
            assert!(game
                .path()
                .join(BACKUPS)
                .join(format!("{original}.app"))
                .is_dir());
        }
        fs::write(
            app.join("Contents/Resources/Data/boot.config"),
            b"steam-update",
        )
        .unwrap();
        add_duplicate(game.path(), &app);
        let updated = fingerprint(&app.join(DUPLICATE)).unwrap();
        normalize(game.path()).unwrap();
        let backups = game.path().join(BACKUPS);
        assert!(!backups.join(format!("{original}.app")).exists());
        assert_eq!(
            fingerprint(&backups.join(format!("{updated}.app"))).unwrap(),
            updated
        );
        assert_eq!(children(&backups).unwrap().len(), 2);
    }

    #[test]
    fn interrupted_move_and_cleanup_resume_from_content_record() {
        let game = tempfile::tempdir().unwrap();
        let app = make_bundle(game.path());
        add_duplicate(game.path(), &app);
        normalize(game.path()).unwrap();
        fs::write(
            app.join("Contents/Resources/Data/boot.config"),
            b"new-build",
        )
        .unwrap();
        add_duplicate(game.path(), &app);
        let digest = fingerprint(&app.join(DUPLICATE)).unwrap();
        let backups = game.path().join(BACKUPS);
        // A process exit after recording the intended destination but before moving.
        write_current(&backups, &digest).unwrap();
        normalize(game.path()).unwrap();
        assert_eq!(children(&backups).unwrap().len(), 2);
        // A process exit after moving, before cleanup. Both backups are intact.
        let old = game.path().join("old");
        fs::create_dir(&old).unwrap();
        fs::write(old.join("preserved"), b"old").unwrap();
        let old_digest = fingerprint(&old).unwrap();
        fs::rename(&old, backups.join(format!("{old_digest}.app"))).unwrap();
        normalize(game.path()).unwrap();
        assert_eq!(children(&backups).unwrap().len(), 2);
        assert!(backups.join(format!("{digest}.app")).is_dir());
    }

    #[test]
    fn unknown_or_modified_contents_are_preserved_and_reported() {
        let game = tempfile::tempdir().unwrap();
        let app = make_bundle(game.path());
        fs::write(app.join(".DS_Store"), b"Finder metadata").unwrap();
        add_duplicate(game.path(), &app);
        fs::write(app.join("user-file"), b"keep").unwrap();
        assert!(normalize(game.path())
            .unwrap_err()
            .contains("Unexpected application root"));
        assert!(app.join(DUPLICATE).exists());
        fs::remove_file(app.join("user-file")).unwrap();
        normalize(game.path()).unwrap();
        assert_eq!(fs::read(app.join(".DS_Store")).unwrap(), b"Finder metadata");
        let backups = game.path().join(BACKUPS);
        let current = fs::read_to_string(backups.join("current")).unwrap();
        let saved = backups.join(format!("{}.app", current.trim()));
        fs::write(saved.join("user-file"), b"keep").unwrap();
        add_duplicate(game.path(), &app);
        assert!(normalize(game.path())
            .unwrap_err()
            .contains("Backup was modified"));
        assert_eq!(fs::read(saved.join("user-file")).unwrap(), b"keep");
        assert!(app.join(DUPLICATE).exists());
    }

    #[test]
    fn legacy_stash_is_adopted_without_restoring_invalid_root() {
        let game = tempfile::tempdir().unwrap();
        let app = make_bundle(game.path());
        fs::write(app.join(".DS_Store"), b"Finder metadata").unwrap();
        let backups = game.path().join(BACKUPS);
        fs::create_dir(&backups).unwrap();
        copy_tree(&app, &backups.join(DUPLICATE));
        let digest = fingerprint(&backups.join(DUPLICATE)).unwrap();
        fs::write(backups.join(".DS_Store"), b"Legacy Finder metadata").unwrap();
        normalize(game.path()).unwrap();
        normalize(game.path()).unwrap();
        add_duplicate(game.path(), &app);
        normalize(game.path()).unwrap();
        assert!(!app.join(DUPLICATE).exists());
        assert_eq!(
            fingerprint(&backups.join(format!("{digest}.app"))).unwrap(),
            digest
        );
        assert!(!backups.join(DUPLICATE).exists());
        assert_eq!(children(&backups).unwrap().len(), 3);
        assert_eq!(fs::read(app.join(".DS_Store")).unwrap(), b"Finder metadata");
        assert_eq!(
            fs::read(backups.join(".DS_Store")).unwrap(),
            b"Legacy Finder metadata"
        );
    }

    #[test]
    fn backup_metadata_rejects_directories_links_and_unknown_files() {
        for kind in ["directory", "symbolic-link", "unknown-file"] {
            let game = tempfile::tempdir().unwrap();
            let app = make_bundle(game.path());
            add_duplicate(game.path(), &app);
            let backups = game.path().join(BACKUPS);
            fs::create_dir(&backups).unwrap();
            let outside = game.path().join("outside");
            fs::write(&outside, b"keep").unwrap();
            let entry = backups.join(if kind == "unknown-file" {
                "user-file"
            } else {
                ".DS_Store"
            });
            match kind {
                "directory" => fs::create_dir(&entry).unwrap(),
                "symbolic-link" => std::os::unix::fs::symlink(&outside, &entry).unwrap(),
                _ => fs::write(&entry, b"keep").unwrap(),
            }
            assert!(normalize(game.path()).is_err(), "{kind}");
            assert!(app.join(DUPLICATE).is_dir());
            assert!(!backups.join("current").exists());
            assert_eq!(fs::read(&outside).unwrap(), b"keep");
            if kind == "directory" {
                assert!(entry.is_dir());
            } else {
                assert_eq!(fs::read(&entry).unwrap(), b"keep");
            }
        }
    }

    #[test]
    fn partly_deleted_duplicates_and_legacy_record_gaps_are_recoverable() {
        let game = tempfile::tempdir().unwrap();
        let app = make_bundle(game.path());
        add_duplicate(game.path(), &app);
        normalize(game.path()).unwrap();
        let backups = game.path().join(BACKUPS);
        let current = fs::read_to_string(backups.join("current"))
            .unwrap()
            .trim()
            .to_string();
        // Cleanup was interrupted after the verified redundant directory was renamed.
        let tombstone = backups.join(format!(".delete-{current}.app"));
        fs::create_dir(&tombstone).unwrap();
        fs::write(tombstone.join("last-file"), b"partly deleted contents").unwrap();
        normalize(game.path()).unwrap();
        assert!(!tombstone.exists());
        assert!(backups.join(format!("{current}.app")).is_dir());
        // Adopt an intact, uniquely identifiable snapshot left by an older migration.
        fs::remove_file(backups.join("current")).unwrap();
        normalize(game.path()).unwrap();
        assert_eq!(
            fs::read_to_string(backups.join("current")).unwrap().trim(),
            current
        );
    }

    #[test]
    fn mismatched_engine_and_symbolic_links_are_not_moved() {
        let game = tempfile::tempdir().unwrap();
        let app = make_bundle(game.path());
        add_duplicate(game.path(), &app);
        let engine = app
            .join(DUPLICATE)
            .join("Contents/Frameworks/UnityPlayer.dylib");
        fs::write(&engine, b"different").unwrap();
        assert!(normalize(game.path()).unwrap_err().contains("differs"));
        fs::write(&engine, b"original").unwrap();
        std::os::unix::fs::symlink(game.path(), app.join(DUPLICATE).join("link")).unwrap();
        assert!(normalize(game.path())
            .unwrap_err()
            .contains("symbolic link"));
        assert!(app.join(DUPLICATE).exists());
        assert!(!game.path().join(BACKUPS).exists());
    }
}

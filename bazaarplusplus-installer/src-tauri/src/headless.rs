//! Process entry for developer commands; never initializes a Tauri application.

use std::ffi::OsString;
use std::io::Write;
use std::path::{Path, PathBuf};

const HELP: &str = "Usage:\n  bppinstaller repair --game <directory> [--stub <file>] [--manifest <file>]\n  bppinstaller import --game <directory> --manifest <file> [--now <RFC 3339 time>]\n\nrepair: Repair the macOS launch trampoline of an existing BepInEx installation.\nKeeps the installed Payload and user data. With --manifest, writes a sorted\npath/sha256 manifest outside the game directory. Packaged apps use their bundled\nstub; source checkouts must supply --stub.\n\nimport: Import BazaarPlusPlusV5 history into the current Data Root once, as the\napp does. Writes the outcome, the Data Root's path/sha256 records, and its\ndatabase rows as JSON to --manifest, outside the game directory, and exits 1\nwhen the import failed. --now fixes the clock for the replay retention window.\n\nWithout arguments, starts the desktop app.";

pub fn run(arguments: impl IntoIterator<Item = OsString>) -> Option<i32> {
    let mut args = arguments.into_iter();
    let command = args.next()?;
    if command == "--help" || command == "-h" {
        println!("{HELP}");
        return Some(0);
    }
    let result = if command == "repair" {
        repair(args)
    } else if command == "import" {
        import(args)
    } else {
        Err(format!(
            "Unknown command: {}\n{HELP}",
            command.to_string_lossy()
        ))
    };
    Some(match result {
        Ok(()) => 0,
        Err(error) => {
            eprintln!("{error}");
            1
        }
    })
}

/// Reads `--name value` pairs; each name may appear once.
fn options<const N: usize>(
    command: &str,
    names: [&str; N],
    mut args: impl Iterator<Item = OsString>,
) -> Result<[Option<OsString>; N], String> {
    let mut values: [Option<OsString>; N] = std::array::from_fn(|_| None);
    while let Some(option) = args.next() {
        let Some(index) = names.iter().position(|name| option == *name) else {
            return Err(format!(
                "Unknown {command} option: {}",
                option.to_string_lossy()
            ));
        };
        if values[index].is_some() {
            return Err(format!("Duplicate option: {}", option.to_string_lossy()));
        }
        values[index] = Some(
            args.next()
                .ok_or_else(|| format!("Missing value for {}", option.to_string_lossy()))?,
        );
    }
    Ok(values)
}

fn canonical_game(game: Option<OsString>, command: &str) -> Result<PathBuf, String> {
    PathBuf::from(game.ok_or_else(|| format!("{command} requires --game <directory>"))?)
        .canonicalize()
        .map_err(|error| format!("Cannot open game directory: {error}"))
}

/// A temporary file beside the manifest destination, published over it once
/// written. The destination must lie outside the game directory and must not
/// be a symbolic link or anything else but a regular file.
fn stage_manifest(
    path: PathBuf,
    game: &Path,
) -> Result<(tempfile::NamedTempFile, PathBuf), String> {
    let parent = path
        .parent()
        .filter(|parent| !parent.as_os_str().is_empty())
        .unwrap_or(Path::new("."));
    let parent = parent
        .canonicalize()
        .map_err(|error| format!("Cannot open manifest directory: {error}"))?;
    if parent.starts_with(game) {
        return Err("Write the manifest outside the game directory".into());
    }
    match std::fs::symlink_metadata(&path) {
        Ok(metadata) if !metadata.file_type().is_file() => {
            return Err("Manifest destination must be a regular file".into());
        }
        Err(error) if error.kind() != std::io::ErrorKind::NotFound => {
            return Err(format!("Cannot inspect manifest destination: {error}"));
        }
        _ => {}
    }
    let file = tempfile::NamedTempFile::new_in(parent)
        .map_err(|error| format!("Cannot stage manifest: {error}"))?;
    Ok((file, path))
}

fn publish_manifest(
    (mut file, path): (tempfile::NamedTempFile, PathBuf),
    output: &str,
) -> Result<(), String> {
    file.write_all(output.as_bytes())
        .map_err(|error| format!("Cannot write manifest: {error}"))?;
    file.persist(path)
        .map_err(|error| format!("Cannot publish manifest: {error}"))?;
    Ok(())
}

fn repair(args: impl Iterator<Item = OsString>) -> Result<(), String> {
    let [game, stub, manifest] = options("repair", ["--game", "--stub", "--manifest"], args)?;
    let game = canonical_game(game, "repair")?;
    let stub = match stub {
        Some(path) => PathBuf::from(path),
        None => std::env::current_exe()
            .map_err(|error| error.to_string())?
            .parent()
            .ok_or("Cannot locate installer executable directory")?
            .join("../Resources/Trampoline/bpp_launcher"),
    };
    let manifest = manifest
        .map(|path| stage_manifest(PathBuf::from(path), &game))
        .transpose()?;
    crate::services::bepinex::repair_deployed_game(&game, &stub)?;
    match manifest {
        Some(staged) => {
            let entries = crate::services::file_manifest::tree_manifest(&game)?;
            publish_manifest(staged, &crate::services::file_manifest::json(&entries)?)
        }
        None => {
            println!("Repaired {}", game.display());
            Ok(())
        }
    }
}

fn import(args: impl Iterator<Item = OsString>) -> Result<(), String> {
    let [game, manifest, now] = options("import", ["--game", "--manifest", "--now"], args)?;
    let game = canonical_game(game, "import")?;
    let manifest = manifest.ok_or("import requires --manifest <file>")?;
    let now_ms = match now {
        Some(text) => chrono::DateTime::parse_from_rfc3339(&text.to_string_lossy())
            .map_err(|error| format!("--now is not an RFC 3339 time: {error}"))?
            .timestamp_millis(),
        None => chrono::Utc::now().timestamp_millis(),
    };
    let staged = stage_manifest(PathBuf::from(manifest), &game)?;
    let outcome = tauri::async_runtime::block_on(crate::v5_import::import_v5(
        &crate::services::data_maintenance::DataMaintenanceLock::default(),
        &crate::stream::runtime::StreamRuntime::default(),
        game.clone(),
        now_ms,
    ));
    publish_manifest(
        staged,
        &crate::v5_import::manifest::manifest_json(&game, &outcome)?,
    )?;
    match outcome {
        Ok(_) | Err(crate::v5_import::ImportError::AlreadyImported) => Ok(()),
        Err(error) => Err(format!("Import failed ({}): {error}", error.code())),
    }
}

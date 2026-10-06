//! Process entry for developer repair; never initializes a Tauri application.

use std::ffi::OsString;
use std::io::Write;
use std::path::PathBuf;

const HELP: &str = "Usage: bppinstaller repair --game <directory> [--stub <file>] [--manifest <file>]\n\nRepair the macOS launch trampoline of an existing BepInEx installation.\nKeeps the installed Payload and user data. With --manifest, writes a sorted\npath/sha256 manifest outside the game directory. Packaged apps use their bundled\nstub; source checkouts must supply --stub. Without arguments, starts the desktop app.";

pub fn run(arguments: impl IntoIterator<Item = OsString>) -> Option<i32> {
    let mut args = arguments.into_iter();
    let command = args.next()?;
    if command == "--help" || command == "-h" {
        println!("{HELP}");
        return Some(0);
    }
    let result = if command == "repair" {
        repair(args)
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

fn repair(mut args: impl Iterator<Item = OsString>) -> Result<(), String> {
    let mut game = None;
    let mut stub = None;
    let mut manifest = None;
    while let Some(option) = args.next() {
        let slot = if option == "--game" {
            &mut game
        } else if option == "--stub" {
            &mut stub
        } else if option == "--manifest" {
            &mut manifest
        } else {
            return Err(format!(
                "Unknown repair option: {}",
                option.to_string_lossy()
            ));
        };
        if slot.is_some() {
            return Err(format!("Duplicate option: {}", option.to_string_lossy()));
        }
        *slot = Some(PathBuf::from(args.next().ok_or_else(|| {
            format!("Missing value for {}", option.to_string_lossy())
        })?));
    }
    let game = game.ok_or("repair requires --game <directory>")?;
    let game = game
        .canonicalize()
        .map_err(|error| format!("Cannot open game directory: {error}"))?;
    let stub = match stub {
        Some(path) => path,
        None => std::env::current_exe()
            .map_err(|error| error.to_string())?
            .parent()
            .ok_or("Cannot locate installer executable directory")?
            .join("../Resources/Trampoline/bpp_launcher"),
    };
    let manifest = if let Some(path) = manifest {
        let parent = path
            .parent()
            .filter(|parent| !parent.as_os_str().is_empty())
            .unwrap_or(std::path::Path::new("."));
        let parent = parent
            .canonicalize()
            .map_err(|error| format!("Cannot open manifest directory: {error}"))?;
        if parent.starts_with(&game) {
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
        Some((file, path))
    } else {
        None
    };
    crate::services::bepinex::repair_deployed_game(&game, &stub)?;
    match manifest {
        Some((mut file, path)) => {
            let entries = crate::services::file_manifest::tree_manifest(&game)?;
            let output = crate::services::file_manifest::json(&entries)?;
            file.write_all(output.as_bytes())
                .map_err(|error| format!("Cannot write manifest: {error}"))?;
            file.persist(path)
                .map_err(|error| format!("Cannot publish manifest: {error}"))?;
            Ok(())
        }
        None => {
            println!("Repaired {}", game.display());
            Ok(())
        }
    }
}

use anyhow::{anyhow, bail, Context as _};
use std::ffi::OsString;
use std::io::Write;
use std::path::{Path, PathBuf};

const HELP: &str = "Usage:\n  bppinstaller repair --game <directory> [--stub <file>] [--manifest <file>]\n  bppinstaller card-catalog --data <directory> --game-data <file> --translations <file> --out <directory> --revision <id> [--gamedata-etag <etag>] [--zh-etag <etag>]\n\nrepair: Repair the macOS launch trampoline of an existing BepInEx installation.\nKeeps the installed Payload and user data. With --manifest, writes a sorted\npath/sha256 manifest outside the game directory. Packaged apps use their bundled\nstub; source checkouts must supply --stub.\n\ncard-catalog: Export the card catalog History shows from the installed game.\nThe desktop app runs it as a child process and stops it by closing stdin.\n\nWithout arguments, starts the desktop app.";

pub fn run(arguments: impl IntoIterator<Item = OsString>) -> Option<i32> {
    let mut args = arguments.into_iter();
    let command = args.next()?;
    if command == "--help" || command == "-h" {
        println!("{HELP}");
        return Some(0);
    }
    let result = if command == "repair" {
        repair(args)
    } else if command == "card-catalog" {
        card_catalog(args)
    } else {
        Err(anyhow!(
            "Unknown command: {}\n{HELP}",
            command.to_string_lossy()
        ))
    };
    Some(match result {
        Ok(()) => 0,
        Err(error) => {
            eprintln!("{error:#}");
            1
        }
    })
}

fn options<const N: usize>(
    command: &str,
    names: [&str; N],
    mut args: impl Iterator<Item = OsString>,
) -> anyhow::Result<[Option<OsString>; N]> {
    let mut values: [Option<OsString>; N] = std::array::from_fn(|_| None);
    while let Some(option) = args.next() {
        let Some(index) = names.iter().position(|name| option == *name) else {
            bail!("Unknown {command} option: {}", option.to_string_lossy());
        };
        if values[index].is_some() {
            bail!("Duplicate option: {}", option.to_string_lossy());
        }
        values[index] = Some(
            args.next()
                .with_context(|| format!("Missing value for {}", option.to_string_lossy()))?,
        );
    }
    Ok(values)
}

fn canonical_game(game: Option<OsString>, command: &str) -> anyhow::Result<PathBuf> {
    PathBuf::from(game.with_context(|| format!("{command} requires --game <directory>"))?)
        .canonicalize()
        .context("Cannot open game directory")
}

/// Stage next to the destination so publishing can use an atomic rename.
fn stage_manifest(
    path: PathBuf,
    game: &Path,
) -> anyhow::Result<(tempfile::NamedTempFile, PathBuf)> {
    let parent = path
        .parent()
        .filter(|parent| !parent.as_os_str().is_empty())
        .unwrap_or(Path::new("."));
    let parent = parent
        .canonicalize()
        .context("Cannot open manifest directory")?;
    if parent.starts_with(game) {
        bail!("Write the manifest outside the game directory");
    }
    match std::fs::symlink_metadata(&path) {
        Ok(metadata) if !metadata.file_type().is_file() => {
            bail!("Manifest destination must be a regular file");
        }
        Err(error) if error.kind() != std::io::ErrorKind::NotFound => {
            bail!("Cannot inspect manifest destination: {error}");
        }
        _ => {}
    }
    let file = tempfile::NamedTempFile::new_in(parent).context("Cannot stage manifest")?;
    Ok((file, path))
}

fn publish_manifest(
    (mut file, path): (tempfile::NamedTempFile, PathBuf),
    output: &str,
) -> anyhow::Result<()> {
    file.write_all(output.as_bytes())
        .context("Cannot write manifest")?;
    file.persist(path).context("Cannot publish manifest")?;
    Ok(())
}

fn repair(args: impl Iterator<Item = OsString>) -> anyhow::Result<()> {
    let [game, stub, manifest] = options("repair", ["--game", "--stub", "--manifest"], args)?;
    let game = canonical_game(game, "repair")?;
    let stub = match stub {
        Some(path) => PathBuf::from(path),
        None => std::env::current_exe()?
            .parent()
            .context("Cannot locate installer executable directory")?
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

fn card_catalog(args: impl Iterator<Item = OsString>) -> anyhow::Result<()> {
    let [data, game_data, translations, out, revision, gamedata_etag, zh_etag] = options(
        "card-catalog",
        [
            "--data",
            "--game-data",
            "--translations",
            "--out",
            "--revision",
            "--gamedata-etag",
            "--zh-etag",
        ],
        args,
    )?;
    let path = |value: Option<OsString>, option: &str| {
        value
            .map(PathBuf::from)
            .with_context(|| format!("card-catalog requires {option}"))
    };
    let text = |value: Option<OsString>| value.map(|v| v.to_string_lossy().into_owned());
    crate::card_catalog::export(
        &path(data, "--data")?,
        &path(game_data, "--game-data")?,
        &path(translations, "--translations")?,
        &path(out, "--out")?,
        &text(revision).context("card-catalog requires --revision")?,
        text(gamedata_etag),
        text(zh_etag),
    )
}

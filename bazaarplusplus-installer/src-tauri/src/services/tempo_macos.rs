use anyhow::Context as _;
use std::path::{Path, PathBuf};

const BUNDLE: &str = "TheBazaar_ARM64.app";

pub(super) fn default_game_path() -> Option<PathBuf> {
    Some(dirs::data_dir()?.join("Tempo Launcher - Beta/game/buildarm64"))
}

pub(super) fn library(game: &Path) -> Option<&Path> {
    let parent = game.parent()?;
    (game.file_name()? == "buildarm64" && game.join(BUNDLE).is_dir()).then_some(parent)
}

pub(crate) fn game_bundle(game: &Path) -> PathBuf {
    game.join(if library(game).is_some() {
        BUNDLE
    } else {
        "TheBazaar.app"
    })
}

// Launcher 1.0.19 looks for TheBazaar.app first, then guesses from the first
// child directory. BepInEx sorts before the official ARM64 bundle.
const LAUNCH_ENTRY: &str = "TheBazaar.app";

pub(super) fn launch_entry_ready(game: &Path) -> bool {
    library(game).is_none()
        || std::fs::read_link(game.join(LAUNCH_ENTRY))
            .is_ok_and(|target| target == Path::new(BUNDLE))
}

pub(super) fn ensure_launch_entry(game: &Path) -> anyhow::Result<()> {
    if launch_entry_ready(game) {
        return Ok(());
    }
    let entry = game.join(LAUNCH_ENTRY);
    std::os::unix::fs::symlink(BUNDLE, &entry)
        .with_context(|| format!("Cannot create the Tempo launch link {} to {BUNDLE}; an existing entry must be preserved", entry.display()))
}

pub(super) fn clean_launch_entry(game: &Path) -> anyhow::Result<()> {
    if library(game).is_none() || !launch_entry_ready(game) {
        return Ok(());
    }
    // Extra directories (including another mod's runtime) still confuse Play.
    for entry in std::fs::read_dir(game)? {
        let entry = entry?;
        if entry.file_name() != BUNDLE && entry.file_type()?.is_dir() {
            return Ok(());
        }
    }
    std::fs::remove_file(game.join(LAUNCH_ENTRY)).context("Cannot remove the Tempo launch link")
}

/// Only the Steam copy owns this installation's launch options.
pub(super) fn steam_config<'a>(game: &Path, steam: Option<&'a Path>) -> Option<&'a Path> {
    steam.filter(|_| library(game).is_none())
}

pub(super) fn open_launcher() -> anyhow::Result<()> {
    let status = std::process::Command::new("/usr/bin/open")
        .args(["-b", "com.electron.tempo-launcher-beta"])
        .status()
        .context("Could not open Tempo Launcher")?;
    anyhow::ensure!(status.success(), "Tempo Launcher could not be opened");
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn launcher_finds_the_game_with_bepinex_present_and_foreign_entries_are_preserved() {
        let root = tempfile::tempdir().unwrap();
        let game = root.path().join("buildarm64");
        let bundle = game.join(BUNDLE);
        std::fs::create_dir_all(&bundle).unwrap();
        let bep = game.join("BepInEx");
        std::fs::create_dir(&bep).unwrap();
        ensure_launch_entry(&game).unwrap();
        ensure_launch_entry(&game).unwrap();
        assert_eq!(
            game.join(LAUNCH_ENTRY).canonicalize().unwrap(),
            bundle.canonicalize().unwrap()
        );
        clean_launch_entry(&game).unwrap();
        assert!(launch_entry_ready(&game));
        std::fs::remove_dir(&bep).unwrap();
        clean_launch_entry(&game).unwrap();
        assert!(!launch_entry_ready(&game));
        std::fs::create_dir(game.join(LAUNCH_ENTRY)).unwrap();
        assert!(ensure_launch_entry(&game).is_err());
        clean_launch_entry(&game).unwrap();
        assert!(game.join(LAUNCH_ENTRY).is_dir());
    }

    #[test]
    fn tempo_and_steam_have_independent_game_bundles_launch_options_and_data() {
        let root = tempfile::tempdir().unwrap();
        let tempo = root.path().join("custom library/buildarm64");
        let steam = root.path().join("Steam");
        let steam_game = steam.join("steamapps/common/The Bazaar");
        std::fs::create_dir_all(tempo.join(BUNDLE)).unwrap();
        std::fs::create_dir_all(steam_game.join("TheBazaar.app")).unwrap();
        assert_eq!(game_bundle(&tempo), tempo.join(BUNDLE));
        assert_eq!(steam_config(&tempo, Some(&steam)), None);
        assert_eq!(
            steam_config(&steam_game, Some(&steam)),
            Some(steam.as_path())
        );
        assert_eq!(game_bundle(&steam_game), steam_game.join("TheBazaar.app"));

        let name = crate::config::BAZAAR_DATA_DIRECTORY;
        let old = tempo.join(name);
        std::fs::create_dir(&old).unwrap();
        std::fs::write(old.join("history"), b"original").unwrap();
        crate::services::paths::migrate_tempo_data(&tempo).unwrap();
        let stable = crate::services::paths::bpp_data_dir(&tempo);
        assert_eq!(stable, tempo.parent().unwrap().join(name));
        assert_eq!(std::fs::read(stable.join("history")).unwrap(), b"original");
        assert_eq!(
            crate::services::paths::bpp_data_dir(&steam_game),
            steam_game.join(name)
        );
    }
}

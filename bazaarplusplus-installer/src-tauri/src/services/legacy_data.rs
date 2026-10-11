//! Legacy Roots: Data Roots an earlier major version wrote into the game
//! directory. They are user-owned (ADR-0008): the installer measures them and
//! deletes one only after the user confirms that root. Nothing here runs on its
//! own, and Reset and uninstall never reach these directories.

use std::fs::Metadata;
use std::io;
use std::path::{Path, PathBuf};

use serde::Serialize;

use crate::config::BAZAAR_DATA_DIRECTORY;
use crate::services::bepinex::{ensure_valid_game_path, remove_dir_with_retry};
use crate::services::data_maintenance::DataMaintenanceLock;
use crate::services::game_process::{
    ensure_game_stopped_with, GameProcessProbe, GameStoppedOperation, SystemGameProcessProbe,
};

/// Stable error-code prefixes for a refused or partially failed Legacy Root
/// deletion; `install_action_problem` turns them into semantic problems.
pub(crate) const LEGACY_ROOT_ERR_UNKNOWN: &str = "legacy_root_unknown";
pub(crate) const LEGACY_ROOT_ERR_PARTIAL_FAILURE: &str = "legacy_root_delete_partial_failure";

/// The mod's Payload file that names the Data Root it writes. Mods before 6.0
/// ship it without `dataRootDirectoryName`.
const INSTALLED_HISTORY_DATABASE_CONTRACT: &str =
    "BepInEx/plugins/BazaarPlusPlus.history-database.json";

/// One Legacy Root. [`LegacyRoot::ALL`] is the only list of them in the
/// installer; it never contains the current Data Root.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, specta::Type)]
pub enum LegacyRoot {
    #[serde(rename = "BazaarPlusPlus")]
    Original,
    #[serde(rename = "BazaarPlusPlusV4")]
    V4,
    #[serde(rename = "BazaarPlusPlusV5")]
    V5,
}

impl LegacyRoot {
    pub(crate) const ALL: [Self; 3] = [Self::Original, Self::V4, Self::V5];

    pub(crate) fn directory_name(self) -> &'static str {
        match self {
            Self::Original => "BazaarPlusPlus",
            Self::V4 => "BazaarPlusPlusV4",
            Self::V5 => "BazaarPlusPlusV5",
        }
    }

    pub(crate) fn from_directory_name(name: &str) -> Option<Self> {
        Self::ALL
            .into_iter()
            .find(|root| root.directory_name() == name)
    }

    pub(crate) fn path_in(self, game_path: &Path) -> PathBuf {
        game_path.join(self.directory_name())
    }
}

/// One Legacy Root present in the selected game directory, measured without
/// following symbolic links. A file with more than one hard link is counted in
/// `size_bytes` and in `hard_linked_file_count`: deleting the root frees its
/// blocks only when no other link remains, so the size is an upper bound on
/// what deletion reclaims.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, specta::Type)]
pub struct LegacyDataRoot {
    pub name: LegacyRoot,
    pub size_bytes: u64,
    pub file_count: u64,
    pub hard_linked_file_count: u64,
    /// Entries whose metadata or contents could not be read; their size is
    /// missing from `size_bytes`.
    pub unreadable_entry_count: u64,
}

/// Which Data Root the installed mod writes, from its Payload contract file.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, specta::Type)]
#[serde(rename_all = "snake_case")]
pub enum InstalledModDataRoot {
    /// The contract names `BAZAAR_DATA_DIRECTORY`.
    Current,
    /// The contract is missing, unreadable, has no `dataRootDirectoryName`, or
    /// names another directory: the installed mod predates the current Data
    /// Root and must be reinstalled.
    ReinstallRequired,
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, specta::Type)]
pub struct LegacyDataState {
    pub game_path: Option<String>,
    pub roots: Vec<LegacyDataRoot>,
    pub installed_mod_data_root: InstalledModDataRoot,
    /// The one-time V5 import may run (`v5_import::is_v5_import_eligible`).
    pub v5_import_eligible: bool,
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, specta::Type)]
pub struct DeleteLegacyRootResult {
    /// False when the root was already gone: nothing was deleted.
    pub removed: bool,
    pub state: LegacyDataState,
}

impl LegacyDataState {
    pub(crate) fn without_game() -> Self {
        Self {
            game_path: None,
            roots: Vec::new(),
            installed_mod_data_root: InstalledModDataRoot::ReinstallRequired,
            v5_import_eligible: false,
        }
    }
}

/// The installer's own settings folder (`legacy_overlay_settings_path`'s
/// parent). It shares the `BazaarPlusPlusV4` name but is never a Legacy Root.
pub(crate) fn installer_settings_directory() -> PathBuf {
    let settings = crate::services::paths::legacy_overlay_settings_path();
    settings.parent().map(Path::to_path_buf).unwrap_or(settings)
}

/// Measures the Legacy Roots in `game_path`. Blocking: it walks every file.
pub(crate) fn legacy_data_state(
    game_path: &Path,
    installer_settings_dir: &Path,
) -> LegacyDataState {
    LegacyDataState {
        game_path: Some(game_path.to_string_lossy().into_owned()),
        roots: present_legacy_roots(game_path, installer_settings_dir)
            .into_iter()
            .map(|root| measure_root(root, &root.path_in(game_path)))
            .collect(),
        installed_mod_data_root: installed_mod_data_root(game_path),
        v5_import_eligible: crate::v5_import::is_v5_import_eligible(game_path),
    }
}

/// The Legacy Roots that exist as real directories directly under `game_path`.
/// A file or symbolic link with a Legacy Root's name is not one.
fn present_legacy_roots(game_path: &Path, installer_settings_dir: &Path) -> Vec<LegacyRoot> {
    LegacyRoot::ALL
        .into_iter()
        .filter(|root| {
            let path = root.path_in(game_path);
            is_real_directory(&path) && !same_directory(&path, installer_settings_dir)
        })
        .collect()
}

pub(crate) fn is_real_directory(path: &Path) -> bool {
    std::fs::symlink_metadata(path).is_ok_and(|metadata| metadata.file_type().is_dir())
}

fn same_directory(left: &Path, right: &Path) -> bool {
    match (left.canonicalize(), right.canonicalize()) {
        (Ok(left), Ok(right)) => left == right,
        _ => left == right,
    }
}

fn measure_root(name: LegacyRoot, root: &Path) -> LegacyDataRoot {
    let mut usage = LegacyDataRoot {
        name,
        size_bytes: 0,
        file_count: 0,
        hard_linked_file_count: 0,
        unreadable_entry_count: 0,
    };
    let mut pending = vec![root.to_path_buf()];
    while let Some(directory) = pending.pop() {
        let Ok(entries) = std::fs::read_dir(&directory) else {
            usage.unreadable_entry_count += 1;
            continue;
        };
        for entry in entries {
            let Ok(entry) = entry else {
                usage.unreadable_entry_count += 1;
                continue;
            };
            let path = entry.path();
            // `symlink_metadata` describes a link itself, so links are never
            // followed out of the root.
            let Ok(metadata) = std::fs::symlink_metadata(&path) else {
                usage.unreadable_entry_count += 1;
                continue;
            };
            let file_type = metadata.file_type();
            if file_type.is_dir() {
                pending.push(path);
            } else if file_type.is_file() {
                usage.size_bytes += metadata.len();
                usage.file_count += 1;
                match link_count(&path, &metadata) {
                    Ok(links) if links > 1 => usage.hard_linked_file_count += 1,
                    Ok(_) => {}
                    Err(_) => usage.unreadable_entry_count += 1,
                }
            }
        }
    }
    usage
}

#[cfg(unix)]
fn link_count(_path: &Path, metadata: &Metadata) -> io::Result<u64> {
    use std::os::unix::fs::MetadataExt;
    Ok(metadata.nlink())
}

#[cfg(windows)]
fn link_count(path: &Path, _metadata: &Metadata) -> io::Result<u64> {
    use std::os::windows::fs::OpenOptionsExt;
    use std::os::windows::io::AsRawHandle;

    use windows::Win32::Foundation::HANDLE;
    use windows::Win32::Storage::FileSystem::{
        GetFileInformationByHandle, BY_HANDLE_FILE_INFORMATION,
    };

    // Access mode 0 queries metadata without read access, so a file the game
    // or another process holds open still reports its link count.
    let file = std::fs::OpenOptions::new().access_mode(0).open(path)?;
    let mut information = BY_HANDLE_FILE_INFORMATION::default();
    // SAFETY: `file` owns a valid handle for the duration of the call and
    // `information` is a writable struct of the expected layout.
    unsafe { GetFileInformationByHandle(HANDLE(file.as_raw_handle()), &raw mut information) }?;
    Ok(u64::from(information.nNumberOfLinks))
}

#[cfg(not(any(unix, windows)))]
fn link_count(_path: &Path, _metadata: &Metadata) -> io::Result<u64> {
    Ok(1)
}

/// Which Data Root the installed mod writes. Any doubt reads as
/// [`InstalledModDataRoot::ReinstallRequired`].
pub(crate) fn installed_mod_data_root(game_path: &Path) -> InstalledModDataRoot {
    let named = std::fs::read(game_path.join(INSTALLED_HISTORY_DATABASE_CONTRACT))
        .ok()
        .and_then(|bytes| serde_json::from_slice::<serde_json::Value>(&bytes).ok())
        .and_then(|contract| {
            contract
                .get("dataRootDirectoryName")
                .and_then(serde_json::Value::as_str)
                .map(str::to_owned)
        });
    if named.as_deref() == Some(BAZAAR_DATA_DIRECTORY) {
        InstalledModDataRoot::Current
    } else {
        InstalledModDataRoot::ReinstallRequired
    }
}

/// Deletes the confirmed Legacy Root `name` under the data maintenance lock,
/// refusing while the game runs, and returns the remeasured state.
pub(crate) async fn delete_legacy_root(
    maintenance: &DataMaintenanceLock,
    game_path: String,
    name: String,
) -> Result<DeleteLegacyRootResult, String> {
    let installer_settings_dir = installer_settings_directory();
    maintenance
        .run_blocking(move || {
            delete_legacy_root_blocking_with(
                Path::new(&game_path),
                &name,
                &installer_settings_dir,
                &SystemGameProcessProbe,
            )
        })
        .await?
}

pub(crate) fn delete_legacy_root_blocking_with(
    game_path: &Path,
    name: &str,
    installer_settings_dir: &Path,
    probe: &impl GameProcessProbe,
) -> Result<DeleteLegacyRootResult, String> {
    let root = LegacyRoot::from_directory_name(name)
        .ok_or_else(|| format!("{LEGACY_ROOT_ERR_UNKNOWN}:{name}"))?;
    ensure_valid_game_path(game_path)?;
    ensure_game_stopped_with(probe, game_path, GameStoppedOperation::LegacyDelete)?;

    let path = root.path_in(game_path);
    if same_directory(&path, installer_settings_dir) {
        return Err(format!("{LEGACY_ROOT_ERR_UNKNOWN}:{name}"));
    }
    let removed = is_real_directory(&path);
    if removed {
        // The same removal Reset uses for the current Data Root.
        let report = remove_dir_with_retry(&path);
        if !report.is_empty() {
            return Err(format!(
                "{LEGACY_ROOT_ERR_PARTIAL_FAILURE}:{}",
                report
                    .failed
                    .iter()
                    .map(|failed| failed.display().to_string())
                    .collect::<Vec<_>>()
                    .join("\u{1f}")
            ));
        }
    }
    Ok(DeleteLegacyRootResult {
        removed,
        state: legacy_data_state(game_path, installer_settings_dir),
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::problem::SemanticProblemCode;
    use crate::services::install::install_action_problem;

    fn stopped(_: &Path) -> Result<bool, String> {
        Ok(false)
    }

    fn valid_game_dir() -> tempfile::TempDir {
        let tmp = tempfile::tempdir().unwrap();
        #[cfg(target_os = "macos")]
        std::fs::create_dir_all(tmp.path().join("TheBazaar.app")).unwrap();
        #[cfg(target_os = "windows")]
        std::fs::write(tmp.path().join("TheBazaar.exe"), b"exe").unwrap();
        #[cfg(not(any(target_os = "macos", target_os = "windows")))]
        std::fs::write(tmp.path().join("TheBazaar"), b"exe").unwrap();
        tmp
    }

    fn write(path: &Path, bytes: &[u8]) {
        std::fs::create_dir_all(path.parent().unwrap()).unwrap();
        std::fs::write(path, bytes).unwrap();
    }

    /// Three Legacy Roots of known size and the current Data Root.
    fn seed_legacy_roots(game: &Path) {
        write(&game.join("BazaarPlusPlus/bazaarplusplus.db"), &[0; 10]);
        write(&game.join("BazaarPlusPlusV4/a/b/c.png"), &[0; 7]);
        write(&game.join("BazaarPlusPlusV4/d.txt"), &[0; 3]);
        write(&game.join("BazaarPlusPlusV5/bazaarplusplus.db"), &[0; 100]);
        write(&game.join("BazaarPlusPlusV5/Screenshots/run.png"), &[0; 20]);
        write(
            &game.join(BAZAAR_DATA_DIRECTORY).join("bazaarplusplus.db"),
            &[0; 1000],
        );
    }

    fn no_settings_dir() -> PathBuf {
        PathBuf::from("/nonexistent-installer-settings")
    }

    #[test]
    fn the_legacy_root_list_never_names_the_current_data_root() {
        for root in LegacyRoot::ALL {
            assert_ne!(root.directory_name(), BAZAAR_DATA_DIRECTORY);
            assert_eq!(
                serde_json::to_value(root).unwrap(),
                serde_json::json!(root.directory_name())
            );
            assert_eq!(
                LegacyRoot::from_directory_name(root.directory_name()),
                Some(root)
            );
        }
        assert_eq!(LegacyRoot::from_directory_name(BAZAAR_DATA_DIRECTORY), None);
    }

    #[test]
    fn lists_only_present_legacy_root_directories_with_their_sizes() {
        let game = tempfile::tempdir().unwrap();
        seed_legacy_roots(game.path());
        // Files beside the roots, named after one, are not roots.
        write(&game.path().join("BazaarPlusPlusV5.zip"), &[0; 64]);
        write(&game.path().join("BazaarPlusPlusV4.txt"), b"file");

        let state = legacy_data_state(game.path(), &no_settings_dir());

        assert_eq!(
            state
                .roots
                .iter()
                .map(|root| (root.name, root.size_bytes, root.file_count))
                .collect::<Vec<_>>(),
            vec![
                (LegacyRoot::Original, 10, 1),
                (LegacyRoot::V4, 10, 2),
                (LegacyRoot::V5, 120, 2),
            ]
        );
        assert!(state
            .roots
            .iter()
            .all(|root| root.hard_linked_file_count == 0 && root.unreadable_entry_count == 0));
    }

    #[test]
    fn a_file_named_like_a_legacy_root_is_not_listed() {
        let game = tempfile::tempdir().unwrap();
        write(&game.path().join("BazaarPlusPlusV5"), b"file");

        assert!(legacy_data_state(game.path(), &no_settings_dir())
            .roots
            .is_empty());
    }

    #[test]
    fn the_installer_settings_folder_is_never_a_legacy_root() {
        // The installer keeps overlay settings in a `BazaarPlusPlusV4` folder of
        // its config directory. Even when that directory is the selected one,
        // the folder is neither listed nor deleted.
        let config = valid_game_dir();
        let settings = config.path().join("BazaarPlusPlusV4");
        write(&settings.join("stream-overlay-crop.json"), b"{}");

        let state = legacy_data_state(config.path(), &settings);
        assert!(state.roots.is_empty());

        let refused = delete_legacy_root_blocking_with(
            config.path(),
            "BazaarPlusPlusV4",
            &settings,
            &stopped,
        )
        .unwrap_err();
        assert_eq!(
            install_action_problem("delete_legacy_root", refused).code,
            SemanticProblemCode::InstallActionFailed
        );
        assert!(settings.join("stream-overlay-crop.json").is_file());
    }

    #[test]
    fn the_real_installer_settings_path_is_the_legacy_overlay_settings_folder() {
        assert_eq!(
            installer_settings_directory().join("stream-overlay-crop.json"),
            crate::services::paths::legacy_overlay_settings_path()
        );
    }

    #[cfg(unix)]
    #[test]
    fn measurement_does_not_follow_symbolic_links_and_counts_hard_links() {
        let game = tempfile::tempdir().unwrap();
        let outside = tempfile::tempdir().unwrap();
        write(&outside.path().join("big.bin"), &[0; 4096]);
        write(&game.path().join("BazaarPlusPlusV5/own.bin"), &[0; 5]);
        std::os::unix::fs::symlink(outside.path(), game.path().join("BazaarPlusPlusV5/outside"))
            .unwrap();
        std::fs::hard_link(
            game.path().join("BazaarPlusPlusV5/own.bin"),
            outside.path().join("linked.bin"),
        )
        .unwrap();
        // A symlinked Legacy Root name is not a Legacy Root directory.
        std::os::unix::fs::symlink(outside.path(), game.path().join("BazaarPlusPlusV4")).unwrap();

        let state = legacy_data_state(game.path(), &no_settings_dir());

        assert_eq!(state.roots.len(), 1);
        let root = &state.roots[0];
        assert_eq!(root.name, LegacyRoot::V5);
        assert_eq!(root.size_bytes, 5);
        assert_eq!(root.file_count, 1);
        assert_eq!(root.hard_linked_file_count, 1);
    }

    #[test]
    fn installed_mod_data_root_requires_the_current_name_in_the_contract() {
        let game = tempfile::tempdir().unwrap();
        let contract = game.path().join(INSTALLED_HISTORY_DATABASE_CONTRACT);

        // No mod contract at all.
        assert_eq!(
            installed_mod_data_root(game.path()),
            InstalledModDataRoot::ReinstallRequired
        );

        // 5.7.0 shipped the contract without `dataRootDirectoryName`.
        write(
            &contract,
            br#"{ "formatVersion": 1, "historyDatabaseUserVersion": 3 }"#,
        );
        assert_eq!(
            installed_mod_data_root(game.path()),
            InstalledModDataRoot::ReinstallRequired
        );

        // A contract naming another directory.
        write(
            &contract,
            br#"{ "formatVersion": 1, "dataRootDirectoryName": "BazaarPlusPlusV5" }"#,
        );
        assert_eq!(
            installed_mod_data_root(game.path()),
            InstalledModDataRoot::ReinstallRequired
        );

        // Corrupt JSON.
        write(&contract, br#"{ "dataRootDirectoryName": "#);
        assert_eq!(
            installed_mod_data_root(game.path()),
            InstalledModDataRoot::ReinstallRequired
        );

        // The contract the current mod ships.
        let current = std::fs::read(
            Path::new(env!("CARGO_MANIFEST_DIR"))
                .join("../../bazaarplusplus-mod/src/BazaarPlusPlus.Storage/BazaarPlusPlus.history-database.json"),
        )
        .unwrap();
        write(&contract, &current);
        assert_eq!(
            installed_mod_data_root(game.path()),
            InstalledModDataRoot::Current
        );
    }

    #[test]
    fn deletes_only_the_confirmed_root_and_reports_an_absent_one() {
        let game = valid_game_dir();
        seed_legacy_roots(game.path());

        let result = delete_legacy_root_blocking_with(
            game.path(),
            "BazaarPlusPlusV4",
            &no_settings_dir(),
            &stopped,
        )
        .unwrap();

        assert!(result.removed);
        assert!(!game.path().join("BazaarPlusPlusV4").exists());
        assert!(game.path().join("BazaarPlusPlus").is_dir());
        assert!(game.path().join("BazaarPlusPlusV5").is_dir());
        assert!(game.path().join(BAZAAR_DATA_DIRECTORY).is_dir());
        assert_eq!(
            result
                .state
                .roots
                .iter()
                .map(|root| root.name)
                .collect::<Vec<_>>(),
            vec![LegacyRoot::Original, LegacyRoot::V5]
        );

        let again = delete_legacy_root_blocking_with(
            game.path(),
            "BazaarPlusPlusV4",
            &no_settings_dir(),
            &stopped,
        )
        .unwrap();
        assert!(!again.removed);
    }

    #[test]
    fn a_name_outside_the_list_is_refused_and_nothing_changes() {
        let game = valid_game_dir();
        seed_legacy_roots(game.path());
        write(
            &game.path().join("BepInEx/plugins/BazaarPlusPlus.dll"),
            b"bpp",
        );

        for name in [
            BAZAAR_DATA_DIRECTORY,
            "BepInEx",
            "bazaarplusplusv5",
            "BazaarPlusPlusV5/Screenshots",
            "../BazaarPlusPlusV5",
            "",
        ] {
            let refused =
                delete_legacy_root_blocking_with(game.path(), name, &no_settings_dir(), &stopped)
                    .unwrap_err();
            let problem = install_action_problem("delete_legacy_root", refused);
            assert_eq!(problem.code, SemanticProblemCode::InstallActionFailed);
            assert_eq!(
                problem.params.get("operation").map(String::as_str),
                Some("delete_legacy_root")
            );
        }

        assert!(game
            .path()
            .join(BAZAAR_DATA_DIRECTORY)
            .join("bazaarplusplus.db")
            .is_file());
        assert!(game
            .path()
            .join("BepInEx/plugins/BazaarPlusPlus.dll")
            .is_file());
        assert!(game
            .path()
            .join("BazaarPlusPlusV5/Screenshots/run.png")
            .is_file());
        assert_eq!(
            legacy_data_state(game.path(), &no_settings_dir())
                .roots
                .len(),
            3
        );
    }

    #[test]
    fn deletion_refuses_while_the_game_runs_or_cannot_be_inspected() {
        fn running(_: &Path) -> Result<bool, String> {
            Ok(true)
        }
        fn unknown(_: &Path) -> Result<bool, String> {
            Err("cannot inspect processes".to_string())
        }
        type Probe = fn(&Path) -> Result<bool, String>;
        let probes: [Probe; 2] = [running, unknown];
        for refusing in probes {
            let game = valid_game_dir();
            seed_legacy_roots(game.path());

            let refused = delete_legacy_root_blocking_with(
                game.path(),
                "BazaarPlusPlusV5",
                &no_settings_dir(),
                &refusing,
            )
            .unwrap_err();

            assert_eq!(
                install_action_problem("delete_legacy_root", refused).code,
                SemanticProblemCode::LegacyDeleteBlockedByGame
            );
            assert!(game
                .path()
                .join("BazaarPlusPlusV5/bazaarplusplus.db")
                .is_file());
            assert!(game
                .path()
                .join("BazaarPlusPlusV5/Screenshots/run.png")
                .is_file());
        }
    }

    #[tokio::test]
    async fn locked_deletion_refuses_while_the_game_runs() {
        let game = valid_game_dir();
        seed_legacy_roots(game.path());
        let lock = DataMaintenanceLock::default();
        let game_path = game.path().to_path_buf();

        let refused = lock
            .run_blocking(move || {
                delete_legacy_root_blocking_with(
                    &game_path,
                    "BazaarPlusPlusV5",
                    &no_settings_dir(),
                    &|_: &Path| Ok(true),
                )
            })
            .await
            .unwrap()
            .unwrap_err();

        assert_eq!(
            install_action_problem("delete_legacy_root", refused).code,
            SemanticProblemCode::LegacyDeleteBlockedByGame
        );
        assert!(game.path().join("BazaarPlusPlusV5").is_dir());
    }

    #[tokio::test]
    async fn reset_leaves_every_legacy_root_in_place() {
        let game = valid_game_dir();
        seed_legacy_roots(game.path());

        let removed = crate::services::bepinex::reset_bpp_data_with(
            &DataMaintenanceLock::default(),
            &crate::stream::runtime::StreamRuntime::default(),
            game.path().to_string_lossy().into_owned(),
            stopped,
        )
        .await
        .unwrap();

        assert!(removed);
        assert!(!game.path().join(BAZAAR_DATA_DIRECTORY).exists());
        let state = legacy_data_state(game.path(), &no_settings_dir());
        assert_eq!(
            state
                .roots
                .iter()
                .map(|root| (root.name, root.size_bytes))
                .collect::<Vec<_>>(),
            vec![
                (LegacyRoot::Original, 10),
                (LegacyRoot::V4, 10),
                (LegacyRoot::V5, 120),
            ]
        );
    }

    #[test]
    fn listing_and_measuring_never_delete() {
        let game = valid_game_dir();
        seed_legacy_roots(game.path());
        write(
            &crate::v5_import::v5_import_marker_path(game.path()),
            b"imported",
        );

        for _ in 0..2 {
            let state = legacy_data_state(game.path(), &no_settings_dir());
            assert_eq!(state.roots.len(), 3);
        }
        for root in LegacyRoot::ALL {
            assert!(root.path_in(game.path()).is_dir());
        }
    }
}

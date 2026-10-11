//! The one-time import of V5 history into the current Data Root (ADR-0008).
//! This module is deleted before 6.2.0; `release::check` refuses that version
//! while it exists.

use std::path::{Path, PathBuf};

use crate::services::legacy_data::{
    installed_mod_data_root, is_real_directory, InstalledModDataRoot, LegacyRoot,
};

/// The marker a completed import writes inside `BazaarPlusPlusV5/`. It lives
/// in the Legacy Root rather than the current Data Root, so a Reset does not
/// trigger a second import and deleting V5 removes the marker with it. Any
/// entry with this name counts, whatever its contents.
pub(crate) const V5_IMPORT_MARKER_FILE_NAME: &str = "imported-to-BazaarPlusPlusV6";

pub(crate) fn v5_import_marker_path(game_path: &Path) -> PathBuf {
    LegacyRoot::V5
        .path_in(game_path)
        .join(V5_IMPORT_MARKER_FILE_NAME)
}

/// The import may run: the installed mod writes the current Data Root,
/// `BazaarPlusPlusV5/` exists, and no earlier import left its marker.
pub(crate) fn is_v5_import_eligible(game_path: &Path) -> bool {
    installed_mod_data_root(game_path) == InstalledModDataRoot::Current
        && is_real_directory(&LegacyRoot::V5.path_in(game_path))
        && std::fs::symlink_metadata(v5_import_marker_path(game_path)).is_err()
}

#[cfg(test)]
mod tests {
    use super::*;

    fn write(path: &Path, bytes: &[u8]) {
        std::fs::create_dir_all(path.parent().unwrap()).unwrap();
        std::fs::write(path, bytes).unwrap();
    }

    fn install_mod_contract(game: &Path, data_root: Option<&str>) {
        let contract = match data_root {
            Some(name) => format!(r#"{{ "formatVersion": 1, "dataRootDirectoryName": "{name}" }}"#),
            None => r#"{ "formatVersion": 1, "historyDatabaseUserVersion": 3 }"#.to_string(),
        };
        write(
            &game.join("BepInEx/plugins/BazaarPlusPlus.history-database.json"),
            contract.as_bytes(),
        );
    }

    #[test]
    fn eligible_only_with_a_current_mod_a_v5_root_and_no_marker() {
        let game = tempfile::tempdir().unwrap();
        let game = game.path();
        assert!(!is_v5_import_eligible(game));

        write(&game.join("BazaarPlusPlusV5/bazaarplusplus.db"), b"db");
        // No mod installed.
        assert!(!is_v5_import_eligible(game));

        // A 5.x mod still writes V5.
        install_mod_contract(game, None);
        assert!(!is_v5_import_eligible(game));

        install_mod_contract(game, Some(crate::config::BAZAAR_DATA_DIRECTORY));
        assert!(is_v5_import_eligible(game));

        write(&v5_import_marker_path(game), b"");
        assert!(!is_v5_import_eligible(game));
        assert!(v5_import_marker_path(game).starts_with(game.join("BazaarPlusPlusV5")));
    }

    #[test]
    fn a_file_named_like_the_v5_root_is_not_eligible() {
        let game = tempfile::tempdir().unwrap();
        install_mod_contract(game.path(), Some(crate::config::BAZAAR_DATA_DIRECTORY));
        write(&game.path().join("BazaarPlusPlusV5"), b"file");

        assert!(!is_v5_import_eligible(game.path()));
    }
}

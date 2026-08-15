use std::{
    path::{Path, PathBuf},
    sync::{Arc, Mutex},
};

use crate::services::game_path::{
    fallback_game_candidates, fs_probe, resolve_game_path_core, GamePathAcceptance, GamePathInputs,
    GamePathProbe, GamePathResolution,
};
use crate::services::path::normalize_requested_game_path;
use crate::services::startup::InstallerContextState;
use tauri::Manager;

#[derive(Clone, Debug, PartialEq, Eq)]
pub(crate) struct SelectedGameInstallation {
    game_path: PathBuf,
}

impl SelectedGameInstallation {
    pub(crate) fn game_path(&self) -> &Path {
        &self.game_path
    }
}

#[derive(Clone, Default)]
pub struct SelectedGameInstallationState {
    selected: Arc<Mutex<Option<SelectedGameInstallation>>>,
}

impl SelectedGameInstallationState {
    pub(crate) fn snapshot(&self) -> Option<SelectedGameInstallation> {
        self.selected
            .lock()
            .expect("selected game installation poisoned")
            .clone()
    }

    pub(crate) fn accept_explicit_path(
        &self,
        game_path: PathBuf,
    ) -> Result<SelectedGameInstallation, String> {
        if !crate::services::detect::is_valid_game_path(&game_path) {
            return Err(format!(
                "The selected directory is not a valid The Bazaar installation: {}",
                game_path.display()
            ));
        }

        let installation = SelectedGameInstallation { game_path };
        *self
            .selected
            .lock()
            .expect("selected game installation poisoned") = Some(installation.clone());
        Ok(installation)
    }

    pub(crate) fn resolve(
        &self,
        app: &tauri::AppHandle,
        requested_game_path: Option<String>,
        acceptance: GamePathAcceptance,
    ) -> Option<GamePathResolution> {
        let requested = normalize_requested_game_path(requested_game_path);
        if let Some(game_path) = requested.as_ref() {
            let _ = self.accept_explicit_path(game_path.clone());
        }
        let startup = app
            .state::<InstallerContextState>()
            .get_or_initialize(app)
            .game_path
            .clone();
        let resolved = self.resolve_with(
            requested,
            startup,
            acceptance,
            fallback_game_candidates,
            fs_probe,
        );
        crate::services::debug_log!(
            "[selected_game_installation] acceptance={:?} -> game_path={:?} source={:?} db={}",
            acceptance,
            resolved
                .as_ref()
                .map(|resolution| resolution.game_path.display().to_string()),
            resolved.as_ref().map(|resolution| &resolution.source),
            resolved
                .as_ref()
                .map(|resolution| resolution.database_path.is_some())
                .unwrap_or(false),
        );
        resolved
    }

    fn resolve_with(
        &self,
        requested: Option<PathBuf>,
        startup: Option<PathBuf>,
        acceptance: GamePathAcceptance,
        candidates: impl Fn() -> Vec<PathBuf>,
        probe: impl Fn(GamePathProbe, &Path) -> bool,
    ) -> Option<GamePathResolution> {
        resolve_game_path_core(
            GamePathInputs {
                requested,
                selected: self
                    .snapshot()
                    .map(|installation| installation.game_path().to_path_buf()),
                startup,
            },
            acceptance,
            candidates,
            probe,
        )
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::services::game_path::GamePathSource;
    use std::collections::HashSet;

    fn path(value: &str) -> PathBuf {
        PathBuf::from(value)
    }

    fn mark_valid_game(game_path: &Path) {
        std::fs::create_dir_all(game_path).unwrap();
        #[cfg(target_os = "macos")]
        std::fs::create_dir(game_path.join("TheBazaar.app")).unwrap();
        #[cfg(target_os = "windows")]
        std::fs::write(game_path.join("TheBazaar.exe"), b"exe").unwrap();
        #[cfg(not(any(target_os = "macos", target_os = "windows")))]
        std::fs::write(game_path.join("TheBazaar"), b"exe").unwrap();
    }

    #[test]
    fn selected_game_installation_uses_explicit_selected_startup_fallback_priority() {
        struct Row {
            requested: Option<&'static str>,
            selected: Option<&'static str>,
            startup: Option<&'static str>,
            fallback: &'static [&'static str],
            installed: &'static [&'static str],
            expected: &'static str,
            source: GamePathSource,
        }

        let rows = [
            Row {
                requested: Some("/explicit"),
                selected: Some("/selected"),
                startup: Some("/startup"),
                fallback: &["/fallback"],
                installed: &["/fallback"],
                expected: "/explicit",
                source: GamePathSource::Explicit,
            },
            Row {
                requested: None,
                selected: Some("/selected"),
                startup: Some("/startup"),
                fallback: &["/fallback"],
                installed: &["/fallback"],
                expected: "/selected",
                source: GamePathSource::Selected,
            },
            Row {
                requested: None,
                selected: None,
                startup: Some("/startup"),
                fallback: &["/fallback"],
                installed: &["/fallback"],
                expected: "/startup",
                source: GamePathSource::Startup,
            },
            Row {
                requested: None,
                selected: None,
                startup: None,
                fallback: &["/fallback"],
                installed: &["/fallback"],
                expected: "/fallback",
                source: GamePathSource::Fallback,
            },
        ];

        for row in rows {
            let state = SelectedGameInstallationState::default();
            if let Some(selected) = row.selected {
                *state.selected.lock().unwrap() = Some(SelectedGameInstallation {
                    game_path: path(selected),
                });
            }
            let installed: HashSet<PathBuf> = row.installed.iter().map(|item| path(item)).collect();
            let resolved = state
                .resolve_with(
                    row.requested.map(path),
                    row.startup.map(path),
                    GamePathAcceptance::DetectionPick,
                    || row.fallback.iter().map(|item| path(item)).collect(),
                    |probe, candidate| {
                        probe == GamePathProbe::GameInstalled && installed.contains(candidate)
                    },
                )
                .unwrap();

            assert_eq!(resolved.game_path, path(row.expected));
            assert_eq!(resolved.source, row.source);
        }
    }

    #[test]
    fn invalid_explicit_path_does_not_replace_a_valid_selection() {
        let root = tempfile::tempdir().unwrap();
        let valid = root.path().join("valid");
        let invalid = root.path().join("invalid");
        mark_valid_game(&valid);
        std::fs::create_dir(&invalid).unwrap();
        let state = SelectedGameInstallationState::default();

        state.accept_explicit_path(valid.clone()).unwrap();
        assert!(state.accept_explicit_path(invalid).is_err());

        assert_eq!(state.snapshot().unwrap().game_path(), valid);
    }

    #[test]
    fn selected_installation_is_not_persisted_across_state_recreation() {
        let root = tempfile::tempdir().unwrap();
        let valid = root.path().join("valid");
        mark_valid_game(&valid);
        let first_session = SelectedGameInstallationState::default();
        first_session.accept_explicit_path(valid).unwrap();

        let next_session = SelectedGameInstallationState::default();

        assert!(next_session.snapshot().is_none());
    }

    #[test]
    fn resolving_without_any_installation_returns_none() {
        let state = SelectedGameInstallationState::default();

        let resolved = state.resolve_with(
            None,
            None,
            GamePathAcceptance::DetectionPick,
            Vec::new,
            |_, _| false,
        );

        assert!(resolved.is_none());
    }
}

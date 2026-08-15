use std::collections::BTreeMap;

use serde::Serialize;

#[derive(Clone, Debug, Serialize, specta::Type)]
pub struct InstallState {
    pub selected_game_path: Option<String>,
    pub steam_path: Option<String>,
    pub game: InstallGameState,
    pub mod_state: InstallModState,
    pub actions: InstallActions,
    pub has_resettable_data: bool,
    /// Whether a `BepInEx/` directory physically exists — gates the blunt
    /// "reset BepInEx folder" action independently of a healthy install, so it
    /// stays usable when BepInEx is broken/half-installed.
    pub has_bepinex_files: bool,
    pub warnings: Vec<InstallWarning>,
}

#[derive(Clone, Debug, Serialize, specta::Type)]
pub struct ResetBppDataResult {
    pub state: InstallState,
    pub removed_data: bool,
}

#[derive(Clone, Debug, Serialize, specta::Type)]
pub struct ResetBepinexResult {
    pub state: InstallState,
    pub removed: bool,
}

#[derive(Clone, Debug, Serialize, specta::Type)]
pub struct InstallGameState {
    pub found: bool,
    pub path_valid: bool,
    pub display_version: Option<String>,
}

#[derive(Clone, Debug, Serialize, specta::Type)]
pub struct InstallModState {
    pub installed: bool,
    pub installed_version: Option<String>,
    pub bundled_version: Option<String>,
    /// Payload version and the platform launch bootstrap are both ready.
    pub ready: bool,
}

#[derive(Clone, Debug, Serialize, specta::Type)]
pub struct InstallActions {
    pub can_install: bool,
    pub can_reinstall: bool,
    pub can_reset_data: bool,
    pub can_reset_bepinex: bool,
    pub can_uninstall: bool,
    pub can_launch: bool,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, specta::Type)]
#[serde(rename_all = "snake_case")]
pub enum InstallWarningCode {
    GameMissing,
    SteamConfigUnavailable,
    LaunchOptionsNotEmpty,
    TrampolineNotReady,
    ObsoleteMacosArtifacts,
}

#[derive(Clone, Debug, Serialize, specta::Type)]
pub struct InstallWarning {
    pub code: InstallWarningCode,
    pub params: BTreeMap<String, String>,
}

#[derive(Clone, Debug, Serialize, specta::Type)]
pub struct GameDirectorySelection {
    pub game_path: Option<String>,
}

#[derive(Clone, Debug, Serialize, specta::Type)]
pub struct FileActionResult {
    pub ok: bool,
}

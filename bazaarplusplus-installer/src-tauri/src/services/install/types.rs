use serde::Serialize;

#[derive(Clone, Debug, Serialize, ts_rs::TS)]
#[ts(export)]
pub struct InstallState {
    pub selected_game_path: Option<String>,
    pub steam_path: Option<String>,
    pub steam_launch_options_supported: bool,
    pub launch_flow: String,
    pub game: InstallGameState,
    pub mod_state: InstallModState,
    pub compat: InstallCompatState,
    pub actions: InstallActions,
    pub has_resettable_data: bool,
    pub warnings: Vec<InstallWarning>,
}

#[derive(Clone, Debug, Serialize, ts_rs::TS)]
#[ts(export)]
pub struct ResetBppDataResult {
    pub state: InstallState,
    pub removed_data: bool,
}

/// macOS launch-mode (兼容模式 / trampoline) state surfaced to the UI.
#[derive(Clone, Debug, Serialize, ts_rs::TS)]
#[ts(export)]
pub struct InstallCompatState {
    /// Show the opt-in "兼容模式" checkbox (macOS <= 26 only).
    pub mode_available: bool,
    /// macOS 27+: trampoline forced — render the checkbox checked and locked.
    pub forced: bool,
    /// The desired launch mode (checkbox default): forced, or the persisted marker.
    pub desired: bool,
    /// Whether the bundle currently has the trampoline applied.
    pub applied: bool,
}

#[derive(Clone, Debug, Serialize, ts_rs::TS)]
#[ts(export)]
pub struct InstallGameState {
    pub found: bool,
    pub path_valid: bool,
    pub display_version: Option<String>,
}

#[derive(Clone, Debug, Serialize, ts_rs::TS)]
#[ts(export)]
pub struct InstallModState {
    pub installed: bool,
    pub installed_version: Option<String>,
    pub bundled_version: Option<String>,
    pub version_matches: bool,
}

#[derive(Clone, Debug, Serialize, ts_rs::TS)]
#[ts(export)]
pub struct InstallActions {
    pub can_install: bool,
    pub can_reinstall: bool,
    pub can_reset_data: bool,
    pub can_uninstall: bool,
    pub can_launch: bool,
}

#[derive(Clone, Debug, Serialize, ts_rs::TS)]
#[ts(export)]
pub struct InstallWarning {
    pub code: String,
    pub message: String,
}

#[derive(Clone, Debug, Serialize, ts_rs::TS)]
#[ts(export)]
pub struct GameDirectorySelection {
    pub game_path: Option<String>,
}

#[derive(Clone, Debug, Serialize, ts_rs::TS)]
#[ts(export)]
pub struct FileActionResult {
    pub ok: bool,
}

use std::sync::OnceLock;

pub const BAZAAR_DATA_DIRECTORY: &str = "BazaarPlusPlusV5";
pub const INSTALLER_STATE_DIRECTORY: &str = "BazaarPlusPlusInstaller";
pub const COMBAT_REPLAYS_DIRECTORY: &str = "CombatReplays";
pub const COMBAT_REPLAY_VIDEOS_DIRECTORY: &str = "CombatReplayVideos";
pub const DATABASE_FILE_NAME: &str = "bazaarplusplus.db";
pub const SCREENSHOTS_DIRECTORY: &str = "Screenshots";

#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
struct HistoryDatabaseCompatibility {
    format_version: u32,
    supported_user_versions: Vec<i64>,
}

pub fn supported_mod_db_user_versions() -> &'static [i64] {
    static COMPATIBILITY: OnceLock<HistoryDatabaseCompatibility> = OnceLock::new();
    let compatibility = COMPATIBILITY.get_or_init(|| {
        let compatibility: HistoryDatabaseCompatibility =
            serde_json::from_str(include_str!("../history-database-compatibility.json"))
                .expect("history database compatibility must be valid JSON");
        assert_eq!(
            compatibility.format_version, 1,
            "history database compatibility format must be supported"
        );
        assert!(
            !compatibility.supported_user_versions.is_empty()
                && compatibility
                    .supported_user_versions
                    .windows(2)
                    .all(|versions| versions[0] > 0 && versions[0] < versions[1])
                && compatibility
                    .supported_user_versions
                    .last()
                    .is_some_and(|version| *version > 0),
            "history database compatibility versions must be positive, sorted, and unique"
        );
        compatibility
    });
    &compatibility.supported_user_versions
}

#[cfg(target_os = "windows")]
pub const STEAM_LIBRARY_FALLBACK_CANDIDATES: &[&str] = &[
    r"C:\Program Files (x86)\Steam\steamapps\common\The Bazaar",
    r"C:\Program Files\Steam\steamapps\common\The Bazaar",
    r"D:\Steam\steamapps\common\The Bazaar",
    r"D:\SteamLibrary\steamapps\common\The Bazaar",
    r"E:\Steam\steamapps\common\The Bazaar",
    r"E:\SteamLibrary\steamapps\common\The Bazaar",
];

#[cfg_attr(not(target_os = "windows"), allow(dead_code))]
#[cfg(not(target_os = "windows"))]
pub const STEAM_LIBRARY_FALLBACK_CANDIDATES: &[&str] = &[];

pub const BAZAAR_DATA_DIRECTORY: &str = "BazaarPlusPlus";
pub const DATABASE_FILE_NAME: &str = "bazaarplusplus.db";
pub const SCREENSHOTS_DIRECTORY: &str = "Screenshots";

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

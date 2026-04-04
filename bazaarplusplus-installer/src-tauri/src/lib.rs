mod commands;

use commands::{
    bepinex::{get_legacy_record_directory_info, install_bepinex, repair_bpp, uninstall_bpp},
    detect::{detect_dotnet_runtime, detect_environment, verify_game_path},
    game::detect_bazaar_running,
    steam::detect_steam_running,
    supporters::load_supporters,
    vdf::patch_launch_options,
};

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_updater::Builder::new().build())
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_opener::init())
        .invoke_handler(tauri::generate_handler![
            detect_environment,
            detect_dotnet_runtime,
            detect_bazaar_running,
            detect_steam_running,
            verify_game_path,
            install_bepinex,
            repair_bpp,
            get_legacy_record_directory_info,
            uninstall_bpp,
            patch_launch_options,
            load_supporters,
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}

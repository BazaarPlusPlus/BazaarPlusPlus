//! Canonical Tauri IPC registration and TypeScript export.

use tauri_specta::{collect_commands, Builder, ErrorHandlingMode};

pub fn builder() -> Builder<tauri::Wry> {
    Builder::<tauri::Wry>::new()
        .error_handling(ErrorHandlingMode::Throw)
        .dangerously_cast_bigints_to_number()
        .commands(collect_commands![
            crate::commands::app::get_app_bootstrap,
            crate::tray::set_app_locale,
            crate::commands::install::get_install_state,
            crate::commands::install::choose_game_directory,
            crate::commands::install::install_mod,
            crate::commands::install::reset_bpp_data,
            crate::commands::install::reset_bepinex,
            crate::commands::install::uninstall_mod,
            crate::commands::install::launch_game,
            crate::commands::game::end_game_process,
            crate::commands::stream::get_stream_status,
            crate::commands::stream::ensure_stream_session,
            crate::commands::stream::restart_stream_session,
            crate::commands::stream::set_stream_window,
            crate::commands::stream::get_overlay_settings,
            crate::commands::stream::save_overlay_display_mode,
            crate::commands::stream::apply_overlay_crop_code,
            crate::commands::stream::reset_overlay_crop,
            crate::commands::history::list_history_runs,
            crate::commands::history::get_history_run_detail,
            crate::commands::history::reveal_run_screenshot,
            crate::commands::history::reveal_battle_video,
            crate::commands::history::delete_battle_video,
            crate::commands::history::delete_run_videos,
            crate::commands::history::preview_storage_cleanup,
            crate::commands::history::execute_storage_cleanup,
        ])
}

#[cfg(test)]
mod tests {
    #[test]
    fn export_bindings() {
        let Ok(path) = std::env::var("BPP_SPECTA_EXPORT_PATH") else {
            return;
        };

        super::builder()
            .export(specta_typescript::Typescript::default(), &path)
            .unwrap_or_else(|error| panic!("export TypeScript bindings to {path}: {error}"));
    }
}

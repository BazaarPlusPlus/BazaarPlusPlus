//! Canonical Tauri IPC registration. Edit the command list only inside `with_commands!`.

macro_rules! with_commands {
    ($macro:ident) => {
        $macro! {
            (commands::app, get_app_bootstrap),
            (tray, set_app_locale),
            (commands::install, get_install_state),
            (commands::install, choose_game_directory),
            (commands::install, install_mod),
            (commands::install, reset_bpp_data),
            (commands::install, uninstall_mod),
            (commands::install, launch_game),
            (commands::install, cancel_tempo_launch),
            (commands::stream, get_stream_status),
            (commands::stream, ensure_stream_session),
            (commands::stream, restart_stream_session),
            (commands::stream, set_stream_window),
            (commands::stream, get_overlay_settings),
            (commands::stream, save_overlay_display_mode),
            (commands::stream, apply_overlay_crop_code),
            (commands::stream, reset_overlay_crop),
            (commands::history, list_history_runs),
            (commands::history, get_history_run_detail),
            (commands::history, reveal_run_screenshot),
            (commands::history, reveal_battle_video),
            (commands::history, delete_battle_video),
            (commands::history, delete_run_videos)
        }
    };
}

macro_rules! bpp_command_names {
    ($(($mod:path, $name:ident)),* $(,)?) => {
        /// Mirrors the `with_commands!` list; consumed by the test in this file.
        /// (`scripts/generate-bindings.mjs` parses this file's `with_commands!`
        /// source text directly — it does not read this constant.)
        #[allow(dead_code)]
        pub const TAURI_COMMAND_NAMES: &[&str] = &[$(stringify!($name)),*];
    };
}

macro_rules! bpp_invoke_handler {
    ($(($mod:path, $name:ident)),* $(,)?) => {
        #[macro_export]
        macro_rules! invoke_handler {
            () => {
                tauri::generate_handler![$($crate::$mod::$name),*]
            };
        }
    };
}

with_commands! { bpp_command_names }
with_commands! { bpp_invoke_handler }

#[cfg(test)]
mod tests {
    use super::TAURI_COMMAND_NAMES;

    pub(crate) fn parse_with_commands_names(registry_source: &str) -> Vec<String> {
        let production_source = registry_source
            .split("#[cfg(test)]")
            .next()
            .expect("production registry source");
        let list_start = production_source
            .find("$macro! {")
            .expect("with_commands command list")
            + "$macro! {".len();
        let list_end = production_source[list_start..]
            .find("\n        }")
            .expect("with_commands command list end");
        let block = &production_source[list_start..list_start + list_end];

        block
            .lines()
            .filter_map(|line| {
                let line = line.trim();
                if !line.starts_with('(') {
                    return None;
                }
                let line = line.trim().trim_end_matches(',').trim_end_matches(')');
                let name = line.split(',').last()?.trim();
                if name.is_empty()
                    || !name
                        .chars()
                        .all(|ch| ch.is_ascii_alphanumeric() || ch == '_')
                {
                    return None;
                }
                Some(name.to_string())
            })
            .collect()
    }

    #[test]
    fn tauri_command_names_match_with_commands_list() {
        let registry_source = include_str!("registry.rs");
        let parsed = parse_with_commands_names(registry_source);
        let names = TAURI_COMMAND_NAMES
            .iter()
            .map(|name| (*name).to_string())
            .collect::<Vec<_>>();

        assert_eq!(parsed, names);
        assert_eq!(names.len(), 23);
    }
}

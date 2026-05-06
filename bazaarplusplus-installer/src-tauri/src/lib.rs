mod commands;
mod config;
mod stream;

use std::sync::Mutex;

use tauri::{
    menu::{Menu, MenuItem, MenuItemBuilder},
    tray::{MouseButton, TrayIconBuilder, TrayIconEvent},
    Manager, WindowEvent,
};

use commands::{
    bepinex::{get_legacy_record_directory_info, install_bepinex, repair_bpp, uninstall_bpp},
    detect::{detect_environment, verify_game_path},
    game_process::detect_bazaar_running,
    startup::{initialize_installer_context, InstallerContextState},
    steam::{close_steam, detect_steam_running},
    stream::{
        delete_stream_record, detect_stream_db_path, get_stream_overlay_crop_settings,
        get_stream_service_status, import_stream_overlay_crop_code, list_stream_overlay_records,
        load_stream_record_strip_preview, load_stream_record_strip_previews,
        reveal_stream_record_image, save_stream_overlay_crop_settings,
        save_stream_overlay_display_mode, set_stream_overlay_window_offset, start_stream_service,
        stop_stream_service,
    },
    vdf::patch_launch_options,
};

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_updater::Builder::new().build())
        .plugin(tauri_plugin_dialog::init())
        .manage(crate::stream::state::StreamRuntimeState::default())
        .manage(InstallerContextState::default())
        .manage(TrayMenuState::default())
        .plugin(tauri_plugin_opener::init())
        .setup(|app| {
            let handle = app.app_handle();
            build_tray(&handle)?;
            Ok(())
        })
        .on_window_event(|window, event| {
            if window.label() != "main" {
                return;
            }

            if let WindowEvent::CloseRequested { api, .. } = event {
                let state = window.state::<crate::stream::state::StreamRuntimeState>();
                if state.snapshot().running {
                    api.prevent_close();
                    let _ = window.hide();
                }
            }
        })
        .invoke_handler(tauri::generate_handler![
            initialize_installer_context,
            detect_environment,
            detect_bazaar_running,
            detect_steam_running,
            close_steam,
            verify_game_path,
            install_bepinex,
            repair_bpp,
            get_legacy_record_directory_info,
            uninstall_bpp,
            patch_launch_options,
            start_stream_service,
            stop_stream_service,
            set_tray_locale,
            get_stream_service_status,
            set_stream_overlay_window_offset,
            get_stream_overlay_crop_settings,
            save_stream_overlay_crop_settings,
            save_stream_overlay_display_mode,
            import_stream_overlay_crop_code,
            list_stream_overlay_records,
            load_stream_record_strip_preview,
            load_stream_record_strip_previews,
            reveal_stream_record_image,
            delete_stream_record,
            detect_stream_db_path,
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}

fn build_tray(app: &tauri::AppHandle) -> tauri::Result<()> {
    let labels = tray_menu_labels(TrayLocale::Zh);
    let show_window = MenuItemBuilder::with_id("show_window", labels.show_window).build(app)?;
    let copy_obs_url = MenuItemBuilder::with_id("copy_obs_url", labels.copy_obs_url).build(app)?;
    let stop_stream_service =
        MenuItemBuilder::with_id("stop_stream_service", labels.stop_stream_service).build(app)?;
    let quit = MenuItemBuilder::with_id("quit_app", labels.quit).build(app)?;
    let menu = Menu::with_items(
        app,
        &[&show_window, &copy_obs_url, &stop_stream_service, &quit],
    )?;

    app.state::<TrayMenuState>().set_items(TrayMenuItems {
        show_window: show_window.clone(),
        copy_obs_url: copy_obs_url.clone(),
        stop_stream_service: stop_stream_service.clone(),
        quit: quit.clone(),
    });

    let mut tray = TrayIconBuilder::with_id("main-tray")
        .menu(&menu)
        .tooltip("BazaarPlusPlus")
        .show_menu_on_left_click(false)
        .on_menu_event(|app, event| match event.id().as_ref() {
            "show_window" => show_main_window(app),
            "copy_obs_url" => {
                let state = app.state::<crate::stream::state::StreamRuntimeState>();
                if let Some(url) = state.snapshot().overlay_url {
                    let _ = copy_text_to_clipboard(&url);
                }
            }
            "stop_stream_service" => {
                let app_handle = app.clone();
                tauri::async_runtime::spawn(async move {
                    let state = app_handle.state::<crate::stream::state::StreamRuntimeState>();
                    let _ = crate::stream::server::stop(state.inner()).await;
                });
            }
            "quit_app" => quit_app(app),
            _ => {}
        })
        .on_tray_icon_event(|tray, event| {
            if should_show_main_window_for_tray_event(&event) {
                show_main_window(tray.app_handle());
            }
        });

    if let Some(icon) = app.default_window_icon().cloned() {
        tray = tray.icon(icon);
    }

    let _ = tray.build(app)?;
    Ok(())
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum TrayLocale {
    En,
    Zh,
}

impl TrayLocale {
    fn from_code(locale: &str) -> Self {
        match locale {
            "en" => Self::En,
            _ => Self::Zh,
        }
    }
}

struct TrayMenuLabels {
    show_window: &'static str,
    copy_obs_url: &'static str,
    stop_stream_service: &'static str,
    quit: &'static str,
}

fn tray_menu_labels(locale: TrayLocale) -> TrayMenuLabels {
    match locale {
        TrayLocale::En => TrayMenuLabels {
            show_window: "Show Window",
            copy_obs_url: "Copy OBS URL",
            stop_stream_service: "Stop Stream Service",
            quit: "Quit",
        },
        TrayLocale::Zh => TrayMenuLabels {
            show_window: "显示窗口",
            copy_obs_url: "复制 OBS 地址",
            stop_stream_service: "关闭直播服务",
            quit: "退出",
        },
    }
}

struct TrayMenuItems {
    show_window: MenuItem<tauri::Wry>,
    copy_obs_url: MenuItem<tauri::Wry>,
    stop_stream_service: MenuItem<tauri::Wry>,
    quit: MenuItem<tauri::Wry>,
}

#[derive(Default)]
struct TrayMenuState {
    items: Mutex<Option<TrayMenuItems>>,
}

impl TrayMenuState {
    fn set_items(&self, items: TrayMenuItems) {
        *self.items.lock().expect("tray menu state poisoned") = Some(items);
    }

    fn apply_locale(&self, locale: TrayLocale) -> Result<(), String> {
        let labels = tray_menu_labels(locale);
        let items = self.items.lock().expect("tray menu state poisoned");
        let items = items
            .as_ref()
            .ok_or_else(|| "Tray menu is not initialized.".to_string())?;

        items
            .show_window
            .set_text(labels.show_window)
            .map_err(|err| format!("Failed to update tray menu text: {err}"))?;
        items
            .copy_obs_url
            .set_text(labels.copy_obs_url)
            .map_err(|err| format!("Failed to update tray menu text: {err}"))?;
        items
            .stop_stream_service
            .set_text(labels.stop_stream_service)
            .map_err(|err| format!("Failed to update tray menu text: {err}"))?;
        items
            .quit
            .set_text(labels.quit)
            .map_err(|err| format!("Failed to update tray menu text: {err}"))?;
        Ok(())
    }
}

#[tauri::command]
async fn set_tray_locale(
    state: tauri::State<'_, TrayMenuState>,
    locale: String,
) -> Result<(), String> {
    state.apply_locale(TrayLocale::from_code(&locale))
}

fn should_show_main_window_for_tray_event(event: &TrayIconEvent) -> bool {
    matches!(
        event,
        TrayIconEvent::Click {
            button: MouseButton::Left,
            ..
        } | TrayIconEvent::DoubleClick {
            button: MouseButton::Left,
            ..
        }
    )
}

fn quit_app(app: &tauri::AppHandle) {
    let app_handle = app.clone();
    tauri::async_runtime::spawn(async move {
        let state = app_handle.state::<crate::stream::state::StreamRuntimeState>();
        let _ = crate::stream::server::stop(state.inner()).await;
        app_handle.exit(0);
    });
}

fn show_main_window(app: &tauri::AppHandle) {
    if let Some(window) = app.get_webview_window("main") {
        let _ = window.show();
        let _ = window.unminimize();
        let _ = window.set_focus();
    }
}

fn copy_text_to_clipboard(text: &str) -> Result<(), String> {
    #[cfg(target_os = "macos")]
    {
        use std::io::Write;
        use std::process::{Command, Stdio};

        let mut child = Command::new("pbcopy")
            .stdin(Stdio::piped())
            .spawn()
            .map_err(|err| format!("failed to launch pbcopy: {err}"))?;
        let stdin = child
            .stdin
            .as_mut()
            .ok_or_else(|| "pbcopy stdin unavailable".to_string())?;
        stdin
            .write_all(text.as_bytes())
            .map_err(|err| format!("failed to write to pbcopy: {err}"))?;
        child
            .wait()
            .map_err(|err| format!("failed to wait for pbcopy: {err}"))?;
        return Ok(());
    }

    #[cfg(target_os = "windows")]
    {
        use std::io::Write;
        use std::process::{Command, Stdio};

        let mut child = Command::new("cmd")
            .args(["/C", "clip"])
            .stdin(Stdio::piped())
            .spawn()
            .map_err(|err| format!("failed to launch clip: {err}"))?;
        let stdin = child
            .stdin
            .as_mut()
            .ok_or_else(|| "clip stdin unavailable".to_string())?;
        stdin
            .write_all(text.as_bytes())
            .map_err(|err| format!("failed to write to clip: {err}"))?;
        child
            .wait()
            .map_err(|err| format!("failed to wait for clip: {err}"))?;
        return Ok(());
    }

    #[cfg(not(any(target_os = "macos", target_os = "windows")))]
    {
        let _ = text;
        Err("clipboard copy is unsupported on this platform".to_string())
    }
}

#[cfg(test)]
mod tests {
    use super::{should_show_main_window_for_tray_event, tray_menu_labels, TrayLocale};
    use tauri::{
        tray::{MouseButton, MouseButtonState, TrayIconEvent, TrayIconId},
        PhysicalPosition, Rect,
    };

    fn click_event(button: MouseButton) -> TrayIconEvent {
        TrayIconEvent::Click {
            id: TrayIconId::new("main-tray"),
            position: PhysicalPosition::default(),
            rect: Rect::default(),
            button,
            button_state: MouseButtonState::Down,
        }
    }

    fn double_click_event(button: MouseButton) -> TrayIconEvent {
        TrayIconEvent::DoubleClick {
            id: TrayIconId::new("main-tray"),
            position: PhysicalPosition::default(),
            rect: Rect::default(),
            button,
        }
    }

    #[test]
    fn tray_right_click_is_reserved_for_context_menu() {
        assert!(!should_show_main_window_for_tray_event(&click_event(
            MouseButton::Right
        )));
    }

    #[test]
    fn tray_left_click_opens_main_window() {
        assert!(should_show_main_window_for_tray_event(&click_event(
            MouseButton::Left
        )));
        assert!(should_show_main_window_for_tray_event(&double_click_event(
            MouseButton::Left
        )));
    }

    #[test]
    fn tray_menu_labels_support_chinese_and_english() {
        let zh = tray_menu_labels(TrayLocale::Zh);
        assert_eq!(zh.show_window, "显示窗口");
        assert_eq!(zh.copy_obs_url, "复制 OBS 地址");
        assert_eq!(zh.stop_stream_service, "关闭直播服务");
        assert_eq!(zh.quit, "退出");

        let en = tray_menu_labels(TrayLocale::En);
        assert_eq!(en.show_window, "Show Window");
        assert_eq!(en.copy_obs_url, "Copy OBS URL");
        assert_eq!(en.stop_stream_service, "Stop Stream Service");
        assert_eq!(en.quit, "Quit");
    }

    #[test]
    fn tray_locale_defaults_to_chinese_for_unknown_values() {
        assert_eq!(TrayLocale::from_code("en"), TrayLocale::En);
        assert_eq!(TrayLocale::from_code("zh"), TrayLocale::Zh);
        assert_eq!(TrayLocale::from_code("fr"), TrayLocale::Zh);
    }
}

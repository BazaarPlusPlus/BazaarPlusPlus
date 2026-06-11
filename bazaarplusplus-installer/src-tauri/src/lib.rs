mod commands;
mod config;
mod history;
mod services;
mod stream;
mod tray;

use tauri::{Emitter, Manager, WindowEvent};

use services::startup::InstallerContextState;
use tray::{build_tray, TrayMenuState};

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    #[allow(unused_mut)]
    let mut builder = tauri::Builder::default();

    #[cfg(not(any(target_os = "android", target_os = "ios")))]
    {
        // single-instance must be registered first; window-state restores the
        // remembered window size/position on launch and saves it on close.
        builder = builder
            .plugin(tauri_plugin_single_instance::init(|app, _args, _cwd| {
                if let Some(window) = app.get_webview_window("main") {
                    let _ = window.show();
                    let _ = window.set_focus();
                }
            }))
            .plugin(tauri_plugin_window_state::Builder::default().build());
    }

    builder
        .plugin(tauri_plugin_updater::Builder::new().build())
        .plugin(tauri_plugin_process::init())
        .plugin(tauri_plugin_dialog::init())
        .manage(crate::stream::state::StreamRuntimeState::default())
        .manage(InstallerContextState::default())
        .manage(TrayMenuState::default())
        .plugin(tauri_plugin_opener::init())
        .setup(|app| {
            let handle = app.app_handle();
            build_tray(&handle)?;
            let startup_handle = handle.clone();
            tauri::async_runtime::spawn_blocking(move || {
                let state = startup_handle.state::<InstallerContextState>();
                let _ = state.get_or_initialize(&startup_handle);
                let _ = startup_handle.emit("startup-ready", ());
            });
            let app_handle = handle.clone();
            tauri::async_runtime::spawn(async move {
                let state = app_handle.state::<crate::stream::state::StreamRuntimeState>();
                let _ = crate::stream::server::start(app_handle.clone(), state.inner(), None).await;
            });
            Ok(())
        })
        // While the stream service is running, hide the main window to the tray
        // on close instead of quitting, so the local overlay HTTP server keeps
        // serving OBS. The tray menu's quit action is the real exit path.
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
        .invoke_handler(invoke_handler!())
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}

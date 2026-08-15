mod commands;
mod config;
mod history;
mod main_window;
mod problem;
mod services;
mod stream;
mod tray;

// The unit-test harness links the same Tauri dialog code as the application,
// but tauri-build only attaches its Common Controls v6 resource to binaries.
// Without this test-only link, Windows fails before running any test because
// comctl32!TaskDialogIndirect is unavailable from the legacy activation context.
#[cfg(all(test, target_os = "windows"))]
#[link(name = "resource", kind = "static")]
unsafe extern "C" {}

use tauri::{Manager, WindowEvent};
#[cfg(not(any(target_os = "android", target_os = "ios")))]
use tauri_plugin_window_state::StateFlags;

use services::startup::InstallerContextState;
use tray::{build_tray, TrayMenuState};

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    #[allow(unused_mut)]
    let mut builder = tauri::Builder::default();
    let command_builder = crate::commands::registry::builder();

    #[cfg(not(any(target_os = "android", target_os = "ios")))]
    {
        // single-instance must be registered first; window-state restores only
        // geometry and maximization, never visibility or frame configuration.
        builder = builder
            .plugin(tauri_plugin_single_instance::init(|app, _args, _cwd| {
                crate::main_window::restore(app);
            }))
            .plugin(
                tauri_plugin_window_state::Builder::default()
                    .with_state_flags(
                        StateFlags::SIZE | StateFlags::POSITION | StateFlags::MAXIMIZED,
                    )
                    .build(),
            );
    }

    let app = builder
        .plugin(tauri_plugin_updater::Builder::new().build())
        .plugin(tauri_plugin_process::init())
        .plugin(tauri_plugin_dialog::init())
        .manage(
            crate::services::selected_game_installation::SelectedGameInstallationState::default(),
        )
        .manage(crate::stream::runtime::StreamRuntime::default())
        .manage(InstallerContextState::default())
        .manage(TrayMenuState::default())
        .plugin(tauri_plugin_opener::init())
        .setup(|app| {
            #[cfg(target_os = "windows")]
            {
                let window = app
                    .get_webview_window("main")
                    .ok_or_else(|| std::io::Error::other("main window is unavailable"))?;
                window.set_decorations(false)?;
                crate::main_window::enforce_minimum_size(&window)?;

                // Programmatic window-state restoration and undecorated native
                // resizing can bypass the configured logical minimum on Windows.
                // Correct those native resize events before the WebView can be
                // left with an unusably small layout.
                let size_window = window.clone();
                window.on_window_event(move |event| {
                    if matches!(event, WindowEvent::Resized(_)) {
                        if let Err(error) = crate::main_window::enforce_minimum_size(&size_window) {
                            eprintln!("failed to enforce the Windows window minimum: {error}");
                        }
                    }
                });

                window.show()?;
            }

            let handle = app.app_handle();
            build_tray(handle)?;
            let startup_handle = handle.clone();
            tauri::async_runtime::spawn_blocking(move || {
                let state = startup_handle.state::<InstallerContextState>();
                let _ = state.get_or_initialize(&startup_handle);
            });
            let app_handle = handle.clone();
            tauri::async_runtime::spawn(async move {
                let runtime = app_handle.state::<crate::stream::runtime::StreamRuntime>();
                let _ = runtime.ensure(app_handle.clone(), None).await;
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
                let runtime = window.state::<crate::stream::runtime::StreamRuntime>();
                if runtime.snapshot().running {
                    api.prevent_close();
                    let _ = window.hide();
                }
            }
        })
        .invoke_handler(command_builder.invoke_handler())
        .build(tauri::generate_context!())
        .expect("error while building tauri application");

    app.run(|_app_handle, _event| {
        #[cfg(target_os = "macos")]
        if matches!(_event, tauri::RunEvent::Reopen { .. }) {
            crate::main_window::restore(_app_handle);
        }
    });
}

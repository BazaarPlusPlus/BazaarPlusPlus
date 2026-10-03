use tauri::Manager;
#[cfg(target_os = "windows")]
use tauri::{LogicalSize, PhysicalSize};

#[cfg(target_os = "windows")]
const MAIN_WINDOW_MIN_WIDTH: f64 = 900.0;
#[cfg(target_os = "windows")]
const MAIN_WINDOW_MIN_HEIGHT: f64 = 600.0;

#[cfg(target_os = "windows")]
fn should_enforce_minimum_size(is_minimized: bool, is_maximized: bool) -> bool {
    // Reapplying a Windows size constraint mutates the inner size and restores
    // a maximized window. Only correct ordinary, restored windows here.
    !is_minimized && !is_maximized
}

#[cfg(target_os = "windows")]
fn corrected_main_window_size(
    current: PhysicalSize<u32>,
    scale_factor: f64,
) -> Option<LogicalSize<f64>> {
    let logical = current.to_logical::<f64>(scale_factor);
    let corrected = LogicalSize::new(
        logical.width.max(MAIN_WINDOW_MIN_WIDTH),
        logical.height.max(MAIN_WINDOW_MIN_HEIGHT),
    );

    (corrected != logical).then_some(corrected)
}

#[cfg(target_os = "windows")]
pub(crate) fn enforce_minimum_size(window: &tauri::WebviewWindow) -> tauri::Result<()> {
    if !should_enforce_minimum_size(window.is_minimized()?, window.is_maximized()?) {
        return Ok(());
    }

    window.set_min_size(Some(LogicalSize::new(
        MAIN_WINDOW_MIN_WIDTH,
        MAIN_WINDOW_MIN_HEIGHT,
    )))?;

    if let Some(corrected) =
        corrected_main_window_size(window.inner_size()?, window.scale_factor()?)
    {
        window.set_size(corrected)?;
    }

    Ok(())
}

pub(crate) fn restore(app: &tauri::AppHandle) {
    let Some(window) = app.get_webview_window("main") else {
        eprintln!("failed to restore the main window: window is unavailable");
        return;
    };

    if let Err(error) = window.show() {
        eprintln!("failed to show the main window: {error}");
    }
    if let Err(error) = window.unminimize() {
        eprintln!("failed to unminimize the main window: {error}");
    }
    if let Err(error) = window.set_focus() {
        eprintln!("failed to focus the main window: {error}");
    }
}

use tauri::{Emitter, Manager};
#[cfg(target_os = "windows")]
use tauri::{LogicalSize, PhysicalSize};

/// Emitted after the main window hides to the tray. A WebView hidden this way
/// need not report `visibilitychange`, so the frontend pauses its polling on
/// this event (`MAIN_WINDOW_HIDDEN_EVENT` in `src/features/shared/windowActivity.ts`).
pub(crate) const HIDDEN_EVENT: &str = "main-window-hidden";

/// Hides the main window to the tray and tells its page.
pub(crate) fn hide_to_tray(window: &tauri::Window) {
    if let Err(error) = window.hide() {
        eprintln!("failed to hide the main window: {error}");
        return;
    }
    if let Err(error) = window.emit(HIDDEN_EVENT, ()) {
        eprintln!("failed to announce the hidden main window: {error}");
    }
}

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

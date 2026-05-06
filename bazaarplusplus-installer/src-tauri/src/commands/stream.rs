use crate::config::{BAZAAR_DATA_DIRECTORY, DATABASE_FILE_NAME};
use crate::stream::{
    http::remove_overlay_strip_cache,
    overlay_settings::{
        OverlayCropSettings, OverlayCropSettingsPayload, OverlayDisplayMode, OverlaySettingsStore,
    },
    path_resolution::{normalize_requested_game_path, resolve_game_path_with_fallback},
    records::{OverlayRecord, OverlayRecordRepository},
    state::{StreamRuntimeState, StreamServiceStatus},
};
use base64::{engine::general_purpose::STANDARD, Engine as _};
use image::{DynamicImage, ImageFormat};
use std::collections::HashMap;
use std::process::Command;

#[tauri::command]
pub async fn start_stream_service(
    app: tauri::AppHandle,
    state: tauri::State<'_, StreamRuntimeState>,
    game_path: Option<String>,
) -> Result<StreamServiceStatus, String> {
    crate::stream::server::start(app, state.inner(), normalize_requested_game_path(game_path)).await
}

#[tauri::command]
pub async fn stop_stream_service(
    state: tauri::State<'_, StreamRuntimeState>,
) -> Result<StreamServiceStatus, String> {
    crate::stream::server::stop(state.inner()).await
}

#[tauri::command]
pub fn get_stream_service_status(
    state: tauri::State<'_, StreamRuntimeState>,
) -> StreamServiceStatus {
    state.snapshot()
}

#[tauri::command]
pub fn set_stream_overlay_window_offset(
    app: tauri::AppHandle,
    state: tauri::State<'_, StreamRuntimeState>,
    game_path: Option<String>,
    offset: usize,
) -> Result<StreamServiceStatus, String> {
    let snapshot = state.snapshot();
    if !snapshot.running {
        return Ok(snapshot);
    }

    let started_at = snapshot
        .started_at
        .clone()
        .ok_or_else(|| "Stream start time is unavailable.".to_string())?;
    let resolved_game_path = resolve_game_path_with_fallback(&app, Some(&state), game_path);
    let repository = OverlayRecordRepository::new(resolved_game_path);

    if offset == 0 {
        return Ok(state.set_active_window(Some(started_at), 0));
    }

    let captured_since_start = repository.count_since(Some(started_at.as_str()))?;
    let total = repository.count_since(None)?;
    let existing_before_start = total.saturating_sub(captured_since_start);
    if offset > existing_before_start {
        return Err(format!(
            "Requested stream window offset {offset} exceeds the available {existing_before_start} earlier record(s)."
        ));
    }

    let record_offset = captured_since_start + offset - 1;
    let record = repository
        .load_record_at_offset(None, record_offset)?
        .ok_or_else(|| {
            format!("No end-of-run record is available for stream window offset {offset}.")
        })?;

    Ok(state.set_active_window(Some(record.captured_at_utc), offset))
}

#[tauri::command]
pub fn get_stream_overlay_crop_settings() -> Result<OverlayCropSettingsPayload, String> {
    OverlaySettingsStore::default().load_payload()
}

#[tauri::command]
pub fn save_stream_overlay_crop_settings(
    crop: OverlayCropSettings,
) -> Result<OverlayCropSettingsPayload, String> {
    OverlaySettingsStore::default().save(crop)
}

#[tauri::command]
pub fn import_stream_overlay_crop_code(code: String) -> Result<OverlayCropSettingsPayload, String> {
    OverlaySettingsStore::default().import_code(&code)
}

#[tauri::command]
pub fn save_stream_overlay_display_mode(
    display_mode: OverlayDisplayMode,
) -> Result<OverlayCropSettingsPayload, String> {
    OverlaySettingsStore::default().save_display_mode(display_mode)
}

/// Returns the resolved DB path for display/debug purposes.
/// `found` = DB file exists at that path.
/// `path` = full path to bazaarplusplus.db (or None if not located at all).
#[tauri::command]
pub fn detect_stream_db_path(
    app: tauri::AppHandle,
    state: tauri::State<'_, StreamRuntimeState>,
    game_path: Option<String>,
) -> StreamDbPathInfo {
    let game_path = resolve_game_path_with_fallback(&app, Some(&state), game_path);
    match game_path {
        None => StreamDbPathInfo {
            found: false,
            path: None,
        },
        Some(game_path) => {
            let db = game_path
                .join(BAZAAR_DATA_DIRECTORY)
                .join(DATABASE_FILE_NAME);
            let found = db.exists();
            StreamDbPathInfo {
                found,
                path: Some(db.to_string_lossy().into_owned()),
            }
        }
    }
}

#[derive(serde::Serialize, ts_rs::TS)]
#[ts(export)]
pub struct StreamDbPathInfo {
    pub found: bool,
    pub path: Option<String>,
}

#[tauri::command]
pub fn list_stream_overlay_records(
    app: tauri::AppHandle,
    state: tauri::State<'_, StreamRuntimeState>,
    game_path: Option<String>,
    limit: Option<usize>,
) -> Result<Vec<OverlayRecord>, String> {
    let game_path = resolve_game_path_with_fallback(&app, Some(&state), game_path);
    let repository = OverlayRecordRepository::new(game_path);
    repository.load_record_list(None, limit)
}

#[tauri::command]
pub fn reveal_stream_record_image(
    app: tauri::AppHandle,
    state: tauri::State<'_, StreamRuntimeState>,
    game_path: Option<String>,
    record_id: String,
) -> Result<(), String> {
    let requested_game_path = game_path.clone();
    let game_path = resolve_game_path_with_fallback(&app, Some(&state), game_path);
    let repository = OverlayRecordRepository::new(game_path.clone());
    let path = repository
        .load_image_path(&record_id)?
        .ok_or_else(|| {
            let message = format!(
                "Stream image not found for record {record_id}. requested_game_path={requested_game_path:?}, resolved_game_path={game_path:?}"
            );
            eprintln!("{message}");
            message
        })?;

    reveal_in_file_browser(&path)
}

#[tauri::command]
pub fn delete_stream_record(
    app: tauri::AppHandle,
    state: tauri::State<'_, StreamRuntimeState>,
    game_path: Option<String>,
    record_id: String,
) -> Result<(), String> {
    let game_path = resolve_game_path_with_fallback(&app, Some(&state), game_path);
    let repository = OverlayRecordRepository::new(game_path);
    let deleted = repository.delete_record(&record_id)?;
    if !deleted {
        return Err(format!("Stream record {record_id} was not found."));
    }

    remove_overlay_strip_cache(&record_id)?;
    Ok(())
}

#[tauri::command]
pub fn load_stream_record_strip_preview(
    app: tauri::AppHandle,
    state: tauri::State<'_, StreamRuntimeState>,
    game_path: Option<String>,
    record_id: String,
) -> Result<Option<String>, String> {
    let game_path = resolve_game_path_with_fallback(&app, Some(&state), game_path);
    let repository = OverlayRecordRepository::new(game_path);
    let Some((_path, bytes)) = repository.load_image(&record_id)? else {
        return Ok(None);
    };

    let crop = OverlaySettingsStore::default().load_payload()?.crop;
    let image = image::load_from_memory(&bytes)
        .map_err(|err| format!("Failed to decode overlay source image: {err}"))?;
    let cropped = crop_dynamic_image(image, &crop)?;

    let mut encoded = Vec::new();
    cropped
        .write_to(&mut std::io::Cursor::new(&mut encoded), ImageFormat::Png)
        .map_err(|err| format!("Failed to encode overlay strip image: {err}"))?;

    Ok(Some(format!(
        "data:image/png;base64,{}",
        STANDARD.encode(encoded)
    )))
}

#[tauri::command]
pub fn load_stream_record_strip_previews(
    app: tauri::AppHandle,
    state: tauri::State<'_, StreamRuntimeState>,
    game_path: Option<String>,
    record_ids: Vec<String>,
) -> Result<HashMap<String, String>, String> {
    let game_path = resolve_game_path_with_fallback(&app, Some(&state), game_path);
    let repository = OverlayRecordRepository::new(game_path);
    let crop = OverlaySettingsStore::default().load_payload()?.crop;
    let mut previews = HashMap::new();

    for record_id in record_ids {
        let Some((_path, bytes)) = repository.load_image(&record_id)? else {
            continue;
        };

        let image = image::load_from_memory(&bytes)
            .map_err(|err| format!("Failed to decode overlay source image: {err}"))?;
        let cropped = crop_dynamic_image(image, &crop)?;

        let mut encoded = Vec::new();
        cropped
            .write_to(&mut std::io::Cursor::new(&mut encoded), ImageFormat::Png)
            .map_err(|err| format!("Failed to encode overlay strip image: {err}"))?;

        previews.insert(
            record_id,
            format!("data:image/png;base64,{}", STANDARD.encode(encoded)),
        );
    }

    Ok(previews)
}

fn crop_dynamic_image(
    image: DynamicImage,
    crop: &OverlayCropSettings,
) -> Result<DynamicImage, String> {
    let left = crop.left.clamp(0.0, 1.0);
    let top = crop.top.clamp(0.0, 1.0);
    let width = crop.width.clamp(0.01, 1.0);
    let height = crop.height.clamp(0.01, 1.0);

    let source_width = image.width().max(1);
    let source_height = image.height().max(1);

    let left_px = (left * source_width as f64).floor() as u32;
    let top_px = (top * source_height as f64).floor() as u32;
    let width_px = ((width * source_width as f64).round() as u32).max(1);
    let height_px = ((height * source_height as f64).round() as u32).max(1);

    let max_width = source_width.saturating_sub(left_px).max(1);
    let max_height = source_height.saturating_sub(top_px).max(1);
    let final_width = width_px.min(max_width);
    let final_height = height_px.min(max_height);

    Ok(image.crop_imm(left_px, top_px, final_width, final_height))
}

#[cfg(target_os = "windows")]
fn strip_extended_length_prefix(value: &str) -> String {
    if let Some(stripped) = value.strip_prefix(r"\\?\UNC\") {
        format!(r"\\{stripped}")
    } else if let Some(stripped) = value.strip_prefix(r"\\?\") {
        stripped.to_string()
    } else {
        value.to_string()
    }
}

fn reveal_in_file_browser(path: &std::path::Path) -> Result<(), String> {
    #[cfg(target_os = "windows")]
    {
        use std::os::windows::process::CommandExt;

        let canonical = std::fs::canonicalize(path)
            .map(|buf| strip_extended_length_prefix(&buf.to_string_lossy()))
            .unwrap_or_else(|_| path.to_string_lossy().into_owned());

        Command::new("explorer")
            .raw_arg(format!("/select,\"{}\"", canonical))
            .spawn()
            .map_err(|err| format!("failed to reveal image in Explorer: {err}"))?;
        return Ok(());
    }

    #[cfg(target_os = "macos")]
    {
        Command::new("open")
            .args(["-R", &path.to_string_lossy()])
            .spawn()
            .map_err(|err| format!("failed to reveal image in Finder: {err}"))?;
        return Ok(());
    }

    #[cfg(all(not(target_os = "windows"), not(target_os = "macos")))]
    {
        let parent = path
            .parent()
            .ok_or_else(|| "image parent directory is missing".to_string())?;
        Command::new("xdg-open")
            .arg(parent)
            .spawn()
            .map_err(|err| format!("failed to open image directory: {err}"))?;
        Ok(())
    }
}
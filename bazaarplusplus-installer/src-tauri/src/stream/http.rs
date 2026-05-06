use super::overlay_settings::{validate_crop_settings, OverlayCropSettings, OverlaySettingsStore};
use super::records::OverlayRecordRepository;
use super::state::StreamRuntimeState;
use axum::http::StatusCode;
use axum::{
    extract::{Path, Query, State},
    http::{header, HeaderValue, Method},
    response::{Html, IntoResponse, Response},
    routing::get,
    Json, Router,
};
use image::{DynamicImage, ImageFormat};
use include_dir::{include_dir, Dir};
use serde::{Deserialize, Serialize};
use std::{
    io::Cursor,
    path::{Path as FsPath, PathBuf},
    time::UNIX_EPOCH,
};
use tower_http::cors::{Any, CorsLayer};

const OVERLAY_HTML: &str = include_str!("../../resources/stream/overlay.html");
const OVERLAY_CSS: &str = include_str!("../../resources/stream/overlay.css");
const OVERLAY_JS: &str = include_str!("../../resources/stream/overlay.js");
const SETTINGS_HTML: &str = include_str!("../../resources/stream/settings.html");
const SETTINGS_CSS: &str = include_str!("../../resources/stream/settings.css");
const SETTINGS_JS: &str = include_str!("../../resources/stream/settings.js");
static BADGES_DIR: Dir<'_> = include_dir!("$CARGO_MANIFEST_DIR/resources/stream/badges");

#[derive(Clone)]
pub struct HttpAppState {
    pub overlay_records: OverlayRecordRepository,
    pub runtime: StreamRuntimeState,
    pub overlay_settings: OverlaySettingsStore,
}

#[derive(Serialize)]
struct HealthResponse {
    ok: bool,
}

#[derive(Serialize)]
struct RecordWindowSummaryResponse {
    total: usize,
    existing_before_start: usize,
    captured_since_start: usize,
}

#[derive(Debug, Deserialize)]
struct LatestRecordQuery {
    offset: Option<usize>,
    from: Option<String>,
}

#[derive(Debug, Deserialize)]
struct RecordListQuery {
    limit: Option<usize>,
    from: Option<String>,
}

#[derive(Debug, Deserialize)]
struct SaveCropConfigRequest {
    crop: OverlayCropSettings,
}

#[derive(Debug, Deserialize)]
struct StripPreviewQuery {
    left: Option<f64>,
    top: Option<f64>,
    width: Option<f64>,
    height: Option<f64>,
    preview: Option<bool>,
}

pub fn router(
    overlay_records: OverlayRecordRepository,
    runtime: StreamRuntimeState,
    overlay_settings: OverlaySettingsStore,
) -> Router {
    // Allow the Tauri WebView (tauri://localhost, http://tauri.localhost, http://localhost:*)
    // to fetch from this local HTTP server. Without these headers the browser inside the
    // WebView blocks every cross-origin response, making all badge counts and record lists
    // return silently-caught zeros.
    let cors = CorsLayer::new()
        .allow_origin(Any)
        .allow_methods([Method::GET, Method::POST])
        .allow_headers(Any);

    Router::new()
        .route("/health", get(health))
        .route("/overlay", get(overlay_page))
        .route("/settings", get(settings_page))
        .route("/api/records/latest", get(latest_record))
        .route("/api/records/list", get(record_list))
        .route("/api/records/summary", get(record_window_summary))
        .route(
            "/api/overlay/crop-config",
            get(get_crop_config).post(save_crop_config),
        )
        .route("/images/{record_id}/strip", get(record_strip_image))
        .route("/images/{record_id}", get(record_image))
        .route("/assets/overlay.css", get(overlay_css))
        .route("/assets/overlay.js", get(overlay_js))
        .route("/assets/settings.css", get(settings_css))
        .route("/assets/settings.js", get(settings_js))
        .route("/assets/badges/{category}/{file_name}", get(badge_asset))
        .layer(cors)
        .with_state(HttpAppState {
            overlay_records,
            runtime,
            overlay_settings,
        })
}

async fn health() -> Json<HealthResponse> {
    Json(HealthResponse { ok: true })
}

#[cfg(any(debug_assertions, test))]
fn overlay_asset_path(file_name: &str) -> PathBuf {
    FsPath::new(env!("CARGO_MANIFEST_DIR"))
        .join("resources")
        .join("stream")
        .join(file_name)
}

#[cfg(debug_assertions)]
fn badge_asset_path(category: &str, file_name: &str) -> PathBuf {
    FsPath::new(env!("CARGO_MANIFEST_DIR"))
        .join("resources")
        .join("stream")
        .join("badges")
        .join(category)
        .join(file_name)
}

fn load_overlay_asset(_file_name: &str, embedded: &'static str) -> String {
    #[cfg(debug_assertions)]
    {
        if let Ok(contents) = std::fs::read_to_string(overlay_asset_path(_file_name)) {
            return contents;
        }
    }

    embedded.to_string()
}

async fn overlay_page() -> Html<String> {
    Html(load_overlay_asset("overlay.html", OVERLAY_HTML))
}

async fn settings_page() -> Html<String> {
    Html(load_overlay_asset("settings.html", SETTINGS_HTML))
}

async fn latest_record(
    State(app_state): State<HttpAppState>,
    Query(query): Query<LatestRecordQuery>,
) -> Response {
    let offset = query.offset.unwrap_or(0);
    let snapshot = app_state.runtime.snapshot();
    let from = query.from.as_deref().or(snapshot.active_from.as_deref());

    match app_state
        .overlay_records
        .load_record_at_offset(from, offset)
    {
        Ok(record) => Json(record).into_response(),
        Err(message) => (StatusCode::INTERNAL_SERVER_ERROR, message).into_response(),
    }
}

async fn record_list(
    State(app_state): State<HttpAppState>,
    Query(query): Query<RecordListQuery>,
) -> Response {
    let limit = query.limit.unwrap_or(20);
    let snapshot = app_state.runtime.snapshot();
    let from = query.from.as_deref().or(snapshot.active_from.as_deref());

    match app_state
        .overlay_records
        .load_record_list(from, Some(limit))
    {
        Ok(records) => Json(records).into_response(),
        Err(message) => (StatusCode::INTERNAL_SERVER_ERROR, message).into_response(),
    }
}

async fn record_window_summary(State(app_state): State<HttpAppState>) -> Response {
    let from = app_state.runtime.snapshot().started_at;
    let total = match app_state.overlay_records.count_since(None) {
        Ok(total) => total,
        Err(message) => return (StatusCode::INTERNAL_SERVER_ERROR, message).into_response(),
    };

    match app_state.overlay_records.count_since(from.as_deref()) {
        Ok(captured_since_start) => {
            let existing_before_start = total.saturating_sub(captured_since_start);
            Json(RecordWindowSummaryResponse {
                total,
                existing_before_start,
                captured_since_start,
            })
            .into_response()
        }
        Err(message) => (StatusCode::INTERNAL_SERVER_ERROR, message).into_response(),
    }
}

async fn get_crop_config(State(app_state): State<HttpAppState>) -> Response {
    match app_state.overlay_settings.load_payload() {
        Ok(payload) => Json(payload).into_response(),
        Err(message) => (StatusCode::INTERNAL_SERVER_ERROR, message).into_response(),
    }
}

async fn save_crop_config(
    State(app_state): State<HttpAppState>,
    Json(request): Json<SaveCropConfigRequest>,
) -> Response {
    match app_state.overlay_settings.save(request.crop) {
        Ok(payload) => Json(payload).into_response(),
        Err(message) => (StatusCode::BAD_REQUEST, message).into_response(),
    }
}

async fn record_image(
    Path(record_id): Path<String>,
    State(app_state): State<HttpAppState>,
) -> Response {
    let (path, bytes) = match app_state.overlay_records.load_image(&record_id) {
        Ok(Some(value)) => value,
        Ok(None) => return StatusCode::NOT_FOUND.into_response(),
        Err(message) => return (StatusCode::INTERNAL_SERVER_ERROR, message).into_response(),
    };

    let content_type = detect_content_type(&path);

    (
        [(
            header::CONTENT_TYPE,
            HeaderValue::from_str(content_type)
                .unwrap_or_else(|_| HeaderValue::from_static("application/octet-stream")),
        )],
        bytes,
    )
        .into_response()
}

async fn record_strip_image(
    Path(record_id): Path<String>,
    Query(query): Query<StripPreviewQuery>,
    State(app_state): State<HttpAppState>,
) -> Response {
    let crop = match resolve_strip_crop(&query, &app_state) {
        Ok(crop) => crop,
        Err(message) => return (StatusCode::BAD_REQUEST, message).into_response(),
    };

    let (path, bytes) = match app_state.overlay_records.load_image(&record_id) {
        Ok(Some(value)) => value,
        Ok(None) => return StatusCode::NOT_FOUND.into_response(),
        Err(message) => return (StatusCode::INTERNAL_SERVER_ERROR, message).into_response(),
    };

    let strip_bytes = if query.preview.unwrap_or(false) {
        match crop_strip_image(&bytes, crop) {
            Ok(bytes) => bytes,
            Err(message) => return (StatusCode::INTERNAL_SERVER_ERROR, message).into_response(),
        }
    } else {
        match load_or_create_strip_cache(&record_id, &path, &bytes, crop) {
            Ok(bytes) => bytes,
            Err(message) => return (StatusCode::INTERNAL_SERVER_ERROR, message).into_response(),
        }
    };

    (
        [
            (header::CONTENT_TYPE, HeaderValue::from_static("image/png")),
            (header::CACHE_CONTROL, HeaderValue::from_static("no-store")),
        ],
        strip_bytes,
    )
        .into_response()
}

fn resolve_strip_crop(
    query: &StripPreviewQuery,
    app_state: &HttpAppState,
) -> Result<OverlayCropSettings, String> {
    if query.left.is_some()
        || query.top.is_some()
        || query.width.is_some()
        || query.height.is_some()
    {
        let defaults = OverlayCropSettings::default();
        return validate_crop_settings(OverlayCropSettings {
            left: query.left.unwrap_or(defaults.left),
            top: query.top.unwrap_or(defaults.top),
            width: query.width.unwrap_or(defaults.width),
            height: query.height.unwrap_or(defaults.height),
        });
    }

    app_state
        .overlay_settings
        .load()
        .map(|settings| settings.crop)
}

fn detect_content_type(path: &FsPath) -> &'static str {
    match path
        .extension()
        .and_then(|ext| ext.to_str())
        .map(|ext| ext.to_ascii_lowercase())
        .as_deref()
    {
        Some("png") => "image/png",
        Some("jpg") | Some("jpeg") => "image/jpeg",
        Some("webp") => "image/webp",
        _ => "application/octet-stream",
    }
}

fn overlay_cache_directory() -> PathBuf {
    let base = dirs::cache_dir()
        .or_else(dirs::config_dir)
        .or_else(dirs::data_local_dir)
        .unwrap_or_else(std::env::temp_dir);
    base.join("BazaarPlusPlus").join("stream-overlay-cache")
}

fn sanitized_cache_name(value: &str) -> String {
    value
        .chars()
        .map(|ch| match ch {
            'a'..='z' | 'A'..='Z' | '0'..='9' | '-' | '_' => ch,
            _ => '_',
        })
        .collect()
}

pub(crate) fn remove_overlay_strip_cache(record_id: &str) -> Result<(), String> {
    let directory = overlay_cache_directory();
    if !directory.exists() {
        return Ok(());
    }

    let prefix = format!("{}-", sanitized_cache_name(record_id));
    let entries = std::fs::read_dir(&directory).map_err(|err| {
        format!(
            "Failed to read overlay cache directory {}: {err}",
            directory.display()
        )
    })?;

    for entry in entries {
        let entry = entry.map_err(|err| {
            format!(
                "Failed to read overlay cache entry in {}: {err}",
                directory.display()
            )
        })?;
        let file_name = entry.file_name();
        let Some(file_name) = file_name.to_str() else {
            continue;
        };
        if !file_name.starts_with(&prefix) {
            continue;
        }

        let path = entry.path();
        if path.is_file() {
            std::fs::remove_file(&path).map_err(|err| {
                format!(
                    "Failed to remove cached overlay strip {}: {err}",
                    path.display()
                )
            })?;
        }
    }

    Ok(())
}

fn crop_cache_path(
    record_id: &str,
    source_path: &FsPath,
    crop: OverlayCropSettings,
) -> Result<PathBuf, String> {
    let metadata = std::fs::metadata(source_path).map_err(|err| {
        format!(
            "Failed to read source image metadata from {}: {err}",
            source_path.display()
        )
    })?;
    let modified = metadata
        .modified()
        .ok()
        .and_then(|value| value.duration_since(UNIX_EPOCH).ok())
        .map(|value| value.as_secs())
        .unwrap_or(0);
    let cache_name = format!(
        "{}-{}-{}-{}-{}-{}-{}-strip.png",
        sanitized_cache_name(record_id),
        metadata.len(),
        modified,
        (crop.left * 10_000.0).round() as i64,
        (crop.top * 10_000.0).round() as i64,
        (crop.width * 10_000.0).round() as i64,
        (crop.height * 10_000.0).round() as i64
    );

    Ok(overlay_cache_directory().join(cache_name))
}

fn load_or_create_strip_cache(
    record_id: &str,
    source_path: &FsPath,
    source_bytes: &[u8],
    crop: OverlayCropSettings,
) -> Result<Vec<u8>, String> {
    let cache_path = crop_cache_path(record_id, source_path, crop)?;
    if cache_path.exists() {
        return std::fs::read(&cache_path).map_err(|err| {
            format!(
                "Failed to read cached overlay strip from {}: {err}",
                cache_path.display()
            )
        });
    }

    let bytes = crop_strip_image(source_bytes, crop)?;

    if let Some(parent) = cache_path.parent() {
        std::fs::create_dir_all(parent).map_err(|err| {
            format!(
                "Failed to create overlay cache directory {}: {err}",
                parent.display()
            )
        })?;
    }

    std::fs::write(&cache_path, &bytes).map_err(|err| {
        format!(
            "Failed to write cached overlay strip to {}: {err}",
            cache_path.display()
        )
    })?;

    Ok(bytes)
}

fn crop_strip_image(source_bytes: &[u8], crop: OverlayCropSettings) -> Result<Vec<u8>, String> {
    let image = image::load_from_memory(source_bytes)
        .map_err(|err| format!("Failed to decode overlay source image: {err}"))?;
    let cropped = crop_dynamic_image(image, crop)?;
    let mut output = Cursor::new(Vec::new());
    cropped
        .write_to(&mut output, ImageFormat::Png)
        .map_err(|err| format!("Failed to encode overlay strip image: {err}"))?;
    Ok(output.into_inner())
}

fn crop_dynamic_image(
    image: DynamicImage,
    crop: OverlayCropSettings,
) -> Result<DynamicImage, String> {
    let width = image.width();
    let height = image.height();
    if width == 0 || height == 0 {
        return Err("Overlay source image is empty.".to_string());
    }

    let left = ((width as f64) * crop.left)
        .floor()
        .clamp(0.0, (width - 1) as f64) as u32;
    let top = ((height as f64) * crop.top)
        .floor()
        .clamp(0.0, (height - 1) as f64) as u32;
    let crop_width = ((width as f64) * crop.width)
        .round()
        .clamp(1.0, (width - left) as f64) as u32;
    let crop_height = ((height as f64) * crop.height)
        .round()
        .clamp(1.0, (height - top) as f64) as u32;

    Ok(image.crop_imm(left, top, crop_width, crop_height))
}

async fn overlay_css() -> Response {
    (
        [(
            header::CONTENT_TYPE,
            HeaderValue::from_static("text/css; charset=utf-8"),
        )],
        load_overlay_asset("overlay.css", OVERLAY_CSS),
    )
        .into_response()
}

async fn overlay_js() -> Response {
    (
        [(
            header::CONTENT_TYPE,
            HeaderValue::from_static("application/javascript; charset=utf-8"),
        )],
        load_overlay_asset("overlay.js", OVERLAY_JS),
    )
        .into_response()
}

async fn settings_css() -> Response {
    (
        [(
            header::CONTENT_TYPE,
            HeaderValue::from_static("text/css; charset=utf-8"),
        )],
        load_overlay_asset("settings.css", SETTINGS_CSS),
    )
        .into_response()
}

async fn settings_js() -> Response {
    (
        [(
            header::CONTENT_TYPE,
            HeaderValue::from_static("application/javascript; charset=utf-8"),
        )],
        load_overlay_asset("settings.js", SETTINGS_JS),
    )
        .into_response()
}

async fn badge_asset(Path((category, file_name)): Path<(String, String)>) -> Response {
    if !file_name.ends_with(".svg") {
        return StatusCode::NOT_FOUND.into_response();
    }

    #[cfg(debug_assertions)]
    {
        let path = badge_asset_path(&category, &file_name);
        if let Ok(bytes) = std::fs::read(&path) {
            return (
                [(
                    header::CONTENT_TYPE,
                    HeaderValue::from_static("image/svg+xml; charset=utf-8"),
                )],
                bytes,
            )
                .into_response();
        }
    }

    let relative = format!("{category}/{file_name}");
    match BADGES_DIR.get_file(&relative) {
        Some(file) => (
            [(
                header::CONTENT_TYPE,
                HeaderValue::from_static("image/svg+xml; charset=utf-8"),
            )],
            file.contents(),
        )
            .into_response(),
        None => StatusCode::NOT_FOUND.into_response(),
    }
}

#[cfg(test)]
mod tests {
    use super::{crop_dynamic_image, overlay_asset_path};
    use crate::stream::overlay_settings::OverlayCropSettings;
    use image::{DynamicImage, GenericImageView, RgbaImage};

    #[test]
    fn overlay_asset_path_points_to_stream_resources() {
        let path = overlay_asset_path("overlay.js");

        assert!(path.ends_with("resources/stream/overlay.js"));
    }

    #[test]
    fn crop_dynamic_image_returns_expected_dimensions() {
        let image = DynamicImage::ImageRgba8(RgbaImage::new(1000, 500));
        let crop = OverlayCropSettings {
            left: 0.25,
            top: 0.2,
            width: 0.5,
            height: 0.3,
        };

        let cropped = crop_dynamic_image(image, crop).unwrap();

        assert_eq!(cropped.dimensions(), (500, 150));
    }
}

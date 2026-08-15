use super::overlay_settings::{validate_crop_settings, OverlayCropSettings, OverlaySettingsStore};
use super::records::OverlayRecordRepository;
use super::runtime::StreamRuntime;
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
use serde::Deserialize;
use std::{
    io::Cursor,
    path::{Path as FsPath, PathBuf},
    time::UNIX_EPOCH,
};
use tower_http::cors::{AllowOrigin, CorsLayer};

const OVERLAY_HTML: &str = include_str!("../../resources/stream/overlay.html");
const OVERLAY_CSS: &str = include_str!("../../resources/stream/overlay.css");
const OVERLAY_JS: &str = include_str!("../../resources/stream/overlay.js");
const SETTINGS_HTML: &str = include_str!("../../resources/stream/settings.html");
const SETTINGS_CSS: &str = include_str!("../../resources/stream/settings.css");
const SETTINGS_JS: &str = include_str!("../../resources/stream/settings.js");
static BADGES_DIR: Dir<'_> = include_dir!("$CARGO_MANIFEST_DIR/resources/stream/badges");
const CINZEL_FONT: &[u8] = include_bytes!("../../resources/stream/fonts/cinzel-latin.woff2");
const OVERLAY_ROUTE: &str = "/overlay";
const SETTINGS_ROUTE: &str = "/settings";
const LATEST_RECORD_ROUTE: &str = "/api/stream/records/latest";
const RECORD_LIST_ROUTE: &str = "/api/stream/records";
const CROP_CONFIG_ROUTE: &str = "/api/overlay/crop-config";
const STRIP_IMAGE_ROUTE: &str = "/images/{record_id}/strip";
const RECORD_IMAGE_ROUTE: &str = "/images/{record_id}";
const OVERLAY_CSS_ROUTE: &str = "/assets/overlay.css";
const OVERLAY_JS_ROUTE: &str = "/assets/overlay.js";
const SETTINGS_CSS_ROUTE: &str = "/assets/settings.css";
const SETTINGS_JS_ROUTE: &str = "/assets/settings.js";
const BADGE_ROUTE: &str = "/assets/badges/{category}/{file_name}";
const CINZEL_FONT_ROUTE: &str = "/assets/fonts/cinzel-latin.woff2";

#[derive(Clone)]
struct HttpAppState {
    overlay_records: OverlayRecordRepository,
    runtime: StreamRuntime,
    overlay_settings: OverlaySettingsStore,
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

pub(super) fn router(
    overlay_records: OverlayRecordRepository,
    runtime: StreamRuntime,
    overlay_settings: OverlaySettingsStore,
) -> Router {
    let cors = CorsLayer::new()
        .allow_origin(AllowOrigin::predicate(|origin, _| {
            is_allowed_cors_origin(origin)
        }))
        .allow_methods([Method::GET, Method::POST])
        .allow_headers([header::CONTENT_TYPE]);

    Router::new()
        .route(OVERLAY_ROUTE, get(overlay_page))
        .route(SETTINGS_ROUTE, get(settings_page))
        .route(LATEST_RECORD_ROUTE, get(latest_record))
        .route(RECORD_LIST_ROUTE, get(record_list))
        .route(
            CROP_CONFIG_ROUTE,
            get(get_crop_config).post(save_crop_config),
        )
        .route(STRIP_IMAGE_ROUTE, get(record_strip_image))
        .route(RECORD_IMAGE_ROUTE, get(record_image))
        .route(OVERLAY_CSS_ROUTE, get(overlay_css))
        .route(OVERLAY_JS_ROUTE, get(overlay_js))
        .route(SETTINGS_CSS_ROUTE, get(settings_css))
        .route(SETTINGS_JS_ROUTE, get(settings_js))
        .route(BADGE_ROUTE, get(badge_asset))
        .route(CINZEL_FONT_ROUTE, get(cinzel_font))
        .layer(cors)
        .with_state(HttpAppState {
            overlay_records,
            runtime,
            overlay_settings,
        })
}

fn is_allowed_cors_origin(origin: &HeaderValue) -> bool {
    matches!(
        origin.to_str().ok(),
        Some(
            "tauri://localhost"
                | "http://tauri.localhost"
                | "https://tauri.localhost"
                | "http://localhost:14207"
                | "http://127.0.0.1:14207"
        )
    )
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

    let path = match app_state.overlay_records.load_image_path(&record_id) {
        Ok(Some(value)) => value,
        Ok(None) => return StatusCode::NOT_FOUND.into_response(),
        Err(message) => return (StatusCode::INTERNAL_SERVER_ERROR, message).into_response(),
    };

    let strip_result = if query.preview.unwrap_or(false) {
        run_strip_image_task(move || {
            let bytes = std::fs::read(&path)
                .map_err(|err| format!("Failed to read overlay source image: {err}"))?;
            crop_strip_image(&bytes, crop)
        })
        .await
    } else {
        let cache_directory = overlay_cache_directory();
        run_strip_image_task(move || {
            load_or_create_strip_cache(&cache_directory, &record_id, &path, crop)
        })
        .await
    };
    let strip_bytes = match strip_result {
        Ok(bytes) => bytes,
        Err(message) => return (StatusCode::INTERNAL_SERVER_ERROR, message).into_response(),
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

async fn run_strip_image_task<F>(task: F) -> Result<Vec<u8>, String>
where
    F: FnOnce() -> Result<Vec<u8>, String> + Send + 'static,
{
    tauri::async_runtime::spawn_blocking(task)
        .await
        .map_err(|err| format!("Overlay strip task failed: {err}"))?
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
    crate::services::paths::overlay_cache_dir()
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

fn crop_cache_path(
    cache_directory: &FsPath,
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

    Ok(cache_directory.join(cache_name))
}

fn load_or_create_strip_cache(
    cache_directory: &FsPath,
    record_id: &str,
    source_path: &FsPath,
    crop: OverlayCropSettings,
) -> Result<Vec<u8>, String> {
    let cache_path = crop_cache_path(cache_directory, record_id, source_path, crop)?;
    if cache_path.exists() {
        return std::fs::read(&cache_path).map_err(|err| {
            format!(
                "Failed to read cached overlay strip from {}: {err}",
                cache_path.display()
            )
        });
    }

    let source_bytes = std::fs::read(source_path).map_err(|err| {
        format!(
            "Failed to read overlay source image from {}: {err}",
            source_path.display()
        )
    })?;
    let bytes = crop_strip_image(&source_bytes, crop)?;

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

async fn cinzel_font() -> Response {
    (
        [
            (header::CONTENT_TYPE, HeaderValue::from_static("font/woff2")),
            (
                header::CACHE_CONTROL,
                HeaderValue::from_static("public, max-age=31536000, immutable"),
            ),
        ],
        CINZEL_FONT,
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
    use super::{
        crop_cache_path, crop_dynamic_image, is_allowed_cors_origin, load_or_create_strip_cache,
        overlay_asset_path, BADGES_DIR, BADGE_ROUTE, CINZEL_FONT, CINZEL_FONT_ROUTE,
        CROP_CONFIG_ROUTE, LATEST_RECORD_ROUTE, OVERLAY_CSS, OVERLAY_CSS_ROUTE, OVERLAY_JS_ROUTE,
        OVERLAY_ROUTE, RECORD_IMAGE_ROUTE, RECORD_LIST_ROUTE, SETTINGS_CSS_ROUTE,
        SETTINGS_JS_ROUTE, SETTINGS_ROUTE, STRIP_IMAGE_ROUTE,
    };
    use crate::stream::overlay_settings::OverlayCropSettings;
    use axum::http::HeaderValue;
    use image::{DynamicImage, GenericImageView, RgbaImage};

    #[test]
    fn overlay_asset_path_points_to_stream_resources() {
        let path = overlay_asset_path("overlay.js");

        assert!(path.ends_with("resources/stream/overlay.js"));
    }

    #[test]
    fn embedded_badge_assets_are_complete_for_every_hero() {
        for hero_key in [
            "van", "pyg", "doo", "mak", "jul", "kar", "ste", "dra", "unk",
        ] {
            for path in [
                format!("heroes/hero-{hero_key}.svg"),
                format!("herohalf/herohalf-{hero_key}.svg"),
            ] {
                assert!(BADGES_DIR.get_file(&path).is_some(), "missing {path}");
            }

            for battle_count in 0..=20 {
                let path = format!("info/info-{hero_key}-{battle_count}.svg");
                assert!(BADGES_DIR.get_file(&path).is_some(), "missing {path}");
            }
        }
    }

    #[test]
    fn production_routes_remain_stable() {
        assert_eq!(
            [
                OVERLAY_ROUTE,
                SETTINGS_ROUTE,
                LATEST_RECORD_ROUTE,
                RECORD_LIST_ROUTE,
                CROP_CONFIG_ROUTE,
                STRIP_IMAGE_ROUTE,
                RECORD_IMAGE_ROUTE,
                OVERLAY_CSS_ROUTE,
                OVERLAY_JS_ROUTE,
                SETTINGS_CSS_ROUTE,
                SETTINGS_JS_ROUTE,
                BADGE_ROUTE,
                CINZEL_FONT_ROUTE,
            ],
            [
                "/overlay",
                "/settings",
                "/api/stream/records/latest",
                "/api/stream/records",
                "/api/overlay/crop-config",
                "/images/{record_id}/strip",
                "/images/{record_id}",
                "/assets/overlay.css",
                "/assets/overlay.js",
                "/assets/settings.css",
                "/assets/settings.js",
                "/assets/badges/{category}/{file_name}",
                "/assets/fonts/cinzel-latin.woff2",
            ]
        );
    }

    #[test]
    fn overlay_css_font_url_resolves_to_a_served_route() {
        assert!(
            OVERLAY_CSS.contains(&format!("url('{CINZEL_FONT_ROUTE}')")),
            "overlay.css must load the brand face from the route the server exposes"
        );
        assert_eq!(
            &CINZEL_FONT[..4],
            b"wOF2",
            "the embedded brand face must be a woff2 payload"
        );
    }

    #[test]
    fn cors_origin_policy_allows_only_tauri_and_repo_dev_origins() {
        for allowed in [
            "tauri://localhost",
            "http://tauri.localhost",
            "https://tauri.localhost",
            "http://localhost:14207",
            "http://127.0.0.1:14207",
        ] {
            let origin = HeaderValue::from_static(allowed);
            assert!(
                is_allowed_cors_origin(&origin),
                "{allowed} should be allowed"
            );
        }

        for denied in ["https://example.com", "http://localhost:3000", "null"] {
            let origin = HeaderValue::from_static(denied);
            assert!(
                !is_allowed_cors_origin(&origin),
                "{denied} should be denied"
            );
        }
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

    #[test]
    fn strip_cache_hit_does_not_decode_source_image() {
        let temp_dir = tempfile::tempdir().unwrap();
        let source_path = temp_dir.path().join("source.png");
        let cache_directory = temp_dir.path().join("cache");
        let crop = OverlayCropSettings::default();
        std::fs::write(&source_path, b"not an image").unwrap();
        let cache_path = crop_cache_path(&cache_directory, "shot-1", &source_path, crop).unwrap();
        std::fs::create_dir_all(cache_path.parent().unwrap()).unwrap();
        std::fs::write(&cache_path, b"cached strip").unwrap();

        let bytes =
            load_or_create_strip_cache(&cache_directory, "shot-1", &source_path, crop).unwrap();

        assert_eq!(bytes, b"cached strip");
    }
}

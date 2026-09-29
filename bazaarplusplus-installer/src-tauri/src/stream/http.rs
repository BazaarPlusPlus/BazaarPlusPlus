use super::history_thumbnails::{self, HistoryThumbnails};
use super::overlay_settings::{validate_crop_settings, OverlayCropSettings, OverlaySettingsStore};
use super::records::OverlayRecordRepository;
use super::runtime::StreamRuntime;
use super::strip::{render_strip, StripRequest};
use axum::http::StatusCode;
use axum::{
    extract::{Path, Query, State},
    http::{header, HeaderValue, Method},
    response::{Html, IntoResponse, Response},
    routing::get,
    Json, Router,
};
use include_dir::{include_dir, Dir};
use serde::Deserialize;
use std::{
    borrow::Cow,
    path::{Path as FsPath, PathBuf},
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
    cache_directory: PathBuf,
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
    history_thumbnails: HistoryThumbnails,
    overlay_settings: OverlaySettingsStore,
    cache_directory: PathBuf,
) -> Router {
    let cors = CorsLayer::new()
        .allow_origin(AllowOrigin::predicate(|origin, _| {
            is_allowed_cors_origin(origin)
        }))
        .allow_methods([Method::GET, Method::POST])
        .allow_headers([header::CONTENT_TYPE]);

    let history_thumbnails = history_thumbnails::router(
        history_thumbnails,
        overlay_settings.clone(),
        cache_directory.clone(),
    );

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
        .with_state(HttpAppState {
            overlay_records,
            runtime,
            overlay_settings,
            cache_directory,
        })
        .merge(history_thumbnails)
        .layer(cors)
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

/// Borrowed in release so the six static asset routes serve the embedded text
/// without copying; owned only on the debug-only filesystem hot-reload path.
async fn load_overlay_asset(_file_name: &str, embedded: &'static str) -> Cow<'static, str> {
    #[cfg(debug_assertions)]
    {
        let path = overlay_asset_path(_file_name);
        let read = move || std::fs::read_to_string(path).map_err(|err| err.to_string());
        if let Ok(contents) = run_record_task(read).await {
            return Cow::Owned(contents);
        }
    }

    Cow::Borrowed(embedded)
}

async fn overlay_page() -> Html<Cow<'static, str>> {
    Html(load_overlay_asset("overlay.html", OVERLAY_HTML).await)
}

async fn settings_page() -> Html<Cow<'static, str>> {
    Html(load_overlay_asset("settings.html", SETTINGS_HTML).await)
}

async fn latest_record(
    State(app_state): State<HttpAppState>,
    Query(query): Query<LatestRecordQuery>,
) -> Response {
    let offset = query.offset.unwrap_or(0);
    let snapshot = app_state.runtime.snapshot();
    let from = query.from.or(snapshot.active_from);
    let repository = app_state.overlay_records;

    match run_record_task(move || repository.load_record_at_offset(from.as_deref(), offset)).await {
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
    let from = query.from.or(snapshot.active_from);
    let repository = app_state.overlay_records;

    match run_record_task(move || repository.load_record_list(from.as_deref(), Some(limit))).await {
        Ok(records) => Json(records).into_response(),
        Err(message) => (StatusCode::INTERNAL_SERVER_ERROR, message).into_response(),
    }
}

async fn get_crop_config(State(app_state): State<HttpAppState>) -> Response {
    let settings = app_state.overlay_settings;
    match run_record_task(move || settings.load_payload()).await {
        Ok(payload) => Json(payload).into_response(),
        Err(message) => (StatusCode::INTERNAL_SERVER_ERROR, message).into_response(),
    }
}

async fn save_crop_config(
    State(app_state): State<HttpAppState>,
    Json(request): Json<SaveCropConfigRequest>,
) -> Response {
    let settings = app_state.overlay_settings;
    match run_record_task(move || settings.save(request.crop)).await {
        Ok(payload) => Json(payload).into_response(),
        Err(message) => (StatusCode::BAD_REQUEST, message).into_response(),
    }
}

async fn record_image(
    Path(record_id): Path<String>,
    State(app_state): State<HttpAppState>,
) -> Response {
    let repository = app_state.overlay_records;
    let (path, bytes) = match run_record_task(move || repository.load_image(&record_id)).await {
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
    let crop = match requested_strip_crop(&query) {
        Ok(crop) => crop,
        Err(message) => return (StatusCode::BAD_REQUEST, message).into_response(),
    };
    let request = StripRequest {
        record_id,
        crop,
        preview: query.preview.unwrap_or(false),
    };
    let records = app_state.overlay_records;
    let settings = app_state.overlay_settings;
    let cache_root = app_state.cache_directory;

    match run_record_task(move || render_strip(&records, &settings, &cache_root, &request)).await {
        Ok(Some(bytes)) => png_response(bytes),
        Ok(None) => StatusCode::NOT_FOUND.into_response(),
        Err(message) => (StatusCode::INTERNAL_SERVER_ERROR, message).into_response(),
    }
}

/// A rendered strip; `no-store` because the saved crop can change its pixels.
pub(super) fn png_response(bytes: Vec<u8>) -> Response {
    (
        [
            (header::CONTENT_TYPE, HeaderValue::from_static("image/png")),
            (header::CACHE_CONTROL, HeaderValue::from_static("no-store")),
        ],
        bytes,
    )
        .into_response()
}

/// Every SQLite or filesystem access a handler makes runs here: a SQLite open
/// can sleep on a busy database, and file reads and writes block, so none may
/// run on the shared tokio workers that also serve the Tauri async commands.
pub(super) async fn run_record_task<T, F>(task: F) -> Result<T, String>
where
    T: Send + 'static,
    F: FnOnce() -> Result<T, String> + Send + 'static,
{
    tauri::async_runtime::spawn_blocking(task)
        .await
        .map_err(|err| format!("Overlay record task failed: {err}"))?
}

/// A crop given in the query, validated before any blocking work; `None` means
/// the saved overlay settings apply.
fn requested_strip_crop(query: &StripPreviewQuery) -> Result<Option<OverlayCropSettings>, String> {
    if query.left.is_none()
        && query.top.is_none()
        && query.width.is_none()
        && query.height.is_none()
    {
        return Ok(None);
    }

    let defaults = OverlayCropSettings::default();
    validate_crop_settings(OverlayCropSettings {
        left: query.left.unwrap_or(defaults.left),
        top: query.top.unwrap_or(defaults.top),
        width: query.width.unwrap_or(defaults.width),
        height: query.height.unwrap_or(defaults.height),
    })
    .map(Some)
}

fn detect_content_type(path: &FsPath) -> &'static str {
    match path
        .extension()
        .and_then(|ext| ext.to_str())
        .map(str::to_ascii_lowercase)
        .as_deref()
    {
        Some("png") => "image/png",
        Some("jpg" | "jpeg") => "image/jpeg",
        Some("webp") => "image/webp",
        _ => "application/octet-stream",
    }
}

async fn overlay_css() -> Response {
    (
        [(
            header::CONTENT_TYPE,
            HeaderValue::from_static("text/css; charset=utf-8"),
        )],
        load_overlay_asset("overlay.css", OVERLAY_CSS).await,
    )
        .into_response()
}

async fn overlay_js() -> Response {
    (
        [(
            header::CONTENT_TYPE,
            HeaderValue::from_static("application/javascript; charset=utf-8"),
        )],
        load_overlay_asset("overlay.js", OVERLAY_JS).await,
    )
        .into_response()
}

async fn settings_css() -> Response {
    (
        [(
            header::CONTENT_TYPE,
            HeaderValue::from_static("text/css; charset=utf-8"),
        )],
        load_overlay_asset("settings.css", SETTINGS_CSS).await,
    )
        .into_response()
}

async fn settings_js() -> Response {
    (
        [(
            header::CONTENT_TYPE,
            HeaderValue::from_static("application/javascript; charset=utf-8"),
        )],
        load_overlay_asset("settings.js", SETTINGS_JS).await,
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
        let read = move || std::fs::read(path).map_err(|err| err.to_string());
        if let Ok(bytes) = run_record_task(read).await {
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
        history_thumbnails, router, HistoryThumbnails, OverlayRecordRepository,
        OverlaySettingsStore, StreamRuntime,
    };
    use super::{
        is_allowed_cors_origin, overlay_asset_path, BADGES_DIR, BADGE_ROUTE, CINZEL_FONT,
        CINZEL_FONT_ROUTE, CROP_CONFIG_ROUTE, LATEST_RECORD_ROUTE, OVERLAY_CSS, OVERLAY_CSS_ROUTE,
        OVERLAY_JS_ROUTE, OVERLAY_ROUTE, RECORD_IMAGE_ROUTE, RECORD_LIST_ROUTE, SETTINGS_CSS_ROUTE,
        SETTINGS_JS_ROUTE, SETTINGS_ROUTE, STRIP_IMAGE_ROUTE,
    };
    use axum::http::HeaderValue;

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
                history_thumbnails::ROUTE,
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
                "/history/{source}/images/{screenshot_id}/strip",
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

    fn locked_screenshot_fixture(game_path: &std::path::Path) -> rusqlite::Connection {
        let screenshots = crate::services::paths::screenshots_dir(game_path);
        std::fs::create_dir_all(&screenshots).unwrap();
        image::RgbaImage::from_pixel(64, 32, image::Rgba([20, 80, 160, 255]))
            .save(screenshots.join("shot.png"))
            .unwrap();
        // The default rollback journal lets an exclusive lock block readers, as a
        // game write or checkpoint can.
        let connection =
            rusqlite::Connection::open(crate::services::paths::database_path(game_path)).unwrap();
        connection
            .execute_batch(
                "pragma user_version = 2;
                create table run_screenshots (
                    screenshot_id text primary key, capture_source text,
                    image_relative_path text, hero_name text, captured_at_local text,
                    captured_at_utc text, victories_at_capture integer, day integer,
                    player_rank text, player_rating integer
                );
                insert into run_screenshots values (
                    'shot-1', 'end_of_run_auto', 'shot.png', 'Vanessa',
                    '2026-09-01T00:00:00Z', '2026-09-01T00:00:00Z', 10, 10, null, null
                );
                begin exclusive;",
            )
            .unwrap();
        connection
    }

    #[tokio::test]
    async fn strip_requests_leave_the_async_runtime_free_while_the_database_is_locked() {
        use tower::ServiceExt;
        let temp = tempfile::tempdir().unwrap();
        let game_path = temp.path().join("game");
        let lock = locked_screenshot_fixture(&game_path);
        let thumbnails = HistoryThumbnails::default();
        let thumbnail_url = history_thumbnails::url(thumbnails.register(&game_path), "shot-1");
        let app = router(
            OverlayRecordRepository::new(Some(game_path)),
            StreamRuntime::default(),
            thumbnails,
            OverlaySettingsStore::new(temp.path().join("settings.json")),
            temp.path().join("cache"),
        );
        let requests = [
            "/images/shot-1/strip".to_string(),
            thumbnail_url.replace("http://127.0.0.1:17654", ""),
            "/images/shot-1/strip?preview=true&left=0.1".to_string(),
        ]
        .map(|uri| {
            tokio::spawn(
                app.clone().oneshot(
                    axum::http::Request::builder()
                        .uri(uri)
                        .body(axum::body::Body::empty())
                        .unwrap(),
                ),
            )
        });

        // This current-thread runtime is the only worker: the ticker advances
        // only if every pending strip request has yielded it.
        let started = std::time::Instant::now();
        for _ in 0..32 {
            tokio::task::yield_now().await;
        }
        assert!(
            started.elapsed() < std::time::Duration::from_millis(500),
            "ticker stalled for {:?}",
            started.elapsed()
        );
        assert!(requests.iter().all(|request| !request.is_finished()));

        drop(lock);
        for request in requests {
            let response = request.await.unwrap().unwrap();
            let status = response.status();
            let headers = response.headers().clone();
            let body = axum::body::to_bytes(response.into_body(), 1024 * 1024)
                .await
                .unwrap();
            assert_eq!(
                status,
                axum::http::StatusCode::OK,
                "{}",
                String::from_utf8_lossy(&body)
            );
            assert_eq!(headers["content-type"], "image/png");
            assert_eq!(headers["cache-control"], "no-store");
            image::load_from_memory(&body).unwrap();
        }
    }

    /// Opening a FIFO blocks until the other end opens, so the settings file
    /// stalls whichever thread reads or writes it until `serve` runs.
    #[cfg(unix)]
    fn blocking_settings_file(
        path: &std::path::Path,
        serve: impl FnOnce(&std::path::Path) + Send + 'static,
    ) -> std::sync::mpsc::Sender<()> {
        let status = std::process::Command::new("mkfifo")
            .arg(path)
            .status()
            .unwrap();
        assert!(status.success());
        let (release, released) = std::sync::mpsc::channel();
        let path = path.to_path_buf();
        std::thread::spawn(move || {
            // Serves anyway after the timeout, so a handler that blocks the
            // runtime fails the ticker assertion instead of hanging the test.
            let _ = released.recv_timeout(std::time::Duration::from_secs(2));
            serve(&path);
        });
        release
    }

    #[cfg(unix)]
    async fn assert_request_leaves_the_async_runtime_free(
        app: axum::Router,
        request: axum::http::Request<axum::body::Body>,
        release: std::sync::mpsc::Sender<()>,
    ) -> (axum::http::StatusCode, serde_json::Value) {
        use tower::ServiceExt;
        let request = tokio::spawn(app.oneshot(request));

        // This current-thread runtime is the only worker: the ticker advances
        // only if the pending request has yielded it.
        let started = std::time::Instant::now();
        for _ in 0..32 {
            tokio::task::yield_now().await;
        }
        assert!(
            started.elapsed() < std::time::Duration::from_millis(500),
            "ticker stalled for {:?}",
            started.elapsed()
        );
        assert!(!request.is_finished());

        release.send(()).unwrap();
        let response = request.await.unwrap().unwrap();
        let status = response.status();
        let body = axum::body::to_bytes(response.into_body(), 1024 * 1024)
            .await
            .unwrap();
        (status, serde_json::from_slice(&body).unwrap())
    }

    #[cfg(unix)]
    #[tokio::test]
    async fn crop_config_requests_leave_the_async_runtime_free_while_the_settings_file_blocks() {
        use std::io::{Read, Write};
        let temp = tempfile::tempdir().unwrap();
        let settings_path = temp.path().join("settings.json");
        let app = router(
            OverlayRecordRepository::new(None),
            StreamRuntime::default(),
            HistoryThumbnails::default(),
            OverlaySettingsStore::new(settings_path.clone()),
            temp.path().join("cache"),
        );
        let saved = serde_json::json!({
            "v": 4,
            "crop": { "left": 0.1, "top": 0.2, "width": 0.3, "height": 0.4 },
            "display_mode": "hero"
        });

        let document = saved.to_string();
        let release = blocking_settings_file(&settings_path, move |path| {
            let mut writer = std::fs::OpenOptions::new().write(true).open(path).unwrap();
            writer.write_all(document.as_bytes()).unwrap();
        });
        let (status, payload) = assert_request_leaves_the_async_runtime_free(
            app.clone(),
            axum::http::Request::builder()
                .uri(CROP_CONFIG_ROUTE)
                .body(axum::body::Body::empty())
                .unwrap(),
            release,
        )
        .await;
        assert_eq!(status, axum::http::StatusCode::OK);
        assert_eq!(
            payload["crop"],
            serde_json::json!({ "left": 0.1, "top": 0.2, "width": 0.3, "height": 0.4 })
        );
        assert_eq!(payload["display_mode"], "hero");

        std::fs::remove_file(&settings_path).unwrap();
        let (written_tx, written_rx) = std::sync::mpsc::channel();
        let release = blocking_settings_file(&settings_path, move |path| {
            // An empty read makes the save keep the default display mode.
            drop(std::fs::OpenOptions::new().write(true).open(path).unwrap());
            let mut written = String::new();
            std::fs::File::open(path)
                .unwrap()
                .read_to_string(&mut written)
                .unwrap();
            written_tx.send(written).unwrap();
        });
        let (status, payload) = assert_request_leaves_the_async_runtime_free(
            app,
            axum::http::Request::builder()
                .method("POST")
                .uri(CROP_CONFIG_ROUTE)
                .header("content-type", "application/json")
                .body(axum::body::Body::from(
                    r#"{"crop":{"left":0.25,"top":0.25,"width":0.5,"height":0.5}}"#,
                ))
                .unwrap(),
            release,
        )
        .await;
        assert_eq!(status, axum::http::StatusCode::OK);
        assert_eq!(
            payload["crop"],
            serde_json::json!({ "left": 0.25, "top": 0.25, "width": 0.5, "height": 0.5 })
        );
        let written: serde_json::Value = serde_json::from_str(&written_rx.recv().unwrap()).unwrap();
        assert_eq!(written["crop"], payload["crop"]);
        assert_eq!(written["display_mode"], "current");
    }
}

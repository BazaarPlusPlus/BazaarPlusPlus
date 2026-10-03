use super::{
    cache_directory, sweep, url, CacheLimits, HistoryThumbnails, CACHE_DIRECTORY,
    LEGACY_CACHE_DIRECTORY,
};
use crate::stream::{
    runtime::{
        test_support::{installation, TestServer},
        StreamInstallation, StreamRuntime,
    },
    server,
};
use std::{
    path::{Path, PathBuf},
    sync::{atomic::Ordering, Arc},
    time::{Duration, SystemTime},
};
use tokio::sync::Notify;

/// Adds a screenshot row and its image, creating the database on first use.
fn add_screenshot(game_path: &Path, id: &str, capture_source: &str, color: [u8; 4]) -> PathBuf {
    let screenshots = crate::services::paths::screenshots_dir(game_path);
    std::fs::create_dir_all(&screenshots).unwrap();
    let file_name = format!("{id}.png");
    let path = screenshots.join(&file_name);
    image::RgbaImage::from_pixel(64, 32, image::Rgba(color))
        .save(&path)
        .unwrap();
    let connection =
        rusqlite::Connection::open(crate::services::paths::database_path(game_path)).unwrap();
    connection
        .execute_batch(
            "pragma user_version = 2;
            create table if not exists run_screenshots (
                screenshot_id text primary key, capture_source text, is_primary integer,
                image_relative_path text, hero_name text, captured_at_local text,
                captured_at_utc text, victories_at_capture integer, day integer,
                player_rank text, player_rating integer
            );",
        )
        .unwrap();
    connection
        .execute(
            "insert into run_screenshots values (
                ?1, ?2, 1, ?3, 'Vanessa', '2026-09-01T00:00:00Z',
                '2026-09-01T00:00:00Z', 10, 10, null, null
            )",
            [id, capture_source, &file_name],
        )
        .unwrap();
    path
}

fn screenshot_fixture(game_path: &Path, color: [u8; 4]) -> PathBuf {
    add_screenshot(game_path, "shot-1", "end_of_run_auto", color)
}

fn history_router(
    runtime: &StreamRuntime,
    thumbnails: &HistoryThumbnails,
    overlay_path: &Path,
    root: &Path,
) -> axum::Router {
    crate::stream::http::router(
        crate::stream::records::OverlayRecordRepository::new(Some(overlay_path.to_path_buf())),
        runtime.clone(),
        thumbnails.clone(),
        crate::stream::overlay_settings::OverlaySettingsStore::new(
            root.join("settings.json"),
            None,
        ),
        root.join("cache"),
    )
}

/// The request path of an absolute service URL.
fn route_of(url: &str) -> &str {
    url.strip_prefix(&server::origin())
        .unwrap_or_else(|| panic!("{url} is not on the service origin"))
}

async fn get(router: axum::Router, uri: &str) -> (axum::http::StatusCode, Vec<u8>) {
    use tower::ServiceExt;
    let response = router
        .oneshot(
            axum::http::Request::builder()
                .uri(uri)
                .body(axum::body::Body::empty())
                .unwrap(),
        )
        .await
        .unwrap();
    let status = response.status();
    let bytes = axum::body::to_bytes(response.into_body(), 1024 * 1024)
        .await
        .unwrap();
    (status, bytes.to_vec())
}

async fn image_pixel(router: axum::Router, uri: &str) -> [u8; 4] {
    let (status, bytes) = get(router, uri).await;
    assert_eq!(
        status,
        axum::http::StatusCode::OK,
        "{}",
        String::from_utf8_lossy(&bytes)
    );
    image::load_from_memory(&bytes)
        .unwrap()
        .to_rgba8()
        .get_pixel(0, 0)
        .0
}

fn files_in(directory: &Path) -> Vec<PathBuf> {
    let mut files = std::fs::read_dir(directory)
        .map(|entries| entries.map(|entry| entry.unwrap().path()).collect())
        .unwrap_or_else(|_| Vec::new());
    files.sort();
    files
}

fn set_age(path: &Path, now: SystemTime, age: Duration) {
    std::fs::File::options()
        .write(true)
        .open(path)
        .unwrap()
        .set_modified(now - age)
        .unwrap();
}

#[test]
fn urls_are_absolute_on_the_service_origin_and_stable_per_path() {
    let thumbnails = HistoryThumbnails::default();
    let a = thumbnails.register(Path::new("/Games/A"));
    let b = thumbnails.register(Path::new("/Games/B"));

    assert_eq!(thumbnails.register(Path::new("/Games/A")), a);
    assert_ne!(a, b);
    assert_eq!(
        url(a, "shot-1"),
        "http://127.0.0.1:17654/history/0/images/shot-1/strip"
    );
    assert_eq!(
        url(b, "shot-1"),
        "http://127.0.0.1:17654/history/1/images/shot-1/strip"
    );
}

#[test]
fn cache_namespace_is_a_stable_digest_of_the_installation_path() {
    let root = Path::new("/cache");

    assert_eq!(
        cache_directory(root, Path::new("/Games/The Bazaar")),
        root.join("history-thumbnails").join("66c8b33aa11cd442")
    );
}

#[tokio::test]
async fn one_installation_caches_its_thumbnails_in_one_namespace() {
    let temp = tempfile::tempdir().unwrap();
    let game_path = temp.path().join("game");
    add_screenshot(&game_path, "shot-1", "end_of_run_auto", [255, 0, 0, 255]);
    add_screenshot(&game_path, "shot-2", "end_of_run_auto", [0, 255, 0, 255]);
    let thumbnails = HistoryThumbnails::default();
    let source = thumbnails.register(&game_path);
    let router = history_router(
        &StreamRuntime::default(),
        &thumbnails,
        &game_path,
        temp.path(),
    );

    for id in ["shot-1", "shot-2"] {
        image_pixel(router.clone(), route_of(&url(source, id))).await;
    }

    let cache_root = temp.path().join("cache");
    let namespaces = files_in(&cache_root.join(CACHE_DIRECTORY));
    assert_eq!(namespaces, [cache_directory(&cache_root, &game_path)]);
    assert_eq!(files_in(&namespaces[0]).len(), 2);
}

#[tokio::test]
async fn thumbnails_serve_a_primary_screenshot_whatever_its_capture_source() {
    let temp = tempfile::tempdir().unwrap();
    let game_path = temp.path().join("game");
    let color = [30, 60, 90, 255];
    add_screenshot(&game_path, "manual-1", "manual", color);
    let thumbnails = HistoryThumbnails::default();
    let source = thumbnails.register(&game_path);
    let router = history_router(
        &StreamRuntime::default(),
        &thumbnails,
        &game_path,
        temp.path(),
    );

    assert_eq!(
        image_pixel(router.clone(), route_of(&url(source, "manual-1"))).await,
        color
    );
    // The OBS overlay still shows only end-of-run captures.
    assert_eq!(
        get(router, "/images/manual-1/strip").await.0,
        axum::http::StatusCode::NOT_FOUND
    );
}

#[tokio::test]
async fn unknown_sources_and_missing_screenshots_are_not_found() {
    let temp = tempfile::tempdir().unwrap();
    let game_path = temp.path().join("game");
    let thumbnails = HistoryThumbnails::default();
    let source = thumbnails.register(&game_path);
    let router = history_router(
        &StreamRuntime::default(),
        &thumbnails,
        &game_path,
        temp.path(),
    );

    for uri in [
        "/history/7/images/shot-1/strip".to_string(),
        route_of(&url(source, "shot-1")).to_string(),
    ] {
        assert_eq!(
            get(router.clone(), &uri).await.0,
            axum::http::StatusCode::NOT_FOUND
        );
    }
}

#[tokio::test]
async fn a_cache_hit_marks_the_entry_as_recently_used() {
    let temp = tempfile::tempdir().unwrap();
    let game_path = temp.path().join("game");
    screenshot_fixture(&game_path, [1, 2, 3, 255]);
    let thumbnails = HistoryThumbnails::default();
    let uri = url(thumbnails.register(&game_path), "shot-1");
    let router = history_router(
        &StreamRuntime::default(),
        &thumbnails,
        &game_path,
        temp.path(),
    );
    image_pixel(router.clone(), route_of(&uri)).await;
    let [entry] = files_in(&cache_directory(&temp.path().join("cache"), &game_path))
        .try_into()
        .unwrap();
    let stale = SystemTime::now() - Duration::from_secs(90 * 24 * 60 * 60);
    set_age(
        &entry,
        SystemTime::now(),
        Duration::from_secs(90 * 24 * 60 * 60),
    );

    image_pixel(router, route_of(&uri)).await;

    assert!(std::fs::metadata(&entry).unwrap().modified().unwrap() > stale);
}

#[test]
fn sweep_removes_the_legacy_tree_stale_entries_and_orphans_but_keeps_fresh_ones() {
    let temp = tempfile::tempdir().unwrap();
    let root = temp.path();
    let now = SystemTime::now();
    let limits = CacheLimits {
        unused_for: Duration::from_secs(3600),
        max_entries: 100,
        fresh_for: Duration::from_secs(10),
    };
    let legacy = root.join(LEGACY_CACHE_DIRECTORY).join("0123456789abcdef");
    std::fs::create_dir_all(&legacy).unwrap();
    std::fs::write(legacy.join("shot-1-strip.png"), b"old").unwrap();
    let obs_entry = root.join("shot-1-strip.png");
    std::fs::write(&obs_entry, b"obs").unwrap();
    set_age(&obs_entry, now, Duration::from_secs(7200));
    let namespace = cache_directory(root, Path::new("/Games/The Bazaar"));
    std::fs::create_dir_all(&namespace).unwrap();
    let entry = |name: &str, age: u64| {
        let path = namespace.join(name);
        std::fs::write(&path, b"png").unwrap();
        set_age(&path, now, Duration::from_secs(age));
        path
    };
    let stale = entry("stale-strip.png", 7200);
    let used = entry("used-strip.png", 60);
    let orphan = entry("used-strip.png.1-0.tmp", 60);
    let writing = entry("used-strip.png.1-1.tmp", 1);
    let just_served = entry("just-served-strip.png", 1);

    sweep(root, now, &limits);

    assert!(!root.join(LEGACY_CACHE_DIRECTORY).exists());
    assert!(!stale.exists());
    assert!(!orphan.exists());
    assert!(used.exists());
    assert!(writing.exists());
    assert!(just_served.exists());
    // The OBS strips at the cache root are not History's to sweep.
    assert!(obs_entry.exists());
}

#[test]
fn sweep_caps_the_entry_count_by_removing_the_oldest() {
    let temp = tempfile::tempdir().unwrap();
    let root = temp.path();
    let now = SystemTime::now();
    let limits = CacheLimits {
        unused_for: Duration::from_secs(3600),
        max_entries: 2,
        fresh_for: Duration::from_secs(10),
    };
    let entries = ["/Games/A", "/Games/B", "/Games/A"]
        .into_iter()
        .zip([300, 200, 100])
        .enumerate()
        .map(|(index, (game_path, age))| {
            let namespace = cache_directory(root, Path::new(game_path));
            std::fs::create_dir_all(&namespace).unwrap();
            let path = namespace.join(format!("shot-{index}-strip.png"));
            std::fs::write(&path, b"png").unwrap();
            set_age(&path, now, Duration::from_secs(age));
            path
        })
        .collect::<Vec<_>>();

    sweep(root, now, &limits);

    assert_eq!(
        entries.iter().map(|path| path.exists()).collect::<Vec<_>>(),
        [false, true, true]
    );
}

#[tokio::test]
async fn preparation_preserves_obs_and_isolates_sources_and_cached_pixels() {
    let temp = tempfile::tempdir().unwrap();
    let a = temp.path().join("A");
    let b = temp.path().join("B");
    let red = [255, 0, 0, 255];
    let blue = [0, 0, 255, 255];
    let first_image = screenshot_fixture(&a, red);
    let second_image = screenshot_fixture(&b, blue);
    // Equal id, length, mtime and crop: only the installation tells them apart.
    let length = std::fs::metadata(&first_image)
        .unwrap()
        .len()
        .max(std::fs::metadata(&second_image).unwrap().len());
    for path in [&first_image, &second_image] {
        let mut bytes = std::fs::read(path).unwrap();
        bytes.resize(length as usize, 0);
        std::fs::write(path, bytes).unwrap();
        std::fs::File::options()
            .write(true)
            .open(path)
            .unwrap()
            .set_times(std::fs::FileTimes::new().set_modified(
                std::time::SystemTime::UNIX_EPOCH + Duration::from_secs(1_700_000_000),
            ))
            .unwrap();
    }
    let runtime = StreamRuntime::default();
    let server = TestServer::default();
    let requested = StreamInstallation {
        game_path: Some(a.clone()),
        record_game_path: Some(a.clone()),
    };
    runtime.ensure_with(&server, requested).await.unwrap();
    runtime
        .set_window_with(|_, _| Ok((Some("2026-08-01T00:00:00Z".into()), 3)))
        .await
        .unwrap();
    let before = serde_json::to_value(runtime.snapshot()).unwrap();
    let thumbnails = HistoryThumbnails::default();
    let url_a = url(thumbnails.register(&a), "shot-1");
    let url_b = url(thumbnails.register(&b), "shot-1");
    runtime
        .prepare_history_thumbnails_with(&server, || StreamInstallation {
            game_path: Some(b.clone()),
            record_game_path: Some(b.clone()),
        })
        .await
        .unwrap();
    assert_eq!(serde_json::to_value(runtime.snapshot()).unwrap(), before);
    assert_eq!(server.starts.load(Ordering::SeqCst), 1);
    let router = history_router(&runtime, &thumbnails, &a, temp.path());
    assert_eq!(
        image_pixel(router.clone(), "/images/shot-1/strip").await,
        red
    );
    assert_eq!(image_pixel(router.clone(), route_of(&url_a)).await, red);
    assert_eq!(image_pixel(router.clone(), route_of(&url_b)).await, blue);
    // Cached requests, and the old A URL after B was registered, retain their source.
    assert_eq!(image_pixel(router.clone(), route_of(&url_b)).await, blue);
    assert_eq!(image_pixel(router.clone(), route_of(&url_a)).await, red);
    assert_eq!(image_pixel(router, "/images/shot-1/strip").await, red);
    runtime.stop().await.unwrap();
}

#[tokio::test]
async fn thumbnails_work_when_the_database_appears_after_an_empty_startup() {
    let temp = tempfile::tempdir().unwrap();
    let game_path = temp.path().join("game");
    let runtime = StreamRuntime::default();
    let server = TestServer::default();
    runtime
        .prepare_history_thumbnails_with(&server, || StreamInstallation {
            game_path: Some(game_path.clone()),
            record_game_path: None,
        })
        .await
        .unwrap();
    assert!(!crate::services::paths::database_path(&game_path).exists());
    let color = [20, 80, 160, 255];
    screenshot_fixture(&game_path, color);
    let thumbnails = HistoryThumbnails::default();
    let uri = url(thumbnails.register(&game_path), "shot-1");
    runtime
        .prepare_history_thumbnails_with(&server, || panic!("running OBS must not be rebound"))
        .await
        .unwrap();
    // An unbound OBS repository must not be consulted for History's image path.
    let router = history_router(
        &runtime,
        &thumbnails,
        &temp.path().join("unbound"),
        temp.path(),
    );
    assert_eq!(image_pixel(router, route_of(&uri)).await, color);
    assert_eq!(server.starts.load(Ordering::SeqCst), 1);
    runtime.stop().await.unwrap();
}

#[tokio::test]
async fn preparation_coalesces_starts_and_recovers_after_stop() {
    let runtime = StreamRuntime::default();
    let server = TestServer::default();
    let (first, second) = tokio::join!(
        runtime.prepare_history_thumbnails_with(&server, || installation("/A")),
        runtime.prepare_history_thumbnails_with(&server, || installation("/B")),
    );
    first.unwrap();
    second.unwrap();
    assert!(runtime.snapshot().running);
    assert_eq!(server.starts.load(Ordering::SeqCst), 1);
    runtime.stop().await.unwrap();
    assert!(!runtime.snapshot().running);
    runtime
        .prepare_history_thumbnails_with(&server, || installation("/B"))
        .await
        .unwrap();
    assert!(runtime.snapshot().running);
    assert_eq!(server.starts.load(Ordering::SeqCst), 2);
    runtime.stop().await.unwrap();
}

#[tokio::test]
async fn preparation_waits_for_maintenance_and_then_resumes() {
    let runtime = StreamRuntime::default();
    let server = TestServer::default();
    runtime
        .prepare_history_thumbnails_with(&server, || installation("/A"))
        .await
        .unwrap();
    let entered = Arc::new(Notify::new());
    let release = Arc::new(Notify::new());
    let worker = runtime.clone();
    let entering = entered.clone();
    let releasing = release.clone();
    let maintenance = tokio::spawn(async move {
        worker
            .exclusive_maintenance(|| async move {
                entering.notify_one();
                releasing.notified().await;
                Ok(())
            })
            .await
    });
    entered.notified().await;
    let worker = runtime.clone();
    let starting_server = server.clone();
    let preparation = tokio::spawn(async move {
        worker
            .prepare_history_thumbnails_with(&starting_server, || installation("/A"))
            .await
    });
    for _ in 0..8 {
        tokio::task::yield_now().await;
    }
    assert!(!preparation.is_finished());
    assert!(!runtime.snapshot().running);
    assert_eq!(server.starts.load(Ordering::SeqCst), 1);
    release.notify_one();
    maintenance.await.unwrap().unwrap();
    preparation.await.unwrap().unwrap();
    assert!(runtime.snapshot().running);
    assert_eq!(server.starts.load(Ordering::SeqCst), 2);
    runtime.stop().await.unwrap();
}

#[tokio::test]
async fn history_preparation_keeps_start_errors_visible_to_stream_status() {
    let runtime = StreamRuntime::default();
    let server = TestServer::failing("port occupied");
    assert_eq!(
        runtime
            .prepare_history_thumbnails_with(&server, || installation("/A"))
            .await
            .unwrap_err(),
        "port occupied"
    );
    assert!(!runtime.snapshot().running);
    assert_eq!(
        runtime.snapshot().last_error.as_deref(),
        Some("port occupied")
    );
}

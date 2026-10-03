//! HTTP goldens for the stream service: requests go through the production
//! `router` with `tower::ServiceExt::oneshot` against a fixture database, the
//! committed screenshots in `tests/goldens/http/fixtures/` and a temporary
//! cache directory.
//!
//! Writes `tests/goldens/http/records.json`, `records-latest.json` and
//! `strip-hashes.json` (the sha256 of every rendered strip PNG). Regenerate with
//! `BPP_UPDATE_GOLDENS=1 npm run generate:bindings:test` (see the installer `AGENTS.md`);
//! an `image` crate upgrade can change the PNG bytes, which needs a regenerate
//! and a note in the pull request.

use super::history_thumbnails::{self, HistoryThumbnails};
use super::http::router;
use super::overlay_settings::OverlaySettingsStore;
use super::records::OverlayRecordRepository;
use super::runtime::StreamRuntime;
use crate::goldens::{assert_golden, golden_path, json};
use crate::history::test_schema::create_history_schema;
use crate::services::paths;
use sha2::{Digest, Sha256};
use tower::ServiceExt;

/// One screenshot per subtitle shape: wins and battles, wins only, battles only
/// (its image file is missing, so it has no strip), and neither.
fn seed(game: &std::path::Path) {
    let screenshots = paths::screenshots_dir(game);
    std::fs::create_dir_all(screenshots.join("2026-01-01")).unwrap();
    for name in ["run-a.png", "run-b.png"] {
        std::fs::copy(
            golden_path(&format!("http/fixtures/{name}")),
            screenshots.join("2026-01-01").join(name),
        )
        .unwrap();
    }
    let conn = rusqlite::Connection::open(paths::database_path(game)).unwrap();
    create_history_schema(&conn);
    conn.execute_batch(
        "
        insert into run_screenshots (
            screenshot_id, run_id, hero_name, capture_source, is_primary, image_relative_path,
            captured_at_utc, captured_at_local, day, victories_at_capture, player_rank,
            player_rating
        ) values
            ('shot-1', 'run-1', 'Vanessa', 'end_of_run_auto', 1, '2026-01-01/run-a.png',
             '2026-01-01T09:40:00Z', '2026-01-01T17:40:00+08:00', 14, 10, 'Gold', 1520),
            ('shot-2', 'run-2', 'Hero8', 'end_of_run_auto', 1, '2026-01-01/run-b.png',
             '2026-01-01T09:30:00Z', '2026-01-01T17:30:00+08:00', null, 7, null, null),
            ('shot-3', 'run-3', 'Mak', 'end_of_run_auto', 1, '2026-01-01/missing.png',
             '2026-01-01T09:20:00Z', '2026-01-01T17:20:00+08:00', 3, null, null, null),
            ('shot-4', 'run-4', null, 'end_of_run_auto', 1, '2026-01-01/run-a.png',
             '2026-01-01T09:10:00Z', '2026-01-01T17:10:00+08:00', null, null, null, null);
        ",
    )
    .unwrap();
}

async fn get(app: &axum::Router, uri: &str) -> (axum::http::StatusCode, Vec<u8>) {
    let response = app
        .clone()
        .oneshot(
            axum::http::Request::builder()
                .uri(uri)
                .body(axum::body::Body::empty())
                .unwrap(),
        )
        .await
        .unwrap();
    let status = response.status();
    let body = axum::body::to_bytes(response.into_body(), 16 * 1024 * 1024)
        .await
        .unwrap();
    (status, body.to_vec())
}

#[tokio::test]
async fn stream_routes_match_goldens() {
    let temp = tempfile::tempdir().unwrap();
    let game = temp.path().join("The Bazaar");
    seed(&game);
    let thumbnails = HistoryThumbnails::default();
    let source = thumbnails.register(&game);
    let app = router(
        OverlayRecordRepository::new(Some(game.clone())),
        StreamRuntime::default(),
        thumbnails,
        OverlaySettingsStore::new(temp.path().join("settings.json"), None),
        temp.path().join("cache"),
    );
    let roots = [(game.as_path(), "<GAME>")];

    for (uri, golden) in [
        ("/api/stream/records", "http/records.json"),
        ("/api/stream/records/latest", "http/records-latest.json"),
    ] {
        let (status, body) = get(&app, uri).await;
        assert_eq!(status, axum::http::StatusCode::OK, "{uri}");
        let value: serde_json::Value = serde_json::from_slice(&body).unwrap();
        assert_golden(golden, &json(&value, &roots));
    }

    let mut hashes = Vec::new();
    for record in ["shot-1", "shot-2", "shot-3", "shot-4"] {
        let thumbnail = history_thumbnails::url(source, record);
        let thumbnail = &thumbnail[thumbnail.find("/history/").unwrap()..];
        for (route, uri) in [
            (
                "/images/{record_id}/strip",
                format!("/images/{record}/strip"),
            ),
            (history_thumbnails::ROUTE, thumbnail.to_string()),
        ] {
            let (status, body) = get(&app, &uri).await;
            let sha256 = (status == axum::http::StatusCode::OK).then(|| {
                Sha256::digest(&body)
                    .iter()
                    .map(|byte| format!("{byte:02x}"))
                    .collect::<String>()
            });
            hashes.push(serde_json::json!({
                "route": route,
                "record": record,
                "status": status.as_u16(),
                "sha256": sha256,
            }));
        }
    }
    assert_golden("http/strip-hashes.json", &json(&hashes, &roots));
}

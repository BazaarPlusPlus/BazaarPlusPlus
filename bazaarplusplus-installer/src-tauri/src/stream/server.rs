use super::{
    http,
    overlay_settings::OverlaySettingsStore,
    records::OverlayRecordRepository,
    runtime::{
        StartFuture, StartedStream, StreamInstallation, StreamRuntime, StreamServerAdapter,
        StreamTaskHandle,
    },
    state::{StreamDbStatus, StreamServiceStatus, StreamWindowStatus},
};
use crate::services::paths;
use chrono::{Local, SecondsFormat};
use std::path::PathBuf;
use tokio::{net::TcpListener, sync::oneshot};

const HOST: &str = "127.0.0.1";
const PREFERRED_PORT: u16 = 17654;

pub(super) struct ProductionServer;

impl StreamServerAdapter for ProductionServer {
    fn start(&self, runtime: StreamRuntime, installation: StreamInstallation) -> StartFuture<'_> {
        Box::pin(async move {
            let listener = bind_listener(HOST, PREFERRED_PORT).await?;
            let urls = service_urls(HOST, PREFERRED_PORT);
            let started_at = current_timestamp();
            let active_record_game_path = installation.record_game_path.clone();
            let overlay_record_repository =
                OverlayRecordRepository::new(installation.record_game_path);
            let db = stream_db_status(installation.game_path.as_ref());
            let window = stream_window_status(&overlay_record_repository, Some(&started_at));
            let overlay_settings = OverlaySettingsStore::default();
            let router = http::router(overlay_record_repository, runtime, overlay_settings);
            let (shutdown_tx, shutdown_rx) = oneshot::channel();

            let join_handle = tauri::async_runtime::spawn(async move {
                let server = axum::serve(listener, router).with_graceful_shutdown(async move {
                    let _ = shutdown_rx.await;
                });

                if let Err(err) = server.await {
                    eprintln!("stream service stopped with error: {err}");
                }
            });

            Ok(StartedStream {
                status: StreamServiceStatus {
                    running: true,
                    host: HOST.to_string(),
                    port: Some(PREFERRED_PORT),
                    base_url: Some(urls.base_url),
                    overlay_url: Some(urls.overlay_url),
                    settings_url: Some(urls.settings_url),
                    last_error: None,
                    started_at: Some(started_at.clone()),
                    active_from: Some(started_at),
                    active_window_offset: 0,
                    db,
                    window,
                },
                task: StreamTaskHandle {
                    shutdown: shutdown_tx,
                    join_handle,
                },
                active_installation_path: installation.game_path,
                active_record_game_path,
            })
        })
    }
}

fn stream_db_status(game_path: Option<&PathBuf>) -> StreamDbStatus {
    let Some(game_path) = game_path else {
        return StreamDbStatus::default();
    };
    let database_path = paths::database_path(game_path);
    StreamDbStatus {
        found: database_path.exists(),
        path: Some(database_path.to_string_lossy().into_owned()),
    }
}

fn stream_window_status(
    repository: &OverlayRecordRepository,
    started_at: Option<&str>,
) -> StreamWindowStatus {
    let total_records = repository.count_since(None).unwrap_or(0);
    let captured_since_start = repository.count_since(started_at).unwrap_or(0);
    let existing_before_start = total_records.saturating_sub(captured_since_start);
    let current = repository
        .load_record_at_offset(started_at, 0)
        .ok()
        .flatten();

    StreamWindowStatus {
        total_records,
        existing_before_start,
        captured_since_start,
        current_hero: current.as_ref().map(|record| record.title.clone()),
        current_start_label: current.map(|record| record.captured_at),
    }
}

struct ServiceUrls {
    base_url: String,
    overlay_url: String,
    settings_url: String,
}

fn service_urls(host: &str, port: u16) -> ServiceUrls {
    let base_url = format!("http://{host}:{port}");
    ServiceUrls {
        overlay_url: format!("{base_url}/overlay"),
        settings_url: format!("{base_url}/settings"),
        base_url,
    }
}

async fn bind_listener(host: &str, port: u16) -> Result<TcpListener, String> {
    TcpListener::bind((host, port)).await.map_err(|err| {
        format!(
            "OBS overlay port {port} is unavailable on {host}. Free that port and try again. ({err})"
        )
    })
}

fn current_timestamp() -> String {
    Local::now().to_rfc3339_opts(SecondsFormat::Secs, false)
}

#[cfg(test)]
mod tests {
    use super::{bind_listener, service_urls, HOST, PREFERRED_PORT};
    use tokio::net::TcpListener;

    #[tokio::test]
    async fn bind_listener_returns_error_when_port_is_occupied() {
        let occupied = TcpListener::bind(("127.0.0.1", 0)).await.unwrap();
        let occupied_port = occupied.local_addr().unwrap().port();

        let error = bind_listener("127.0.0.1", occupied_port).await.unwrap_err();

        assert!(error.contains("OBS overlay port"));
    }

    #[test]
    fn production_service_urls_remain_stable() {
        assert_eq!(HOST, "127.0.0.1");
        assert_eq!(PREFERRED_PORT, 17654);
        let urls = service_urls(HOST, PREFERRED_PORT);

        assert_eq!(urls.base_url, "http://127.0.0.1:17654");
        assert_eq!(urls.overlay_url, "http://127.0.0.1:17654/overlay");
        assert_eq!(urls.settings_url, "http://127.0.0.1:17654/settings");
    }
}

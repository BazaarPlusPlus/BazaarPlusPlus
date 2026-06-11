use super::{
    http,
    overlay_settings::OverlaySettingsStore,
    records::OverlayRecordRepository,
    state::{
        StreamDbStatus, StreamRuntimeState, StreamServiceStatus, StreamTaskHandle,
        StreamWindowStatus,
    },
};
use crate::services::game_path::{resolve_game_path_with_database, resolve_game_path_with_source};
use crate::services::paths;
use chrono::{Local, SecondsFormat};
use std::path::PathBuf;
use tokio::{net::TcpListener, sync::oneshot};

const HOST: &str = "127.0.0.1";
const PREFERRED_PORT: u16 = 17654;

pub async fn start(
    app: tauri::AppHandle,
    state: &StreamRuntimeState,
    requested_game_path: Option<PathBuf>,
) -> Result<StreamServiceStatus, String> {
    let snapshot = state.snapshot();
    if state.is_running_for_game_path(requested_game_path.as_deref()) {
        return Ok(snapshot);
    }
    if snapshot.running {
        stop(state).await?;
    }

    state.clear_error();

    let listener = match bind_listener(HOST, PREFERRED_PORT).await {
        Ok(listener) => listener,
        Err(err) => {
            state.set_error(err.clone());
            return Err(err);
        }
    };
    let urls = service_urls(HOST, PREFERRED_PORT);
    let status_with_start = state.mark_started(current_timestamp());
    let requested_game_path = requested_game_path.map(|path| path.to_string_lossy().into_owned());
    let game_resolution = resolve_game_path_with_source(&app, requested_game_path.clone(), None);
    let record_resolution = game_resolution
        .as_ref()
        .filter(|resolution| resolution.database_path.is_some())
        .cloned()
        .or_else(|| resolve_game_path_with_database(&app, requested_game_path, None));
    let game_path = game_resolution
        .as_ref()
        .map(|resolution| resolution.game_path.clone());
    let record_game_path = record_resolution
        .as_ref()
        .map(|resolution| resolution.game_path.clone());
    let overlay_record_repository = OverlayRecordRepository::new(record_game_path);
    let db = stream_db_status(game_path.as_ref());
    let window = stream_window_status(
        &overlay_record_repository,
        status_with_start.started_at.as_deref(),
    );
    let overlay_settings = OverlaySettingsStore::default();
    let router = http::router(overlay_record_repository, state.clone(), overlay_settings);
    let (shutdown_tx, shutdown_rx) = oneshot::channel();

    let join_handle = tauri::async_runtime::spawn(async move {
        let server = axum::serve(listener, router).with_graceful_shutdown(async move {
            let _ = shutdown_rx.await;
        });

        if let Err(err) = server.await {
            eprintln!("stream service stopped with error: {err}");
        }
    });

    let status = StreamServiceStatus {
        running: true,
        host: HOST.to_string(),
        port: Some(PREFERRED_PORT),
        base_url: Some(urls.base_url),
        overlay_url: Some(urls.overlay_url),
        settings_url: Some(urls.settings_url),
        last_error: None,
        started_at: status_with_start.started_at,
        active_from: status_with_start.active_from,
        active_window_offset: status_with_start.active_window_offset,
        db,
        window,
    };
    state.set_running(
        status.clone(),
        StreamTaskHandle {
            shutdown: shutdown_tx,
            join_handle,
        },
        game_path.clone(),
    );

    Ok(status)
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

pub async fn stop(state: &StreamRuntimeState) -> Result<StreamServiceStatus, String> {
    if let Some(task) = state.take_task() {
        let _ = task.shutdown.send(());
        let _ = task.join_handle.await;
    }

    Ok(state.set_idle())
}

pub async fn restart(
    app: tauri::AppHandle,
    state: &StreamRuntimeState,
    requested_game_path: Option<PathBuf>,
) -> Result<StreamServiceStatus, String> {
    stop(state).await?;
    start(app, state, requested_game_path).await
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
    use super::{bind_listener, service_urls};
    use tokio::net::TcpListener;

    #[tokio::test]
    async fn bind_listener_returns_error_when_port_is_occupied() {
        let occupied = TcpListener::bind(("127.0.0.1", 0)).await.unwrap();
        let occupied_port = occupied.local_addr().unwrap().port();

        let error = bind_listener("127.0.0.1", occupied_port).await.unwrap_err();

        assert!(error.contains("OBS overlay port"));
    }

    #[test]
    fn service_urls_expose_base_overlay_and_settings_urls() {
        let urls = service_urls("127.0.0.1", 17654);

        assert_eq!(urls.base_url, "http://127.0.0.1:17654");
        assert_eq!(urls.overlay_url, "http://127.0.0.1:17654/overlay");
        assert_eq!(urls.settings_url, "http://127.0.0.1:17654/settings");
    }
}

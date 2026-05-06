use super::{
    http,
    overlay_settings::OverlaySettingsStore,
    path_resolution::resolve_game_path_with_fallback,
    records::OverlayRecordRepository,
    state::{StreamRuntimeState, StreamServiceStatus, StreamTaskHandle},
};
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
    if snapshot.running {
        return Ok(snapshot);
    }

    state.clear_error();

    let listener = match bind_listener(HOST, PREFERRED_PORT).await {
        Ok(listener) => listener,
        Err(err) => {
            state.set_error(err.clone());
            return Err(err);
        }
    };
    let overlay_url = format!("http://{HOST}:{PREFERRED_PORT}/overlay");
    let status_with_start = state.mark_started(current_timestamp());
    let game_path = resolve_game_path_with_fallback(
        &app,
        None,
        requested_game_path.map(|path| path.to_string_lossy().into_owned()),
    );
    let overlay_record_repository = OverlayRecordRepository::new(game_path.clone());
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
        overlay_url: Some(overlay_url),
        using_fallback_port: false,
        last_error: None,
        started_at: status_with_start.started_at,
        active_from: status_with_start.active_from,
        active_window_offset: status_with_start.active_window_offset,
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

pub async fn stop(state: &StreamRuntimeState) -> Result<StreamServiceStatus, String> {
    if let Some(task) = state.take_task() {
        let _ = task.shutdown.send(());
        let _ = task.join_handle.await;
    }

    Ok(state.set_idle())
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
    use super::bind_listener;
    use tokio::net::TcpListener;

    #[tokio::test]
    async fn bind_listener_returns_error_when_port_is_occupied() {
        let occupied = TcpListener::bind(("127.0.0.1", 0)).await.unwrap();
        let occupied_port = occupied.local_addr().unwrap().port();

        let error = bind_listener("127.0.0.1", occupied_port).await.unwrap_err();

        assert!(error.contains("OBS overlay port"));
    }
}

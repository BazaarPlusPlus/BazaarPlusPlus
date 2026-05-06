use serde::Serialize;
use std::{
    path::PathBuf,
    sync::{Arc, Mutex},
};
use tokio::sync::oneshot;

const DEFAULT_HOST: &str = "127.0.0.1";

#[derive(Clone, Debug, Serialize, ts_rs::TS)]
#[ts(export)]
pub struct StreamServiceStatus {
    pub running: bool,
    pub host: String,
    pub port: Option<u16>,
    pub overlay_url: Option<String>,
    pub using_fallback_port: bool,
    pub last_error: Option<String>,
    pub started_at: Option<String>,
    pub active_from: Option<String>,
    pub active_window_offset: usize,
}

impl Default for StreamServiceStatus {
    fn default() -> Self {
        Self {
            running: false,
            host: DEFAULT_HOST.to_string(),
            port: None,
            overlay_url: None,
            using_fallback_port: false,
            last_error: None,
            started_at: None,
            active_from: None,
            active_window_offset: 0,
        }
    }
}

pub struct StreamTaskHandle {
    pub shutdown: oneshot::Sender<()>,
    pub join_handle: tauri::async_runtime::JoinHandle<()>,
}

#[derive(Clone, Default)]
pub struct StreamRuntimeState {
    inner: Arc<Mutex<StreamRuntimeInner>>,
}

#[derive(Default)]
struct StreamRuntimeInner {
    status: StreamServiceStatus,
    task: Option<StreamTaskHandle>,
    game_path: Option<PathBuf>,
}

impl StreamRuntimeState {
    pub fn snapshot(&self) -> StreamServiceStatus {
        self.inner
            .lock()
            .expect("stream runtime poisoned")
            .status
            .clone()
    }

    pub fn get_game_path(&self) -> Option<PathBuf> {
        self.inner
            .lock()
            .expect("stream runtime poisoned")
            .game_path
            .clone()
    }

    pub fn set_running(
        &self,
        status: StreamServiceStatus,
        task: StreamTaskHandle,
        game_path: Option<PathBuf>,
    ) {
        let mut inner = self.inner.lock().expect("stream runtime poisoned");
        inner.status = status;
        inner.task = Some(task);
        inner.game_path = game_path;
    }

    pub fn mark_started(&self, started_at: String) -> StreamServiceStatus {
        let mut inner = self.inner.lock().expect("stream runtime poisoned");
        inner.status.started_at = Some(started_at);
        inner.status.active_from = inner.status.started_at.clone();
        inner.status.active_window_offset = 0;
        inner.status.clone()
    }

    pub fn set_active_window(
        &self,
        active_from: Option<String>,
        active_window_offset: usize,
    ) -> StreamServiceStatus {
        let mut inner = self.inner.lock().expect("stream runtime poisoned");
        inner.status.active_from = active_from;
        inner.status.active_window_offset = active_window_offset;
        inner.status.clone()
    }

    pub fn set_idle(&self) -> StreamServiceStatus {
        let mut inner = self.inner.lock().expect("stream runtime poisoned");
        inner.status.running = false;
        inner.status.port = None;
        inner.status.overlay_url = None;
        inner.status.using_fallback_port = false;
        inner.status.started_at = None;
        inner.status.active_from = None;
        inner.status.active_window_offset = 0;
        inner.task = None;
        inner.status.clone()
    }

    pub fn set_error(&self, message: String) {
        let mut inner = self.inner.lock().expect("stream runtime poisoned");
        inner.status.running = false;
        inner.status.port = None;
        inner.status.overlay_url = None;
        inner.status.using_fallback_port = false;
        inner.status.started_at = None;
        inner.status.active_from = None;
        inner.status.active_window_offset = 0;
        inner.status.last_error = Some(message);
        inner.task = None;
    }

    pub fn clear_error(&self) {
        let mut inner = self.inner.lock().expect("stream runtime poisoned");
        inner.status.last_error = None;
    }

    pub fn take_task(&self) -> Option<StreamTaskHandle> {
        self.inner
            .lock()
            .expect("stream runtime poisoned")
            .task
            .take()
    }
}

#[cfg(test)]
mod tests {
    use super::StreamServiceStatus;

    #[test]
    fn default_status_starts_idle_without_start_time() {
        let status = StreamServiceStatus::default();

        assert!(!status.running);
        assert!(status.started_at.is_none());
        assert!(status.active_from.is_none());
        assert_eq!(status.active_window_offset, 0);
    }

    #[test]
    fn status_can_represent_running_service() {
        let status = StreamServiceStatus {
            running: true,
            port: Some(17654),
            overlay_url: Some("http://127.0.0.1:17654/overlay".to_string()),
            started_at: Some("2026-04-11T20:00:00+08:00".to_string()),
            active_from: Some("2026-04-11T20:00:00+08:00".to_string()),
            active_window_offset: 0,
            ..StreamServiceStatus::default()
        };

        assert!(status.running);
        assert_eq!(status.port, Some(17654));
        assert_eq!(
            status.overlay_url.as_deref(),
            Some("http://127.0.0.1:17654/overlay")
        );
        assert_eq!(
            status.active_from.as_deref(),
            Some("2026-04-11T20:00:00+08:00")
        );
    }

    #[test]
    fn runtime_state_marks_started_time() {
        let state = super::StreamRuntimeState::default();

        state.mark_started("2026-04-11T21:00:00+08:00".to_string());

        let snapshot = state.snapshot();
        assert_eq!(
            snapshot.started_at.as_deref(),
            Some("2026-04-11T21:00:00+08:00")
        );
        assert_eq!(
            snapshot.active_from.as_deref(),
            Some("2026-04-11T21:00:00+08:00")
        );
        assert_eq!(snapshot.active_window_offset, 0);
    }
}

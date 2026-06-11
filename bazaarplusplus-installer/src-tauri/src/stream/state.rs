use serde::Serialize;
use std::{
    path::{Path, PathBuf},
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
    pub base_url: Option<String>,
    pub overlay_url: Option<String>,
    pub settings_url: Option<String>,
    pub last_error: Option<String>,
    pub started_at: Option<String>,
    pub active_from: Option<String>,
    pub active_window_offset: usize,
    pub db: StreamDbStatus,
    pub window: StreamWindowStatus,
}

#[derive(Clone, Debug, Default, Serialize, ts_rs::TS)]
#[ts(export)]
pub struct StreamDbStatus {
    pub found: bool,
    pub path: Option<String>,
}

#[derive(Clone, Debug, Default, Serialize, ts_rs::TS)]
#[ts(export)]
pub struct StreamWindowStatus {
    pub total_records: usize,
    pub existing_before_start: usize,
    pub captured_since_start: usize,
    pub current_hero: Option<String>,
    pub current_start_label: Option<String>,
}

impl Default for StreamServiceStatus {
    fn default() -> Self {
        Self {
            running: false,
            host: DEFAULT_HOST.to_string(),
            port: None,
            base_url: None,
            overlay_url: None,
            settings_url: None,
            last_error: None,
            started_at: None,
            active_from: None,
            active_window_offset: 0,
            db: StreamDbStatus::default(),
            window: StreamWindowStatus::default(),
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

    pub fn is_running_for_game_path(&self, requested_game_path: Option<&Path>) -> bool {
        let inner = self.inner.lock().expect("stream runtime poisoned");
        if !inner.status.running {
            return false;
        }

        match requested_game_path {
            Some(path) => inner.game_path.as_deref() == Some(path),
            None => true,
        }
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
        inner.status.base_url = None;
        inner.status.overlay_url = None;
        inner.status.settings_url = None;
        inner.status.started_at = None;
        inner.status.active_from = None;
        inner.status.active_window_offset = 0;
        inner.status.db = StreamDbStatus::default();
        inner.status.window = StreamWindowStatus::default();
        inner.task = None;
        inner.status.clone()
    }

    pub fn set_error(&self, message: String) {
        let mut inner = self.inner.lock().expect("stream runtime poisoned");
        inner.status.running = false;
        inner.status.port = None;
        inner.status.base_url = None;
        inner.status.overlay_url = None;
        inner.status.settings_url = None;
        inner.status.started_at = None;
        inner.status.active_from = None;
        inner.status.active_window_offset = 0;
        inner.status.db = StreamDbStatus::default();
        inner.status.window = StreamWindowStatus::default();
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
        assert!(status.base_url.is_none());
        assert!(status.started_at.is_none());
        assert!(status.active_from.is_none());
        assert_eq!(status.active_window_offset, 0);
    }

    #[test]
    fn status_can_represent_running_service() {
        let status = StreamServiceStatus {
            running: true,
            port: Some(17654),
            base_url: Some("http://127.0.0.1:17654".to_string()),
            overlay_url: Some("http://127.0.0.1:17654/overlay".to_string()),
            settings_url: Some("http://127.0.0.1:17654/settings".to_string()),
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
            status.settings_url.as_deref(),
            Some("http://127.0.0.1:17654/settings")
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

    #[tokio::test]
    async fn runtime_state_detects_requested_game_path_changes() {
        use super::StreamTaskHandle;
        use std::path::{Path, PathBuf};
        use tokio::sync::oneshot;

        let state = super::StreamRuntimeState::default();
        let (shutdown, shutdown_rx) = oneshot::channel();
        let join_handle = tauri::async_runtime::spawn(async move {
            let _ = shutdown_rx.await;
        });

        state.set_running(
            StreamServiceStatus {
                running: true,
                ..StreamServiceStatus::default()
            },
            StreamTaskHandle {
                shutdown,
                join_handle,
            },
            Some(PathBuf::from("/Games/The Bazaar")),
        );

        assert!(state.is_running_for_game_path(Some(Path::new("/Games/The Bazaar"))));
        assert!(!state.is_running_for_game_path(Some(Path::new("/Other/The Bazaar"))));
        assert!(state.is_running_for_game_path(None));

        if let Some(task) = state.take_task() {
            let _ = task.shutdown.send(());
            let _ = task.join_handle.await;
        }
    }
}

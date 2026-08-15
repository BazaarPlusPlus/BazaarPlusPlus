use super::{
    records::OverlayRecordRepository,
    server::ProductionServer,
    state::{StreamDbStatus, StreamServiceStatus, StreamWindowStatus},
};
use crate::services::{
    game_path::GamePathAcceptance, selected_game_installation::SelectedGameInstallationState,
};
use std::{
    future::Future,
    path::{Path, PathBuf},
    pin::Pin,
    sync::{Arc, Mutex},
};
use tauri::Manager;
use tokio::sync::{oneshot, Mutex as AsyncMutex};

pub(super) type StartFuture<'a> =
    Pin<Box<dyn Future<Output = Result<StartedStream, String>> + Send + 'a>>;

pub(super) trait StreamServerAdapter: Send + Sync {
    fn start(&self, runtime: StreamRuntime, installation: StreamInstallation) -> StartFuture<'_>;
}

#[derive(Clone, Debug)]
pub(super) struct StreamInstallation {
    pub(super) game_path: Option<PathBuf>,
    pub(super) record_game_path: Option<PathBuf>,
}

pub(super) struct StartedStream {
    pub(super) status: StreamServiceStatus,
    pub(super) task: StreamTaskHandle,
    pub(super) active_installation_path: Option<PathBuf>,
    pub(super) active_record_game_path: Option<PathBuf>,
}

pub(super) struct StreamTaskHandle {
    pub(super) shutdown: oneshot::Sender<()>,
    pub(super) join_handle: tauri::async_runtime::JoinHandle<()>,
}

#[derive(Clone, Default)]
pub(crate) struct StreamRuntime {
    lifecycle: Arc<AsyncMutex<()>>,
    inner: Arc<Mutex<StreamRuntimeInner>>,
}

#[derive(Default)]
struct StreamRuntimeInner {
    status: StreamServiceStatus,
    task: Option<StreamTaskHandle>,
    active_installation_path: Option<PathBuf>,
    active_record_game_path: Option<PathBuf>,
}

impl StreamRuntime {
    pub(crate) fn snapshot(&self) -> StreamServiceStatus {
        self.inner
            .lock()
            .expect("stream runtime poisoned")
            .status
            .clone()
    }

    pub(crate) async fn ensure(
        &self,
        app: tauri::AppHandle,
        requested_game_path: Option<PathBuf>,
    ) -> Result<StreamServiceStatus, String> {
        let _lifecycle = self.lifecycle.lock().await;
        let installation = resolve_installation(&app, requested_game_path);
        let server = ProductionServer;
        self.ensure_locked(&server, installation).await
    }

    pub(crate) async fn restart(
        &self,
        app: tauri::AppHandle,
        requested_game_path: Option<PathBuf>,
    ) -> Result<StreamServiceStatus, String> {
        let _lifecycle = self.lifecycle.lock().await;
        let installation = resolve_installation(&app, requested_game_path);
        let server = ProductionServer;
        self.restart_locked(&server, installation).await
    }

    pub(crate) async fn stop(&self) -> Result<StreamServiceStatus, String> {
        let _lifecycle = self.lifecycle.lock().await;
        self.stop_locked().await
    }

    pub(crate) async fn set_window(&self, offset: usize) -> Result<StreamServiceStatus, String> {
        self.set_window_with(|snapshot, record_game_path| {
            stream_window_for_offset(snapshot, record_game_path, offset)
        })
        .await
    }

    pub(crate) async fn exclusive_maintenance<T, F, Fut>(&self, operation: F) -> Result<T, String>
    where
        F: FnOnce() -> Fut,
        Fut: Future<Output = Result<T, String>>,
    {
        let _lifecycle = self.lifecycle.lock().await;
        self.stop_locked().await?;
        operation().await
    }

    #[cfg(test)]
    async fn ensure_with<A>(
        &self,
        server: &A,
        installation: StreamInstallation,
    ) -> Result<StreamServiceStatus, String>
    where
        A: StreamServerAdapter + ?Sized,
    {
        let _lifecycle = self.lifecycle.lock().await;
        self.ensure_locked(server, installation).await
    }

    async fn ensure_locked<A>(
        &self,
        server: &A,
        installation: StreamInstallation,
    ) -> Result<StreamServiceStatus, String>
    where
        A: StreamServerAdapter + ?Sized,
    {
        if self.is_running_for_game_path(installation.game_path.as_deref()) {
            return Ok(self.snapshot());
        }
        if self.snapshot().running {
            self.stop_locked().await?;
        }

        self.clear_error();
        match server.start(self.clone(), installation).await {
            Ok(started) => {
                let status = started.status.clone();
                self.set_running(started);
                Ok(status)
            }
            Err(message) => {
                self.set_error(message.clone());
                Err(message)
            }
        }
    }

    #[cfg(test)]
    async fn restart_with<A>(
        &self,
        server: &A,
        installation: StreamInstallation,
    ) -> Result<StreamServiceStatus, String>
    where
        A: StreamServerAdapter + ?Sized,
    {
        let _lifecycle = self.lifecycle.lock().await;
        self.restart_locked(server, installation).await
    }

    async fn restart_locked<A>(
        &self,
        server: &A,
        installation: StreamInstallation,
    ) -> Result<StreamServiceStatus, String>
    where
        A: StreamServerAdapter + ?Sized,
    {
        self.stop_locked().await?;
        self.clear_error();
        match server.start(self.clone(), installation).await {
            Ok(started) => {
                let status = started.status.clone();
                self.set_running(started);
                Ok(status)
            }
            Err(message) => {
                self.set_error(message.clone());
                Err(message)
            }
        }
    }

    async fn stop_locked(&self) -> Result<StreamServiceStatus, String> {
        let task = self
            .inner
            .lock()
            .expect("stream runtime poisoned")
            .task
            .take();
        if let Some(task) = task {
            let _ = task.shutdown.send(());
            let _ = task.join_handle.await;
        }

        Ok(self.set_idle())
    }

    async fn set_window_with<F>(&self, apply: F) -> Result<StreamServiceStatus, String>
    where
        F: FnOnce(&StreamServiceStatus, Option<PathBuf>) -> Result<(Option<String>, usize), String>,
    {
        let _lifecycle = self.lifecycle.lock().await;
        let snapshot = self.snapshot();
        if !snapshot.running {
            return Ok(snapshot);
        }
        let record_game_path = self
            .inner
            .lock()
            .expect("stream runtime poisoned")
            .active_record_game_path
            .clone();
        let (active_from, active_window_offset) = apply(&snapshot, record_game_path)?;
        Ok(self.set_active_window(active_from, active_window_offset))
    }

    fn is_running_for_game_path(&self, requested_game_path: Option<&Path>) -> bool {
        let inner = self.inner.lock().expect("stream runtime poisoned");
        if !inner.status.running {
            return false;
        }

        match requested_game_path {
            Some(path) => inner.active_installation_path.as_deref() == Some(path),
            None => true,
        }
    }

    fn set_running(&self, started: StartedStream) {
        let mut inner = self.inner.lock().expect("stream runtime poisoned");
        inner.status = started.status;
        inner.task = Some(started.task);
        inner.active_installation_path = started.active_installation_path;
        inner.active_record_game_path = started.active_record_game_path;
    }

    fn set_active_window(
        &self,
        active_from: Option<String>,
        active_window_offset: usize,
    ) -> StreamServiceStatus {
        let mut inner = self.inner.lock().expect("stream runtime poisoned");
        inner.status.active_from = active_from;
        inner.status.active_window_offset = active_window_offset;
        inner.status.clone()
    }

    fn set_idle(&self) -> StreamServiceStatus {
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
        inner.active_installation_path = None;
        inner.active_record_game_path = None;
        inner.status.clone()
    }

    fn set_error(&self, message: String) {
        let mut inner = self.inner.lock().expect("stream runtime poisoned");
        inner.status = StreamServiceStatus {
            last_error: Some(message),
            ..StreamServiceStatus::default()
        };
        inner.task = None;
        inner.active_installation_path = None;
        inner.active_record_game_path = None;
    }

    fn clear_error(&self) {
        self.inner
            .lock()
            .expect("stream runtime poisoned")
            .status
            .last_error = None;
    }

    #[cfg(test)]
    fn has_task(&self) -> bool {
        self.inner
            .lock()
            .expect("stream runtime poisoned")
            .task
            .is_some()
    }
}

fn resolve_installation(
    app: &tauri::AppHandle,
    requested_game_path: Option<PathBuf>,
) -> StreamInstallation {
    let requested_game_path = requested_game_path.map(|path| path.to_string_lossy().into_owned());
    let selected_installation = app.state::<SelectedGameInstallationState>();
    let game_resolution =
        selected_installation.resolve(app, requested_game_path.clone(), GamePathAcceptance::Any);
    let record_resolution = game_resolution
        .as_ref()
        .filter(|resolution| resolution.database_path.is_some())
        .cloned()
        .or_else(|| {
            selected_installation.resolve(
                app,
                requested_game_path,
                GamePathAcceptance::DatabaseExists,
            )
        });

    StreamInstallation {
        game_path: game_resolution.map(|resolution| resolution.game_path),
        record_game_path: record_resolution.map(|resolution| resolution.game_path),
    }
}

fn stream_window_for_offset(
    snapshot: &StreamServiceStatus,
    record_game_path: Option<PathBuf>,
    offset: usize,
) -> Result<(Option<String>, usize), String> {
    let started_at = snapshot
        .started_at
        .clone()
        .ok_or_else(|| "Stream start time is unavailable.".to_string())?;
    let repository = OverlayRecordRepository::new(record_game_path);

    if offset == 0 {
        return Ok((Some(started_at), 0));
    }

    let captured_since_start = repository.count_since(Some(started_at.as_str()))?;
    let total = repository.count_since(None)?;
    let existing_before_start = total.saturating_sub(captured_since_start);
    if offset > existing_before_start {
        return Err(format!(
            "Requested stream window offset {offset} exceeds the available {existing_before_start} earlier record(s)."
        ));
    }

    let record_offset = captured_since_start + offset - 1;
    let record = repository
        .load_record_at_offset(None, record_offset)?
        .ok_or_else(|| {
            format!("No end-of-run record is available for stream window offset {offset}.")
        })?;

    Ok((Some(record.captured_at_utc), offset))
}

#[cfg(test)]
mod tests {
    use super::{
        StartFuture, StartedStream, StreamInstallation, StreamRuntime, StreamServerAdapter,
        StreamTaskHandle,
    };
    use crate::stream::state::StreamServiceStatus;
    use std::{
        path::PathBuf,
        sync::{
            atomic::{AtomicUsize, Ordering},
            Arc,
        },
    };
    use tokio::sync::{oneshot, Notify};

    #[derive(Clone, Default)]
    struct TestServer {
        starts: Arc<AtomicUsize>,
        stops: Arc<AtomicUsize>,
        failure: Option<String>,
    }

    impl TestServer {
        fn failing(message: &str) -> Self {
            Self {
                failure: Some(message.to_string()),
                ..Self::default()
            }
        }
    }

    impl StreamServerAdapter for TestServer {
        fn start(
            &self,
            _runtime: StreamRuntime,
            installation: StreamInstallation,
        ) -> StartFuture<'_> {
            Box::pin(async move {
                self.starts.fetch_add(1, Ordering::SeqCst);
                if let Some(message) = &self.failure {
                    return Err(message.clone());
                }

                let (shutdown, shutdown_rx) = oneshot::channel();
                let stops = self.stops.clone();
                let join_handle = tauri::async_runtime::spawn(async move {
                    let _ = shutdown_rx.await;
                    stops.fetch_add(1, Ordering::SeqCst);
                });
                let started_at = "2026-07-18T10:00:00+08:00".to_string();
                Ok(StartedStream {
                    status: StreamServiceStatus {
                        running: true,
                        port: Some(17654),
                        base_url: Some("http://127.0.0.1:17654".to_string()),
                        overlay_url: Some("http://127.0.0.1:17654/overlay".to_string()),
                        settings_url: Some("http://127.0.0.1:17654/settings".to_string()),
                        started_at: Some(started_at.clone()),
                        active_from: Some(started_at),
                        ..StreamServiceStatus::default()
                    },
                    task: StreamTaskHandle {
                        shutdown,
                        join_handle,
                    },
                    active_installation_path: installation.game_path,
                    active_record_game_path: installation.record_game_path,
                })
            })
        }
    }

    fn installation(path: &str) -> StreamInstallation {
        StreamInstallation {
            game_path: Some(PathBuf::from(path)),
            record_game_path: Some(PathBuf::from(path)),
        }
    }

    #[tokio::test]
    async fn stream_runtime_concurrent_ensure_starts_exactly_one_task() {
        let runtime = StreamRuntime::default();
        let server = TestServer::default();
        let requested = installation("/Games/The Bazaar");

        let (first, second) = tokio::join!(
            runtime.ensure_with(&server, requested.clone()),
            runtime.ensure_with(&server, requested),
        );

        assert!(first.unwrap().running);
        assert!(second.unwrap().running);
        assert_eq!(server.starts.load(Ordering::SeqCst), 1);
        runtime.stop().await.unwrap();
        assert_eq!(server.stops.load(Ordering::SeqCst), 1);
    }

    #[tokio::test]
    async fn stream_runtime_lifecycle_transitions_keep_snapshot_and_task_consistent() {
        let runtime = StreamRuntime::default();
        let server = TestServer::default();
        let requested = installation("/Games/The Bazaar");

        runtime
            .ensure_with(&server, requested.clone())
            .await
            .unwrap();
        assert!(runtime.snapshot().running);

        runtime
            .set_window_with(|status, record_game_path| {
                assert!(status.running);
                assert_eq!(
                    record_game_path.as_deref(),
                    Some(std::path::Path::new("/Games/The Bazaar"))
                );
                Ok((Some("2026-07-17T10:00:00Z".to_string()), 2))
            })
            .await
            .unwrap();
        assert_eq!(runtime.snapshot().active_window_offset, 2);

        runtime.restart_with(&server, requested).await.unwrap();
        assert_eq!(server.starts.load(Ordering::SeqCst), 2);
        assert_eq!(server.stops.load(Ordering::SeqCst), 1);
        assert_eq!(runtime.snapshot().active_window_offset, 0);

        let stopped = runtime.stop().await.unwrap();
        assert!(!stopped.running);
        assert_eq!(server.stops.load(Ordering::SeqCst), 2);
        assert!(!runtime.has_task());
    }

    #[tokio::test]
    async fn stream_runtime_failed_start_leaves_an_idle_runtime_without_a_task() {
        let runtime = StreamRuntime::default();
        let server = TestServer::failing("bind failed");

        let error = runtime
            .ensure_with(&server, installation("/Games/The Bazaar"))
            .await
            .unwrap_err();

        assert_eq!(error, "bind failed");
        assert!(!runtime.snapshot().running);
        assert_eq!(
            runtime.snapshot().last_error.as_deref(),
            Some("bind failed")
        );
        assert!(!runtime.has_task());
    }

    #[tokio::test]
    async fn stream_runtime_exclusive_maintenance_blocks_lifecycle_calls_and_does_not_resume() {
        let runtime = StreamRuntime::default();
        let server = TestServer::default();
        let requested = installation("/Games/The Bazaar");
        runtime
            .ensure_with(&server, requested.clone())
            .await
            .unwrap();

        let entered = Arc::new(Notify::new());
        let release = Arc::new(Notify::new());
        let maintenance_runtime = runtime.clone();
        let maintenance_entered = entered.clone();
        let maintenance_release = release.clone();
        let maintenance = tokio::spawn(async move {
            maintenance_runtime
                .exclusive_maintenance(|| async move {
                    maintenance_entered.notify_one();
                    maintenance_release.notified().await;
                    Ok(())
                })
                .await
        });
        entered.notified().await;
        assert!(!runtime.snapshot().running);

        let ensure_runtime = runtime.clone();
        let ensure_server = server.clone();
        let ensure_requested = requested.clone();
        let ensure = tokio::spawn(async move {
            ensure_runtime
                .ensure_with(&ensure_server, ensure_requested)
                .await
        });
        let restart_runtime = runtime.clone();
        let restart_server = server.clone();
        let restart = tokio::spawn(async move {
            restart_runtime
                .restart_with(&restart_server, requested)
                .await
        });
        let window_runtime = runtime.clone();
        let window = tokio::spawn(async move { window_runtime.set_window(1).await });
        for _ in 0..8 {
            tokio::task::yield_now().await;
        }
        assert_eq!(server.starts.load(Ordering::SeqCst), 1);
        assert!(!ensure.is_finished());
        assert!(!restart.is_finished());
        assert!(!window.is_finished());

        ensure.abort();
        restart.abort();
        window.abort();
        release.notify_one();
        maintenance.await.unwrap().unwrap();
        assert!(!runtime.snapshot().running);
        assert!(!runtime.has_task());
    }
}

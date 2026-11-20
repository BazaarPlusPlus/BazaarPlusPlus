use anyhow::Context as _;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::Arc;

use tokio::sync::Mutex as AsyncMutex;

use crate::stream::runtime::StreamRuntime;

/// Acquire the data maintenance lock before `StreamRuntime::exclusive_maintenance` to avoid deadlock.
#[derive(Clone, Default)]
pub(crate) struct DataMaintenanceLock {
    gate: Arc<AsyncMutex<()>>,
    database_generation: Arc<AtomicU64>,
}

impl DataMaintenanceLock {
    /// Move the guard into the blocking task so cancellation of the caller cannot release it early.
    pub(crate) async fn run_blocking<T, F>(&self, task: F) -> anyhow::Result<T>
    where
        T: Send + 'static,
        F: FnOnce() -> T + Send + 'static,
    {
        let guard = self.gate.clone().lock_owned().await;
        run_on_blocking_pool(guard, task).await
    }

    /// Advance the generation on both edges so previews made during Reset also become stale.
    pub(crate) async fn run_blocking_with_overlay_stopped<T, F>(
        &self,
        stream_runtime: &StreamRuntime,
        task: F,
    ) -> anyhow::Result<T>
    where
        T: Send + 'static,
        F: FnOnce() -> T + Send + 'static,
    {
        let guard = self.gate.clone().lock_owned().await;
        self.database_generation.fetch_add(1, Ordering::SeqCst);
        let ended = AdvanceOnDrop(self.database_generation.clone());
        let task = move || {
            // Dropped when the task returns or unwinds, while the lock is held.
            let _ended = ended;
            task()
        };
        stream_runtime
            .exclusive_maintenance(|| run_on_blocking_pool(guard, task))
            .await
    }

    /// Read before deriving database state and compare again under the lock to detect a concurrent Reset.
    pub(crate) fn database_generation(&self) -> u64 {
        self.database_generation.load(Ordering::SeqCst)
    }
}

struct AdvanceOnDrop(Arc<AtomicU64>);

impl Drop for AdvanceOnDrop {
    fn drop(&mut self) {
        self.0.fetch_add(1, Ordering::SeqCst);
    }
}

async fn run_on_blocking_pool<T, F>(
    guard: tokio::sync::OwnedMutexGuard<()>,
    task: F,
) -> anyhow::Result<T>
where
    T: Send + 'static,
    F: FnOnce() -> T + Send + 'static,
{
    tauri::async_runtime::spawn_blocking(move || {
        let _guard = guard;
        task()
    })
    .await
    .context("data maintenance task failed")
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::services::history::{
        execute_storage_cleanup_with, StorageCleanupPreset, StorageCleanupScope,
    };
    use std::path::Path;
    use std::sync::mpsc;
    use std::time::Duration;

    fn game_dir_with_data() -> tempfile::TempDir {
        let tmp = tempfile::tempdir().unwrap();
        #[cfg(target_os = "macos")]
        std::fs::create_dir_all(tmp.path().join("TheBazaar.app")).unwrap();
        #[cfg(target_os = "windows")]
        std::fs::write(tmp.path().join("TheBazaar.exe"), b"exe").unwrap();
        #[cfg(not(any(target_os = "macos", target_os = "windows")))]
        std::fs::write(tmp.path().join("TheBazaar"), b"exe").unwrap();
        let data_dir = crate::services::paths::bpp_data_dir(tmp.path());
        std::fs::create_dir_all(&data_dir).unwrap();
        std::fs::write(crate::services::paths::database_path(tmp.path()), b"db").unwrap();
        tmp
    }

    #[tokio::test]
    async fn cleanup_started_during_reset_waits_until_the_reset_finishes() {
        let lock = DataMaintenanceLock::default();
        let stream_runtime = StreamRuntime::default();
        let game = game_dir_with_data();
        let game_path = game.path().to_path_buf();
        let data_dir = crate::services::paths::bpp_data_dir(&game_path);

        let (probe_entered_tx, probe_entered) = mpsc::channel::<()>();
        let (release_probe, probe_release) = mpsc::channel::<()>();
        let probe_release = std::sync::Mutex::new(probe_release);
        let probe = move |_: &Path| {
            probe_entered_tx.send(()).unwrap();
            probe_release.lock().unwrap().recv().unwrap();
            Ok(false)
        };
        let reset = tokio::spawn({
            let lock = lock.clone();
            let stream_runtime = stream_runtime.clone();
            let game_path = game_path.to_string_lossy().into_owned();
            async move {
                crate::services::bepinex::reset_bpp_data_with(
                    &lock,
                    &stream_runtime,
                    game_path,
                    probe,
                )
                .await
            }
        });
        tokio::task::spawn_blocking(move || probe_entered.recv_timeout(Duration::from_secs(10)))
            .await
            .unwrap()
            .expect("reset reached its game check");

        let (resolved_tx, resolved) = mpsc::channel::<bool>();
        let cleanup = tokio::spawn({
            let lock = lock.clone();
            let game_path = game_path.clone();
            let data_dir = data_dir.clone();
            async move {
                execute_storage_cleanup_with(
                    &lock,
                    Default::default(),
                    move || {
                        resolved_tx.send(data_dir.exists()).unwrap();
                        Some(game_path)
                    },
                    StorageCleanupScope::RunData,
                    StorageCleanupPreset::All,
                    0,
                )
                .await
            }
        });
        for _ in 0..16 {
            tokio::task::yield_now().await;
        }
        tokio::time::sleep(Duration::from_millis(50)).await;
        assert!(!cleanup.is_finished());
        assert!(resolved.try_recv().is_err(), "cleanup entered during reset");

        release_probe.send(()).unwrap();
        assert_eq!(reset.await.unwrap(), Ok(true));
        let _ = cleanup.await.unwrap();
        assert_eq!(resolved.try_recv(), Ok(false));
    }
}

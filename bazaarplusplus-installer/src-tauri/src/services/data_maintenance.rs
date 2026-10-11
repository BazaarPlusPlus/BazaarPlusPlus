use std::sync::Arc;

use tokio::sync::Mutex as AsyncMutex;

use crate::stream::runtime::StreamRuntime;

/// The installer's process-wide data maintenance lock. Every operation that
/// deletes or rewrites user data under a game directory (History cleanup,
/// Reset, BepInEx reset, Legacy Root deletion) runs while holding it, so two of
/// them never interleave.
///
/// Lock order: this lock first, then the stream lifecycle gate inside
/// [`StreamRuntime::exclusive_maintenance`]. Nothing that holds the lifecycle
/// gate waits for this lock, so a long cleanup never stalls overlay start and
/// stop, and the two cannot deadlock.
#[derive(Clone, Default)]
pub(crate) struct DataMaintenanceLock {
    gate: Arc<AsyncMutex<()>>,
}

impl DataMaintenanceLock {
    /// Runs `task` on the blocking pool while holding the lock. The guard moves
    /// into the task, so the lock outlives a dropped caller until the
    /// filesystem work actually finishes.
    pub(crate) async fn run_blocking<T, F>(&self, task: F) -> Result<T, String>
    where
        T: Send + 'static,
        F: FnOnce() -> T + Send + 'static,
    {
        let guard = self.gate.clone().lock_owned().await;
        run_on_blocking_pool(guard, task).await
    }

    /// Like [`Self::run_blocking`], and also stops the OBS overlay for the
    /// duration, for work that deletes the database the overlay reads.
    pub(crate) async fn run_blocking_with_overlay_stopped<T, F>(
        &self,
        stream_runtime: &StreamRuntime,
        task: F,
    ) -> Result<T, String>
    where
        T: Send + 'static,
        F: FnOnce() -> T + Send + 'static,
    {
        let guard = self.gate.clone().lock_owned().await;
        stream_runtime
            .exclusive_maintenance(|| run_on_blocking_pool(guard, task))
            .await
    }
}

async fn run_on_blocking_pool<T, F>(
    guard: tokio::sync::OwnedMutexGuard<()>,
    task: F,
) -> Result<T, String>
where
    T: Send + 'static,
    F: FnOnce() -> T + Send + 'static,
{
    tauri::async_runtime::spawn_blocking(move || {
        let _guard = guard;
        task()
    })
    .await
    .map_err(|err| format!("data maintenance task failed: {err}"))
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

        // The reset's process probe parks inside the locked section until
        // released, holding the reset mid-operation.
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

        // Cleanup resolves its installation only once it holds the lock, so the
        // data directory it observes tells whether the reset already finished.
        let (resolved_tx, resolved) = mpsc::channel::<bool>();
        let cleanup = tokio::spawn({
            let lock = lock.clone();
            let game_path = game_path.clone();
            let data_dir = data_dir.clone();
            async move {
                execute_storage_cleanup_with(
                    &lock,
                    move || {
                        resolved_tx.send(data_dir.exists()).unwrap();
                        Some(game_path)
                    },
                    StorageCleanupScope::RunData,
                    StorageCleanupPreset::All,
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
        // The cleanup ran after the reset removed the data, so it finds no
        // history; what matters is that it observed the finished reset.
        let _ = cleanup.await.unwrap();
        assert_eq!(resolved.try_recv(), Ok(false));
    }
}

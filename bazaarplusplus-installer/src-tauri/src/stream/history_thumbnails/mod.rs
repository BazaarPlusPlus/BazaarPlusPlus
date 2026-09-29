//! History Thumbnails: the cropped strip of a run's screenshot on a History
//! List card. This module owns the process-local source registry, the only
//! thumbnail URL builder, the image route, and the per-installation cache.

use super::{
    http::{png_response, run_record_task},
    overlay_settings::OverlaySettingsStore,
    records::resolve_overlay_image_path,
    server,
    strip::{cached_strip, TEMP_SUFFIX},
};
use crate::{history::screenshots::load_screenshot_image_path, services::paths};
use axum::{
    extract::{Path as RoutePath, State},
    http::StatusCode,
    response::{IntoResponse, Response},
    routing::get,
    Router,
};
use sha2::{Digest, Sha256};
use std::{
    path::{Path, PathBuf},
    sync::{Arc, Mutex, OnceLock},
    time::{Duration, SystemTime, UNIX_EPOCH},
};

#[cfg(test)]
mod tests;

pub(super) const ROUTE: &str = "/history/{source}/images/{screenshot_id}/strip";
/// Beneath the service cache root; the OBS strips at that root stay unswept.
const CACHE_DIRECTORY: &str = "history-thumbnails";
/// Where History strips were cached per source file before this namespace.
const LEGACY_CACHE_DIRECTORY: &str = "history";
const CACHE_LIMITS: CacheLimits = CacheLimits {
    unused_for: Duration::from_secs(30 * 24 * 60 * 60),
    max_entries: 1000,
    fresh_for: Duration::from_secs(10),
};

/// An opaque handle for one registered installation path. It stays valid, and
/// names the same path, until the installer exits.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(crate) struct ThumbnailSource(usize);

#[derive(Clone, Default)]
pub(crate) struct HistoryThumbnails {
    sources: Arc<Mutex<Vec<PathBuf>>>,
}

impl HistoryThumbnails {
    /// Registers the installation a History page was read from. The path is
    /// stored as given and its database is resolved on each request, so a
    /// database that appears after startup is still found.
    pub(crate) fn register(&self, game_path: &Path) -> ThumbnailSource {
        let mut sources = self.sources.lock().expect("thumbnail sources poisoned");
        let index = match sources.iter().position(|path| path == game_path) {
            Some(index) => index,
            None => {
                sources.push(game_path.to_path_buf());
                sources.len() - 1
            }
        };
        ThumbnailSource(index)
    }

    fn game_path(&self, source: usize) -> Option<PathBuf> {
        self.sources
            .lock()
            .expect("thumbnail sources poisoned")
            .get(source)
            .cloned()
    }
}

/// The absolute URL of a screenshot's History Thumbnail.
pub(crate) fn url(source: ThumbnailSource, screenshot_id: &str) -> String {
    let route = ROUTE
        .replace("{source}", &source.0.to_string())
        .replace("{screenshot_id}", screenshot_id);
    format!("{}{route}", server::origin())
}

#[derive(Clone)]
struct RouteState {
    thumbnails: HistoryThumbnails,
    settings: OverlaySettingsStore,
    cache_root: PathBuf,
}

pub(super) fn router(
    thumbnails: HistoryThumbnails,
    settings: OverlaySettingsStore,
    cache_root: PathBuf,
) -> Router {
    Router::new()
        .route(ROUTE, get(thumbnail))
        .with_state(RouteState {
            thumbnails,
            settings,
            cache_root,
        })
}

async fn thumbnail(
    RoutePath((source, screenshot_id)): RoutePath<(usize, String)>,
    State(state): State<RouteState>,
) -> Response {
    let Some(game_path) = state.thumbnails.game_path(source) else {
        return StatusCode::NOT_FOUND.into_response();
    };
    let RouteState {
        settings,
        cache_root,
        ..
    } = state;
    match run_record_task(move || render(&game_path, &screenshot_id, &settings, &cache_root)).await
    {
        Ok(Some(bytes)) => png_response(bytes),
        Ok(None) => StatusCode::NOT_FOUND.into_response(),
        Err(message) => (StatusCode::INTERNAL_SERVER_ERROR, message).into_response(),
    }
}

/// `None` when the database, the screenshot row or its image is missing.
fn render(
    game_path: &Path,
    screenshot_id: &str,
    settings: &OverlaySettingsStore,
    cache_root: &Path,
) -> Result<Option<Vec<u8>>, String> {
    let crop = settings.load()?.crop;
    let stored = load_screenshot_image_path(&paths::database_path(game_path), screenshot_id)?;
    let Some(image_path) =
        resolve_overlay_image_path(Some(game_path.to_path_buf()), stored.as_deref())
            .filter(|path| path.exists())
    else {
        return Ok(None);
    };
    cached_strip(
        &cache_directory(cache_root, game_path),
        screenshot_id,
        &image_path,
        crop,
    )
    .map(Some)
}

/// One directory per installation path, stable across restarts.
fn cache_directory(cache_root: &Path, game_path: &Path) -> PathBuf {
    let digest = Sha256::digest(game_path.as_os_str().as_encoded_bytes());
    let namespace = digest[..8]
        .iter()
        .map(|byte| format!("{byte:02x}"))
        .collect::<String>();
    cache_root.join(CACHE_DIRECTORY).join(namespace)
}

/// Sweeps the History Thumbnail cache once per process, off the async runtime.
pub(super) fn sweep_cache() {
    static SWEPT: OnceLock<()> = OnceLock::new();
    if SWEPT.set(()).is_err() {
        return;
    }
    let cache_root = paths::overlay_cache_dir();
    tauri::async_runtime::spawn_blocking(move || {
        sweep(&cache_root, SystemTime::now(), &CACHE_LIMITS);
    });
}

struct CacheLimits {
    /// An entry not read or written for longer is removed.
    unused_for: Duration,
    /// The oldest entries beyond this count are removed.
    max_entries: usize,
    /// Entries this recent may be in use and are left alone.
    fresh_for: Duration,
}

/// Every deletion is best effort: Windows refuses to remove an open file.
/// Namespace directories stay, so a concurrent write never loses its parent.
fn sweep(cache_root: &Path, now: SystemTime, limits: &CacheLimits) {
    let _ = std::fs::remove_dir_all(cache_root.join(LEGACY_CACHE_DIRECTORY));

    let Ok(namespaces) = std::fs::read_dir(cache_root.join(CACHE_DIRECTORY)) else {
        return;
    };
    let mut kept = Vec::new();
    for namespace in namespaces.flatten() {
        let Ok(files) = std::fs::read_dir(namespace.path()) else {
            continue;
        };
        for file in files.flatten() {
            let Ok(metadata) = file.metadata() else {
                continue;
            };
            if !metadata.is_file() {
                continue;
            }
            let modified = metadata.modified().unwrap_or(UNIX_EPOCH);
            let age = now.duration_since(modified).unwrap_or_default();
            if age < limits.fresh_for {
                continue;
            }
            let path = file.path();
            let orphan = path.to_string_lossy().ends_with(TEMP_SUFFIX);
            if orphan || age > limits.unused_for {
                let _ = std::fs::remove_file(&path);
            } else {
                kept.push((modified, path));
            }
        }
    }

    if kept.len() > limits.max_entries {
        kept.sort_by_key(|(modified, _)| *modified);
        let excess = kept.len() - limits.max_entries;
        for (_, path) in kept.into_iter().take(excess) {
            let _ = std::fs::remove_file(path);
        }
    }
}

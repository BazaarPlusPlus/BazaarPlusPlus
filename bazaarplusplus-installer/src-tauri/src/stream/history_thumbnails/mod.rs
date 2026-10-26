//! History Thumbnails: the cropped strip of a run's screenshot on a History
//! List card. This module owns the process-local source registry, the only
//! thumbnail URL builder, the image route, and its namespace of the strip
//! cache.

use super::{
    http::{png_response, run_record_task},
    overlay_settings::OverlaySettingsStore,
    records::resolve_overlay_image_path,
    server,
    strip::{cache_namespace, cached_strip, SourcePaths, HISTORY_THUMBNAIL_CACHE},
};
use crate::{history::screenshots::load_screenshot_image_path, services::paths};
use axum::{
    extract::{Path as RoutePath, State},
    http::StatusCode,
    response::{IntoResponse, Response},
    routing::get,
    Router,
};
use std::{
    path::{Path, PathBuf},
    sync::{Arc, Mutex},
};

#[cfg(test)]
mod tests;

pub(super) const ROUTE: &str = "/history/{source}/images/{screenshot_id}/strip";

/// An opaque handle for one registered installation path. It stays valid, and
/// names the same path, until the installer exits.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(crate) struct ThumbnailSource(usize);

#[derive(Clone, Default)]
pub(crate) struct HistoryThumbnails {
    sources: Arc<Mutex<Vec<PathBuf>>>,
    source_paths: SourcePaths,
}

impl HistoryThumbnails {
    /// Registers the installation a History page was read from. The path is
    /// stored as given and its database is resolved on each uncached request,
    /// so a database that appears after startup is still found.
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
        thumbnails,
        settings,
        cache_root,
    } = state;
    match run_record_task(move || {
        render(
            &game_path,
            &screenshot_id,
            &settings,
            &thumbnails.source_paths,
            &cache_root,
        )
    })
    .await
    {
        Ok(Some(bytes)) => png_response(bytes),
        Ok(None) => StatusCode::NOT_FOUND.into_response(),
        Err(error) => (
            StatusCode::INTERNAL_SERVER_ERROR,
            crate::problem::error_diagnostic(&error),
        )
            .into_response(),
    }
}

/// `None` when the database, the screenshot row or its image is missing.
fn render(
    game_path: &Path,
    screenshot_id: &str,
    settings: &OverlaySettingsStore,
    source_paths: &SourcePaths,
    cache_root: &Path,
) -> anyhow::Result<Option<Vec<u8>>> {
    let crop = settings.load()?.crop;
    let Some(image_path) = source_paths.resolve(game_path, screenshot_id, || {
        let stored = load_screenshot_image_path(&paths::database_path(game_path), screenshot_id)?;
        Ok(
            resolve_overlay_image_path(Some(game_path.to_path_buf()), stored.as_deref())
                .filter(|path| path.exists()),
        )
    })?
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
    cache_namespace(cache_root, HISTORY_THUMBNAIL_CACHE, game_path)
}

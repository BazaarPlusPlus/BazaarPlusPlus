use std::path::{Path, PathBuf};

use serde::Serialize;

use crate::history::hero::{canonical_hero_id, hero_display_name};
use crate::history::screenshots::OverlaySnapshotRow;

use super::image::resolve_overlay_image_path;

/// The OBS overlay's strip route for a screenshot, relative to the service origin.
fn strip_url_for_screenshot(screenshot_id: &str) -> String {
    format!("/images/{screenshot_id}/strip")
}

#[derive(Clone, Debug, Serialize)]
pub struct OverlayRecord {
    pub id: String,
    pub hero_id: String,
    pub title: String,
    pub subtitle: String,
    pub captured_at: String,
    pub captured_at_utc: String,
    pub strip_url: Option<String>,
    pub wins: Option<i64>,
    pub battle_count: Option<i64>,
    pub rank: Option<String>,
    pub rating: Option<i64>,
}

pub(super) fn to_overlay_record(
    game_path: Option<&Path>,
    row: OverlaySnapshotRow,
) -> OverlayRecord {
    let image_path =
        resolve_overlay_image_path(game_path.map(PathBuf::from), row.image_path.as_deref())
            .filter(|path| path.exists());
    let strip_url = image_path
        .as_ref()
        .map(|_| strip_url_for_screenshot(&row.id));
    let hero_id = canonical_hero_id(&row.hero);
    let title = hero_display_name(&hero_id).to_string();

    OverlayRecord {
        id: row.id,
        hero_id,
        title,
        subtitle: build_subtitle(&row.game_mode, row.wins, row.battle_count),
        captured_at: row.captured_at,
        captured_at_utc: row.captured_at_utc,
        strip_url,
        wins: row.wins,
        battle_count: row.battle_count,
        rank: row.rank,
        rating: row.rating,
    }
}

fn build_subtitle(game_mode: &str, wins: Option<i64>, battle_count: Option<i64>) -> String {
    match (wins, battle_count) {
        (Some(wins), Some(battles)) => format!("{game_mode} · {wins}W · {battles} battles"),
        (Some(wins), None) => format!("{game_mode} · {wins}W"),
        (None, Some(battles)) => format!("{game_mode} · {battles} battles"),
        (None, None) => game_mode.to_owned(),
    }
}

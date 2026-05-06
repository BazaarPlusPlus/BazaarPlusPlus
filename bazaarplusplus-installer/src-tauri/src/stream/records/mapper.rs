use std::path::{Path, PathBuf};

use serde::Serialize;

use super::image::resolve_overlay_image_path;
use super::repo::OverlayRecordRow;

#[derive(Clone, Debug, Serialize, ts_rs::TS)]
#[ts(export, rename = "StreamRecordSummary")]
pub struct OverlayRecord {
    pub id: String,
    pub title: String,
    pub subtitle: String,
    pub captured_at: String,
    pub captured_at_utc: String,
    pub image_url: Option<String>,
    pub image_path: Option<String>,
    pub wins: Option<i64>,
    pub position: Option<i64>,
    pub battle_count: Option<i64>,
    pub rank: Option<String>,
    pub rating: Option<i64>,
}

pub(super) fn to_overlay_record(game_path: Option<&Path>, row: OverlayRecordRow) -> OverlayRecord {
    let image_path =
        resolve_overlay_image_path(game_path.map(PathBuf::from), row.image_path.as_deref())
            .filter(|path| path.exists());
    let image_url = image_path.as_ref().map(|_| format!("/images/{}", row.id));
    let image_path = image_path.map(|path| path.to_string_lossy().into_owned());

    OverlayRecord {
        id: row.id,
        title: row.hero.clone(),
        subtitle: build_subtitle(&row.game_mode, row.wins, row.battle_count),
        captured_at: row.captured_at,
        captured_at_utc: row.captured_at_utc,
        image_url,
        image_path,
        wins: row.wins,
        position: row.position,
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

#[cfg(test)]
mod tests {
    use super::build_subtitle;

    #[test]
    fn subtitle_includes_wins_and_battles_when_both_present() {
        assert_eq!(
            build_subtitle("End of run", Some(10), Some(14)),
            "End of run · 10W · 14 battles"
        );
    }

    #[test]
    fn subtitle_omits_battles_when_only_wins_are_present() {
        assert_eq!(
            build_subtitle("End of run", Some(7), None),
            "End of run · 7W"
        );
    }

    #[test]
    fn subtitle_omits_wins_when_only_battles_are_present() {
        assert_eq!(
            build_subtitle("End of run", None, Some(3)),
            "End of run · 3 battles"
        );
    }

    #[test]
    fn subtitle_is_bare_mode_when_no_metrics_are_present() {
        assert_eq!(build_subtitle("End of run", None, None), "End of run");
    }
}

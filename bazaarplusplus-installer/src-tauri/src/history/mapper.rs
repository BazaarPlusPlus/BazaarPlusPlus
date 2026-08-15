use crate::history::dto::{
    HistoryBattleRow, HistoryBattleVideo, HistoryRunDetailRow, HistoryRunRow,
};
use crate::history::hero::{canonical_hero_id, hero_display_name};
use crate::history::queries::RunRow;

struct RunSharedFields {
    run_id: String,
    hero: String,
    game_mode: String,
    started_at_utc: String,
    ended_at_utc: Option<String>,
    last_seen_at_utc: String,
    result: String,
    victories: Option<i64>,
    losses: Option<i64>,
    final_day: Option<i64>,
    final_player_rank: Option<String>,
    final_player_rating: Option<i64>,
    screenshot_id: Option<String>,
    strip_url: Option<String>,
    video_count: i64,
}

fn build_run_shared_fields(
    row: RunRow,
    screenshot_id: Option<String>,
    video_count: i64,
) -> RunSharedFields {
    let strip_url = screenshot_id
        .as_ref()
        .map(|id| strip_url_for_screenshot(id));
    RunSharedFields {
        result: derive_run_result(&row.status, row.victories),
        run_id: row.run_id,
        hero: hero_display_name(&canonical_hero_id(&row.hero)).to_string(),
        game_mode: row.game_mode,
        started_at_utc: row.started_at_utc,
        ended_at_utc: row.ended_at_utc,
        last_seen_at_utc: row.last_seen_at_utc,
        victories: row.victories,
        losses: row.losses,
        final_day: row.final_day,
        final_player_rank: row.final_player_rank,
        final_player_rating: row.final_player_rating,
        screenshot_id,
        strip_url,
        video_count,
    }
}

pub fn strip_url_for_screenshot(screenshot_id: &str) -> String {
    format!("/images/{screenshot_id}/strip")
}

pub fn derive_run_result(status: &str, victories: Option<i64>) -> String {
    match status {
        "abandoned" => "abandoned".to_string(),
        "completed" if victories.unwrap_or(0) >= 10 => "win".to_string(),
        "completed" => "loss".to_string(),
        value if value != "completed" && value != "abandoned" => "in_progress".to_string(),
        _ => "unknown".to_string(),
    }
}

pub fn map_battle_result(result: Option<&str>) -> String {
    // LOCAL battles persist lowercase "win"/"loss" or NULL; GHOST rows use
    // capitalized "Won"/"Lost". Normalize case (and trim) so every source
    // resolves, and map NULL/empty/unrecognized to "unknown" — a neutral
    // marker the frontend renders muted, never as a defeat.
    match result
        .map(str::trim)
        .map(str::to_ascii_lowercase)
        .as_deref()
    {
        Some("win" | "won") => "win".to_string(),
        Some("loss" | "lost") => "loss".to_string(),
        _ => "unknown".to_string(),
    }
}

pub fn map_run_to_list_row(
    row: RunRow,
    screenshot_id: Option<String>,
    video_count: i64,
) -> HistoryRunRow {
    let shared = build_run_shared_fields(row, screenshot_id, video_count);
    HistoryRunRow {
        run_id: shared.run_id,
        hero: shared.hero,
        game_mode: shared.game_mode,
        started_at_utc: shared.started_at_utc,
        ended_at_utc: shared.ended_at_utc,
        last_seen_at_utc: shared.last_seen_at_utc,
        result: shared.result,
        victories: shared.victories,
        losses: shared.losses,
        final_day: shared.final_day,
        final_player_rank: shared.final_player_rank,
        final_player_rating: shared.final_player_rating,
        screenshot_id: shared.screenshot_id,
        strip_url: shared.strip_url,
        video_count: shared.video_count,
    }
}

pub fn map_run_to_detail_row(
    row: RunRow,
    screenshot_id: Option<String>,
    video_count: i64,
    player_name: Option<String>,
) -> HistoryRunDetailRow {
    let status = row.status.clone();
    let final_hour = row.final_hour;
    let shared = build_run_shared_fields(row, screenshot_id, video_count);
    HistoryRunDetailRow {
        run_id: shared.run_id,
        hero: shared.hero,
        game_mode: shared.game_mode,
        started_at_utc: shared.started_at_utc,
        ended_at_utc: shared.ended_at_utc,
        last_seen_at_utc: shared.last_seen_at_utc,
        status,
        result: shared.result,
        victories: shared.victories,
        losses: shared.losses,
        final_day: shared.final_day,
        final_hour,
        final_player_rank: shared.final_player_rank,
        final_player_rating: shared.final_player_rating,
        screenshot_id: shared.screenshot_id,
        strip_url: shared.strip_url,
        video_count: shared.video_count,
        player_name,
    }
}

pub(super) struct BattleFields {
    pub(super) battle_id: String,
    pub(super) day: Option<i64>,
    pub(super) hour: Option<i64>,
    pub(super) result: Option<String>,
    pub(super) opponent_hero: Option<String>,
    pub(super) opponent_name: Option<String>,
    pub(super) opponent_rank: Option<String>,
    pub(super) opponent_rating: Option<i64>,
}

pub(super) struct BattleVideoFields {
    pub(super) video_id: String,
    pub(super) status: Option<String>,
    pub(super) file_size_bytes: Option<i64>,
    pub(super) duration_ms: Option<i64>,
}

pub(super) fn map_battle_row(
    battle: BattleFields,
    video: Option<BattleVideoFields>,
) -> HistoryBattleRow {
    HistoryBattleRow {
        battle_id: battle.battle_id,
        day: battle.day,
        hour: battle.hour,
        result: map_battle_result(battle.result.as_deref()),
        opponent_hero: battle
            .opponent_hero
            .map(|hero| hero_display_name(&canonical_hero_id(&hero)).to_string()),
        opponent_name: battle.opponent_name,
        opponent_rank: battle.opponent_rank,
        opponent_rating: battle.opponent_rating,
        video: video.map(|video| HistoryBattleVideo {
            video_id: video.video_id,
            status: video.status.unwrap_or_else(|| "COMPLETED".to_string()),
            file_size_bytes: video.file_size_bytes,
            duration_ms: video.duration_ms,
        }),
    }
}

#[cfg(test)]
mod tests {
    use super::{
        map_battle_result, map_battle_row, map_run_to_detail_row, map_run_to_list_row,
        BattleFields, BattleVideoFields,
    };
    use crate::history::dto::{HistoryBattleRow, HistoryBattleVideo};
    use crate::history::queries::RunRow;

    fn run_row(hero: &str) -> RunRow {
        RunRow {
            run_id: "run-1".to_string(),
            hero: hero.to_string(),
            game_mode: "Ranked".to_string(),
            started_at_utc: "2026-08-06T12:00:00Z".to_string(),
            ended_at_utc: Some("2026-08-06T13:00:00Z".to_string()),
            last_seen_at_utc: "2026-08-06T13:00:00Z".to_string(),
            status: "completed".to_string(),
            victories: Some(10),
            losses: Some(2),
            final_day: Some(12),
            final_hour: Some(1),
            final_player_rank: Some("Diamond".to_string()),
            final_player_rating: Some(1500),
        }
    }

    fn battle_fields(opponent_hero: Option<&str>) -> BattleFields {
        BattleFields {
            battle_id: "battle-1".to_string(),
            day: Some(8),
            hour: Some(1),
            result: Some("Won".to_string()),
            opponent_hero: opponent_hero.map(str::to_string),
            opponent_name: Some("Opponent A".to_string()),
            opponent_rank: Some("Diamond III".to_string()),
            opponent_rating: Some(1410),
        }
    }

    #[test]
    fn run_rows_use_the_dragons_display_name() {
        let list_row = map_run_to_list_row(run_row("Hero8"), None, 0);
        let detail_row = map_run_to_detail_row(run_row("Hero8"), None, 0, None);

        assert_eq!(list_row.hero, "The Dragons");
        assert_eq!(detail_row.hero, "The Dragons");
    }

    #[test]
    fn battle_rows_normalize_only_present_hero_names() {
        let the_dragons = map_battle_row(battle_fields(Some("TheDragons")), None);
        let unknown = map_battle_row(battle_fields(None), None);
        let dooley = map_battle_row(battle_fields(Some("Dooley")), None);

        assert_eq!(the_dragons.opponent_hero.as_deref(), Some("The Dragons"));
        assert_eq!(unknown.opponent_hero, None);
        assert_eq!(dooley.opponent_hero.as_deref(), Some("Dooley"));
    }

    #[test]
    fn map_battle_row_preserves_battle_and_video_fields_with_default_status() {
        let mapped = map_battle_row(
            BattleFields {
                battle_id: "battle-1".to_string(),
                day: Some(8),
                hour: Some(1),
                result: Some("Won".to_string()),
                opponent_hero: Some("Dooley".to_string()),
                opponent_name: Some("Opponent A".to_string()),
                opponent_rank: Some("Diamond III".to_string()),
                opponent_rating: Some(1410),
            },
            Some(BattleVideoFields {
                video_id: "video-1".to_string(),
                status: None,
                file_size_bytes: Some(2200),
                duration_ms: Some(1200),
            }),
        );

        assert_eq!(
            mapped,
            HistoryBattleRow {
                battle_id: "battle-1".to_string(),
                day: Some(8),
                hour: Some(1),
                result: "win".to_string(),
                opponent_hero: Some("Dooley".to_string()),
                opponent_name: Some("Opponent A".to_string()),
                opponent_rank: Some("Diamond III".to_string()),
                opponent_rating: Some(1410),
                video: Some(HistoryBattleVideo {
                    video_id: "video-1".to_string(),
                    status: "COMPLETED".to_string(),
                    file_size_bytes: Some(2200),
                    duration_ms: Some(1200),
                }),
            }
        );
    }

    #[test]
    fn map_battle_row_omits_video_without_video_id() {
        let mapped = map_battle_row(
            BattleFields {
                battle_id: "battle-1".to_string(),
                day: None,
                hour: None,
                result: None,
                opponent_hero: None,
                opponent_name: None,
                opponent_rank: None,
                opponent_rating: None,
            },
            None,
        );

        assert_eq!(mapped.video, None);
    }

    #[test]
    fn map_battle_result_resolves_known_outcomes_case_insensitively() {
        // Lowercase is what LOCAL (PvP) battles actually persist.
        assert_eq!(map_battle_result(Some("win")), "win");
        assert_eq!(map_battle_result(Some("loss")), "loss");
        // Capitalized GHOST-style values (with stray whitespace) still resolve.
        assert_eq!(map_battle_result(Some("Win")), "win");
        assert_eq!(map_battle_result(Some("Won")), "win");
        assert_eq!(map_battle_result(Some(" Lost ")), "loss");
    }

    #[test]
    fn map_battle_result_maps_null_and_unrecognized_to_neutral() {
        // NULL is a legitimate draw/unresolved state, not a defeat.
        assert_eq!(map_battle_result(None), "unknown");
        // Empty / whitespace-only / garbage all collapse to the neutral marker.
        assert_eq!(map_battle_result(Some("")), "unknown");
        assert_eq!(map_battle_result(Some("   ")), "unknown");
        assert_eq!(map_battle_result(Some("draw")), "unknown");
    }
}

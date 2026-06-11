use crate::history::dto::{
    HistoryBattleRow, HistoryBattleVideo, HistoryRunDetailRow, HistoryRunRow,
};
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
        hero: row.hero,
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

pub fn map_battle_row(
    battle_id: String,
    day: Option<i64>,
    hour: Option<i64>,
    result: Option<String>,
    opponent_hero: Option<String>,
    opponent_name: Option<String>,
    opponent_rank: Option<String>,
    opponent_rating: Option<i64>,
    video_id: Option<String>,
    video_status: Option<String>,
    file_size_bytes: Option<i64>,
    duration_ms: Option<i64>,
) -> HistoryBattleRow {
    HistoryBattleRow {
        battle_id,
        day,
        hour,
        result: map_battle_result(result.as_deref()),
        opponent_hero,
        opponent_name,
        opponent_rank,
        opponent_rating,
        video: video_id.map(|video_id| HistoryBattleVideo {
            video_id,
            status: video_status.unwrap_or_else(|| "COMPLETED".to_string()),
            file_size_bytes,
            duration_ms,
        }),
    }
}

#[cfg(test)]
mod tests {
    use super::map_battle_result;

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

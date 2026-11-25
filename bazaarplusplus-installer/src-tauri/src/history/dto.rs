use serde::Serialize;

#[derive(Clone, Debug, PartialEq, Serialize, specta::Type)]
pub struct HistoryRunList {
    /// Totals over every Run the filter selects, not only this page.
    pub summary: HistorySummary,
    pub runs: Vec<HistoryRunRow>,
    /// Canonical ids of every hero with a Run, whatever the filter.
    pub heroes: Vec<String>,
    /// The items on the Final Boards of the Runs the other filters select,
    /// held by the most Runs first; a selected key card is always listed.
    pub key_cards: Vec<HistoryKeyCard>,
}

#[derive(Clone, Debug, PartialEq, Serialize, specta::Type)]
pub struct HistoryKeyCard {
    pub template_id: String,
    /// The English title, or the item's internal name when the game's data
    /// cache does not have it.
    pub name: String,
    /// The Simplified Chinese title from the game's data cache.
    pub name_zh: Option<String>,
}

#[derive(Clone, Debug, PartialEq, Serialize, specta::Type)]
pub struct HistorySummary {
    pub runs: i64,
    pub videos: i64,
    pub win_rate: Option<f64>,
}

#[derive(Clone, Debug, PartialEq, Serialize, specta::Type)]
pub struct HistoryRunRow {
    pub run_id: String,
    pub hero: String,
    pub game_mode: String,
    pub started_at_utc: String,
    pub ended_at_utc: Option<String>,
    pub result: String,
    pub victories: Option<i64>,
    pub losses: Option<i64>,
    pub final_day: Option<i64>,
    pub final_player_rank: Option<String>,
    pub final_player_rating: Option<i64>,
    /// The Run's rating change; `None` when the starting rating was not captured.
    pub final_player_rating_delta: Option<i64>,
    pub screenshot_id: Option<String>,
    /// The absolute URL of the run's History Thumbnail.
    pub thumbnail_url: Option<String>,
}

#[derive(Clone, Debug, PartialEq, Serialize, specta::Type)]
pub struct HistoryRunDetail {
    pub run: HistoryRunDetailRow,
    pub battles: Vec<HistoryBattleRow>,
}

#[derive(Clone, Debug, PartialEq, Serialize, specta::Type)]
pub struct HistoryRunDetailRow {
    pub run_id: String,
    pub hero: String,
    pub game_mode: String,
    pub started_at_utc: String,
    pub ended_at_utc: Option<String>,
    pub status: String,
    pub result: String,
    pub victories: Option<i64>,
    pub losses: Option<i64>,
    pub final_day: Option<i64>,
    pub final_player_rank: Option<String>,
    pub final_player_rating: Option<i64>,
    /// The Run's rating change; `None` when the starting rating was not captured.
    pub final_player_rating_delta: Option<i64>,
    pub screenshot_id: Option<String>,
    pub player_name: Option<String>,
}

#[derive(Clone, Debug, PartialEq, Serialize, specta::Type)]
pub struct HistoryBattleRow {
    pub battle_id: String,
    pub day: Option<i64>,
    pub hour: Option<i64>,
    pub result: String,
    pub opponent_hero: Option<String>,
    pub opponent_name: Option<String>,
    pub opponent_rank: Option<String>,
    pub opponent_rating: Option<i64>,
    pub player_board: Option<Vec<HistoryBattleCard>>,
    pub player_skills: Option<Vec<HistoryBattleCard>>,
    pub opponent_board: Option<Vec<HistoryBattleCard>>,
    pub opponent_skills: Option<Vec<HistoryBattleCard>>,
    pub video: Option<HistoryBattleVideo>,
}

#[derive(Clone, Debug, PartialEq, Serialize, specta::Type)]
pub struct HistoryBattleVideo {
    pub video_id: String,
    pub status: String,
    pub file_size_bytes: Option<i64>,
    pub duration_ms: Option<i64>,
}

#[derive(Clone, Debug, PartialEq, Serialize, specta::Type)]
pub struct HistoryBattleCard {
    pub instance_id: String,
    pub template_id: String,
    pub name: String,
    pub name_zh: Option<String>,
    pub size: u8,
    pub slot: Option<u8>,
    pub tier: Option<String>,
    pub enchant: Option<String>,
}

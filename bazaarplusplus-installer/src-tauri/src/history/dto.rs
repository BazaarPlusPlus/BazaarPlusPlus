use serde::Serialize;

#[derive(Clone, Debug, PartialEq, Serialize, ts_rs::TS)]
#[ts(export)]
pub struct HistoryRunList {
    pub summary: HistorySummary,
    pub runs: Vec<HistoryRunRow>,
}

#[derive(Clone, Debug, PartialEq, Serialize, ts_rs::TS)]
#[ts(export)]
pub struct HistorySummary {
    pub runs: i64,
    pub videos: i64,
    pub last_run_at_utc: Option<String>,
    pub win_rate: Option<f64>,
}

#[derive(Clone, Debug, PartialEq, Serialize, ts_rs::TS)]
#[ts(export)]
pub struct HistoryRunRow {
    pub run_id: String,
    pub hero: String,
    pub game_mode: String,
    pub started_at_utc: String,
    pub ended_at_utc: Option<String>,
    pub last_seen_at_utc: String,
    pub result: String,
    pub victories: Option<i64>,
    pub losses: Option<i64>,
    pub final_day: Option<i64>,
    pub final_player_rank: Option<String>,
    pub final_player_rating: Option<i64>,
    pub screenshot_id: Option<String>,
    pub strip_url: Option<String>,
    pub video_count: i64,
}

#[derive(Clone, Debug, PartialEq, Serialize, ts_rs::TS)]
#[ts(export)]
pub struct HistoryRunDetail {
    pub run: HistoryRunDetailRow,
    pub battles: Vec<HistoryBattleRow>,
}

#[derive(Clone, Debug, PartialEq, Serialize, ts_rs::TS)]
#[ts(export)]
pub struct HistoryRunDetailRow {
    pub run_id: String,
    pub hero: String,
    pub game_mode: String,
    pub started_at_utc: String,
    pub ended_at_utc: Option<String>,
    pub last_seen_at_utc: String,
    pub status: String,
    pub result: String,
    pub victories: Option<i64>,
    pub losses: Option<i64>,
    pub final_day: Option<i64>,
    pub final_hour: Option<i64>,
    pub final_player_rank: Option<String>,
    pub final_player_rating: Option<i64>,
    pub screenshot_id: Option<String>,
    pub strip_url: Option<String>,
    pub video_count: i64,
    pub player_name: Option<String>,
}

#[derive(Clone, Debug, PartialEq, Serialize, ts_rs::TS)]
#[ts(export)]
pub struct HistoryBattleRow {
    pub battle_id: String,
    pub day: Option<i64>,
    pub hour: Option<i64>,
    pub result: String,
    pub opponent_hero: Option<String>,
    pub opponent_name: Option<String>,
    pub opponent_rank: Option<String>,
    pub opponent_rating: Option<i64>,
    pub video: Option<HistoryBattleVideo>,
}

#[derive(Clone, Debug, PartialEq, Serialize, ts_rs::TS)]
#[ts(export)]
pub struct HistoryBattleVideo {
    pub video_id: String,
    pub status: String,
    pub file_size_bytes: Option<i64>,
    pub duration_ms: Option<i64>,
}

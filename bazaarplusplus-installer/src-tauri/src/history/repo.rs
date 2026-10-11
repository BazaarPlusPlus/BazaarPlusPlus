use std::path::{Path, PathBuf};

use rusqlite::params;

pub use crate::history::dto::{HistoryRunDetail, HistoryRunList, HistorySummary};

use crate::history::dto::HistoryRunDetail as HistoryRunDetailDto;
use crate::history::files::{remove_video_file, resolve_data_file_path, resolve_screenshot_path};
use crate::history::mapper::{map_run_to_detail_row, map_run_to_list_row};
use crate::history::queries::{
    self, list_run_rows, load_battle_rows, load_battle_video_ref, load_run_row, load_summary,
    local_player_name, open_connection, open_write_connection, table_exists, HistoryReadError,
};
use crate::history::screenshots::{primary_screenshot, primary_screenshot_ids};

/// `thumbnail_url` names the History Thumbnail of a screenshot id.
pub fn list_history_runs(
    database_path: &Path,
    limit: usize,
    offset: usize,
    thumbnail_url: impl Fn(&str) -> String,
) -> Result<HistoryRunList, HistoryReadError> {
    let empty = || HistoryRunList {
        summary: HistorySummary {
            runs: 0,
            videos: 0,
            win_rate: None,
        },
        runs: Vec::new(),
    };

    if !database_path.exists() {
        return Ok(empty());
    }

    let conn = open_connection(database_path)?;
    if !table_exists(&conn, "runs")? {
        return Ok(empty());
    }

    let summary = load_summary(&conn)?;
    let effective_limit = i64::try_from(limit.max(1)).map_err(|err| err.to_string())?;
    let effective_offset = i64::try_from(offset).map_err(|err| err.to_string())?;
    let rows = list_run_rows(&conn, effective_limit, effective_offset)?;
    let run_ids = rows
        .iter()
        .map(|row| row.run_id.clone())
        .collect::<Vec<_>>();
    let screenshot_ids = primary_screenshot_ids(&conn, &run_ids)?;

    let runs = rows
        .into_iter()
        .map(|row| {
            let screenshot_id = screenshot_ids.get(&row.run_id).cloned();
            map_run_to_list_row(row, screenshot_id, &thumbnail_url)
        })
        .collect();

    Ok(HistoryRunList { summary, runs })
}

pub fn get_history_run_detail(
    database_path: &Path,
    run_id: &str,
) -> Result<Option<HistoryRunDetail>, HistoryReadError> {
    if !database_path.exists() {
        return Ok(None);
    }

    let conn = open_connection(database_path)?;
    if !table_exists(&conn, "runs")? {
        return Ok(None);
    }

    let Some(row) = load_run_row(&conn, run_id)? else {
        return Ok(None);
    };
    let screenshot_id = primary_screenshot(&conn, run_id)?.map(|screenshot| screenshot.id);
    let player_name = local_player_name(&conn, run_id)?;
    let battles = load_battle_rows(&conn, run_id)?;

    Ok(Some(HistoryRunDetailDto {
        run: map_run_to_detail_row(row, screenshot_id, player_name),
        battles,
    }))
}

pub fn load_run_screenshot_path(
    database_path: &Path,
    data_root: &Path,
    run_id: &str,
) -> Result<Option<PathBuf>, String> {
    let conn = open_connection(database_path)?;
    let path = primary_screenshot(&conn, run_id)?
        .and_then(|screenshot| resolve_screenshot_path(data_root, &screenshot.image_relative_path));
    Ok(path)
}

pub fn load_battle_video_path(
    database_path: &Path,
    video_dir: &Path,
    battle_id: &str,
    video_id: Option<&str>,
) -> Result<Option<PathBuf>, String> {
    let conn = open_connection(database_path)?;
    let row = load_battle_video_ref(&conn, battle_id, video_id)?;
    Ok(row.and_then(|video| resolve_data_file_path(video_dir, &video.relative_path)))
}

pub fn load_run_id_for_battle(
    database_path: &Path,
    battle_id: &str,
) -> Result<Option<String>, String> {
    let conn = open_connection(database_path)?;
    queries::load_run_id_for_battle(&conn, battle_id)
}

pub fn delete_battle_video(
    database_path: &Path,
    video_dir: &Path,
    battle_id: &str,
    video_id: &str,
) -> Result<bool, String> {
    let mut conn = open_write_connection(database_path)?;
    let Some(video) = load_battle_video_ref(&conn, battle_id, Some(video_id))? else {
        return Ok(false);
    };
    remove_video_file(video_dir, &video.relative_path)?;
    let transaction = conn.transaction().map_err(|err| err.to_string())?;
    let deleted = transaction
        .execute(
            "delete from combat_replay_videos where battle_id = ?1 and video_id = ?2",
            params![battle_id, video_id],
        )
        .map_err(|err| err.to_string())?;
    transaction.commit().map_err(|err| err.to_string())?;
    Ok(deleted > 0)
}

#[cfg(test)]
mod tests {
    use super::{delete_battle_video, get_history_run_detail, list_history_runs};
    use crate::config::DATABASE_FILE_NAME;
    use crate::history::dto::{HistoryBattleRow, HistoryBattleVideo};
    use crate::history::test_schema::create_mod_schema;
    use crate::services::paths;

    #[test]
    fn list_history_runs_derives_summary_results_and_thumbnail_urls() {
        let temp_dir = tempfile::tempdir().unwrap();
        let database_path = temp_dir.path().join(DATABASE_FILE_NAME);
        let conn = rusqlite::Connection::open(&database_path).unwrap();
        create_mod_schema(&conn);
        conn.execute_batch(
            "
            insert into runs (
                run_id, started_at_utc, last_seen_at_utc, status, completed,
                hero, game_mode, ended_at_utc, final_day, victories, losses,
                final_player_rank, final_player_rating
            ) values
                ('run-win', '2026-05-20T10:00:00Z', '2026-05-20T11:00:00Z', 'completed', 1,
                 'Vanessa', 'Ranked', '2026-05-20T11:00:00Z', 10, 10, 2, 'Diamond II', 1450),
                ('run-loss', '2026-05-19T10:00:00Z', '2026-05-19T10:40:00Z', 'completed', 1,
                 'Dooley', 'Ranked', '2026-05-19T10:40:00Z', 6, 4, 3, 'Gold I', 1100),
                ('run-live', '2026-05-21T10:00:00Z', '2026-05-21T10:20:00Z', 'active', 0,
                 'Mak', 'Normal', null, null, null, null, null, null);

            insert into run_screenshots (
                screenshot_id, run_id, hero_name, capture_source, is_primary,
                image_relative_path, captured_at_utc, captured_at_local
            ) values
                ('shot-win', 'run-win', 'Vanessa', 'end_of_run_auto', 1,
                 'win.png', '2026-05-20T11:00:00Z', '2026-05-20T19:00:00+08:00');

            insert into battles (
                battle_id, source, run_id, recorded_at_utc, combat_kind, opponent_name,
                has_local_payload, local_payload_state
            ) values
                ('battle-1', 'LOCAL', 'run-win', '2026-05-20T10:30:00Z', 'PVP', 'Opponent',
                 0, 'missing');

            insert into combat_replay_videos (
                video_id, battle_id, source, video_relative_path, width, height, fps, codec,
                started_at_utc, duration_ms, file_size_bytes, status
            ) values
                ('video-1', 'battle-1', 'GAME_CAPTURE', 'Videos/video-1.mp4', 1920, 1080, 60,
                 'h264', '2026-05-20T10:31:00Z', 1000, 2000, 'COMPLETED');
            ",
        )
        .unwrap();

        let payload =
            list_history_runs(&database_path, 20, 0, |id| format!("thumbnail:{id}")).unwrap();

        assert_eq!(payload.summary.runs, 3);
        assert_eq!(payload.summary.videos, 1);
        assert_eq!(payload.summary.win_rate, Some(0.5));
        assert_eq!(payload.runs.len(), 3);
        assert_eq!(payload.runs[0].run_id, "run-live");
        assert_eq!(payload.runs[0].result, "in_progress");
        assert_eq!(payload.runs[1].run_id, "run-win");
        assert_eq!(payload.runs[1].result, "win");
        assert_eq!(
            payload.runs[1].thumbnail_url.as_deref(),
            Some("thumbnail:shot-win")
        );
        assert_eq!(payload.runs[2].run_id, "run-loss");
        assert_eq!(payload.runs[2].result, "loss");
    }

    #[test]
    fn history_pages_reach_older_runs_without_duplicates_and_keep_the_full_summary() {
        let temp_dir = tempfile::tempdir().unwrap();
        let database_path = temp_dir.path().join(DATABASE_FILE_NAME);
        let conn = rusqlite::Connection::open(&database_path).unwrap();
        create_mod_schema(&conn);
        for index in 0..235 {
            conn.execute(
                "insert into runs (run_id, started_at_utc, last_seen_at_utc, status, hero, game_mode)
                 values (?1, '2026-09-12T10:00:00Z', '2026-09-12T11:00:00Z', 'active', 'Vanessa', 'Ranked')",
                [format!("run-{index:03}")],
            )
            .unwrap();
        }

        let mut ids = Vec::new();
        for offset in [0, 50, 100, 150, 200] {
            let page = list_history_runs(&database_path, 50, offset, str::to_string).unwrap();
            assert_eq!(page.summary.runs, 235);
            assert_eq!(page.runs.len(), if offset == 200 { 35 } else { 50 });
            ids.extend(page.runs.into_iter().map(|run| run.run_id));
        }
        let expected: Vec<_> = (0..235)
            .rev()
            .map(|index| format!("run-{index:03}"))
            .collect();
        assert_eq!(ids, expected);
        let beyond_end = list_history_runs(&database_path, 50, 250, str::to_string).unwrap();
        assert!(beyond_end.runs.is_empty());
        assert_eq!(beyond_end.summary.runs, 235);
    }

    #[test]
    fn run_detail_maps_local_battles_latest_completed_video_and_deletes_video_rows() {
        let temp_dir = tempfile::tempdir().unwrap();
        let game_path = temp_dir.path();
        let video_dir = paths::combat_replay_videos_dir(game_path);
        let dated_videos_dir = video_dir.join("2026-05-20");
        std::fs::create_dir_all(&dated_videos_dir).unwrap();
        std::fs::write(dated_videos_dir.join("new.mp4"), b"new-video").unwrap();
        std::fs::write(dated_videos_dir.join("old.mp4"), b"old-video").unwrap();

        let database_path = paths::database_path(game_path);
        let conn = rusqlite::Connection::open(&database_path).unwrap();
        create_mod_schema(&conn);
        conn.execute_batch(
            "
            insert into runs (
                run_id, started_at_utc, last_seen_at_utc, status, completed,
                hero, game_mode, ended_at_utc, final_day, final_hour, victories, losses,
                final_player_rank, final_player_rating
            ) values (
                'run-win', '2026-05-20T10:00:00Z', '2026-05-20T11:00:00Z', 'completed', 1,
                'Vanessa', 'Ranked', '2026-05-20T11:00:00Z', 10, 7, 10, 2,
                'Diamond II', 1450
            );

            insert into run_screenshots (
                screenshot_id, run_id, hero_name, capture_source, is_primary,
                image_relative_path, captured_at_utc, captured_at_local
            ) values (
                'shot-win', 'run-win', 'Vanessa', 'end_of_run_auto', 1,
                'win.png', '2026-05-20T11:00:00Z', '2026-05-20T19:00:00+08:00'
            );

            insert into battles (
                battle_id, source, run_id, recorded_at_utc, combat_kind, day, hour,
                player_name, opponent_hero, opponent_name, opponent_rank, opponent_rating, result,
                has_local_payload, local_payload_state, remote_battle_id, uploader_account_id
            ) values
                ('battle-1', 'LOCAL', 'run-win', '2026-05-20T10:30:00Z', 'PVP', 8, 1,
                 'cauyxy', 'Dooley', 'Opponent A', 'Diamond III', 1410, 'Won', 1, 'ready',
                 null, null),
                ('battle-2', 'LOCAL', 'run-win', '2026-05-20T10:10:00Z', 'PVP', 7, 0,
                 'cauyxy', 'Pygmalien', 'Opponent B', 'Diamond IV', 1360, 'Lost', 0, 'missing',
                 null, null),
                ('battle-ghost', 'GHOST', null, '2026-05-20T10:40:00Z', 'PVP', 9, 0,
                 'cauyxy', 'Mak', 'Ghost', 'Diamond I', 1500, 'Won', 0, null,
                 'remote-ghost', 'uploader-1');

            insert into combat_replay_videos (
                video_id, battle_id, source, video_relative_path, width, height, fps, codec,
                started_at_utc, duration_ms, file_size_bytes, status
            ) values
                ('video-old', 'battle-1', 'GAME_CAPTURE', '2026-05-20/old.mp4', 1920, 1080, 60,
                 'h264', '2026-05-20T10:31:00Z', 1000, 2000, 'COMPLETED'),
                ('video-new', 'battle-1', 'GAME_CAPTURE', '2026-05-20/new.mp4', 1920, 1080, 60,
                 'h264', '2026-05-20T10:32:00Z', 1200, 2200, 'COMPLETED'),
                ('video-failed', 'battle-2', 'GAME_CAPTURE', '2026-05-20/failed.mp4', 1920, 1080,
                 60, 'h264', '2026-05-20T10:11:00Z', null, null, 'FAILED');
            ",
        )
        .unwrap();
        drop(conn);

        let detail = get_history_run_detail(&database_path, "run-win")
            .unwrap()
            .unwrap();
        assert_eq!(detail.run.player_name.as_deref(), Some("cauyxy"));
        assert_eq!(detail.run.screenshot_id.as_deref(), Some("shot-win"));
        assert_eq!(detail.battles.len(), 2);
        assert_eq!(
            detail.battles[0],
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
                    video_id: "video-new".to_string(),
                    status: "COMPLETED".to_string(),
                    file_size_bytes: Some(2200),
                    duration_ms: Some(1200),
                }),
            }
        );
        assert_eq!(detail.battles[1].battle_id, "battle-2");
        assert_eq!(detail.battles[1].result, "loss");
        assert_eq!(detail.battles[1].video, None);

        assert!(delete_battle_video(&database_path, &video_dir, "battle-1", "video-new").unwrap());
        assert!(!dated_videos_dir.join("new.mp4").exists());

        let detail = get_history_run_detail(&database_path, "run-win")
            .unwrap()
            .unwrap();
        assert_eq!(
            detail.battles[0]
                .video
                .as_ref()
                .map(|video| video.video_id.as_str()),
            Some("video-old")
        );
    }
}

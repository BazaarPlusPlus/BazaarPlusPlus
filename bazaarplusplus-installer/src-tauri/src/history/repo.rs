use std::path::{Path, PathBuf};

use rusqlite::params;

pub use crate::history::dto::{HistoryRunDetail, HistoryRunList, HistorySummary};

use crate::history::dto::HistoryRunDetail as HistoryRunDetailDto;
use crate::history::files::{remove_video_file, resolve_data_file_path, resolve_screenshot_path};
use crate::history::mapper::{map_run_to_detail_row, map_run_to_list_row};
use crate::history::queries::{
    self, completed_video_count, completed_video_counts, list_run_rows, load_battle_rows,
    load_battle_video_ref, load_run_row, load_run_video_refs, load_summary, local_player_name,
    open_connection, open_write_connection, table_exists,
};
use crate::history::screenshots::{primary_screenshot, primary_screenshot_ids};

pub fn list_history_runs(database_path: &Path, limit: usize) -> Result<HistoryRunList, String> {
    let empty = || HistoryRunList {
        summary: HistorySummary {
            runs: 0,
            videos: 0,
            last_run_at_utc: None,
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
    let rows = list_run_rows(&conn, effective_limit)?;
    let run_ids = rows
        .iter()
        .map(|row| row.run_id.clone())
        .collect::<Vec<_>>();
    let screenshot_ids = primary_screenshot_ids(&conn, &run_ids)?;
    let video_counts = completed_video_counts(&conn, &run_ids)?;

    let runs = rows
        .into_iter()
        .map(|row| {
            let run_id = row.run_id.clone();
            let screenshot_id = screenshot_ids.get(&run_id).cloned();
            let video_count = video_counts.get(&run_id).copied().unwrap_or(0);
            map_run_to_list_row(row, screenshot_id, video_count)
        })
        .collect();

    Ok(HistoryRunList { summary, runs })
}

pub fn get_history_run_detail(
    database_path: &Path,
    run_id: &str,
) -> Result<Option<HistoryRunDetail>, String> {
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
    let video_count = completed_video_count(&conn, run_id)?;
    let player_name = local_player_name(&conn, run_id)?;
    let battles = load_battle_rows(&conn, run_id)?;

    Ok(Some(HistoryRunDetailDto {
        run: map_run_to_detail_row(row, screenshot_id, video_count, player_name),
        battles,
    }))
}

pub fn load_run_screenshot_path(
    database_path: &Path,
    game_path: &Path,
    run_id: &str,
) -> Result<Option<PathBuf>, String> {
    let conn = open_connection(database_path)?;
    let path = primary_screenshot(&conn, run_id)?
        .and_then(|screenshot| resolve_screenshot_path(game_path, &screenshot.image_relative_path));
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

pub fn delete_run_videos(
    database_path: &Path,
    video_dir: &Path,
    run_id: &str,
) -> Result<usize, String> {
    let mut conn = open_write_connection(database_path)?;
    let videos = load_run_video_refs(&conn, run_id)?;
    for video in &videos {
        remove_video_file(video_dir, &video.relative_path)?;
    }
    let transaction = conn.transaction().map_err(|err| err.to_string())?;
    for video in &videos {
        transaction
            .execute(
                "delete from combat_replay_videos where video_id = ?1",
                [&video.video_id],
            )
            .map_err(|err| err.to_string())?;
    }
    transaction.commit().map_err(|err| err.to_string())?;
    Ok(videos.len())
}

#[cfg(test)]
mod tests {
    use super::{
        delete_battle_video, delete_run_videos, get_history_run_detail, list_history_runs,
    };

    fn create_history_schema(conn: &rusqlite::Connection) {
        conn.execute_batch(
            "
            create table runs (
                run_id text primary key,
                started_at_utc text not null,
                last_seen_at_utc text not null,
                status text not null,
                completed integer not null default 0,
                hero text not null,
                game_mode text not null,
                ended_at_utc text null,
                final_day integer null,
                final_hour integer null,
                victories integer null,
                losses integer null,
                final_player_rank text null,
                final_player_rating integer null,
                final_player_rating_delta integer null
            );
            create table battles (
                battle_id text primary key,
                source text not null,
                run_id text null,
                recorded_at_utc text not null,
                day integer null,
                hour integer null,
                player_name text null,
                player_hero text null,
                opponent_hero text null,
                opponent_name text null,
                opponent_rank text null,
                opponent_rating integer null,
                result text null,
                deleted_at_utc text null
            );
            create table run_screenshots (
                screenshot_id text primary key,
                run_id text null,
                hero_name text null,
                capture_source text not null,
                is_primary integer not null default 0,
                image_relative_path text not null,
                captured_at_utc text not null,
                captured_at_local text not null,
                player_rank text null,
                player_rating integer null,
                victories_at_capture integer null
            );
            create table combat_replay_videos (
                video_id text primary key,
                battle_id text not null,
                video_relative_path text not null,
                started_at_utc text not null,
                duration_ms integer null,
                file_size_bytes integer null,
                status text not null
            );
            ",
        )
        .unwrap();
    }

    #[test]
    fn list_history_runs_derives_summary_results_video_counts_and_strip_urls() {
        let temp_dir = tempfile::tempdir().unwrap();
        let database_path = temp_dir.path().join("bazaarplusplus.db");
        let conn = rusqlite::Connection::open(&database_path).unwrap();
        create_history_schema(&conn);
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
                battle_id, source, run_id, recorded_at_utc, opponent_name
            ) values
                ('battle-1', 'LOCAL', 'run-win', '2026-05-20T10:30:00Z', 'Opponent');

            insert into combat_replay_videos (
                video_id, battle_id, video_relative_path, started_at_utc,
                duration_ms, file_size_bytes, status
            ) values
                ('video-1', 'battle-1', 'Videos/video-1.mp4', '2026-05-20T10:31:00Z',
                 1000, 2000, 'COMPLETED');
            ",
        )
        .unwrap();

        let payload = list_history_runs(&database_path, 20).unwrap();

        assert_eq!(payload.summary.runs, 3);
        assert_eq!(payload.summary.videos, 1);
        assert_eq!(
            payload.summary.last_run_at_utc.as_deref(),
            Some("2026-05-21T10:20:00Z")
        );
        assert_eq!(payload.summary.win_rate, Some(0.5));
        assert_eq!(payload.runs.len(), 3);
        assert_eq!(payload.runs[0].run_id, "run-live");
        assert_eq!(payload.runs[0].result, "in_progress");
        assert_eq!(payload.runs[1].run_id, "run-win");
        assert_eq!(payload.runs[1].result, "win");
        assert_eq!(
            payload.runs[1].strip_url.as_deref(),
            Some("/images/shot-win/strip")
        );
        assert_eq!(payload.runs[1].video_count, 1);
        assert_eq!(payload.runs[2].run_id, "run-loss");
        assert_eq!(payload.runs[2].result, "loss");
    }

    #[test]
    fn run_detail_maps_local_battles_latest_completed_video_and_deletes_video_rows() {
        let temp_dir = tempfile::tempdir().unwrap();
        let data_dir = temp_dir.path().join("BazaarPlusPlusV4");
        let video_dir = data_dir.join("CombatReplayVideos");
        let dated_videos_dir = video_dir.join("2026-05-20");
        std::fs::create_dir_all(&dated_videos_dir).unwrap();
        std::fs::write(dated_videos_dir.join("new.mp4"), b"new-video").unwrap();
        std::fs::write(dated_videos_dir.join("old.mp4"), b"old-video").unwrap();

        let database_path = data_dir.join("bazaarplusplus.db");
        let conn = rusqlite::Connection::open(&database_path).unwrap();
        create_history_schema(&conn);
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
                battle_id, source, run_id, recorded_at_utc, day, hour,
                player_name, opponent_hero, opponent_name, opponent_rank, opponent_rating, result
            ) values
                ('battle-1', 'LOCAL', 'run-win', '2026-05-20T10:30:00Z', 8, 1,
                 'cauyxy', 'Dooley', 'Opponent A', 'Diamond III', 1410, 'Won'),
                ('battle-2', 'LOCAL', 'run-win', '2026-05-20T10:10:00Z', 7, 0,
                 'cauyxy', 'Pygmalien', 'Opponent B', 'Diamond IV', 1360, 'Lost'),
                ('battle-ghost', 'GHOST', 'run-win', '2026-05-20T10:40:00Z', 9, 0,
                 'cauyxy', 'Mak', 'Ghost', 'Diamond I', 1500, 'Won');

            insert into combat_replay_videos (
                video_id, battle_id, video_relative_path, started_at_utc,
                duration_ms, file_size_bytes, status
            ) values
                ('video-old', 'battle-1', '2026-05-20/old.mp4', '2026-05-20T10:31:00Z',
                 1000, 2000, 'COMPLETED'),
                ('video-new', 'battle-1', '2026-05-20/new.mp4', '2026-05-20T10:32:00Z',
                 1200, 2200, 'COMPLETED'),
                ('video-failed', 'battle-2', '2026-05-20/failed.mp4', '2026-05-20T10:11:00Z',
                 null, null, 'FAILED');
            ",
        )
        .unwrap();
        drop(conn);

        let detail = get_history_run_detail(&database_path, "run-win")
            .unwrap()
            .unwrap();
        assert_eq!(detail.run.player_name.as_deref(), Some("cauyxy"));
        assert_eq!(detail.run.final_hour, Some(7));
        assert_eq!(
            detail.run.strip_url.as_deref(),
            Some("/images/shot-win/strip")
        );
        assert_eq!(detail.battles.len(), 2);
        assert_eq!(detail.battles[0].battle_id, "battle-1");
        assert_eq!(detail.battles[0].result, "win");
        assert_eq!(
            detail.battles[0]
                .video
                .as_ref()
                .map(|video| video.video_id.as_str()),
            Some("video-new")
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

        assert_eq!(
            delete_run_videos(&database_path, &video_dir, "run-win").unwrap(),
            1
        );
        assert!(!dated_videos_dir.join("old.mp4").exists());
        let detail = get_history_run_detail(&database_path, "run-win")
            .unwrap()
            .unwrap();
        assert_eq!(detail.battles[0].video, None);
    }
}

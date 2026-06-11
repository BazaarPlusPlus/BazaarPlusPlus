use std::{collections::HashMap, path::Path, time::Duration};

use rusqlite::{params, params_from_iter, Connection, OpenFlags, OptionalExtension};

use crate::history::dto::{HistoryBattleRow, HistorySummary};
use crate::history::mapper::map_battle_row;

pub struct RunRow {
    pub run_id: String,
    pub hero: String,
    pub game_mode: String,
    pub started_at_utc: String,
    pub ended_at_utc: Option<String>,
    pub last_seen_at_utc: String,
    pub status: String,
    pub victories: Option<i64>,
    pub losses: Option<i64>,
    pub final_day: Option<i64>,
    pub final_hour: Option<i64>,
    pub final_player_rank: Option<String>,
    pub final_player_rating: Option<i64>,
}

pub struct VideoRef {
    pub video_id: String,
    pub relative_path: String,
}

pub fn open_connection(database_path: &Path) -> Result<Connection, String> {
    let conn = Connection::open_with_flags(database_path, OpenFlags::SQLITE_OPEN_READ_ONLY)
        .map_err(|err| err.to_string())?;
    conn.busy_timeout(Duration::from_secs(2))
        .map_err(|err| err.to_string())?;
    Ok(conn)
}

pub fn open_write_connection(database_path: &Path) -> Result<Connection, String> {
    let conn = Connection::open_with_flags(database_path, OpenFlags::SQLITE_OPEN_READ_WRITE)
        .map_err(|err| err.to_string())?;
    conn.busy_timeout(Duration::from_secs(2))
        .map_err(|err| err.to_string())?;
    Ok(conn)
}

pub fn table_exists(conn: &Connection, table_name: &str) -> Result<bool, String> {
    conn.query_row(
        "select exists(select 1 from sqlite_master where type = 'table' and name = ?1)",
        [table_name],
        |row| row.get::<_, i64>(0),
    )
    .map(|value| value != 0)
    .map_err(|err| err.to_string())
}

pub fn sql_placeholders(count: usize) -> String {
    std::iter::repeat("?")
        .take(count)
        .collect::<Vec<_>>()
        .join(", ")
}

pub fn load_summary(conn: &Connection) -> Result<HistorySummary, String> {
    let (runs, completed_runs, win_runs, last_run_at_utc): (i64, i64, i64, Option<String>) = conn
        .query_row(
            "
            select
              count(*) as runs,
              sum(case when status = 'completed' then 1 else 0 end) as completed_runs,
              sum(case when status = 'completed' and coalesce(victories, 0) >= 10 then 1 else 0 end) as win_runs,
              max(coalesce(ended_at_utc, last_seen_at_utc, started_at_utc)) as last_run_at_utc
            from runs
            ",
            [],
            |row| Ok((row.get(0)?, row.get(1)?, row.get(2)?, row.get(3)?)),
        )
        .map_err(|err| err.to_string())?;
    let videos = if table_exists(conn, "combat_replay_videos")? {
        conn.query_row(
            "select count(*) from combat_replay_videos where status = 'COMPLETED'",
            [],
            |row| row.get(0),
        )
        .map_err(|err| err.to_string())?
    } else {
        0
    };

    Ok(HistorySummary {
        runs,
        videos,
        last_run_at_utc,
        win_rate: if completed_runs > 0 {
            Some(win_runs as f64 / completed_runs as f64)
        } else {
            None
        },
    })
}

pub fn list_run_rows(conn: &Connection, limit: i64) -> Result<Vec<RunRow>, String> {
    let mut stmt = conn
        .prepare(
            "
            select
              run_id, hero, game_mode, started_at_utc, ended_at_utc, last_seen_at_utc,
              status, victories, losses, final_day, final_hour, final_player_rank, final_player_rating
            from runs
            order by coalesce(ended_at_utc, last_seen_at_utc, started_at_utc) desc, run_id desc
            limit ?1
            ",
        )
        .map_err(|err| err.to_string())?;
    let rows = stmt
        .query_map([limit], map_run_row_from_statement)
        .map_err(|err| err.to_string())?;
    rows.collect::<Result<Vec<_>, _>>()
        .map_err(|err| err.to_string())
}

pub fn load_run_row(conn: &Connection, run_id: &str) -> Result<Option<RunRow>, String> {
    conn.query_row(
        "
        select
          run_id, hero, game_mode, started_at_utc, ended_at_utc, last_seen_at_utc,
          status, victories, losses, final_day, final_hour, final_player_rank, final_player_rating
        from runs
        where run_id = ?1
        ",
        [run_id],
        map_run_row_from_statement,
    )
    .optional()
    .map_err(|err| err.to_string())
}

fn map_run_row_from_statement(row: &rusqlite::Row<'_>) -> rusqlite::Result<RunRow> {
    Ok(RunRow {
        run_id: row.get(0)?,
        hero: row.get(1)?,
        game_mode: row.get(2)?,
        started_at_utc: row.get(3)?,
        ended_at_utc: row.get(4)?,
        last_seen_at_utc: row.get(5)?,
        status: row.get(6)?,
        victories: row.get(7)?,
        losses: row.get(8)?,
        final_day: row.get(9)?,
        final_hour: row.get(10)?,
        final_player_rank: row.get(11)?,
        final_player_rating: row.get(12)?,
    })
}

pub fn completed_video_counts(
    conn: &Connection,
    run_ids: &[String],
) -> Result<HashMap<String, i64>, String> {
    if run_ids.is_empty()
        || !table_exists(conn, "combat_replay_videos")?
        || !table_exists(conn, "battles")?
    {
        return Ok(HashMap::new());
    }

    let placeholders = sql_placeholders(run_ids.len());
    let sql = format!(
        "
        select b.run_id, count(*)
        from combat_replay_videos cv
        join battles b on b.battle_id = cv.battle_id
        where b.run_id in ({placeholders})
          and b.deleted_at_utc is null
          and cv.status = 'COMPLETED'
        group by b.run_id
        "
    );
    let mut stmt = conn.prepare(&sql).map_err(|err| err.to_string())?;
    let rows = stmt
        .query_map(params_from_iter(run_ids.iter()), |row| {
            Ok((row.get::<_, String>(0)?, row.get::<_, i64>(1)?))
        })
        .map_err(|err| err.to_string())?;

    let mut counts = HashMap::new();
    for row in rows {
        let (run_id, count) = row.map_err(|err| err.to_string())?;
        counts.insert(run_id, count);
    }

    Ok(counts)
}

pub fn completed_video_count(conn: &Connection, run_id: &str) -> Result<i64, String> {
    if !table_exists(conn, "combat_replay_videos")? || !table_exists(conn, "battles")? {
        return Ok(0);
    }

    conn.query_row(
        "
        select count(*)
        from combat_replay_videos cv
        join battles b on b.battle_id = cv.battle_id
        where b.run_id = ?1 and cv.status = 'COMPLETED' and b.deleted_at_utc is null
        ",
        [run_id],
        |row| row.get(0),
    )
    .map_err(|err| err.to_string())
}

pub fn local_player_name(conn: &Connection, run_id: &str) -> Result<Option<String>, String> {
    if !table_exists(conn, "battles")? {
        return Ok(None);
    }

    conn.query_row(
        "
        select player_name
        from battles
        where run_id = ?1 and source = 'LOCAL' and player_name is not null and deleted_at_utc is null
        order by recorded_at_utc desc, battle_id desc
        limit 1
        ",
        [run_id],
        |row| row.get(0),
    )
    .optional()
    .map_err(|err| err.to_string())
}

pub fn load_battle_rows(conn: &Connection, run_id: &str) -> Result<Vec<HistoryBattleRow>, String> {
    if !table_exists(conn, "battles")? {
        return Ok(Vec::new());
    }

    if table_exists(conn, "combat_replay_videos")? {
        let mut stmt = conn
            .prepare(
                "
                select
                  b.battle_id, b.day, b.hour, b.result,
                  b.opponent_hero, b.opponent_name, b.opponent_rank, b.opponent_rating,
                  cv.video_id, cv.status, cv.file_size_bytes, cv.duration_ms
                from battles b
                left join combat_replay_videos cv on cv.video_id = (
                  select video_id
                  from combat_replay_videos
                  where battle_id = b.battle_id and status = 'COMPLETED'
                  order by started_at_utc desc, video_id desc
                  limit 1
                )
                where b.run_id = ?1 and b.source = 'LOCAL' and b.deleted_at_utc is null
                order by b.recorded_at_utc desc, b.battle_id desc
                ",
            )
            .map_err(|err| err.to_string())?;
        let rows = stmt
            .query_map([run_id], |row| {
                let video_id: Option<String> = row.get(8)?;
                Ok(map_battle_row(
                    row.get(0)?,
                    row.get(1)?,
                    row.get(2)?,
                    row.get::<_, Option<String>>(3)?,
                    row.get(4)?,
                    row.get(5)?,
                    row.get(6)?,
                    row.get(7)?,
                    video_id,
                    row.get(9).ok().flatten(),
                    row.get(10).ok().flatten(),
                    row.get(11).ok().flatten(),
                ))
            })
            .map_err(|err| err.to_string())?;
        return rows
            .collect::<Result<Vec<_>, _>>()
            .map_err(|err| err.to_string());
    }

    let mut stmt = conn
        .prepare(
            "
            select
              battle_id, day, hour, result,
              opponent_hero, opponent_name, opponent_rank, opponent_rating
            from battles
            where run_id = ?1 and source = 'LOCAL' and deleted_at_utc is null
            order by recorded_at_utc desc, battle_id desc
            ",
        )
        .map_err(|err| err.to_string())?;
    let rows = stmt
        .query_map([run_id], |row| {
            Ok(map_battle_row(
                row.get(0)?,
                row.get(1)?,
                row.get(2)?,
                row.get::<_, Option<String>>(3)?,
                row.get(4)?,
                row.get(5)?,
                row.get(6)?,
                row.get(7)?,
                None,
                None,
                None,
                None,
            ))
        })
        .map_err(|err| err.to_string())?;
    rows.collect::<Result<Vec<_>, _>>()
        .map_err(|err| err.to_string())
}

pub fn load_battle_video_ref(
    conn: &Connection,
    battle_id: &str,
    video_id: Option<&str>,
) -> Result<Option<VideoRef>, String> {
    if !table_exists(conn, "combat_replay_videos")? {
        return Ok(None);
    }

    if let Some(video_id) = video_id {
        return conn
            .query_row(
                "
                select video_id, video_relative_path
                from combat_replay_videos
                where battle_id = ?1 and video_id = ?2 and status = 'COMPLETED'
                ",
                params![battle_id, video_id],
                |row| {
                    Ok(VideoRef {
                        video_id: row.get(0)?,
                        relative_path: row.get(1)?,
                    })
                },
            )
            .optional()
            .map_err(|err| err.to_string());
    }

    conn.query_row(
        "
        select video_id, video_relative_path
        from combat_replay_videos
        where battle_id = ?1 and status = 'COMPLETED'
        order by started_at_utc desc, video_id desc
        limit 1
        ",
        [battle_id],
        |row| {
            Ok(VideoRef {
                video_id: row.get(0)?,
                relative_path: row.get(1)?,
            })
        },
    )
    .optional()
    .map_err(|err| err.to_string())
}

pub fn load_run_video_refs(conn: &Connection, run_id: &str) -> Result<Vec<VideoRef>, String> {
    if !table_exists(conn, "combat_replay_videos")? || !table_exists(conn, "battles")? {
        return Ok(Vec::new());
    }

    let mut stmt = conn
        .prepare(
            "
            select cv.video_id, cv.video_relative_path
            from combat_replay_videos cv
            join battles b on b.battle_id = cv.battle_id
            where b.run_id = ?1 and b.deleted_at_utc is null and cv.status = 'COMPLETED'
            ",
        )
        .map_err(|err| err.to_string())?;
    let rows = stmt
        .query_map([run_id], |row| {
            Ok(VideoRef {
                video_id: row.get(0)?,
                relative_path: row.get(1)?,
            })
        })
        .map_err(|err| err.to_string())?;
    rows.collect::<Result<Vec<_>, _>>()
        .map_err(|err| err.to_string())
}

pub fn load_run_id_for_battle(
    conn: &Connection,
    battle_id: &str,
) -> Result<Option<String>, String> {
    if !table_exists(conn, "battles")? {
        return Ok(None);
    }

    conn.query_row(
        "select run_id from battles where battle_id = ?1",
        [battle_id],
        |row| row.get(0),
    )
    .optional()
    .map_err(|err| err.to_string())
}

#[cfg(test)]
mod tests {
    use super::open_connection;

    #[test]
    fn open_connection_does_not_create_missing_database() {
        let temp_dir = tempfile::tempdir().unwrap();
        let database_path = temp_dir.path().join("missing.db");

        assert!(open_connection(&database_path).is_err());
        assert!(!database_path.exists());
    }
}

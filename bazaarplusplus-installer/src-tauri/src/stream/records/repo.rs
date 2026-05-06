use rusqlite::Connection;
use std::path::Path;

#[derive(Clone, Debug)]
pub(crate) struct OverlayRecordRow {
    pub(super) id: String,
    pub(super) hero: String,
    pub(super) game_mode: String,
    pub(super) captured_at: String,
    pub(super) captured_at_utc: String,
    pub(super) image_path: Option<String>,
    pub(super) wins: Option<i64>,
    pub(super) position: Option<i64>,
    pub(super) battle_count: Option<i64>,
    pub(super) rank: Option<String>,
    pub(super) rating: Option<i64>,
}

pub(super) fn load_latest_overlay_record(
    database_path: &Path,
    from: Option<&str>,
    offset: usize,
) -> Result<Option<OverlayRecordRow>, String> {
    if !database_path.exists() {
        return Ok(None);
    }

    let conn = Connection::open(database_path).map_err(|err| err.to_string())?;
    if !table_exists(&conn, "run_screenshots")? {
        return Ok(None);
    }

    let offset_value = i64::try_from(offset).map_err(|err| err.to_string())?;
    let mut stmt = if from.is_some() {
        conn.prepare(
            "
select
  rs.screenshot_id,
  coalesce(nullif(trim(rs.hero_name), ''), 'Unknown') as hero,
  'End of run' as game_mode,
  coalesce(nullif(trim(rs.captured_at_local), ''), rs.captured_at_utc) as captured_at,
  rs.image_relative_path as image_path,
  rs.victories_at_capture as wins,
  rs.player_position,
  rs.day as battle_count,
  nullif(trim(rs.player_rank), '') as player_rank,
  rs.player_rating as player_rating,
  rs.captured_at_utc
from run_screenshots rs
where rs.capture_source = 'end_of_run_auto'
  and datetime(rs.captured_at_utc) >= datetime(?1)
order by datetime(rs.captured_at_utc) desc, rs.screenshot_id desc
limit 1 offset ?2
",
        )
    } else {
        conn.prepare(
            "
select
  rs.screenshot_id,
  coalesce(nullif(trim(rs.hero_name), ''), 'Unknown') as hero,
  'End of run' as game_mode,
  coalesce(nullif(trim(rs.captured_at_local), ''), rs.captured_at_utc) as captured_at,
  rs.image_relative_path as image_path,
  rs.victories_at_capture as wins,
  rs.player_position,
  rs.day as battle_count,
  nullif(trim(rs.player_rank), '') as player_rank,
  rs.player_rating as player_rating,
  rs.captured_at_utc
from run_screenshots rs
where rs.capture_source = 'end_of_run_auto'
order by datetime(rs.captured_at_utc) desc, rs.screenshot_id desc
limit 1 offset ?1
",
        )
    }
    .map_err(|err| err.to_string())?;
    let mut rows = if let Some(from) = from {
        stmt.query((from, offset_value))
            .map_err(|err| err.to_string())?
    } else {
        stmt.query([offset_value]).map_err(|err| err.to_string())?
    };
    let Some(row) = rows.next().map_err(|err| err.to_string())? else {
        return Ok(None);
    };

    Ok(Some(map_overlay_record_row(row)?))
}

pub(super) fn load_overlay_record_count(
    database_path: &Path,
    from: Option<&str>,
) -> Result<usize, String> {
    if !database_path.exists() {
        return Ok(0);
    }

    let conn = Connection::open(database_path).map_err(|err| err.to_string())?;
    if !table_exists(&conn, "run_screenshots")? {
        return Ok(0);
    }

    let count: i64 = if let Some(from) = from {
        conn.query_row(
            "
select count(*)
from run_screenshots rs
where rs.capture_source = 'end_of_run_auto'
  and datetime(rs.captured_at_utc) >= datetime(?1)
",
            [from],
            |row| row.get(0),
        )
        .map_err(|err| err.to_string())?
    } else {
        conn.query_row(
            "
select count(*)
from run_screenshots rs
where rs.capture_source = 'end_of_run_auto'
",
            [],
            |row| row.get(0),
        )
        .map_err(|err| err.to_string())?
    };

    usize::try_from(count).map_err(|err| err.to_string())
}

pub(super) fn load_overlay_record_list(
    database_path: &Path,
    from: Option<&str>,
    limit: Option<usize>,
) -> Result<Vec<OverlayRecordRow>, String> {
    if !database_path.exists() {
        return Ok(Vec::new());
    }

    let conn = Connection::open(database_path).map_err(|err| err.to_string())?;
    if !table_exists(&conn, "run_screenshots")? {
        return Ok(Vec::new());
    }

    let effective_limit = limit.unwrap_or(20);
    if effective_limit == 0 {
        return Ok(Vec::new());
    }
    let limit_value = i64::try_from(effective_limit).map_err(|err| err.to_string())?;

    let mut records = Vec::new();
    if let Some(from) = from {
        let mut stmt = conn
            .prepare(
                "
select
  rs.screenshot_id,
  coalesce(nullif(trim(rs.hero_name), ''), 'Unknown') as hero,
  'End of run' as game_mode,
  coalesce(nullif(trim(rs.captured_at_local), ''), rs.captured_at_utc) as captured_at,
  rs.image_relative_path as image_path,
  rs.victories_at_capture as wins,
  rs.player_position,
  rs.day as battle_count,
  nullif(trim(rs.player_rank), '') as player_rank,
  rs.player_rating as player_rating,
  rs.captured_at_utc
from run_screenshots rs
where rs.capture_source = 'end_of_run_auto'
  and datetime(rs.captured_at_utc) >= datetime(?1)
order by datetime(rs.captured_at_utc) desc, rs.screenshot_id desc
limit ?2
",
            )
            .map_err(|err| err.to_string())?;
        let mut rows = stmt
            .query((from, limit_value))
            .map_err(|err| err.to_string())?;
        while let Some(row) = rows.next().map_err(|err| err.to_string())? {
            records.push(map_overlay_record_row(row)?);
        }
    } else {
        let mut stmt = conn
            .prepare(
                "
select
  rs.screenshot_id,
  coalesce(nullif(trim(rs.hero_name), ''), 'Unknown') as hero,
  'End of run' as game_mode,
  coalesce(nullif(trim(rs.captured_at_local), ''), rs.captured_at_utc) as captured_at,
  rs.image_relative_path as image_path,
  rs.victories_at_capture as wins,
  rs.player_position,
  rs.day as battle_count,
  nullif(trim(rs.player_rank), '') as player_rank,
  rs.player_rating as player_rating,
  rs.captured_at_utc
from run_screenshots rs
where rs.capture_source = 'end_of_run_auto'
order by datetime(rs.captured_at_utc) desc, rs.screenshot_id desc
limit ?1
",
            )
            .map_err(|err| err.to_string())?;
        let mut rows = stmt.query([limit_value]).map_err(|err| err.to_string())?;
        while let Some(row) = rows.next().map_err(|err| err.to_string())? {
            records.push(map_overlay_record_row(row)?);
        }
    }

    Ok(records)
}

pub(super) fn load_overlay_record_by_id(
    database_path: &Path,
    record_id: &str,
) -> Result<Option<OverlayRecordRow>, String> {
    if !database_path.exists() {
        return Ok(None);
    }

    let conn = Connection::open(database_path).map_err(|err| err.to_string())?;
    if !table_exists(&conn, "run_screenshots")? {
        return Ok(None);
    }

    let mut stmt = conn
        .prepare(
            "
select
  rs.screenshot_id,
  coalesce(nullif(trim(rs.hero_name), ''), 'Unknown') as hero,
  'End of run' as game_mode,
  coalesce(nullif(trim(rs.captured_at_local), ''), rs.captured_at_utc) as captured_at,
  rs.image_relative_path as image_path,
  rs.victories_at_capture as wins,
  rs.player_position,
  rs.day as battle_count,
  nullif(trim(rs.player_rank), '') as player_rank,
  rs.player_rating as player_rating,
  rs.captured_at_utc
from run_screenshots rs
where rs.capture_source = 'end_of_run_auto'
  and rs.screenshot_id = ?1
limit 1
",
        )
        .map_err(|err| err.to_string())?;

    let mut rows = stmt.query([record_id]).map_err(|err| err.to_string())?;
    let Some(row) = rows.next().map_err(|err| err.to_string())? else {
        return Ok(None);
    };

    Ok(Some(map_overlay_record_row(row)?))
}

pub(super) fn delete_overlay_record_row(
    database_path: &Path,
    record_id: &str,
) -> Result<(bool, Option<String>), String> {
    if !database_path.exists() {
        return Ok((false, None));
    }

    let conn = Connection::open(database_path).map_err(|err| err.to_string())?;
    if !table_exists(&conn, "run_screenshots")? {
        return Ok((false, None));
    }

    let image_relative_path =
        load_overlay_record_by_id(database_path, record_id)?.and_then(|row| row.image_path);

    let deleted = conn
        .execute(
            "
delete from run_screenshots
where capture_source = 'end_of_run_auto'
  and screenshot_id = ?1
",
            [record_id],
        )
        .map_err(|err| err.to_string())?;

    Ok((deleted > 0, image_relative_path))
}

fn table_exists(conn: &Connection, table_name: &str) -> Result<bool, String> {
    let exists = conn
        .query_row(
            "select exists(
                select 1
                from sqlite_master
                where type = 'table' and name = ?1
            )",
            [table_name],
            |row| row.get::<_, i64>(0),
        )
        .map_err(|err| err.to_string())?;

    Ok(exists != 0)
}

fn map_overlay_record_row(row: &rusqlite::Row<'_>) -> Result<OverlayRecordRow, String> {
    Ok(OverlayRecordRow {
        id: row.get(0).map_err(|err| err.to_string())?,
        hero: row.get(1).map_err(|err| err.to_string())?,
        game_mode: row.get(2).map_err(|err| err.to_string())?,
        captured_at: row.get(3).map_err(|err| err.to_string())?,
        image_path: row.get(4).map_err(|err| err.to_string())?,
        wins: row.get(5).map_err(|err| err.to_string())?,
        position: row.get(6).map_err(|err| err.to_string())?,
        battle_count: row.get(7).map_err(|err| err.to_string())?,
        rank: row.get(8).map_err(|err| err.to_string())?,
        rating: row.get(9).map_err(|err| err.to_string())?,
        captured_at_utc: row.get(10).map_err(|err| err.to_string())?,
    })
}

#[cfg(test)]
mod tests {
    use super::{
        load_latest_overlay_record, load_overlay_record_by_id, load_overlay_record_count,
        load_overlay_record_list,
    };

    fn create_run_screenshots_table(conn: &rusqlite::Connection) {
        conn.execute(
            "create table run_screenshots (
                screenshot_id text primary key,
                run_id text,
                battle_id text,
                capture_source text not null,
                is_primary integer not null default 0,
                image_relative_path text not null,
                captured_at_local text not null,
                captured_at_utc text not null,
                day integer,
                player_rank text,
                player_rating integer,
                player_position integer,
                victories_at_capture integer,
                hero_name text
            )",
            [],
        )
        .unwrap();
    }

    #[test]
    fn latest_overlay_record_returns_none_when_database_has_no_rows() {
        let temp = tempfile::NamedTempFile::new().unwrap();
        let conn = rusqlite::Connection::open(temp.path()).unwrap();
        create_run_screenshots_table(&conn);

        let latest = load_latest_overlay_record(temp.path(), None, 0).unwrap();
        assert!(latest.is_none());
    }

    #[test]
    fn latest_overlay_record_reads_latest_end_of_run_snapshot() {
        let temp = tempfile::NamedTempFile::new().unwrap();
        let conn = rusqlite::Connection::open(temp.path()).unwrap();
        create_run_screenshots_table(&conn);
        conn.execute(
            "insert into run_screenshots (
                screenshot_id, run_id, capture_source, image_relative_path, captured_at_local,
                captured_at_utc, day, player_rank, player_rating, player_position, victories_at_capture, hero_name
             ) values (
                'snap-1', 'run-1', 'end_of_run_auto', '2026-04-10\\shot-1.png',
                '2026-04-11T04:30:05+08:00', '2026-04-10T20:30:05+00:00',
                14, 'Diamond', 1942, 1, 10, 'Mak'
             )",
            [],
        )
        .unwrap();

        let latest = load_latest_overlay_record(temp.path(), None, 0)
            .unwrap()
            .unwrap();
        assert_eq!(latest.id, "snap-1");
        assert_eq!(latest.hero, "Mak");
        assert_eq!(latest.game_mode, "End of run");
        assert_eq!(latest.image_path.as_deref(), Some("2026-04-10\\shot-1.png"));
        assert_eq!(latest.wins, Some(10));
        assert_eq!(latest.position, Some(1));
        assert_eq!(latest.battle_count, Some(14));
        assert_eq!(latest.rank.as_deref(), Some("Diamond"));
        assert_eq!(latest.rating, Some(1942));
    }

    #[test]
    fn latest_overlay_record_ignores_other_snapshot_types() {
        let temp = tempfile::NamedTempFile::new().unwrap();
        let conn = rusqlite::Connection::open(temp.path()).unwrap();
        create_run_screenshots_table(&conn);
        conn.execute(
            "insert into run_screenshots (
                screenshot_id, run_id, capture_source, image_relative_path, captured_at_local, captured_at_utc
             ) values
             ('snap-1', 'run-1', 'pvp_battle_start', 'battle-1.png', '2026-04-10T20:30:05+00:00', '2026-04-10T20:30:05+00:00'),
             ('snap-2', 'run-2', 'end_of_run_auto', 'final-2.png', '2026-04-10T21:30:05+00:00', '2026-04-10T21:30:05+00:00')",
            [],
        )
        .unwrap();

        let latest = load_latest_overlay_record(temp.path(), None, 0)
            .unwrap()
            .unwrap();
        assert_eq!(latest.id, "snap-2");
    }

    #[test]
    fn latest_overlay_record_returns_none_when_database_file_is_missing() {
        let temp_dir = tempfile::tempdir().unwrap();
        let missing = temp_dir.path().join("missing-bazaarplusplus.db");
        let latest = load_latest_overlay_record(&missing, None, 0).unwrap();

        assert!(latest.is_none());
    }

    #[test]
    fn latest_overlay_record_supports_backtracking_from_latest() {
        let temp = tempfile::NamedTempFile::new().unwrap();
        let conn = rusqlite::Connection::open(temp.path()).unwrap();
        create_run_screenshots_table(&conn);
        conn.execute(
            "insert into run_screenshots (
                screenshot_id, run_id, capture_source, image_relative_path, captured_at_local, captured_at_utc, hero_name
             ) values
             ('snap-1', 'run-1', 'end_of_run_auto', 'final-1.png', '2026-04-10T20:30:05+00:00', '2026-04-10T20:30:05+00:00', 'Mak'),
             ('snap-2', 'run-2', 'end_of_run_auto', 'final-2.png', '2026-04-10T21:30:05+00:00', '2026-04-10T21:30:05+00:00', 'Pygmalien'),
             ('snap-3', 'run-3', 'end_of_run_auto', 'final-3.png', '2026-04-10T22:30:05+00:00', '2026-04-10T22:30:05+00:00', 'Vanessa')",
            [],
        )
        .unwrap();

        let latest = load_latest_overlay_record(temp.path(), None, 0)
            .unwrap()
            .unwrap();
        let previous = load_latest_overlay_record(temp.path(), None, 1)
            .unwrap()
            .unwrap();
        let oldest = load_latest_overlay_record(temp.path(), None, 2)
            .unwrap()
            .unwrap();
        let beyond = load_latest_overlay_record(temp.path(), None, 3).unwrap();

        assert_eq!(latest.id, "snap-3");
        assert_eq!(previous.id, "snap-2");
        assert_eq!(oldest.id, "snap-1");
        assert_eq!(latest.hero, "Vanessa");
        assert_eq!(previous.hero, "Pygmalien");
        assert!(beyond.is_none());
    }

    #[test]
    fn latest_overlay_record_filters_from_stream_start_time() {
        let temp = tempfile::NamedTempFile::new().unwrap();
        let conn = rusqlite::Connection::open(temp.path()).unwrap();
        create_run_screenshots_table(&conn);
        conn.execute(
            "insert into run_screenshots (
                screenshot_id, run_id, capture_source, image_relative_path, captured_at_local, captured_at_utc, hero_name
             ) values
             ('snap-before', 'run-1', 'end_of_run_auto', 'before.png', '2026-04-10T19:30:05+00:00', '2026-04-10T19:30:05+00:00', 'Mak'),
             ('snap-after', 'run-2', 'end_of_run_auto', 'after.png', '2026-04-10T21:30:05+00:00', '2026-04-10T21:30:05+00:00', 'Pygmalien')",
            [],
        )
        .unwrap();

        let latest = load_latest_overlay_record(temp.path(), Some("2026-04-10T20:00:00+00:00"), 0)
            .unwrap()
            .unwrap();

        assert_eq!(latest.id, "snap-after");
    }

    #[test]
    fn overlay_record_count_only_counts_records_after_stream_start() {
        let temp = tempfile::NamedTempFile::new().unwrap();
        let conn = rusqlite::Connection::open(temp.path()).unwrap();
        create_run_screenshots_table(&conn);
        conn.execute(
            "insert into run_screenshots (
                screenshot_id, run_id, capture_source, image_relative_path, captured_at_local, captured_at_utc
             ) values
             ('snap-before', 'run-1', 'end_of_run_auto', 'before.png', '2026-04-10T19:30:05+00:00', '2026-04-10T19:30:05+00:00'),
             ('snap-after-1', 'run-2', 'end_of_run_auto', 'after-1.png', '2026-04-10T21:30:05+00:00', '2026-04-10T21:30:05+00:00'),
             ('snap-after-2', 'run-3', 'end_of_run_auto', 'after-2.png', '2026-04-10T22:30:05+00:00', '2026-04-10T22:30:05+00:00'),
             ('snap-mid', 'run-4', 'pvp_battle_start', 'mid.png', '2026-04-10T23:30:05+00:00', '2026-04-10T23:30:05+00:00')",
            [],
        )
        .unwrap();

        let count =
            load_overlay_record_count(temp.path(), Some("2026-04-10T20:00:00+00:00")).unwrap();

        assert_eq!(count, 2);
    }

    #[test]
    fn load_overlay_record_by_id_reads_matching_snapshot() {
        let temp = tempfile::NamedTempFile::new().unwrap();
        let conn = rusqlite::Connection::open(temp.path()).unwrap();
        create_run_screenshots_table(&conn);
        conn.execute(
            "insert into run_screenshots (
                screenshot_id, run_id, capture_source, image_relative_path, captured_at_local, captured_at_utc, hero_name
             ) values ('snap-1', 'run-1', 'end_of_run_auto', 'match-1.png', '2026-04-10T20:30:05+00:00', '2026-04-10T20:30:05+00:00', 'Mak')",
            [],
        )
        .unwrap();

        let record = load_overlay_record_by_id(temp.path(), "snap-1")
            .unwrap()
            .unwrap();

        assert_eq!(record.id, "snap-1");
    }

    #[test]
    fn latest_overlay_record_uses_unknown_hero_when_screenshot_is_anonymous() {
        let temp = tempfile::NamedTempFile::new().unwrap();
        let conn = rusqlite::Connection::open(temp.path()).unwrap();
        create_run_screenshots_table(&conn);
        conn.execute(
            "insert into run_screenshots (
                screenshot_id, run_id, capture_source, image_relative_path, captured_at_local, captured_at_utc
             ) values (
                'snap-1', null, 'end_of_run_auto', 'anon.png', '2026-04-10T20:30:05+00:00', '2026-04-10T20:30:05+00:00'
             )",
            [],
        )
        .unwrap();

        let latest = load_latest_overlay_record(temp.path(), None, 0)
            .unwrap()
            .unwrap();

        assert_eq!(latest.hero, "Unknown");
        assert_eq!(latest.game_mode, "End of run");
    }

    #[test]
    fn overlay_record_list_returns_latest_records_in_descending_order() {
        let temp = tempfile::NamedTempFile::new().unwrap();
        let conn = rusqlite::Connection::open(temp.path()).unwrap();
        create_run_screenshots_table(&conn);
        conn.execute(
            "insert into run_screenshots (
                screenshot_id, run_id, capture_source, image_relative_path, captured_at_local,
                captured_at_utc, victories_at_capture, hero_name
             ) values
             ('snap-1', 'run-1', 'end_of_run_auto', 'first.png', '2026-04-10T20:30:05+00:00', '2026-04-10T20:30:05+00:00', 7, 'Mak'),
             ('snap-2', 'run-2', 'end_of_run_auto', 'second.png', '2026-04-10T21:30:05+00:00', '2026-04-10T21:30:05+00:00', 10, 'Pygmalien'),
             ('snap-3', 'run-3', 'end_of_run_auto', 'third.png', '2026-04-10T22:30:05+00:00', '2026-04-10T22:30:05+00:00', 4, 'Vanessa')",
            [],
        )
        .unwrap();

        let records = load_overlay_record_list(temp.path(), None, Some(2)).unwrap();

        assert_eq!(records.len(), 2);
        assert_eq!(records[0].id, "snap-3");
        assert_eq!(records[1].id, "snap-2");
    }

    #[test]
    fn overlay_record_list_without_limit_returns_all_records() {
        let temp = tempfile::NamedTempFile::new().unwrap();
        let conn = rusqlite::Connection::open(temp.path()).unwrap();
        create_run_screenshots_table(&conn);
        conn.execute(
            "insert into run_screenshots (
                screenshot_id, run_id, capture_source, image_relative_path, captured_at_local,
                captured_at_utc, hero_name
             ) values
             ('snap-1', 'run-1', 'end_of_run_auto', 'first.png', '2026-04-10T20:30:05+00:00', '2026-04-10T20:30:05+00:00', 'Mak'),
             ('snap-2', 'run-2', 'end_of_run_auto', 'second.png', '2026-04-10T21:30:05+00:00', '2026-04-10T21:30:05+00:00', 'Pygmalien'),
             ('snap-3', 'run-3', 'end_of_run_auto', 'third.png', '2026-04-10T22:30:05+00:00', '2026-04-10T22:30:05+00:00', 'Vanessa')",
            [],
        )
        .unwrap();

        let records = load_overlay_record_list(temp.path(), None, None).unwrap();

        assert_eq!(records.len(), 3);
        assert_eq!(records[0].id, "snap-3");
        assert_eq!(records[2].id, "snap-1");
    }
}

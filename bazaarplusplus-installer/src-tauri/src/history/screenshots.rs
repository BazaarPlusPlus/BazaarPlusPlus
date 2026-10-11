use std::{collections::HashMap, path::Path};

use chrono::DateTime;
use rusqlite::{params_from_iter, Connection, OptionalExtension};

use crate::history::instant::{local_text, utc_text};
use crate::history::queries::{open_connection, sql_placeholders, table_exists};

pub struct ScreenshotRef {
    pub id: String,
    pub image_relative_path: String,
}

#[derive(Clone, Debug)]
pub struct OverlaySnapshotRow {
    pub id: String,
    pub hero: String,
    pub game_mode: String,
    pub captured_at: String,
    pub captured_at_utc: String,
    pub image_path: Option<String>,
    pub wins: Option<i64>,
    pub battle_count: Option<i64>,
    pub rank: Option<String>,
    pub rating: Option<i64>,
}

pub fn primary_screenshot_ids(
    conn: &Connection,
    run_ids: &[String],
) -> Result<HashMap<String, String>, String> {
    if run_ids.is_empty() || !table_exists(conn, "run_screenshots")? {
        return Ok(HashMap::new());
    }

    let mut selected = HashMap::new();
    let placeholders = sql_placeholders(run_ids.len());
    let primary_sql = format!(
        "
        select run_id, screenshot_id
        from run_screenshots
        where run_id in ({placeholders}) and is_primary = 1
        "
    );
    let mut stmt = conn.prepare(&primary_sql).map_err(|err| err.to_string())?;
    let rows = stmt
        .query_map(params_from_iter(run_ids.iter()), |row| {
            Ok((row.get::<_, String>(0)?, row.get::<_, String>(1)?))
        })
        .map_err(|err| err.to_string())?;

    for row in rows {
        let (run_id, screenshot_id) = row.map_err(|err| err.to_string())?;
        selected.entry(run_id).or_insert(screenshot_id);
    }

    let missing_run_ids = run_ids
        .iter()
        .filter(|run_id| !selected.contains_key(*run_id))
        .collect::<Vec<_>>();
    if missing_run_ids.is_empty() {
        return Ok(selected);
    }

    let placeholders = sql_placeholders(missing_run_ids.len());
    let fallback_sql = format!(
        "
        select run_id, screenshot_id
        from run_screenshots
        where run_id in ({placeholders}) and capture_source = 'end_of_run_auto'
        order by run_id asc, captured_at_ms desc, screenshot_id desc
        "
    );
    let mut stmt = conn.prepare(&fallback_sql).map_err(|err| err.to_string())?;
    let rows = stmt
        .query_map(params_from_iter(missing_run_ids), |row| {
            Ok((row.get::<_, String>(0)?, row.get::<_, String>(1)?))
        })
        .map_err(|err| err.to_string())?;

    for row in rows {
        let (run_id, screenshot_id) = row.map_err(|err| err.to_string())?;
        selected.entry(run_id).or_insert(screenshot_id);
    }

    Ok(selected)
}

pub fn primary_screenshot(
    conn: &Connection,
    run_id: &str,
) -> Result<Option<ScreenshotRef>, String> {
    if !table_exists(conn, "run_screenshots")? {
        return Ok(None);
    }

    let primary = conn
        .query_row(
            "
            select screenshot_id, image_relative_path
            from run_screenshots
            where run_id = ?1 and is_primary = 1
            limit 1
            ",
            [run_id],
            |row| {
                Ok(ScreenshotRef {
                    id: row.get(0)?,
                    image_relative_path: row.get(1)?,
                })
            },
        )
        .optional()
        .map_err(|err| err.to_string())?;
    if primary.is_some() {
        return Ok(primary);
    }

    conn.query_row(
        "
        select screenshot_id, image_relative_path
        from run_screenshots
        where run_id = ?1 and capture_source = 'end_of_run_auto'
        order by captured_at_ms desc, screenshot_id desc
        limit 1
        ",
        [run_id],
        |row| {
            Ok(ScreenshotRef {
                id: row.get(0)?,
                image_relative_path: row.get(1)?,
            })
        },
    )
    .optional()
    .map_err(|err| err.to_string())
}

pub fn load_latest_overlay_snapshot(
    database_path: &Path,
    from: Option<&str>,
    offset: usize,
) -> Result<Option<OverlaySnapshotRow>, String> {
    if !database_path.exists() {
        return Ok(None);
    }

    let conn = open_connection(database_path)?;
    if !table_exists(&conn, "run_screenshots")? {
        return Ok(None);
    }

    let offset_value = i64::try_from(offset).map_err(|err| err.to_string())?;
    let from_ms = from.map(overlay_from_ms);
    let mut stmt = if from.is_some() {
        conn.prepare(
            "
select
  rs.screenshot_id,
  coalesce(nullif(trim(rs.hero_name), ''), 'Unknown') as hero,
  'End of run' as game_mode,
  rs.captured_utc_offset_minutes,
  rs.image_relative_path as image_path,
  rs.victories_at_capture as wins,
  rs.day as battle_count,
  nullif(trim(rs.player_rank), '') as player_rank,
  rs.player_rating as player_rating,
  rs.captured_at_ms
from run_screenshots rs
where rs.capture_source = 'end_of_run_auto'
  and rs.captured_at_ms >= ?1
order by rs.captured_at_ms desc, rs.screenshot_id desc
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
  rs.captured_utc_offset_minutes,
  rs.image_relative_path as image_path,
  rs.victories_at_capture as wins,
  rs.day as battle_count,
  nullif(trim(rs.player_rank), '') as player_rank,
  rs.player_rating as player_rating,
  rs.captured_at_ms
from run_screenshots rs
where rs.capture_source = 'end_of_run_auto'
order by rs.captured_at_ms desc, rs.screenshot_id desc
limit 1 offset ?1
",
        )
    }
    .map_err(|err| err.to_string())?;
    let mut rows = if let Some(from_ms) = from_ms {
        stmt.query((from_ms, offset_value))
            .map_err(|err| err.to_string())?
    } else {
        stmt.query([offset_value]).map_err(|err| err.to_string())?
    };
    let Some(row) = rows.next().map_err(|err| err.to_string())? else {
        return Ok(None);
    };

    Ok(Some(map_overlay_snapshot_row(row)?))
}

pub fn load_overlay_snapshot_count(
    database_path: &Path,
    from: Option<&str>,
) -> Result<usize, String> {
    if !database_path.exists() {
        return Ok(0);
    }

    let conn = open_connection(database_path)?;
    if !table_exists(&conn, "run_screenshots")? {
        return Ok(0);
    }

    let count: i64 = if let Some(from) = from {
        let from_ms = overlay_from_ms(from);
        conn.query_row(
            "
select count(*)
from run_screenshots rs
where rs.capture_source = 'end_of_run_auto'
  and rs.captured_at_ms >= ?1
",
            [from_ms],
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

pub fn load_overlay_snapshot_list(
    database_path: &Path,
    from: Option<&str>,
    limit: Option<usize>,
) -> Result<Vec<OverlaySnapshotRow>, String> {
    if !database_path.exists() {
        return Ok(Vec::new());
    }

    let conn = open_connection(database_path)?;
    if !table_exists(&conn, "run_screenshots")? {
        return Ok(Vec::new());
    }

    let effective_limit = limit.unwrap_or(20);
    if effective_limit == 0 {
        return Ok(Vec::new());
    }
    let limit_value = i64::try_from(effective_limit).map_err(|err| err.to_string())?;
    let from_ms = from.map(overlay_from_ms);

    let mut records = Vec::new();
    let mut stmt = if from.is_some() {
        conn.prepare(
            "
select
  rs.screenshot_id,
  coalesce(nullif(trim(rs.hero_name), ''), 'Unknown') as hero,
  'End of run' as game_mode,
  rs.captured_utc_offset_minutes,
  rs.image_relative_path as image_path,
  rs.victories_at_capture as wins,
  rs.day as battle_count,
  nullif(trim(rs.player_rank), '') as player_rank,
  rs.player_rating as player_rating,
  rs.captured_at_ms
from run_screenshots rs
where rs.capture_source = 'end_of_run_auto'
  and rs.captured_at_ms >= ?1
order by rs.captured_at_ms desc, rs.screenshot_id desc
limit ?2
",
        )
    } else {
        conn.prepare(
            "
select
  rs.screenshot_id,
  coalesce(nullif(trim(rs.hero_name), ''), 'Unknown') as hero,
  'End of run' as game_mode,
  rs.captured_utc_offset_minutes,
  rs.image_relative_path as image_path,
  rs.victories_at_capture as wins,
  rs.day as battle_count,
  nullif(trim(rs.player_rank), '') as player_rank,
  rs.player_rating as player_rating,
  rs.captured_at_ms
from run_screenshots rs
where rs.capture_source = 'end_of_run_auto'
order by rs.captured_at_ms desc, rs.screenshot_id desc
limit ?1
",
        )
    }
    .map_err(|err| err.to_string())?;
    let mut rows = if let Some(from_ms) = from_ms {
        stmt.query((from_ms, limit_value))
            .map_err(|err| err.to_string())?
    } else {
        stmt.query([limit_value]).map_err(|err| err.to_string())?
    };
    while let Some(row) = rows.next().map_err(|err| err.to_string())? {
        records.push(map_overlay_snapshot_row(row)?);
    }

    Ok(records)
}

pub fn load_overlay_snapshot_by_id(
    database_path: &Path,
    record_id: &str,
) -> Result<Option<OverlaySnapshotRow>, String> {
    if !database_path.exists() {
        return Ok(None);
    }

    let conn = open_connection(database_path)?;
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
  rs.captured_utc_offset_minutes,
  rs.image_relative_path as image_path,
  rs.victories_at_capture as wins,
  rs.day as battle_count,
  nullif(trim(rs.player_rank), '') as player_rank,
  rs.player_rating as player_rating,
  rs.captured_at_ms
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

    Ok(Some(map_overlay_snapshot_row(row)?))
}

/// The stored image path of any screenshot, whatever its capture source: History
/// shows a run's primary screenshot, which need not be an end-of-run capture.
pub fn load_screenshot_image_path(
    database_path: &Path,
    screenshot_id: &str,
) -> Result<Option<String>, String> {
    if !database_path.exists() {
        return Ok(None);
    }

    let conn = open_connection(database_path)?;
    if !table_exists(&conn, "run_screenshots")? {
        return Ok(None);
    }

    conn.query_row(
        "select image_relative_path from run_screenshots where screenshot_id = ?1",
        [screenshot_id],
        |row| row.get(0),
    )
    .optional()
    .map_err(|err| err.to_string())
}

fn map_overlay_snapshot_row(row: &rusqlite::Row<'_>) -> Result<OverlaySnapshotRow, String> {
    let captured_at_ms: i64 = row.get(9).map_err(|err| err.to_string())?;
    let utc_offset_minutes: i64 = row.get(3).map_err(|err| err.to_string())?;
    let unrepresentable =
        || format!("captured_at_ms {captured_at_ms} is not a representable instant");
    Ok(OverlaySnapshotRow {
        id: row.get(0).map_err(|err| err.to_string())?,
        hero: row.get(1).map_err(|err| err.to_string())?,
        game_mode: row.get(2).map_err(|err| err.to_string())?,
        captured_at: local_text(captured_at_ms, utc_offset_minutes).ok_or_else(unrepresentable)?,
        image_path: row.get(4).map_err(|err| err.to_string())?,
        wins: row.get(5).map_err(|err| err.to_string())?,
        battle_count: row.get(6).map_err(|err| err.to_string())?,
        rank: row.get(7).map_err(|err| err.to_string())?,
        rating: row.get(8).map_err(|err| err.to_string())?,
        captured_at_utc: utc_text(captured_at_ms).ok_or_else(unrepresentable)?,
    })
}

/// The overlay's `from` bound as a stored instant. A value that is not RFC 3339
/// matches no record rather than every record.
fn overlay_from_ms(value: &str) -> i64 {
    DateTime::parse_from_rfc3339(value.trim())
        .map(|parsed| parsed.timestamp_millis())
        .unwrap_or(i64::MAX)
}

#[cfg(test)]
mod tests {
    use super::{
        load_latest_overlay_snapshot, load_overlay_snapshot_by_id, load_overlay_snapshot_count,
        load_overlay_snapshot_list, load_screenshot_image_path, overlay_from_ms,
    };
    use crate::history::test_schema::create_mod_schema;

    #[test]
    fn latest_overlay_record_returns_none_when_database_has_no_rows() {
        let temp = tempfile::NamedTempFile::new().unwrap();
        let conn = rusqlite::Connection::open(temp.path()).unwrap();
        create_mod_schema(&conn);

        let latest = load_latest_overlay_snapshot(temp.path(), None, 0).unwrap();
        assert!(latest.is_none());
    }

    #[test]
    fn latest_overlay_record_reads_latest_end_of_run_snapshot() {
        let temp = tempfile::NamedTempFile::new().unwrap();
        let conn = rusqlite::Connection::open(temp.path()).unwrap();
        create_mod_schema(&conn);
        conn.execute(
            "insert into run_screenshots (
                screenshot_id, run_id, capture_source, image_relative_path, captured_utc_offset_minutes,
                captured_at_ms, day, player_rank, player_rating, player_position, victories_at_capture, hero_name
             ) values (
                'snap-1', 'run-1', 'end_of_run_auto', '2026-04-10\\shot-1.png',
                480, 1775853005000,
                14, 'Diamond', 1942, 1, 10, 'Mak'
             )",
            [],
        )
        .unwrap();

        let latest = load_latest_overlay_snapshot(temp.path(), None, 0)
            .unwrap()
            .unwrap();
        assert_eq!(latest.id, "snap-1");
        assert_eq!(latest.hero, "Mak");
        assert_eq!(latest.game_mode, "End of run");
        assert_eq!(latest.image_path.as_deref(), Some("2026-04-10\\shot-1.png"));
        assert_eq!(latest.wins, Some(10));
        assert_eq!(latest.battle_count, Some(14));
        assert_eq!(latest.rank.as_deref(), Some("Diamond"));
        assert_eq!(latest.rating, Some(1942));
    }

    #[test]
    fn latest_overlay_record_ignores_other_snapshot_types() {
        let temp = tempfile::NamedTempFile::new().unwrap();
        let conn = rusqlite::Connection::open(temp.path()).unwrap();
        create_mod_schema(&conn);
        conn.execute(
            "insert into run_screenshots (
                screenshot_id, run_id, capture_source, image_relative_path, captured_utc_offset_minutes, captured_at_ms
             ) values
             ('snap-1', 'run-1', 'pvp_battle_start', 'battle-1.png', 0, 1775853005000),
             ('snap-2', 'run-2', 'end_of_run_auto', 'final-2.png', 0, 1775856605000)",
            [],
        )
        .unwrap();

        let latest = load_latest_overlay_snapshot(temp.path(), None, 0)
            .unwrap()
            .unwrap();
        assert_eq!(latest.id, "snap-2");
    }

    #[test]
    fn latest_overlay_record_returns_none_when_database_file_is_missing() {
        let temp_dir = tempfile::tempdir().unwrap();
        let missing = temp_dir.path().join("missing-bazaarplusplus.db");
        let latest = load_latest_overlay_snapshot(&missing, None, 0).unwrap();

        assert!(latest.is_none());
    }

    #[test]
    fn latest_overlay_record_supports_backtracking_from_latest() {
        let temp = tempfile::NamedTempFile::new().unwrap();
        let conn = rusqlite::Connection::open(temp.path()).unwrap();
        create_mod_schema(&conn);
        conn.execute(
            "insert into run_screenshots (
                screenshot_id, run_id, capture_source, image_relative_path, captured_utc_offset_minutes, captured_at_ms, hero_name
             ) values
             ('snap-1', 'run-1', 'end_of_run_auto', 'final-1.png', 0, 1775853005000, 'Mak'),
             ('snap-2', 'run-2', 'end_of_run_auto', 'final-2.png', 0, 1775856605000, 'Pygmalien'),
             ('snap-3', 'run-3', 'end_of_run_auto', 'final-3.png', 0, 1775860205000, 'Vanessa')",
            [],
        )
        .unwrap();

        let latest = load_latest_overlay_snapshot(temp.path(), None, 0)
            .unwrap()
            .unwrap();
        let previous = load_latest_overlay_snapshot(temp.path(), None, 1)
            .unwrap()
            .unwrap();
        let oldest = load_latest_overlay_snapshot(temp.path(), None, 2)
            .unwrap()
            .unwrap();
        let beyond = load_latest_overlay_snapshot(temp.path(), None, 3).unwrap();

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
        create_mod_schema(&conn);
        conn.execute(
            "insert into run_screenshots (
                screenshot_id, run_id, capture_source, image_relative_path, captured_utc_offset_minutes, captured_at_ms, hero_name
             ) values
             ('snap-before', 'run-1', 'end_of_run_auto', 'before.png', 0, 1775849405000, 'Mak'),
             ('snap-after', 'run-2', 'end_of_run_auto', 'after.png', 0, 1775856605000, 'Pygmalien')",
            [],
        )
        .unwrap();

        let latest =
            load_latest_overlay_snapshot(temp.path(), Some("2026-04-10T20:00:00+00:00"), 0)
                .unwrap()
                .unwrap();

        assert_eq!(latest.id, "snap-after");
    }

    #[test]
    fn latest_overlay_record_normalizes_stream_start_time_to_utc() {
        let temp = tempfile::NamedTempFile::new().unwrap();
        let conn = rusqlite::Connection::open(temp.path()).unwrap();
        create_mod_schema(&conn);
        conn.execute(
            "insert into run_screenshots (
                screenshot_id, run_id, capture_source, image_relative_path, captured_utc_offset_minutes, captured_at_ms, hero_name
             ) values
             ('snap-before', 'run-1', 'end_of_run_auto', 'before.png', 0, 1775849405000, 'Mak'),
             ('snap-after', 'run-2', 'end_of_run_auto', 'after.png', 0, 1775856605000, 'Pygmalien')",
            [],
        )
        .unwrap();

        let latest =
            load_latest_overlay_snapshot(temp.path(), Some("2026-04-11T04:00:00+08:00"), 0)
                .unwrap()
                .unwrap();

        assert_eq!(latest.id, "snap-after");
        assert_eq!(
            overlay_from_ms("2026-04-11T04:00:00+08:00"),
            1_775_851_200_000
        );
        assert_eq!(overlay_from_ms("not a time"), i64::MAX);
    }

    #[test]
    fn overlay_record_count_only_counts_records_after_stream_start() {
        let temp = tempfile::NamedTempFile::new().unwrap();
        let conn = rusqlite::Connection::open(temp.path()).unwrap();
        create_mod_schema(&conn);
        conn.execute(
            "insert into run_screenshots (
                screenshot_id, run_id, capture_source, image_relative_path, captured_utc_offset_minutes, captured_at_ms
             ) values
             ('snap-before', 'run-1', 'end_of_run_auto', 'before.png', 0, 1775849405000),
             ('snap-after-1', 'run-2', 'end_of_run_auto', 'after-1.png', 0, 1775856605000),
             ('snap-after-2', 'run-3', 'end_of_run_auto', 'after-2.png', 0, 1775860205000),
             ('snap-mid', 'run-4', 'pvp_battle_start', 'mid.png', 0, 1775863805000)",
            [],
        )
        .unwrap();

        let count =
            load_overlay_snapshot_count(temp.path(), Some("2026-04-10T20:00:00+00:00")).unwrap();

        assert_eq!(count, 2);
    }

    #[test]
    fn load_overlay_record_by_id_reads_matching_snapshot() {
        let temp = tempfile::NamedTempFile::new().unwrap();
        let conn = rusqlite::Connection::open(temp.path()).unwrap();
        create_mod_schema(&conn);
        conn.execute(
            "insert into run_screenshots (
                screenshot_id, run_id, capture_source, image_relative_path, captured_utc_offset_minutes, captured_at_ms, hero_name
             ) values ('snap-1', 'run-1', 'end_of_run_auto', 'match-1.png', 0, 1775853005000, 'Mak')",
            [],
        )
        .unwrap();

        let record = load_overlay_snapshot_by_id(temp.path(), "snap-1")
            .unwrap()
            .unwrap();

        assert_eq!(record.id, "snap-1");
    }

    #[test]
    fn screenshot_image_path_ignores_the_capture_source_the_overlay_requires() {
        let temp = tempfile::NamedTempFile::new().unwrap();
        let conn = rusqlite::Connection::open(temp.path()).unwrap();
        create_mod_schema(&conn);
        conn.execute(
            "insert into run_screenshots (
                screenshot_id, run_id, capture_source, is_primary, image_relative_path,
                captured_utc_offset_minutes, captured_at_ms
             ) values ('manual-1', 'run-1', 'manual', 1, 'manual-1.png',
                0, 1775853005000)",
            [],
        )
        .unwrap();

        assert!(load_overlay_snapshot_by_id(temp.path(), "manual-1")
            .unwrap()
            .is_none());
        assert_eq!(
            load_screenshot_image_path(temp.path(), "manual-1")
                .unwrap()
                .as_deref(),
            Some("manual-1.png")
        );
        assert_eq!(
            load_screenshot_image_path(temp.path(), "missing").unwrap(),
            None
        );
    }

    #[test]
    fn latest_overlay_record_uses_unknown_hero_when_screenshot_is_anonymous() {
        let temp = tempfile::NamedTempFile::new().unwrap();
        let conn = rusqlite::Connection::open(temp.path()).unwrap();
        create_mod_schema(&conn);
        conn.execute(
            "insert into run_screenshots (
                screenshot_id, run_id, capture_source, image_relative_path, captured_utc_offset_minutes, captured_at_ms
             ) values (
                'snap-1', null, 'end_of_run_auto', 'anon.png', 0, 1775853005000
             )",
            [],
        )
        .unwrap();

        let latest = load_latest_overlay_snapshot(temp.path(), None, 0)
            .unwrap()
            .unwrap();

        assert_eq!(latest.hero, "Unknown");
        assert_eq!(latest.game_mode, "End of run");
    }

    #[test]
    fn overlay_record_list_returns_latest_records_in_descending_order() {
        let temp = tempfile::NamedTempFile::new().unwrap();
        let conn = rusqlite::Connection::open(temp.path()).unwrap();
        create_mod_schema(&conn);
        conn.execute(
            "insert into run_screenshots (
                screenshot_id, run_id, capture_source, image_relative_path, captured_utc_offset_minutes,
                captured_at_ms, victories_at_capture, hero_name
             ) values
             ('snap-1', 'run-1', 'end_of_run_auto', 'first.png', 0, 1775853005000, 7, 'Mak'),
             ('snap-2', 'run-2', 'end_of_run_auto', 'second.png', 0, 1775856605000, 10, 'Pygmalien'),
             ('snap-3', 'run-3', 'end_of_run_auto', 'third.png', 0, 1775860205000, 4, 'Vanessa')",
            [],
        )
        .unwrap();

        let records = load_overlay_snapshot_list(temp.path(), None, Some(2)).unwrap();

        assert_eq!(records.len(), 2);
        assert_eq!(records[0].id, "snap-3");
        assert_eq!(records[1].id, "snap-2");
    }

    #[test]
    fn overlay_record_list_without_limit_returns_all_records() {
        let temp = tempfile::NamedTempFile::new().unwrap();
        let conn = rusqlite::Connection::open(temp.path()).unwrap();
        create_mod_schema(&conn);
        conn.execute(
            "insert into run_screenshots (
                screenshot_id, run_id, capture_source, image_relative_path, captured_utc_offset_minutes,
                captured_at_ms, hero_name
             ) values
             ('snap-1', 'run-1', 'end_of_run_auto', 'first.png', 0, 1775853005000, 'Mak'),
             ('snap-2', 'run-2', 'end_of_run_auto', 'second.png', 0, 1775856605000, 'Pygmalien'),
             ('snap-3', 'run-3', 'end_of_run_auto', 'third.png', 0, 1775860205000, 'Vanessa')",
            [],
        )
        .unwrap();

        let records = load_overlay_snapshot_list(temp.path(), None, None).unwrap();

        assert_eq!(records.len(), 3);
        assert_eq!(records[0].id, "snap-3");
        assert_eq!(records[2].id, "snap-1");
    }
}

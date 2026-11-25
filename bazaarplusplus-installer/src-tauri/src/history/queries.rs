use std::{collections::HashSet, path::Path, time::Duration};

use rusqlite::types::Value;
use rusqlite::{params, params_from_iter, Connection, OpenFlags, OptionalExtension};

use crate::history::dto::{HistoryBattleRow, HistorySummary};
use crate::history::filter::{BoardItem, FinalBoards, HeroSpellings, RunCondition, RUN_LIST_TIME};
use crate::history::final_board::FINAL_BOARD_ITEMS;
use crate::history::instant::{optional_utc_column, utc_column};
use crate::history::mapper::{map_battle_row, BattleFields, BattleVideoFields};

/// Keep schema mismatches typed so History can display the found and supported versions.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum HistoryReadError {
    UnsupportedSchema { found: i64, supported: String },
    Failed(String),
}

impl std::fmt::Display for HistoryReadError {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::UnsupportedSchema { found, supported } => write!(
                formatter,
                "Unsupported mod database schema: found={found}, supported={supported}."
            ),
            Self::Failed(diagnostic) => formatter.write_str(diagnostic),
        }
    }
}

impl std::error::Error for HistoryReadError {}

impl From<String> for HistoryReadError {
    fn from(diagnostic: String) -> Self {
        Self::Failed(diagnostic)
    }
}

impl From<anyhow::Error> for HistoryReadError {
    fn from(error: anyhow::Error) -> Self {
        Self::Failed(crate::problem::error_diagnostic(&error))
    }
}

impl From<rusqlite::Error> for HistoryReadError {
    fn from(error: rusqlite::Error) -> Self {
        Self::Failed(error.to_string())
    }
}

pub struct RunRow {
    pub run_id: String,
    pub hero: String,
    pub game_mode: String,
    pub started_at_utc: String,
    pub ended_at_utc: Option<String>,
    pub status: String,
    pub victories: Option<i64>,
    pub losses: Option<i64>,
    pub final_day: Option<i64>,
    pub final_player_rank: Option<String>,
    pub final_player_rating: Option<i64>,
    pub final_player_rating_delta: Option<i64>,
}

pub struct VideoRef {
    pub relative_path: String,
}

/// A running game's SQLite checkpoint can make a read open fail transiently.
const OPEN_RETRY_BACKOFF: [Duration; 2] = [Duration::from_millis(50), Duration::from_millis(150)];

/// A read-only open cannot recover a dirty WAL or create a missing `-shm` file after an unclean exit.
/// SQLite still falls back to read-only when the database file is write-protected.
pub fn open_connection(database_path: &Path) -> Result<Connection, HistoryReadError> {
    open_with_retry(database_path, Writability::Optional)
}

/// SQLite silently downgrades an unwritable database to read-only, so check writability before cleanup.
pub fn open_write_connection(database_path: &Path) -> Result<Connection, HistoryReadError> {
    open_with_retry(database_path, Writability::Required)
}

#[derive(Clone, Copy, PartialEq, Eq)]
enum Writability {
    Optional,
    Required,
}

fn open_with_retry(
    database_path: &Path,
    writability: Writability,
) -> Result<Connection, HistoryReadError> {
    let mut attempt = 0usize;
    loop {
        match open_probed(database_path, writability) {
            Ok((conn, user_version)) => {
                validate_supported_schema(user_version)?;
                return Ok(conn);
            }
            Err(err) => {
                let Some(backoff) = OPEN_RETRY_BACKOFF.get(attempt).copied() else {
                    return Err(describe_error(&err).into());
                };
                if !is_transient_error(&err) {
                    return Err(describe_error(&err).into());
                }
                std::thread::sleep(backoff);
                attempt += 1;
            }
        }
    }
}

fn open_probed(
    database_path: &Path,
    writability: Writability,
) -> Result<(Connection, i64), rusqlite::Error> {
    let conn = Connection::open_with_flags(database_path, OpenFlags::SQLITE_OPEN_READ_WRITE)?;
    conn.busy_timeout(Duration::from_secs(2))?;
    // SQLite opens lazily. The first query exercises the database, WAL, and shared-memory index.
    let user_version = conn.query_row("PRAGMA user_version", [], |row| row.get::<_, i64>(0))?;
    if writability == Writability::Required && conn.is_readonly(rusqlite::MAIN_DB)? {
        return Err(rusqlite::Error::SqliteFailure(
            rusqlite::ffi::Error::new(rusqlite::ffi::SQLITE_READONLY),
            Some("The mod database is not writable.".to_string()),
        ));
    }
    Ok((conn, user_version))
}

fn is_transient_error(error: &rusqlite::Error) -> bool {
    use rusqlite::ffi::ErrorCode;

    matches!(
        error,
        rusqlite::Error::SqliteFailure(inner, _)
            if matches!(
                inner.code,
                ErrorCode::DatabaseBusy | ErrorCode::DatabaseLocked | ErrorCode::SystemIoFailure
            )
    )
}

/// The extended code distinguishes shared-memory lock and WAL recovery failures reported as disk I/O errors.
fn describe_error(error: &rusqlite::Error) -> String {
    match error {
        rusqlite::Error::SqliteFailure(inner, _) => {
            format!("{error} (sqlite extended_code={})", inner.extended_code)
        }
        other => other.to_string(),
    }
}

fn validate_supported_schema(found: i64) -> Result<(), HistoryReadError> {
    if crate::config::supported_mod_db_user_versions().contains(&found) {
        return Ok(());
    }

    Err(HistoryReadError::UnsupportedSchema {
        found,
        supported: crate::config::supported_mod_db_user_versions_label(),
    })
}

/// SQLite enforces cascades only when foreign keys are enabled on this connection.
pub fn open_cleanup_connection(database_path: &Path) -> anyhow::Result<Connection> {
    let conn = open_write_connection(database_path)?;
    conn.execute_batch("PRAGMA foreign_keys = ON;")?;
    Ok(conn)
}

pub fn sql_placeholders(count: usize) -> String {
    std::iter::repeat_n("?", count)
        .collect::<Vec<_>>()
        .join(", ")
}

pub fn load_hero_spellings(conn: &Connection) -> anyhow::Result<HeroSpellings> {
    let mut stmt = conn.prepare("select distinct hero from runs")?;
    let stored = stmt
        .query_map([], |row| row.get::<_, String>(0))?
        .collect::<Result<Vec<_>, _>>()?;
    Ok(HeroSpellings::from_stored(stored))
}

pub fn load_final_boards(conn: &Connection) -> anyhow::Result<FinalBoards> {
    let mut stmt = conn.prepare(&format!(
        "select run_id, template_id, name from ({FINAL_BOARD_ITEMS})"
    ))?;
    let items = stmt
        .query_map([], |row| {
            Ok((
                row.get::<_, String>(0)?,
                BoardItem {
                    template_id: row.get(1)?,
                    name: row.get(2)?,
                },
            ))
        })?
        .collect::<Result<Vec<_>, _>>()?;
    Ok(FinalBoards::from_items(items))
}

pub fn load_run_ids(
    conn: &Connection,
    condition: &RunCondition,
) -> anyhow::Result<HashSet<String>> {
    let mut stmt = conn.prepare(&format!("select run_id from runs where {}", condition.sql))?;
    let ids = stmt
        .query_map(params_from_iter(condition.params.iter()), |row| row.get(0))?
        .collect::<Result<HashSet<_>, _>>()?;
    Ok(ids)
}

/// Unfiltered totals include Ghost Battle recordings. Filtered totals count only videos attached to selected Runs.
pub fn load_summary(conn: &Connection, condition: &RunCondition) -> anyhow::Result<HistorySummary> {
    let RunCondition {
        sql,
        params,
        unfiltered,
    } = condition;
    let (runs, completed_runs, win_runs): (i64, i64, i64) = conn.query_row(
        &format!(
            "
            select
              count(*) as runs,
              coalesce(sum(case when status = 'completed' then 1 else 0 end), 0) as completed_runs,
              coalesce(sum(case when status = 'completed' and coalesce(victories, 0) >= 10 then 1 else 0 end), 0) as win_runs
            from runs
            where {sql}
            "
        ),
        params_from_iter(params.iter()),
        |row| Ok((row.get(0)?, row.get(1)?, row.get(2)?)),
    )?;
    let videos = if *unfiltered {
        conn.query_row(
            "select count(*) from combat_replay_videos where status = 'COMPLETED'",
            [],
            |row| row.get(0),
        )?
    } else {
        conn.query_row(
            &format!(
                "
                select count(*)
                from combat_replay_videos v
                join battles b on b.battle_id = v.battle_id
                where v.status = 'COMPLETED'
                  and b.run_id in (select run_id from runs where {sql})
                "
            ),
            params_from_iter(params.iter()),
            |row| row.get(0),
        )?
    };

    Ok(HistorySummary {
        runs,
        videos,
        win_rate: if completed_runs > 0 {
            Some(win_runs as f64 / completed_runs as f64)
        } else {
            None
        },
    })
}

pub fn list_run_rows(
    conn: &Connection,
    condition: &RunCondition,
    limit: i64,
    offset: i64,
) -> anyhow::Result<Vec<RunRow>> {
    let mut params = condition.params.clone();
    params.push(Value::Integer(limit));
    params.push(Value::Integer(offset));
    let mut stmt = conn.prepare(&format!(
        "
            select
              run_id, hero, game_mode, started_at_ms, ended_at_ms,
              status, victories, losses, final_day, final_player_rank, final_player_rating,
              final_player_rating_delta
            from runs
            where {}
            order by {RUN_LIST_TIME} desc, run_id desc
            limit ? offset ?
            ",
        condition.sql
    ))?;
    let rows = stmt.query_map(params_from_iter(params.iter()), map_run_row_from_statement)?;
    rows.collect::<Result<Vec<_>, _>>()
        .map_err(anyhow::Error::from)
}

pub fn load_run_row(conn: &Connection, run_id: &str) -> anyhow::Result<Option<RunRow>> {
    conn.query_row(
        "
        select
          run_id, hero, game_mode, started_at_ms, ended_at_ms,
          status, victories, losses, final_day, final_player_rank, final_player_rating,
          final_player_rating_delta
        from runs
        where run_id = ?1
        ",
        [run_id],
        map_run_row_from_statement,
    )
    .optional()
    .map_err(anyhow::Error::from)
}

fn map_run_row_from_statement(row: &rusqlite::Row<'_>) -> rusqlite::Result<RunRow> {
    Ok(RunRow {
        run_id: row.get(0)?,
        hero: row.get(1)?,
        game_mode: row.get(2)?,
        started_at_utc: utc_column(row, 3)?,
        ended_at_utc: optional_utc_column(row, 4)?,
        status: row.get(5)?,
        victories: row.get(6)?,
        losses: row.get(7)?,
        final_day: row.get(8)?,
        final_player_rank: row.get(9)?,
        final_player_rating: row.get(10)?,
        final_player_rating_delta: row.get(11)?,
    })
}

fn map_battle_fields(row: &rusqlite::Row<'_>) -> rusqlite::Result<BattleFields> {
    Ok(BattleFields {
        recorded_at_ms: row.get(16)?,
        encounter_id: row.get(17)?,
        combat_kind: row.get(18)?,
        battle_id: row.get(0)?,
        day: row.get(1)?,
        hour: row.get(2)?,
        result: row.get::<_, Option<String>>(3)?,
        opponent_hero: row.get(4)?,
        opponent_name: row.get(5)?,
        opponent_rank: row.get(6)?,
        opponent_rating: row.get(7)?,
    })
}

pub fn local_player_name(conn: &Connection, run_id: &str) -> anyhow::Result<Option<String>> {
    conn.query_row(
        "
        select player_name
        from battles
        where run_id = ?1 and source = 'LOCAL' and player_name is not null and deleted_at_ms is null
        order by recorded_at_ms desc, battle_id desc
        limit 1
        ",
        [run_id],
        |row| row.get(0),
    )
    .optional()
    .map_err(anyhow::Error::from)
}

pub fn load_battle_rows(conn: &Connection, run_id: &str) -> anyhow::Result<Vec<HistoryBattleRow>> {
    let mut stmt = conn.prepare(
        "
            select
              b.battle_id, b.day, b.hour, b.result,
              b.opponent_hero, b.opponent_name, b.opponent_rank, b.opponent_rating,
              cv.video_id, cv.status, cv.file_size_bytes, cv.duration_ms,
              s.player_hand_json, s.player_skills_json, s.opponent_hand_json, s.opponent_skills_json,
              b.recorded_at_ms, b.encounter_id, b.combat_kind
            from battles b
            left join battle_snapshots s on s.battle_id = b.battle_id
            left join combat_replay_videos cv on cv.video_id = (
              select video_id
              from combat_replay_videos
              where battle_id = b.battle_id and status = 'COMPLETED'
              order by started_at_ms desc, video_id desc
              limit 1
            )
            where b.run_id = ?1 and b.source = 'LOCAL' and b.deleted_at_ms is null
            order by b.recorded_at_ms desc, b.battle_id desc
            ",
    )?;
    let rows = stmt.query_map([run_id], |row| {
        let video_id: Option<String> = row.get(8)?;
        let mut battle = map_battle_row(
            map_battle_fields(row)?,
            video_id.map(|video_id| BattleVideoFields {
                video_id,
                status: row.get(9).ok().flatten(),
                file_size_bytes: row.get(10).ok().flatten(),
                duration_ms: row.get(11).ok().flatten(),
            }),
        );
        battle.player_board = super::battle_cards::read_set(row.get(12)?, "Item");
        battle.player_skills = super::battle_cards::read_set(row.get(13)?, "Skill");
        battle.opponent_board = super::battle_cards::read_set(row.get(14)?, "Item");
        battle.opponent_skills = super::battle_cards::read_set(row.get(15)?, "Skill");
        Ok(battle)
    })?;
    rows.collect::<Result<Vec<_>, _>>()
        .map_err(anyhow::Error::from)
}

pub fn load_battle_video_ref(
    conn: &Connection,
    battle_id: &str,
    video_id: &str,
) -> anyhow::Result<Option<VideoRef>> {
    conn.query_row(
        "
        select video_relative_path
        from combat_replay_videos
        where battle_id = ?1 and video_id = ?2 and status = 'COMPLETED'
        ",
        params![battle_id, video_id],
        |row| {
            Ok(VideoRef {
                relative_path: row.get(0)?,
            })
        },
    )
    .optional()
    .map_err(anyhow::Error::from)
}

pub fn load_run_id_for_battle(
    conn: &Connection,
    battle_id: &str,
) -> anyhow::Result<Option<String>> {
    conn.query_row(
        "select run_id from battles where battle_id = ?1",
        [battle_id],
        |row| row.get(0),
    )
    .optional()
    .map_err(anyhow::Error::from)
}

#[cfg(test)]
mod tests {
    use super::{open_connection, open_write_connection, HistoryReadError};
    use crate::config::{supported_mod_db_user_versions, supported_mod_db_user_versions_label};

    #[test]
    fn open_connection_does_not_create_missing_database() {
        let temp_dir = tempfile::tempdir().unwrap();
        let database_path = temp_dir.path().join("missing.db");

        assert!(open_connection(&database_path).is_err());
        assert!(!database_path.exists());
    }

    #[test]
    fn connections_reject_unsupported_mod_database_schema_versions() {
        let temp_dir = tempfile::tempdir().unwrap();
        let database_path = temp_dir.path().join("bazaarplusplus.db");
        rusqlite::Connection::open(&database_path).unwrap();

        for error in [
            open_connection(&database_path).unwrap_err(),
            open_write_connection(&database_path).unwrap_err(),
        ] {
            assert_eq!(
                error,
                HistoryReadError::UnsupportedSchema {
                    found: 0,
                    supported: supported_mod_db_user_versions_label(),
                }
            );
        }
    }

    #[test]
    fn connections_open_supported_mod_database_schema_versions() {
        for user_version in supported_mod_db_user_versions() {
            let temp_dir = tempfile::tempdir().unwrap();
            let database_path = temp_dir.path().join("bazaarplusplus.db");
            rusqlite::Connection::open(&database_path)
                .unwrap()
                .execute_batch(&format!("pragma user_version = {user_version};"))
                .unwrap();

            assert!(open_connection(&database_path).is_ok());
            assert!(open_write_connection(&database_path).is_ok());
        }
    }

    #[test]
    fn read_connection_still_opens_a_write_protected_database() {
        let temp_dir = tempfile::tempdir().unwrap();
        let database_path = temp_dir.path().join("bazaarplusplus.db");
        rusqlite::Connection::open(&database_path)
            .unwrap()
            .execute_batch("pragma user_version = 1;")
            .unwrap();
        write_protect(&database_path);

        assert!(open_connection(&database_path).is_ok());
    }

    #[test]
    fn write_connection_refuses_a_write_protected_database_at_open() {
        if crate::test_support::permission_bits_are_bypassed() {
            return;
        }
        let temp_dir = tempfile::tempdir().unwrap();
        let database_path = temp_dir.path().join("bazaarplusplus.db");
        rusqlite::Connection::open(&database_path)
            .unwrap()
            .execute_batch("pragma user_version = 1; create table runs (run_id text primary key);")
            .unwrap();
        write_protect(&database_path);

        let error = open_write_connection(&database_path)
            .unwrap_err()
            .to_string();

        assert!(error.contains("not writable"), "{error}");
    }

    #[test]
    fn open_failure_diagnostics_carry_the_sqlite_extended_code() {
        let temp_dir = tempfile::tempdir().unwrap();
        let database_path = temp_dir.path().join("bazaarplusplus.db");
        std::fs::write(&database_path, b"this is not a sqlite database").unwrap();

        let error = open_connection(&database_path).unwrap_err().to_string();

        // 26 is SQLITE_NOTADB; without the code the message alone reads as a
        // generic failure and cannot be triaged from a user report.
        assert!(error.contains("extended_code=26"), "{error}");
    }

    fn write_protect(path: &std::path::Path) {
        let mut permissions = std::fs::metadata(path).unwrap().permissions();
        permissions.set_readonly(true);
        std::fs::set_permissions(path, permissions).unwrap();
    }

    #[test]
    fn connection_rejects_a_newer_mod_database_schema_version() {
        let temp_dir = tempfile::tempdir().unwrap();
        let database_path = temp_dir.path().join("bazaarplusplus.db");
        let newer = supported_mod_db_user_versions().last().unwrap() + 1;
        rusqlite::Connection::open(&database_path)
            .unwrap()
            .execute_batch(&format!("pragma user_version = {newer};"))
            .unwrap();

        assert_eq!(
            open_connection(&database_path).unwrap_err(),
            HistoryReadError::UnsupportedSchema {
                found: newer,
                supported: supported_mod_db_user_versions_label(),
            }
        );
    }

    #[test]
    fn cleanup_connection_enables_foreign_key_cascade() {
        let temp_dir = tempfile::tempdir().unwrap();
        let database_path = temp_dir.path().join("bazaarplusplus.db");
        {
            let conn = rusqlite::Connection::open(&database_path).unwrap();
            conn.execute_batch(
                "pragma user_version = 1;
                 create table parents (id text primary key);
                 create table children (
                     id text primary key,
                     parent_id text not null,
                     foreign key (parent_id) references parents(id) on delete cascade
                 );
                 insert into parents values ('p1');
                 insert into children values ('c1', 'p1');",
            )
            .unwrap();
        }

        let conn = super::open_cleanup_connection(&database_path).unwrap();
        conn.execute("delete from parents where id = 'p1'", [])
            .unwrap();

        let remaining: i64 = conn
            .query_row("select count(*) from children", [], |row| row.get(0))
            .unwrap();
        assert_eq!(remaining, 0, "cascade must fire on the cleanup connection");
    }
}

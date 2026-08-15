use std::{collections::HashMap, path::Path, time::Duration};

use rusqlite::{params, params_from_iter, Connection, OpenFlags, OptionalExtension};

use crate::history::dto::{HistoryBattleRow, HistorySummary};
use crate::history::mapper::{map_battle_row, BattleFields, BattleVideoFields};

const UNSUPPORTED_SCHEMA_ERROR_PREFIX: &str = "Unsupported mod database schema: found=";

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

/// Backoff before each retry of a transient open. The mod writes this database
/// from the running game, so a read can land mid-checkpoint and fail once
/// before succeeding.
const OPEN_RETRY_BACKOFF: [Duration; 2] = [Duration::from_millis(50), Duration::from_millis(150)];

/// Read connection for every history query.
///
/// Opened read-write on purpose. A read-only connection cannot create a missing
/// `-shm` or recover a dirty WAL, and both are exactly what a game process that
/// exited uncleanly leaves behind — so the read-only flag turns a recoverable
/// database into an unreadable one. Write access is not a requirement of this
/// path: when the file is write-protected SQLite opens it read-only by itself.
pub fn open_connection(database_path: &Path) -> Result<Connection, String> {
    open_with_retry(database_path, Writability::Optional)
}

/// Write connection for delete and cleanup paths. Because SQLite silently
/// downgrades a write-protected database to read-only, an unwritable file would
/// otherwise open cleanly here and only fail partway through a cleanup
/// transaction; the writability check moves that failure to the open.
pub fn open_write_connection(database_path: &Path) -> Result<Connection, String> {
    open_with_retry(database_path, Writability::Required)
}

#[derive(Clone, Copy, PartialEq, Eq)]
enum Writability {
    Optional,
    Required,
}

fn open_with_retry(database_path: &Path, writability: Writability) -> Result<Connection, String> {
    let mut attempt = 0usize;
    loop {
        match open_probed(database_path, writability) {
            Ok((conn, user_version)) => {
                // A schema mismatch is a stable fact about the file, so it leaves
                // the retry loop immediately with its own diagnostic.
                validate_supported_schema(user_version)?;
                return Ok(conn);
            }
            Err(err) => {
                let Some(backoff) = OPEN_RETRY_BACKOFF.get(attempt).copied() else {
                    return Err(describe_error(&err));
                };
                if !is_transient_error(&err) {
                    return Err(describe_error(&err));
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
    // Opening is lazy: the first statement is what actually touches the database
    // file, the WAL and the shared-memory index, so this probe is what proves the
    // connection can read. It doubles as the schema check's only query.
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

/// Diagnostics carry the SQLite extended code: the bare message collapses a
/// whole family of causes into "disk I/O error", which is not enough to tell a
/// locked shared-memory index from a failed WAL recovery when triaging a report.
fn describe_error(error: &rusqlite::Error) -> String {
    match error {
        rusqlite::Error::SqliteFailure(inner, _) => {
            format!("{error} (sqlite extended_code={})", inner.extended_code)
        }
        other => other.to_string(),
    }
}

fn validate_supported_schema(found: i64) -> Result<(), String> {
    let expected = crate::config::SUPPORTED_MOD_DB_USER_VERSION;
    if found == expected {
        return Ok(());
    }

    Err(format!(
        "{UNSUPPORTED_SCHEMA_ERROR_PREFIX}{found}, expected={expected}."
    ))
}

pub(crate) fn unsupported_schema_versions(diagnostic: &str) -> Option<(i64, i64)> {
    let versions = diagnostic.strip_prefix(UNSUPPORTED_SCHEMA_ERROR_PREFIX)?;
    let (found, expected) = versions.split_once(", expected=")?;
    Some((
        found.parse().ok()?,
        expected.strip_suffix('.')?.parse().ok()?,
    ))
}

/// Write connection for cleanup operations. Unlike `open_write_connection`,
/// this enables per-connection foreign-key enforcement so the mod schema's
/// ON DELETE CASCADE chains (runs -> run_events/battles/bundle_seal_jobs,
/// battles -> battle_snapshots) fire on our deletes.
pub fn open_cleanup_connection(database_path: &Path) -> Result<Connection, String> {
    let conn = open_write_connection(database_path)?;
    conn.execute_batch("PRAGMA foreign_keys = ON;")
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
    std::iter::repeat_n("?", count)
        .collect::<Vec<_>>()
        .join(", ")
}

pub fn load_summary(conn: &Connection) -> Result<HistorySummary, String> {
    let (runs, completed_runs, win_runs, last_run_at_utc): (i64, i64, i64, Option<String>) = conn
        .query_row(
            "
            select
              count(*) as runs,
              coalesce(sum(case when status = 'completed' then 1 else 0 end), 0) as completed_runs,
              coalesce(sum(case when status = 'completed' and coalesce(victories, 0) >= 10 then 1 else 0 end), 0) as win_runs,
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

fn map_battle_fields(row: &rusqlite::Row<'_>) -> rusqlite::Result<BattleFields> {
    Ok(BattleFields {
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
                    map_battle_fields(row)?,
                    video_id.map(|video_id| BattleVideoFields {
                        video_id,
                        status: row.get(9).ok().flatten(),
                        file_size_bytes: row.get(10).ok().flatten(),
                        duration_ms: row.get(11).ok().flatten(),
                    }),
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
            Ok(map_battle_row(map_battle_fields(row)?, None))
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
    use super::{open_connection, open_write_connection};

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
            assert!(error.contains("found=0"), "{error}");
            assert!(error.contains("expected=1"), "{error}");
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

        // SQLite downgrades the read-write open to read-only, which is what keeps
        // history readable when the game lives in a directory we cannot write.
        assert!(open_connection(&database_path).is_ok());
    }

    #[test]
    fn write_connection_refuses_a_write_protected_database_at_open() {
        let temp_dir = tempfile::tempdir().unwrap();
        let database_path = temp_dir.path().join("bazaarplusplus.db");
        rusqlite::Connection::open(&database_path)
            .unwrap()
            .execute_batch("pragma user_version = 1; create table runs (run_id text primary key);")
            .unwrap();
        write_protect(&database_path);

        let error = open_write_connection(&database_path).unwrap_err();

        assert!(error.contains("not writable"), "{error}");
    }

    #[test]
    fn open_failure_diagnostics_carry_the_sqlite_extended_code() {
        let temp_dir = tempfile::tempdir().unwrap();
        let database_path = temp_dir.path().join("bazaarplusplus.db");
        std::fs::write(&database_path, b"this is not a sqlite database").unwrap();

        let error = open_connection(&database_path).unwrap_err();

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
        rusqlite::Connection::open(&database_path)
            .unwrap()
            .execute_batch("pragma user_version = 2;")
            .unwrap();

        let error = open_connection(&database_path).unwrap_err();

        assert!(error.contains("found=2"), "{error}");
        assert!(error.contains("expected=1"), "{error}");
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

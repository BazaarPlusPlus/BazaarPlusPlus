//! Reads a V5 local history database without changing it.
//!
//! The database file, with its `-wal` and `-journal`, is copied into a scratch
//! directory and the copy is opened. SQLite then replays frames a crashed game
//! left in the write-ahead log and rebuilds a missing `-shm`, and nothing in
//! `BazaarPlusPlusV5/` is written. The installer's History opener is not used:
//! its compatibility list names V6 versions, and V5's `user_version` 1 would
//! pass it.
//!
//! Four physical shapes exist (installer `tests/fixtures/v5-import/README.md`):
//! `user_version` 1, 2, 3 created fresh, and 3 upgraded from 1 by `ALTER TABLE`
//! without the table CHECKs. They are told apart by `user_version` and column
//! names, and every row is read through an explicit column list.

use std::collections::BTreeMap;
use std::path::Path;

use rusqlite::types::Value;
use rusqlite::{Connection, OpenFlags};

use super::ImportError;
use crate::config::DATABASE_FILE_NAME;

/// Which V5 lineage a database belongs to.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(crate) enum V5Shape {
    /// `user_version` 1: no lifecycle columns; `UpgradeToLifecycleColumns`
    /// derived them from `has_local_payload` and battle existence.
    First,
    /// `user_version` 2 or 3, created fresh or upgraded.
    Lifecycle,
}

pub(crate) const RUN_COLUMNS: &[&str] = &[
    "run_id",
    "started_at_utc",
    "last_seen_at_utc",
    "status",
    "completed",
    "hero",
    "game_mode",
    "player_rank",
    "player_rating",
    "day",
    "hour",
    "max_health",
    "prestige",
    "level",
    "income",
    "gold",
    "last_seq",
    "ended_at_utc",
    "final_day",
    "final_hour",
    "victories",
    "losses",
    "final_player_rank",
    "final_player_rating",
    "final_player_rating_delta",
    "reason",
    "build_channel",
    "player_account_id",
    "bundle_screenshot_requested",
    "mod_version",
];
const RUN_EVENT_COLUMNS: &[&str] = &["run_id", "seq", "ts_utc", "kind", "payload_json"];
pub(crate) const BATTLE_COLUMNS: &[&str] = &[
    "battle_id",
    "remote_battle_id",
    "uploader_account_id",
    "source",
    "run_id",
    "local_player_account_id",
    "recorded_at_utc",
    "day",
    "hour",
    "encounter_id",
    "combat_kind",
    "player_name",
    "player_account_id",
    "player_hero",
    "player_rank",
    "player_rating",
    "player_level",
    "player_prestige",
    "player_income",
    "player_gold",
    "player_victories",
    "player_hand_item_count",
    "player_skill_count",
    "opponent_name",
    "opponent_account_id",
    "opponent_hero",
    "opponent_rank",
    "opponent_rating",
    "opponent_level",
    "opponent_prestige",
    "opponent_victories",
    "opponent_hand_item_count",
    "opponent_skill_count",
    "result",
    "winner_combatant_id",
    "loser_combatant_id",
    "is_final_battle",
    "has_local_payload",
    "bundle_id",
    "download_url",
    "download_url_expires_at_ms",
    "ghost_replay_state",
    "ghost_replay_unavailable_reason",
    "deleted_at_utc",
];
const BATTLE_LIFECYCLE_COLUMNS: &[&str] =
    &["local_payload_state", "local_payload_maintenance_at_utc"];
const BATTLE_SNAPSHOT_COLUMNS: &[&str] = &[
    "battle_id",
    "player_hand_json",
    "player_skills_json",
    "opponent_hand_json",
    "opponent_skills_json",
];
const SCREENSHOT_COLUMNS: &[&str] = &[
    "screenshot_id",
    "run_id",
    "hero_name",
    "capture_source",
    "is_primary",
    "image_relative_path",
    "captured_at_local",
    "captured_at_utc",
    "day",
    "player_rank",
    "player_rating",
    "player_position",
    "victories_at_capture",
];
const VIDEO_COLUMNS: &[&str] = &[
    "video_id",
    "battle_id",
    "source",
    "video_relative_path",
    "width",
    "height",
    "fps",
    "codec",
    "crf",
    "preset",
    "started_at_utc",
    "ended_at_utc",
    "duration_ms",
    "captured_frames",
    "dropped_frames",
    "file_size_bytes",
    "status",
    "error",
];
const VIDEO_LIFECYCLE_COLUMNS: &[&str] = &[
    "attachment_state",
    "file_state",
    "detached_at_utc",
    "missing_at_utc",
    "last_reconciled_at_utc",
];

/// One V5 row, keyed by column name.
#[derive(Clone, Debug, Default)]
pub(crate) struct V5Row(BTreeMap<&'static str, Value>);

impl V5Row {
    pub(crate) fn value(&self, column: &str) -> Value {
        self.0.get(column).cloned().unwrap_or(Value::Null)
    }

    pub(crate) fn text(&self, column: &str) -> Option<String> {
        match self.0.get(column) {
            Some(Value::Text(text)) => Some(text.clone()),
            Some(Value::Integer(value)) => Some(value.to_string()),
            Some(Value::Real(value)) => Some(value.to_string()),
            _ => None,
        }
    }

    pub(crate) fn integer(&self, column: &str) -> Option<i64> {
        match self.0.get(column) {
            Some(Value::Integer(value)) => Some(*value),
            Some(Value::Text(text)) => text.trim().parse().ok(),
            _ => None,
        }
    }
}

/// Every row the import may bring over, in primary-key order.
#[derive(Debug, Default)]
pub(crate) struct V5History {
    pub(crate) shape: Option<V5Shape>,
    pub(crate) runs: Vec<V5Row>,
    pub(crate) run_events: Vec<V5Row>,
    /// `source = 'LOCAL'` battles that belong to a Run; Ghost battles are never read.
    pub(crate) battles: Vec<V5Row>,
    pub(crate) battle_snapshots: Vec<V5Row>,
    /// Screenshots that belong to a Run.
    pub(crate) screenshots: Vec<V5Row>,
    /// Videos of the battles above.
    pub(crate) videos: Vec<V5Row>,
}

/// Copies the V5 database next to nothing it could disturb and reads it.
pub(crate) fn read_v5_history(v5_root: &Path) -> Result<V5History, ImportError> {
    let database = v5_root.join(DATABASE_FILE_NAME);
    match std::fs::symlink_metadata(&database) {
        Ok(metadata) if metadata.file_type().is_file() => {}
        Ok(_) => {
            return Err(ImportError::V5Unreadable(format!(
                "{} is not a regular file",
                database.display()
            )))
        }
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
            return Err(ImportError::V5DatabaseMissing)
        }
        Err(error) => return Err(ImportError::V5Unreadable(error.to_string())),
    }
    let scratch = tempfile::tempdir().map_err(|error| {
        ImportError::V5Unreadable(format!("Cannot create a scratch directory: {error}"))
    })?;
    let copy = scratch.path().join(DATABASE_FILE_NAME);
    for suffix in ["", "-wal", "-journal"] {
        let source = v5_root.join(format!("{DATABASE_FILE_NAME}{suffix}"));
        let is_file =
            std::fs::symlink_metadata(&source).is_ok_and(|metadata| metadata.file_type().is_file());
        if !is_file {
            continue;
        }
        std::fs::copy(
            &source,
            scratch.path().join(format!("{DATABASE_FILE_NAME}{suffix}")),
        )
        .map_err(|error| {
            ImportError::V5Unreadable(format!("Cannot copy {}: {error}", source.display()))
        })?;
    }
    let conn = Connection::open_with_flags(
        &copy,
        OpenFlags::SQLITE_OPEN_READ_WRITE | OpenFlags::SQLITE_OPEN_NO_MUTEX,
    )
    .map_err(|error| ImportError::V5Unreadable(error.to_string()))?;
    let history = read_connection(&conn)?;
    drop(conn);
    drop(scratch);
    Ok(history)
}

fn read_connection(conn: &Connection) -> Result<V5History, ImportError> {
    let user_version: i64 = conn
        .query_row("pragma user_version", [], |row| row.get(0))
        .map_err(|error| ImportError::V5Unreadable(error.to_string()))?;
    let shape = match user_version {
        1 => V5Shape::First,
        2 | 3 => V5Shape::Lifecycle,
        other => return Err(ImportError::UnsupportedV5Schema(other)),
    };

    let lifecycle = shape == V5Shape::Lifecycle;
    let battle_columns = with_lifecycle(BATTLE_COLUMNS, BATTLE_LIFECYCLE_COLUMNS, lifecycle);
    let video_columns = with_lifecycle(VIDEO_COLUMNS, VIDEO_LIFECYCLE_COLUMNS, lifecycle);

    // `runs` is required; a table a V5 version created lazily may be missing
    // and reads as empty. A present table must carry every column read below.
    if !table_exists(conn, "runs")? {
        return Err(ImportError::MissingV5Column {
            table: "runs".into(),
            column: None,
        });
    }
    for (table, columns) in [
        ("runs", RUN_COLUMNS),
        ("run_events", RUN_EVENT_COLUMNS),
        ("battles", battle_columns.as_slice()),
        ("battle_snapshots", BATTLE_SNAPSHOT_COLUMNS),
        ("run_screenshots", SCREENSHOT_COLUMNS),
        ("combat_replay_videos", video_columns.as_slice()),
    ] {
        require_columns(conn, table, columns)?;
    }

    let has_battles = table_exists(conn, "battles")?;
    let local_battles = "b.source = 'LOCAL' and b.run_id is not null";
    Ok(V5History {
        shape: Some(shape),
        runs: select(conn, "runs", "r", RUN_COLUMNS, "1 = 1", "r.run_id")?,
        run_events: select(
            conn,
            "run_events",
            "e",
            RUN_EVENT_COLUMNS,
            "1 = 1",
            "e.run_id, e.seq",
        )?,
        battles: select(
            conn,
            "battles",
            "b",
            &battle_columns,
            local_battles,
            "b.battle_id",
        )?,
        battle_snapshots: if has_battles {
            select(
                conn,
                "battle_snapshots",
                "s",
                BATTLE_SNAPSHOT_COLUMNS,
                &format!(
                    "exists (select 1 from battles b where b.battle_id = s.battle_id \
                     and {local_battles})"
                ),
                "s.battle_id",
            )?
        } else {
            Vec::new()
        },
        screenshots: select(
            conn,
            "run_screenshots",
            "s",
            SCREENSHOT_COLUMNS,
            "s.run_id is not null",
            "s.screenshot_id",
        )?,
        videos: if has_battles {
            select(
                conn,
                "combat_replay_videos",
                "v",
                &video_columns,
                &format!(
                    "exists (select 1 from battles b where b.battle_id = v.battle_id \
                     and {local_battles})"
                ),
                "v.video_id",
            )?
        } else {
            Vec::new()
        },
    })
}

fn with_lifecycle(
    base: &[&'static str],
    lifecycle: &[&'static str],
    include: bool,
) -> Vec<&'static str> {
    let mut columns = base.to_vec();
    if include {
        columns.extend_from_slice(lifecycle);
    }
    columns
}

fn table_exists(conn: &Connection, table: &str) -> Result<bool, ImportError> {
    conn.query_row(
        "select exists (select 1 from sqlite_master where type = 'table' and name = ?1)",
        [table],
        |row| row.get(0),
    )
    .map_err(|error| ImportError::V5Unreadable(error.to_string()))
}

fn require_columns(conn: &Connection, table: &str, columns: &[&str]) -> Result<(), ImportError> {
    if !table_exists(conn, table)? {
        return Ok(());
    }
    let mut statement = conn
        .prepare("select name from pragma_table_info(?1)")
        .map_err(|error| ImportError::V5Unreadable(error.to_string()))?;
    let present = statement
        .query_map([table], |row| row.get::<_, String>(0))
        .and_then(Iterator::collect::<Result<Vec<_>, _>>)
        .map_err(|error| ImportError::V5Unreadable(error.to_string()))?;
    match columns
        .iter()
        .find(|column| !present.iter().any(|name| name == *column))
    {
        Some(column) => Err(ImportError::MissingV5Column {
            table: table.into(),
            column: Some((*column).into()),
        }),
        None => Ok(()),
    }
}

/// Reads `columns` of `table` by name; a missing table reads as no rows.
fn select(
    conn: &Connection,
    table: &str,
    alias: &str,
    columns: &[&'static str],
    filter: &str,
    order: &str,
) -> Result<Vec<V5Row>, ImportError> {
    if !table_exists(conn, table)? {
        return Ok(Vec::new());
    }
    let list = columns
        .iter()
        .map(|column| format!("{alias}.\"{column}\""))
        .collect::<Vec<_>>()
        .join(", ");
    let sql = format!("select {list} from \"{table}\" {alias} where {filter} order by {order}");
    let mut statement = conn
        .prepare(&sql)
        .map_err(|error| ImportError::V5Unreadable(error.to_string()))?;
    let rows = statement
        .query_map([], |row| {
            let mut values = BTreeMap::new();
            for (index, column) in columns.iter().enumerate() {
                values.insert(*column, row.get::<_, Value>(index)?);
            }
            Ok(V5Row(values))
        })
        .and_then(Iterator::collect::<Result<Vec<_>, _>>)
        .map_err(|error| ImportError::V5Unreadable(error.to_string()))?;
    Ok(rows)
}

//! The headless `import` command's evidence: the outcome, the current Data
//! Root's files as sorted path/SHA-256 records, and every row of its history
//! database. Byte-for-byte stable for the same inputs and `--now`, so tests
//! compare it with goldens.

use std::path::Path;

use rusqlite::types::Value;
use rusqlite::{Connection, OpenFlags};

use super::run::ImportReport;
use super::ImportError;
use crate::config::DATABASE_FILE_NAME;

/// The current database's tables with the order their rows are listed in.
const TABLE_ORDER: [(&str, &str); 8] = [
    ("runs", "run_id"),
    ("run_events", "run_id, seq"),
    ("battles", "battle_id"),
    ("battle_snapshots", "battle_id"),
    ("run_screenshots", "screenshot_id"),
    ("combat_replay_videos", "video_id"),
    ("bundle_seal_jobs", "run_id"),
    ("bundle_outbox", "bundle_id"),
];

pub(crate) fn manifest_json(
    game_path: &Path,
    outcome: &Result<ImportReport, ImportError>,
) -> anyhow::Result<String> {
    let data_root = crate::services::paths::bpp_data_dir(game_path);
    let mut manifest = serde_json::Map::new();
    match outcome {
        Ok(report) => {
            manifest.insert("status".into(), "imported".into());
            manifest.insert("report".into(), serde_json::to_value(report)?);
        }
        Err(ImportError::AlreadyImported) => {
            manifest.insert("status".into(), "already_imported".into());
        }
        Err(error) => {
            manifest.insert("status".into(), "failed".into());
            manifest.insert(
                "error".into(),
                serde_json::json!({"code": error.code(), "message": error.to_string()}),
            );
        }
    }
    manifest.insert(
        "marker".into(),
        std::fs::symlink_metadata(super::v5_import_marker_path(game_path))
            .is_ok()
            .into(),
    );
    manifest.insert("dataRootFiles".into(), data_root_files(&data_root)?);
    manifest.insert("database".into(), database_rows(&data_root)?);
    serde_json::to_string_pretty(&serde_json::Value::Object(manifest))
        .map(|text| text + "\n")
        .map_err(anyhow::Error::from)
}

/// The Data Root's files except the database, whose bytes depend on SQLite's
/// page layout; its rows are listed instead. `null` without a Data Root.
fn data_root_files(data_root: &Path) -> anyhow::Result<serde_json::Value> {
    if !data_root.is_dir() {
        return Ok(serde_json::Value::Null);
    }
    let entries = crate::services::file_manifest::tree_manifest(data_root)?;
    Ok(entries
        .into_iter()
        .filter(|(path, _)| {
            !path
                .strip_prefix(DATABASE_FILE_NAME)
                .is_some_and(|suffix| ["", "-wal", "-shm", "-journal"].contains(&suffix))
        })
        .map(|(path, sha256)| serde_json::json!({"path": path, "sha256": sha256}))
        .collect())
}

/// `{"userVersion", "journalMode", "tables": {table: [row, ...]}}`, or `null`
/// without a database.
fn database_rows(data_root: &Path) -> anyhow::Result<serde_json::Value> {
    let path = data_root.join(DATABASE_FILE_NAME);
    if !path.is_file() {
        return Ok(serde_json::Value::Null);
    }
    let conn = Connection::open_with_flags(
        &path,
        OpenFlags::SQLITE_OPEN_READ_WRITE | OpenFlags::SQLITE_OPEN_NO_MUTEX,
    )?;
    let user_version: i64 = conn.query_row("pragma user_version", [], |row| row.get(0))?;
    let journal_mode: String = conn.query_row("pragma journal_mode", [], |row| row.get(0))?;
    let mut tables = serde_json::Map::new();
    for (table, order) in TABLE_ORDER {
        let exists: bool = conn.query_row(
            "select exists (select 1 from sqlite_master where type = 'table' and name = ?1)",
            [table],
            |row| row.get(0),
        )?;
        if !exists {
            continue;
        }
        let mut statement = conn.prepare(&format!("select * from \"{table}\" order by {order}"))?;
        let columns: Vec<String> = statement.column_names().map(str::to_owned).collect();
        let rows = statement
            .query_map([], |row| {
                let mut object = serde_json::Map::new();
                for (index, column) in columns.iter().enumerate() {
                    object.insert(column.clone(), json_value(row.get::<_, Value>(index)?));
                }
                Ok(serde_json::Value::Object(object))
            })
            .and_then(|rows| rows.collect::<Result<Vec<_>, _>>())?;
        tables.insert(table.into(), rows.into());
    }
    Ok(serde_json::json!({
        "userVersion": user_version,
        "journalMode": journal_mode,
        "tables": tables,
    }))
}

fn json_value(value: Value) -> serde_json::Value {
    match value {
        Value::Null => serde_json::Value::Null,
        Value::Integer(value) => value.into(),
        Value::Real(value) => value.into(),
        Value::Text(text) => text.into(),
        Value::Blob(bytes) => bytes
            .iter()
            .map(|byte| format!("{byte:02x}"))
            .collect::<String>()
            .into(),
    }
}

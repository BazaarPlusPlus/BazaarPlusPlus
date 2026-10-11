//! The current Data Root's history database as the import writes it: created
//! from the mod's schema file when missing, otherwise merged into under one
//! exclusive transaction. Until [`Target::commit`] succeeds, [`Target::abort`] leaves the
//! database and the Data Root directory as they were.

use std::path::{Path, PathBuf};

use rusqlite::types::Value;
use rusqlite::{Connection, OpenFlags};

use super::ImportError;
use crate::config::DATABASE_FILE_NAME;

const SIDE_FILE_SUFFIXES: [&str; 3] = ["-wal", "-shm", "-journal"];

pub(crate) struct Target {
    conn: Option<Connection>,
    data_root: PathBuf,
    created_root: bool,
    created_database: bool,
    /// The schema was created in this transaction: a new file, or an empty one
    /// an interrupted earlier import left behind.
    fresh: bool,
}

impl Target {
    /// Opens or creates the database under `data_root` and begins an exclusive
    /// transaction on it.
    pub(crate) fn begin(data_root: &Path) -> Result<Self, ImportError> {
        let created_root = match std::fs::symlink_metadata(data_root) {
            Ok(metadata) if metadata.file_type().is_dir() => false,
            Ok(_) => {
                return Err(ImportError::Target(format!(
                    "{} is not a directory",
                    data_root.display()
                )))
            }
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
                std::fs::create_dir(data_root).map_err(|error| {
                    ImportError::Target(format!("Cannot create {}: {error}", data_root.display()))
                })?;
                true
            }
            Err(error) => return Err(ImportError::Target(error.to_string())),
        };
        let mut target = Self {
            conn: None,
            data_root: data_root.to_path_buf(),
            created_root,
            created_database: false,
            fresh: false,
        };
        match target.open() {
            Ok(()) => Ok(target),
            Err(error) => {
                target.abort();
                Err(error)
            }
        }
    }

    fn database_path(&self) -> PathBuf {
        self.data_root.join(DATABASE_FILE_NAME)
    }

    fn open(&mut self) -> Result<(), ImportError> {
        let path = self.database_path();
        self.created_database = match std::fs::symlink_metadata(&path) {
            Ok(metadata) if metadata.file_type().is_file() => false,
            Ok(_) => {
                return Err(ImportError::Target(format!(
                    "{} is not a regular file",
                    path.display()
                )))
            }
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => true,
            Err(error) => return Err(ImportError::Target(error.to_string())),
        };
        let conn = Connection::open_with_flags(
            &path,
            OpenFlags::SQLITE_OPEN_READ_WRITE
                | OpenFlags::SQLITE_OPEN_CREATE
                | OpenFlags::SQLITE_OPEN_NO_MUTEX,
        )
        .map_err(target_error)?;
        conn.busy_timeout(std::time::Duration::from_secs(5))
            .map_err(target_error)?;
        // The mod opens with foreign keys on; the import's rows must satisfy them.
        conn.execute_batch("pragma foreign_keys = on; begin exclusive;")
            .map_err(target_error)?;
        self.conn = Some(conn);
        let conn = self.conn.as_ref().expect("just opened");
        let user_version: i64 = conn
            .query_row("pragma user_version", [], |row| row.get(0))
            .map_err(target_error)?;
        let objects: i64 = conn
            .query_row("select count(*) from sqlite_master", [], |row| row.get(0))
            .map_err(target_error)?;
        let newest = crate::config::supported_mod_db_user_versions()
            .last()
            .copied()
            .unwrap_or_default();
        if user_version == 0 && objects == 0 {
            self.fresh = true;
            crate::history::mod_schema::execute_mod_schema(conn).map_err(ImportError::Target)?;
        } else if user_version != newest {
            return Err(ImportError::TargetUnsupported(user_version));
        }
        Ok(())
    }

    pub(crate) fn conn(&self) -> &Connection {
        self.conn
            .as_ref()
            .expect("target is open until commit or abort")
    }

    pub(crate) fn data_root(&self) -> &Path {
        &self.data_root
    }

    pub(crate) fn insert(&self, table: &str, row: &[(&str, Value)]) -> Result<(), ImportError> {
        let columns = row
            .iter()
            .map(|(column, _)| format!("\"{column}\""))
            .collect::<Vec<_>>()
            .join(", ");
        let placeholders = (1..=row.len())
            .map(|index| format!("?{index}"))
            .collect::<Vec<_>>()
            .join(", ");
        let mut statement = self
            .conn()
            .prepare_cached(&format!(
                "insert into \"{table}\" ({columns}) values ({placeholders})"
            ))
            .map_err(target_error)?;
        statement
            .execute(rusqlite::params_from_iter(
                row.iter().map(|(_, value)| value),
            ))
            .map_err(|error| ImportError::Target(format!("insert into {table}: {error}")))?;
        Ok(())
    }

    /// Commits the transaction. On failure the transaction is still open and
    /// [`Self::abort`] undoes it.
    pub(crate) fn commit(&mut self) -> Result<(), ImportError> {
        self.conn().execute_batch("commit").map_err(target_error)
    }

    /// After [`Self::commit`]: puts a database this import created into WAL
    /// mode, as the mod keeps it, checks its version, and closes it. Nothing is
    /// undone on failure; the rows are already committed.
    pub(crate) fn finish(mut self) -> Result<(), ImportError> {
        let conn = self.conn.take().expect("target is open until finish");
        if self.fresh {
            let mode: String = conn
                .query_row("pragma journal_mode = wal", [], |row| row.get(0))
                .map_err(target_error)?;
            if !mode.eq_ignore_ascii_case("wal") {
                return Err(ImportError::Target(format!(
                    "journal_mode is {mode}, not wal"
                )));
            }
        }
        let user_version: i64 = conn
            .query_row("pragma user_version", [], |row| row.get(0))
            .map_err(target_error)?;
        if Some(&user_version) != crate::config::supported_mod_db_user_versions().last() {
            return Err(ImportError::TargetUnsupported(user_version));
        }
        conn.close().map_err(|(_, error)| target_error(error))
    }

    /// Rolls back and removes the database file and the Data Root directory when
    /// this import created them. Files placed into the directory are the
    /// placer's to remove first.
    pub(crate) fn abort(mut self) {
        if let Some(conn) = self.conn.take() {
            let _ = conn.execute_batch("rollback");
            let _ = conn.close();
        }
        if self.created_database {
            let path = self.database_path();
            let _ = std::fs::remove_file(&path);
            for suffix in SIDE_FILE_SUFFIXES {
                let mut side = path.clone().into_os_string();
                side.push(suffix);
                let _ = std::fs::remove_file(PathBuf::from(side));
            }
        }
        if self.created_root {
            let _ = std::fs::remove_dir(&self.data_root);
        }
    }
}

fn target_error(error: rusqlite::Error) -> ImportError {
    ImportError::Target(error.to_string())
}

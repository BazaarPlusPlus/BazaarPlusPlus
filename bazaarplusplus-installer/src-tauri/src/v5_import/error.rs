//! Why an import did not complete. Every variant except [`ImportError::MarkerNotWritten`]
//! leaves the current Data Root as it was and writes no marker.

use std::fmt;

use crate::services::game_process::GameRunningRefusal;
use crate::services::placement::PlacementError;

#[derive(Debug)]
pub(crate) enum ImportError {
    InvalidGamePath,
    /// The marker exists: an earlier import completed. Nothing is done.
    AlreadyImported,
    /// `installed_mod_writes_old_root` or `no_v5_data_root`.
    NotEligible(&'static str),
    /// Refused before starting (`stage` `start`) or found running just before
    /// the commit (`before_commit`), which rolls everything back.
    GameRunning {
        refusal: GameRunningRefusal,
        stage: &'static str,
    },
    /// The installed mod's history database contract lacks the replay retention.
    ModContract(String),
    V5DatabaseMissing,
    V5Unreadable(String),
    UnsupportedV5Schema(i64),
    /// A required table (`column` None) or column is missing from the V5 database.
    MissingV5Column {
        table: String,
        column: Option<String>,
    },
    InvalidV5Value {
        table: &'static str,
        id: String,
        column: &'static str,
        value: String,
    },
    TargetUnsupported(i64),
    Target(String),
    Placement(PlacementError),
    /// The import committed but the marker could not be written; running the
    /// import again skips every Run and writes it.
    MarkerNotWritten(String),
}

impl ImportError {
    /// Stable code for the manifest and, later, the frontend.
    pub(crate) fn code(&self) -> &'static str {
        match self {
            Self::InvalidGamePath => "invalid_game_path",
            Self::AlreadyImported => "already_imported",
            Self::NotEligible(_) => "not_eligible",
            Self::GameRunning { .. } => "import_blocked_by_game",
            Self::ModContract(_) => "mod_contract_invalid",
            Self::V5DatabaseMissing => "v5_database_missing",
            Self::V5Unreadable(_) => "v5_database_unreadable",
            Self::UnsupportedV5Schema(_) => "v5_schema_unsupported",
            Self::MissingV5Column { .. } => "v5_schema_missing_column",
            Self::InvalidV5Value { .. } => "v5_value_invalid",
            Self::TargetUnsupported(_) => "target_schema_unsupported",
            Self::Target(_) => "target_write_failed",
            Self::Placement(PlacementError::InsufficientSpace { .. }) => "insufficient_space",
            Self::Placement(PlacementError::TargetConflict { .. }) => "file_conflict",
            Self::Placement(PlacementError::Io { .. }) => "file_placement_failed",
            Self::MarkerNotWritten(_) => "marker_not_written",
        }
    }
}

impl fmt::Display for ImportError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::InvalidGamePath => f.write_str("not a The Bazaar installation"),
            Self::AlreadyImported => f.write_str("the V5 history was already imported"),
            Self::NotEligible(reason) => write!(f, "the import cannot run: {reason}"),
            Self::GameRunning { refusal, stage } => write!(f, "{refusal} ({stage})"),
            Self::ModContract(detail)
            | Self::V5Unreadable(detail)
            | Self::Target(detail)
            | Self::MarkerNotWritten(detail) => f.write_str(detail),
            Self::V5DatabaseMissing => f.write_str("BazaarPlusPlusV5 has no history database"),
            Self::UnsupportedV5Schema(version) => {
                write!(f, "unsupported V5 database user_version {version}")
            }
            Self::MissingV5Column { table, column } => match column {
                Some(column) => write!(f, "V5 table {table} has no column {column}"),
                None => write!(f, "V5 database has no table {table}"),
            },
            Self::InvalidV5Value {
                table,
                id,
                column,
                value,
            } => write!(f, "{table} {id}: {column} is not an instant: {value}"),
            Self::TargetUnsupported(version) => {
                write!(f, "unsupported current database user_version {version}")
            }
            Self::Placement(error) => write!(f, "{error}"),
        }
    }
}

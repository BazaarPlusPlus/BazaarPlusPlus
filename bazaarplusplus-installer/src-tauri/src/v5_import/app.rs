//! The Install page's entry to the import: the `import_v5_history` command's
//! result and the semantic problems an [`ImportError`] becomes.

use std::path::PathBuf;

use serde::Serialize;

use super::run::{import_v5, ImportReport};
use super::ImportError;
use crate::problem::{SemanticProblem, SemanticProblemCode};
use crate::services::data_maintenance::DataMaintenanceLock;
use crate::services::legacy_data::{legacy_data_state, LegacyDataState};
use crate::services::placement::PlacementError;
use crate::stream::runtime::StreamRuntime;

/// The `operation` parameter of every problem the import reports.
const IMPORT_OPERATION: &str = "import_v5_history";

/// A completed import. `BazaarPlusPlusV5/` stays in `state.roots`: deleting it
/// is a separate confirmation.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, specta::Type)]
pub struct ImportV5HistoryResult {
    pub imported_run_count: u64,
    /// Runs the current database already held, left as they were.
    pub skipped_run_count: u64,
    /// Screenshot and video rows whose file could not be carried over: outside
    /// the V5 folder, or already gone from it.
    pub unavailable_file_count: u64,
    /// The Legacy Roots remeasured after the import.
    pub state: LegacyDataState,
}

impl ImportV5HistoryResult {
    pub(crate) fn new(report: &ImportReport, state: LegacyDataState) -> Self {
        let count = |len: usize| u64::try_from(len).unwrap_or(u64::MAX);
        Self {
            imported_run_count: count(report.plan.imported_runs.len()),
            skipped_run_count: count(report.plan.skipped_runs.len()),
            unavailable_file_count: count(
                report.plan.dropped_references.len() + report.plan.missing_files.len(),
            ),
            state,
        }
    }
}

/// Runs the import for the app with the current clock, then remeasures the
/// Legacy Roots on the blocking pool.
pub(crate) async fn import_v5_history(
    maintenance: &DataMaintenanceLock,
    stream_runtime: &StreamRuntime,
    game_path: PathBuf,
) -> Result<ImportV5HistoryResult, SemanticProblem> {
    let now_ms = chrono::Utc::now().timestamp_millis();
    let report = import_v5(maintenance, stream_runtime, game_path.clone(), now_ms)
        .await
        .map_err(ImportError::into_problem)?;
    let state = tauri::async_runtime::spawn_blocking(move || legacy_data_state(&game_path))
        .await
        .map_err(|error| {
            ImportError::Target(format!("failed to measure legacy data: {error}")).into_problem()
        })?;
    Ok(ImportV5HistoryResult::new(&report, state))
}

impl ImportError {
    /// A running game keeps its own refusal code, with the `stage` that found
    /// it. Every other failure is `import_failed` with `reason` set to
    /// [`ImportError::code`]; a shortage of space also carries the byte counts.
    pub(crate) fn into_problem(self) -> SemanticProblem {
        let problem = match &self {
            Self::GameRunning { refusal, stage } => {
                return refusal
                    .clone()
                    .into_problem()
                    .with_param("operation", IMPORT_OPERATION)
                    .with_param("stage", *stage);
            }
            Self::Placement(PlacementError::InsufficientSpace {
                required,
                available,
                ..
            }) => SemanticProblem::new(SemanticProblemCode::ImportFailed)
                .with_param("required_bytes", required.to_string())
                .with_param("available_bytes", available.to_string()),
            _ => SemanticProblem::new(SemanticProblemCode::ImportFailed),
        };
        problem
            .with_param("operation", IMPORT_OPERATION)
            .with_param("reason", self.code())
            .with_diagnostic(self.to_string())
    }
}

#[cfg(test)]
mod tests {
    use std::path::PathBuf;

    use super::*;
    use crate::services::game_process::{GameRunningRefusal, GameStoppedOperation};

    #[test]
    fn a_running_game_keeps_the_import_refusal_code_and_its_stage() {
        let problem = ImportError::GameRunning {
            refusal: GameRunningRefusal {
                operation: GameStoppedOperation::Import,
                inspection_error: None,
            },
            stage: "before_commit",
        }
        .into_problem();

        assert_eq!(problem.code, SemanticProblemCode::ImportBlockedByGame);
        assert_eq!(problem.params["operation"], IMPORT_OPERATION);
        assert_eq!(problem.params["stage"], "before_commit");
    }

    #[test]
    fn other_failures_carry_their_stable_code_as_the_reason() {
        for error in [
            ImportError::AlreadyImported,
            ImportError::NotEligible("installed_mod_writes_old_root"),
            ImportError::V5DatabaseMissing,
            ImportError::UnsupportedV5Schema(9),
            ImportError::MarkerNotWritten("denied".into()),
        ] {
            let code = error.code();
            let problem = error.into_problem();
            assert_eq!(problem.code, SemanticProblemCode::ImportFailed);
            assert_eq!(problem.params["reason"], code);
            assert_eq!(problem.params["operation"], IMPORT_OPERATION);
            assert!(problem.diagnostic.is_some());
        }
    }

    #[test]
    fn insufficient_space_reports_the_byte_counts() {
        let problem = ImportError::Placement(PlacementError::InsufficientSpace {
            volume: PathBuf::from("/volume"),
            required: 2048,
            available: 1024,
        })
        .into_problem();

        assert_eq!(problem.params["reason"], "insufficient_space");
        assert_eq!(problem.params["required_bytes"], "2048");
        assert_eq!(problem.params["available_bytes"], "1024");
    }
}

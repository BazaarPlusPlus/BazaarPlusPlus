//! The import as one operation: everything it writes into the current Data Root
//! commits together, or none of it stays.
//!
//! Order: data maintenance lock (OBS overlay stopped) -> marker and eligibility
//! -> fail-closed game check -> read V5 -> exclusive transaction on the current
//! database -> place files -> insert rows -> game check again -> commit ->
//! marker. The checks run inside the lock so nothing can change between check
//! and use. A failure before the commit rolls back the transaction, removes
//! every file and directory this import placed, removes a database or Data
//! Root directory it created, and writes no marker.

use std::path::{Path, PathBuf};

use serde::Serialize;

use super::plan::{plan_import, PlanReport, ReplayRetention};
use super::source::read_v5_history;
use super::target::Target;
use super::{is_v5_import_eligible, v5_import_marker_path, ImportError};
use crate::services::data_maintenance::DataMaintenanceLock;
use crate::services::game_process::{
    ensure_game_stopped_with, GameProcessProbe, GameStoppedOperation, SystemGameProcessProbe,
};
use crate::services::legacy_data::{
    installed_mod_data_root, InstalledModDataRoot, LegacyRoot, INSTALLED_HISTORY_DATABASE_CONTRACT,
};
use crate::services::placement::{PlacementBackend, Placer, SystemBackend};
use crate::stream::runtime::StreamRuntime;

/// The marker's contents; any entry with the marker's name counts.
const MARKER_TEXT: &str =
    "BazaarPlusPlus imported this folder's history into BazaarPlusPlusV6 once.\n\
     Deleting this file lets the installer offer the import again.\n";

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct ImportReport {
    #[serde(flatten)]
    pub(crate) plan: PlanReport,
    /// Files the imported rows reference, relative to the current Data Root.
    pub(crate) files: Vec<String>,
}

/// Runs the import for the app and the headless CLI: under the data maintenance
/// lock with the overlay stopped, on the blocking pool.
pub(crate) async fn import_v5(
    maintenance: &DataMaintenanceLock,
    stream_runtime: &StreamRuntime,
    game_path: PathBuf,
    now_ms: i64,
) -> Result<ImportReport, ImportError> {
    maintenance
        .run_blocking_with_overlay_stopped(stream_runtime, move || {
            import_blocking_with(&game_path, &SystemGameProcessProbe, SystemBackend, now_ms)
        })
        .await
        .map_err(ImportError::Target)?
}

/// The import itself; the caller holds the data maintenance lock.
pub(crate) fn import_blocking_with<B: PlacementBackend>(
    game_path: &Path,
    probe: &impl GameProcessProbe,
    backend: B,
    now_ms: i64,
) -> Result<ImportReport, ImportError> {
    if !crate::services::detect::is_valid_game_path(game_path) {
        return Err(ImportError::InvalidGamePath);
    }
    if std::fs::symlink_metadata(v5_import_marker_path(game_path)).is_ok() {
        return Err(ImportError::AlreadyImported);
    }
    if !is_v5_import_eligible(game_path) {
        return Err(ImportError::NotEligible(
            if installed_mod_data_root(game_path) == InstalledModDataRoot::Current {
                "no_v5_data_root"
            } else {
                "installed_mod_writes_old_root"
            },
        ));
    }
    let check_game = |stage| {
        ensure_game_stopped_with(probe, game_path, GameStoppedOperation::Import)
            .map_err(|refusal| ImportError::GameRunning { refusal, stage })
    };
    check_game("start")?;
    let retention = read_retention(game_path)?;

    let v5_root = LegacyRoot::V5.path_in(game_path);
    let v6_root = crate::services::paths::bpp_data_dir(game_path);
    let history = read_v5_history(&v5_root)?;

    let mut target = Target::begin(&v6_root)?;
    let mut placer = Placer::new(backend);
    let staged = (|| {
        let plan = plan_import(
            &history,
            target.conn(),
            &v5_root,
            target.data_root(),
            now_ms,
            retention,
        )?;
        placer
            .place(&plan.placements)
            .map_err(ImportError::Placement)?;
        for (table, row) in &plan.rows {
            target.insert(table, row)?;
        }
        check_game("before_commit")?;
        target.commit()?;
        Ok(plan)
    })();
    let plan = match staged {
        Ok(plan) => plan,
        Err(error) => {
            // Best effort: a file the rollback cannot remove stays an orphan the
            // mod and History cleanup already handle; the error that stopped the
            // import is the one to report.
            let _ = placer.rollback();
            target.abort();
            return Err(error);
        }
    };
    target.finish()?;

    let mut files: Vec<String> = plan
        .placements
        .iter()
        .filter_map(|request| request.target.strip_prefix(&v6_root).ok())
        .map(|path| path.to_string_lossy().replace('\\', "/"))
        .collect();
    files.sort();
    write_marker(game_path)?;
    Ok(ImportReport {
        plan: plan.report,
        files,
    })
}

/// The replay retention the installed mod applies, from its contract file.
fn read_retention(game_path: &Path) -> Result<ReplayRetention, ImportError> {
    let path = game_path.join(INSTALLED_HISTORY_DATABASE_CONTRACT);
    let contract: serde_json::Value = std::fs::read(&path)
        .map_err(|error| error.to_string())
        .and_then(|bytes| serde_json::from_slice(&bytes).map_err(|error| error.to_string()))
        .map_err(|error| ImportError::ModContract(format!("{}: {error}", path.display())))?;
    let positive = |field: &str| {
        contract
            .get(field)
            .and_then(serde_json::Value::as_u64)
            .filter(|value| *value > 0)
            .ok_or_else(|| {
                ImportError::ModContract(format!("{} has no positive {field}", path.display()))
            })
    };
    Ok(ReplayRetention {
        newest_count: usize::try_from(positive("replayPayloadRetainedNewestCount")?)
            .unwrap_or(usize::MAX),
        age_days: i64::try_from(positive("replayPayloadRetainedAgeDays")?).unwrap_or(i64::MAX),
    })
}

fn write_marker(game_path: &Path) -> Result<(), ImportError> {
    use std::io::Write;
    let path = v5_import_marker_path(game_path);
    std::fs::OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(&path)
        .and_then(|mut file| {
            file.write_all(MARKER_TEXT.as_bytes())?;
            file.sync_all()
        })
        .map_err(|error| ImportError::MarkerNotWritten(format!("{}: {error}", path.display())))
}

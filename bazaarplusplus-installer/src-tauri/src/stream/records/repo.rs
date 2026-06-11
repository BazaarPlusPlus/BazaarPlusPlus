use std::path::Path;

pub(crate) type OverlayRecordRow = crate::history::screenshots::OverlaySnapshotRow;

pub(super) fn load_latest_overlay_record(
    database_path: &Path,
    from: Option<&str>,
    offset: usize,
) -> Result<Option<OverlayRecordRow>, String> {
    crate::history::screenshots::load_latest_overlay_snapshot(database_path, from, offset)
}

pub(super) fn load_overlay_record_count(
    database_path: &Path,
    from: Option<&str>,
) -> Result<usize, String> {
    crate::history::screenshots::load_overlay_snapshot_count(database_path, from)
}

pub(super) fn load_overlay_record_list(
    database_path: &Path,
    from: Option<&str>,
    limit: Option<usize>,
) -> Result<Vec<OverlayRecordRow>, String> {
    crate::history::screenshots::load_overlay_snapshot_list(database_path, from, limit)
}

pub(super) fn load_overlay_record_by_id(
    database_path: &Path,
    record_id: &str,
) -> Result<Option<OverlayRecordRow>, String> {
    crate::history::screenshots::load_overlay_snapshot_by_id(database_path, record_id)
}

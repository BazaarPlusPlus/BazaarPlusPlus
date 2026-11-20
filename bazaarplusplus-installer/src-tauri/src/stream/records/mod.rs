mod image;
mod locator;
mod mapper;

use crate::history::screenshots::{
    load_latest_overlay_snapshot, load_overlay_snapshot_by_id, load_overlay_snapshot_count,
    load_overlay_snapshot_list,
};
pub(in crate::stream) use image::resolve_overlay_image_path;
pub use locator::resolve_database_path;
use mapper::to_overlay_record;
pub use mapper::OverlayRecord;
use std::path::{Path, PathBuf};

/// Records from another installation cannot resolve their screenshots against this service's captured path.
#[derive(Debug)]
pub struct NoRecordInstallation;

impl std::fmt::Display for NoRecordInstallation {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str(
            "No game directory had a BazaarPlusPlus database when the stream service started.",
        )
    }
}

impl std::error::Error for NoRecordInstallation {}

#[derive(Clone, Debug)]
pub struct OverlayRecordRepository {
    game_path: Option<PathBuf>,
}

impl OverlayRecordRepository {
    pub fn new(game_path: Option<PathBuf>) -> Self {
        Self { game_path }
    }

    pub fn game_path(&self) -> Option<&Path> {
        self.game_path.as_deref()
    }

    pub fn load_record_at_offset(
        &self,
        from: Option<&str>,
        offset: usize,
    ) -> anyhow::Result<Option<OverlayRecord>> {
        let database_path = self.database_path()?;
        Ok(load_latest_overlay_snapshot(&database_path, from, offset)?
            .map(|row| to_overlay_record(self.game_path.as_deref(), row)))
    }

    pub fn count_since(&self, from: Option<&str>) -> anyhow::Result<usize> {
        let database_path = self.database_path()?;
        load_overlay_snapshot_count(&database_path, from)
    }

    pub fn load_record_list(
        &self,
        from: Option<&str>,
        limit: Option<usize>,
    ) -> anyhow::Result<Vec<OverlayRecord>> {
        let database_path = self.database_path()?;
        Ok(load_overlay_snapshot_list(&database_path, from, limit)?
            .into_iter()
            .map(|row| to_overlay_record(self.game_path.as_deref(), row))
            .collect())
    }

    pub fn load_image(&self, record_id: &str) -> anyhow::Result<Option<(PathBuf, Vec<u8>)>> {
        let Some(image_path) = self.load_image_path(record_id)? else {
            return Ok(None);
        };
        let bytes = std::fs::read(&image_path)?;

        Ok(Some((image_path, bytes)))
    }

    pub fn load_image_path(&self, record_id: &str) -> anyhow::Result<Option<PathBuf>> {
        let database_path = self.database_path()?;
        let Some(row) = load_overlay_snapshot_by_id(&database_path, record_id)? else {
            return Ok(None);
        };

        Ok(self
            .resolve_image_path(row.image_path.as_deref())
            .filter(|path| path.exists()))
    }

    fn database_path(&self) -> anyhow::Result<PathBuf> {
        match &self.game_path {
            Some(game_path) => resolve_database_path(game_path),
            None => Err(NoRecordInstallation.into()),
        }
    }

    fn resolve_image_path(&self, raw_path: Option<&str>) -> Option<PathBuf> {
        resolve_overlay_image_path(self.game_path.clone(), raw_path)
    }
}

#[cfg(test)]
mod tests {
    use super::OverlayRecordRepository;
    use crate::services::paths;

    #[test]
    fn repository_rejects_unsupported_database_schema() {
        let temp_dir = tempfile::tempdir().unwrap();
        let game_path = temp_dir.path().join("TheBazaar");
        let data_dir = paths::bpp_data_dir(&game_path);
        std::fs::create_dir_all(&data_dir).unwrap();
        rusqlite::Connection::open(paths::database_path(&game_path)).unwrap();

        let error = OverlayRecordRepository::new(Some(game_path))
            .load_record_at_offset(None, 0)
            .unwrap_err()
            .to_string();

        assert!(error.contains("found=0"), "{error}");
        assert!(error.contains("supported=1"), "{error}");
    }
}

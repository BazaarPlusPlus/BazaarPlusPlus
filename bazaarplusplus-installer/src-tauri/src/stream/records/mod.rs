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

/// The service started without a record installation: no game directory had a
/// BazaarPlusPlus database when it resolved one. A database found anywhere else
/// could not resolve its screenshots, so the repository reads none.
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

    /// The installation whose records and screenshots this repository reads.
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
    use crate::history::mod_schema::create_mod_schema;
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

    #[test]
    fn repository_sets_strip_url_when_relative_image_exists() {
        let temp_dir = tempfile::tempdir().unwrap();
        let game_path = temp_dir.path().join("TheBazaar");
        let screenshots_dir = paths::screenshots_dir(&game_path);
        std::fs::create_dir_all(&screenshots_dir).unwrap();
        std::fs::write(screenshots_dir.join("match-1.png"), b"png").unwrap();

        let database_path = paths::database_path(&game_path);
        let conn = rusqlite::Connection::open(&database_path).unwrap();
        create_mod_schema(&conn);
        conn.execute(
            "insert into run_screenshots (
                screenshot_id, run_id, capture_source, image_relative_path, captured_utc_offset_minutes,
                captured_at_ms, victories_at_capture
             ) values (
                'snap-1', 'run-1', 'end_of_run_auto', 'match-1.png',
                0, 1775853005000, 10
             )",
            [],
        )
        .unwrap();

        let repository = OverlayRecordRepository::new(Some(game_path));
        let latest = repository.load_record_at_offset(None, 0).unwrap().unwrap();

        assert_eq!(latest.strip_url.as_deref(), Some("/images/snap-1/strip"));
    }

    #[test]
    fn repository_preserves_optional_snapshot_metrics() {
        let temp_dir = tempfile::tempdir().unwrap();
        let game_path = temp_dir.path().join("TheBazaar");
        let data_dir = paths::bpp_data_dir(&game_path);
        std::fs::create_dir_all(&data_dir).unwrap();

        let database_path = paths::database_path(&game_path);
        let conn = rusqlite::Connection::open(&database_path).unwrap();
        create_mod_schema(&conn);
        conn.execute(
            "insert into run_screenshots (
                screenshot_id, run_id, capture_source, image_relative_path, captured_utc_offset_minutes,
                captured_at_ms, day, victories_at_capture, player_position, player_rank, player_rating, hero_name
             ) values (
                'snap-1', 'run-1', 'end_of_run_auto', 'match-1.png',
                0, 1775853005000,
                14, 10, 1, 'Diamond', 500, 'Mak'
             )",
            [],
        )
        .unwrap();

        let repository = OverlayRecordRepository::new(Some(game_path));
        let latest = repository.load_record_at_offset(None, 0).unwrap().unwrap();

        assert_eq!(latest.wins, Some(10));
        assert_eq!(latest.battle_count, Some(14));
        assert_eq!(latest.rank.as_deref(), Some("Diamond"));
        assert_eq!(latest.rating, Some(500));
    }

    #[test]
    fn repository_canonicalizes_the_dragons_legacy_hero_name() {
        let temp_dir = tempfile::tempdir().unwrap();
        let game_path = temp_dir.path().join("TheBazaar");
        let data_dir = paths::bpp_data_dir(&game_path);
        std::fs::create_dir_all(&data_dir).unwrap();

        let database_path = paths::database_path(&game_path);
        let conn = rusqlite::Connection::open(&database_path).unwrap();
        create_mod_schema(&conn);
        conn.execute(
            "insert into run_screenshots (
                screenshot_id, run_id, capture_source, image_relative_path, captured_utc_offset_minutes,
                captured_at_ms, hero_name
             ) values (
                'snap-dra', 'run-dra', 'end_of_run_auto', 'dragons.png',
                0, 1786048205000, 'Hero8'
             )",
            [],
        )
        .unwrap();

        let repository = OverlayRecordRepository::new(Some(game_path));
        let latest = repository.load_record_at_offset(None, 0).unwrap().unwrap();

        assert_eq!(latest.hero_id, "TheDragons");
        assert_eq!(latest.title, "The Dragons");
    }
}

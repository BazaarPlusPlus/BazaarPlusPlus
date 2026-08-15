mod image;
mod locator;
mod mapper;

use crate::history::screenshots::{
    load_latest_overlay_snapshot, load_overlay_snapshot_by_id, load_overlay_snapshot_count,
    load_overlay_snapshot_list,
};
use image::resolve_overlay_image_path;
pub use locator::{find_database_path_anywhere, resolve_database_path};
use mapper::to_overlay_record;
pub use mapper::OverlayRecord;
use std::path::PathBuf;

#[derive(Clone, Debug)]
pub struct OverlayRecordRepository {
    game_path: Option<PathBuf>,
}

impl OverlayRecordRepository {
    pub fn new(game_path: Option<PathBuf>) -> Self {
        Self { game_path }
    }

    pub fn load_record_at_offset(
        &self,
        from: Option<&str>,
        offset: usize,
    ) -> Result<Option<OverlayRecord>, String> {
        let database_path = self.database_path()?;
        Ok(load_latest_overlay_snapshot(&database_path, from, offset)?
            .map(|row| to_overlay_record(self.game_path.as_deref(), row)))
    }

    pub fn count_since(&self, from: Option<&str>) -> Result<usize, String> {
        let database_path = self.database_path()?;
        load_overlay_snapshot_count(&database_path, from)
    }

    pub fn load_record_list(
        &self,
        from: Option<&str>,
        limit: Option<usize>,
    ) -> Result<Vec<OverlayRecord>, String> {
        let database_path = self.database_path()?;
        Ok(load_overlay_snapshot_list(&database_path, from, limit)?
            .into_iter()
            .map(|row| to_overlay_record(self.game_path.as_deref(), row))
            .collect())
    }

    pub fn load_image(&self, record_id: &str) -> Result<Option<(PathBuf, Vec<u8>)>, String> {
        let Some(image_path) = self.load_image_path(record_id)? else {
            return Ok(None);
        };
        let bytes = std::fs::read(&image_path).map_err(|err| err.to_string())?;

        Ok(Some((image_path, bytes)))
    }

    pub fn load_image_path(&self, record_id: &str) -> Result<Option<PathBuf>, String> {
        let database_path = self.database_path()?;
        let Some(row) = load_overlay_snapshot_by_id(&database_path, record_id)? else {
            return Ok(None);
        };

        Ok(self
            .resolve_image_path(row.image_path.as_deref())
            .filter(|path| path.exists()))
    }

    fn database_path(&self) -> Result<PathBuf, String> {
        if let Some(game_path) = &self.game_path {
            return resolve_database_path(game_path);
        }
        find_database_path_anywhere()
    }

    fn resolve_image_path(&self, raw_path: Option<&str>) -> Option<PathBuf> {
        resolve_overlay_image_path(self.game_path.clone(), raw_path)
    }
}

#[cfg(test)]
mod tests {
    use super::OverlayRecordRepository;
    use crate::services::paths;

    fn create_run_screenshots_table(conn: &rusqlite::Connection) {
        conn.execute_batch(
            "pragma user_version = 1;
             create table run_screenshots (
                screenshot_id text primary key,
                run_id text,
                battle_id text,
                capture_source text not null,
                is_primary integer not null default 0,
                image_relative_path text not null,
                captured_at_local text not null,
                captured_at_utc text not null,
                day integer,
                player_rank text,
                player_rating integer,
                player_position integer,
                victories_at_capture integer,
                hero_name text,
                build_channel text
            );",
        )
        .unwrap();
    }

    #[test]
    fn repository_rejects_unsupported_database_schema() {
        let temp_dir = tempfile::tempdir().unwrap();
        let game_path = temp_dir.path().join("TheBazaar");
        let data_dir = paths::bpp_data_dir(&game_path);
        std::fs::create_dir_all(&data_dir).unwrap();
        rusqlite::Connection::open(paths::database_path(&game_path)).unwrap();

        let error = OverlayRecordRepository::new(Some(game_path))
            .load_record_at_offset(None, 0)
            .unwrap_err();

        assert!(error.contains("found=0"), "{error}");
        assert!(error.contains("expected=1"), "{error}");
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
        create_run_screenshots_table(&conn);
        conn.execute(
            "insert into run_screenshots (
                screenshot_id, run_id, capture_source, image_relative_path, captured_at_local,
                captured_at_utc, victories_at_capture
             ) values (
                'snap-1', 'run-1', 'end_of_run_auto', 'match-1.png',
                '2026-04-10T20:30:05+00:00', '2026-04-10T20:30:05+00:00', 10
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
        create_run_screenshots_table(&conn);
        conn.execute(
            "insert into run_screenshots (
                screenshot_id, run_id, capture_source, image_relative_path, captured_at_local,
                captured_at_utc, day, victories_at_capture, player_position, player_rank, player_rating, hero_name
             ) values (
                'snap-1', 'run-1', 'end_of_run_auto', 'match-1.png',
                '2026-04-10T20:30:05+00:00', '2026-04-10T20:30:05+00:00',
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
        create_run_screenshots_table(&conn);
        conn.execute(
            "insert into run_screenshots (
                screenshot_id, run_id, capture_source, image_relative_path, captured_at_local,
                captured_at_utc, hero_name
             ) values (
                'snap-dra', 'run-dra', 'end_of_run_auto', 'dragons.png',
                '2026-08-06T20:30:05+00:00', '2026-08-06T20:30:05+00:00', 'Hero8'
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

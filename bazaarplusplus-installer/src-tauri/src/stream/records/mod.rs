mod image;
mod locator;
mod mapper;
mod repo;

use image::resolve_overlay_image_path;
use locator::{find_database_path_anywhere, resolve_database_path};
use mapper::to_overlay_record;
pub use mapper::OverlayRecord;
use repo::{
    delete_overlay_record_row, load_latest_overlay_record, load_overlay_record_by_id,
    load_overlay_record_count, load_overlay_record_list,
};
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
        Ok(load_latest_overlay_record(&database_path, from, offset)?
            .map(|row| to_overlay_record(self.game_path.as_deref(), row)))
    }

    pub fn count_since(&self, from: Option<&str>) -> Result<usize, String> {
        let database_path = self.database_path()?;
        load_overlay_record_count(&database_path, from)
    }

    pub fn load_record_list(
        &self,
        from: Option<&str>,
        limit: Option<usize>,
    ) -> Result<Vec<OverlayRecord>, String> {
        let database_path = self.database_path()?;
        Ok(load_overlay_record_list(&database_path, from, limit)?
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
        let Some(row) = load_overlay_record_by_id(&database_path, record_id)? else {
            return Ok(None);
        };

        Ok(self
            .resolve_image_path(row.image_path.as_deref())
            .filter(|path| path.exists()))
    }

    pub fn delete_record(&self, record_id: &str) -> Result<bool, String> {
        let database_path = self.database_path()?;
        let (deleted, image_relative_path) = delete_overlay_record_row(&database_path, record_id)?;
        if !deleted {
            return Ok(false);
        }
        let image_path = self.resolve_image_path(image_relative_path.as_deref());

        if let Some(path) = image_path {
            match std::fs::remove_file(&path) {
                Ok(()) => {}
                Err(err) if err.kind() == std::io::ErrorKind::NotFound => {}
                Err(err) => {
                    eprintln!(
                        "failed to remove deleted stream image {}: {err}",
                        path.display()
                    );
                }
            }
        }

        Ok(true)
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
    use super::locator::DATABASE_FILE_NAME;
    use super::OverlayRecordRepository;

    fn create_run_screenshots_table(conn: &rusqlite::Connection) {
        conn.execute(
            "create table run_screenshots (
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
                hero_name text
            )",
            [],
        )
        .unwrap();
    }

    #[test]
    fn repository_sets_image_url_when_relative_image_exists() {
        let temp_dir = tempfile::tempdir().unwrap();
        let game_path = temp_dir.path().join("TheBazaar");
        let data_dir = game_path.join("BazaarPlusPlus");
        let screenshots_dir = data_dir.join("Screenshots");
        std::fs::create_dir_all(&screenshots_dir).unwrap();
        std::fs::write(screenshots_dir.join("match-1.png"), b"png").unwrap();

        let database_path = data_dir.join(DATABASE_FILE_NAME);
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

        assert_eq!(latest.image_url.as_deref(), Some("/images/snap-1"));
    }

    #[test]
    fn repository_preserves_optional_snapshot_metrics() {
        let temp_dir = tempfile::tempdir().unwrap();
        let game_path = temp_dir.path().join("TheBazaar");
        let data_dir = game_path.join("BazaarPlusPlus");
        std::fs::create_dir_all(&data_dir).unwrap();

        let database_path = data_dir.join(DATABASE_FILE_NAME);
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
        assert_eq!(latest.position, Some(1));
        assert_eq!(latest.battle_count, Some(14));
        assert_eq!(latest.rank.as_deref(), Some("Diamond"));
        assert_eq!(latest.rating, Some(500));
    }

    #[test]
    fn repository_delete_record_removes_database_row_and_image_file() {
        let temp_dir = tempfile::tempdir().unwrap();
        let game_path = temp_dir.path().join("TheBazaar");
        let data_dir = game_path.join("BazaarPlusPlus");
        let screenshots_dir = data_dir.join("Screenshots");
        std::fs::create_dir_all(&screenshots_dir).unwrap();
        let image_path = screenshots_dir.join("match-1.png");
        std::fs::write(&image_path, b"png").unwrap();

        let database_path = data_dir.join(DATABASE_FILE_NAME);
        let conn = rusqlite::Connection::open(&database_path).unwrap();
        create_run_screenshots_table(&conn);
        conn.execute(
            "insert into run_screenshots (
                screenshot_id, run_id, capture_source, image_relative_path, captured_at_local, captured_at_utc
             ) values (
                'snap-1', 'run-1', 'end_of_run_auto', 'match-1.png',
                '2026-04-10T20:30:05+00:00', '2026-04-10T20:30:05+00:00'
             )",
            [],
        )
        .unwrap();

        let repository = OverlayRecordRepository::new(Some(game_path));

        assert_eq!(repository.delete_record("snap-1").unwrap(), true);
        assert_eq!(repository.delete_record("snap-1").unwrap(), false);
        assert!(!image_path.exists());

        let remaining: i64 = conn
            .query_row("select count(*) from run_screenshots", [], |row| row.get(0))
            .unwrap();
        assert_eq!(remaining, 0);
    }
}

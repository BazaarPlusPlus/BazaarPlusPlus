use serde::Serialize;

const DEFAULT_HOST: &str = "127.0.0.1";

#[derive(Clone, Debug, Serialize, specta::Type)]
pub struct StreamServiceStatus {
    pub running: bool,
    pub host: String,
    pub port: Option<u16>,
    pub base_url: Option<String>,
    pub overlay_url: Option<String>,
    pub settings_url: Option<String>,
    pub last_error: Option<String>,
    pub started_at: Option<String>,
    pub active_from: Option<String>,
    pub active_window_offset: usize,
    pub db: StreamDbStatus,
    pub window: StreamWindowStatus,
}

#[derive(Clone, Debug, Default, Serialize, specta::Type)]
pub struct StreamDbStatus {
    pub found: bool,
    pub path: Option<String>,
}

#[derive(Clone, Debug, Default, Serialize, specta::Type)]
pub struct StreamWindowStatus {
    pub total_records: usize,
    pub existing_before_start: usize,
    pub captured_since_start: usize,
    pub current_hero: Option<String>,
    pub current_start_label: Option<String>,
}

impl Default for StreamServiceStatus {
    fn default() -> Self {
        Self {
            running: false,
            host: DEFAULT_HOST.to_string(),
            port: None,
            base_url: None,
            overlay_url: None,
            settings_url: None,
            last_error: None,
            started_at: None,
            active_from: None,
            active_window_offset: 0,
            db: StreamDbStatus::default(),
            window: StreamWindowStatus::default(),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::StreamServiceStatus;

    #[test]
    fn default_status_starts_idle_without_start_time() {
        let status = StreamServiceStatus::default();

        assert!(!status.running);
        assert!(status.base_url.is_none());
        assert!(status.started_at.is_none());
        assert!(status.active_from.is_none());
        assert_eq!(status.active_window_offset, 0);
    }

    #[test]
    fn status_can_represent_running_service() {
        let status = StreamServiceStatus {
            running: true,
            port: Some(17654),
            base_url: Some("http://127.0.0.1:17654".to_string()),
            overlay_url: Some("http://127.0.0.1:17654/overlay".to_string()),
            settings_url: Some("http://127.0.0.1:17654/settings".to_string()),
            started_at: Some("2026-04-11T20:00:00+08:00".to_string()),
            active_from: Some("2026-04-11T20:00:00+08:00".to_string()),
            active_window_offset: 0,
            ..StreamServiceStatus::default()
        };

        assert!(status.running);
        assert_eq!(status.port, Some(17654));
        assert_eq!(
            status.overlay_url.as_deref(),
            Some("http://127.0.0.1:17654/overlay")
        );
        assert_eq!(
            status.settings_url.as_deref(),
            Some("http://127.0.0.1:17654/settings")
        );
        assert_eq!(
            status.active_from.as_deref(),
            Some("2026-04-11T20:00:00+08:00")
        );
    }
}

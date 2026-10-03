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
}

#[derive(Clone, Debug, Default, Serialize, specta::Type)]
pub struct StreamDbStatus {
    pub found: bool,
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
        }
    }
}

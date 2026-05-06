use base64::{engine::general_purpose::STANDARD, Engine as _};
use serde::{Deserialize, Serialize};
use std::path::PathBuf;

const SETTINGS_DIRECTORY: &str = "BazaarPlusPlus";
const SETTINGS_FILE_NAME: &str = "stream-overlay-crop.json";

#[derive(Clone, Copy, Debug, Deserialize, Serialize, PartialEq, ts_rs::TS)]
#[ts(export, rename = "StreamOverlayCropSettings")]
pub struct OverlayCropSettings {
    pub left: f64,
    pub top: f64,
    pub width: f64,
    pub height: f64,
}

#[derive(Clone, Copy, Debug, Deserialize, Serialize, PartialEq, Eq, Default, ts_rs::TS)]
#[ts(export, rename = "StreamOverlayDisplayMode")]
#[serde(rename_all = "snake_case")]
pub enum OverlayDisplayMode {
    #[default]
    Current,
    Hero,
    Herohalf,
}

#[derive(Clone, Copy, Debug, Deserialize, Serialize, PartialEq)]
pub struct OverlaySettings {
    pub crop: OverlayCropSettings,
    #[serde(default)]
    pub display_mode: OverlayDisplayMode,
}

impl Default for OverlaySettings {
    fn default() -> Self {
        Self {
            crop: OverlayCropSettings::default(),
            display_mode: OverlayDisplayMode::default(),
        }
    }
}

impl Default for OverlayCropSettings {
    fn default() -> Self {
        Self {
            left: 0.342,
            top: 0.313,
            width: 0.58,
            height: 0.22,
        }
    }
}

#[derive(Clone, Debug, Deserialize, Serialize, PartialEq)]
struct OverlayCropDocument {
    v: u8,
    #[serde(flatten)]
    settings: OverlaySettings,
}

#[derive(Clone, Debug, Serialize, PartialEq, ts_rs::TS)]
#[ts(export, rename = "StreamOverlayCropSettingsPayload")]
pub struct OverlayCropSettingsPayload {
    pub crop: OverlayCropSettings,
    pub code: String,
    pub display_mode: OverlayDisplayMode,
}

#[derive(Clone, Debug)]
pub struct OverlaySettingsStore {
    path: PathBuf,
}

impl Default for OverlaySettingsStore {
    fn default() -> Self {
        Self {
            path: default_settings_path(),
        }
    }
}

impl OverlaySettingsStore {
    #[cfg_attr(not(test), allow(dead_code))]
    pub fn new(path: PathBuf) -> Self {
        Self { path }
    }

    pub fn load(&self) -> Result<OverlaySettings, String> {
        if !self.path.exists() {
            return Ok(OverlaySettings::default());
        }

        let raw = std::fs::read_to_string(&self.path).map_err(|err| {
            format!(
                "Failed to read overlay crop settings from {}: {err}",
                self.path.display()
            )
        })?;
        let document = serde_json::from_str::<OverlayCropDocument>(&raw).map_err(|err| {
            format!(
                "Failed to parse overlay crop settings from {}: {err}",
                self.path.display()
            )
        })?;

        let crop = validate_crop_settings(document.settings.crop)?;
        Ok(OverlaySettings {
            crop,
            display_mode: document.settings.display_mode,
        })
    }

    pub fn save(&self, crop: OverlayCropSettings) -> Result<OverlayCropSettingsPayload, String> {
        let crop = validate_crop_settings(crop)?;
        let display_mode = self
            .load()
            .map(|settings| settings.display_mode)
            .unwrap_or_default();
        let settings = OverlaySettings { crop, display_mode };
        let document = OverlayCropDocument { v: 1, settings };
        let raw = serde_json::to_string_pretty(&document)
            .map_err(|err| format!("Failed to serialize overlay crop settings: {err}"))?;

        if let Some(parent) = self.path.parent() {
            std::fs::create_dir_all(parent).map_err(|err| {
                format!(
                    "Failed to create overlay settings directory {}: {err}",
                    parent.display()
                )
            })?;
        }

        std::fs::write(&self.path, raw).map_err(|err| {
            format!(
                "Failed to write overlay crop settings to {}: {err}",
                self.path.display()
            )
        })?;

        Ok(self.payload(settings))
    }

    pub fn save_display_mode(
        &self,
        display_mode: OverlayDisplayMode,
    ) -> Result<OverlayCropSettingsPayload, String> {
        let crop = self
            .load()
            .map(|settings| settings.crop)
            .unwrap_or_default();
        let settings = OverlaySettings { crop, display_mode };
        let document = OverlayCropDocument { v: 1, settings };
        let raw = serde_json::to_string_pretty(&document)
            .map_err(|err| format!("Failed to serialize overlay crop settings: {err}"))?;

        if let Some(parent) = self.path.parent() {
            std::fs::create_dir_all(parent).map_err(|err| {
                format!(
                    "Failed to create overlay settings directory {}: {err}",
                    parent.display()
                )
            })?;
        }

        std::fs::write(&self.path, raw).map_err(|err| {
            format!(
                "Failed to write overlay crop settings to {}: {err}",
                self.path.display()
            )
        })?;

        Ok(self.payload(settings))
    }

    pub fn payload(&self, settings: OverlaySettings) -> OverlayCropSettingsPayload {
        OverlayCropSettingsPayload {
            crop: settings.crop,
            code: encode_crop_code(settings.crop),
            display_mode: settings.display_mode,
        }
    }

    pub fn load_payload(&self) -> Result<OverlayCropSettingsPayload, String> {
        let settings = self.load()?;
        Ok(self.payload(settings))
    }

    pub fn import_code(&self, code: &str) -> Result<OverlayCropSettingsPayload, String> {
        let crop = decode_crop_code(code)?;
        self.save(crop)
    }
}

fn default_settings_path() -> PathBuf {
    let base = dirs::config_dir()
        .or_else(dirs::data_local_dir)
        .unwrap_or_else(std::env::temp_dir);
    base.join(SETTINGS_DIRECTORY).join(SETTINGS_FILE_NAME)
}

pub fn validate_crop_settings(crop: OverlayCropSettings) -> Result<OverlayCropSettings, String> {
    fn valid_ratio(value: f64) -> bool {
        value.is_finite() && value > 0.0 && value < 1.0
    }

    if !valid_ratio(crop.left) {
        return Err("Overlay crop left must be between 0 and 1.".to_string());
    }
    if !valid_ratio(crop.top) {
        return Err("Overlay crop top must be between 0 and 1.".to_string());
    }
    if !valid_ratio(crop.width) {
        return Err("Overlay crop width must be between 0 and 1.".to_string());
    }
    if !valid_ratio(crop.height) {
        return Err("Overlay crop height must be between 0 and 1.".to_string());
    }
    if crop.left + crop.width > 1.0 {
        return Err("Overlay crop left + width must stay within the image.".to_string());
    }
    if crop.top + crop.height > 1.0 {
        return Err("Overlay crop top + height must stay within the image.".to_string());
    }

    Ok(crop)
}

pub fn encode_crop_code(crop: OverlayCropSettings) -> String {
    let document = OverlayCropDocument {
        v: 1,
        settings: OverlaySettings {
            crop,
            display_mode: OverlayDisplayMode::Current,
        },
    };
    let raw =
        serde_json::to_vec(&document).expect("overlay crop document should serialize to JSON");
    STANDARD.encode(raw)
}

pub fn decode_crop_code(code: &str) -> Result<OverlayCropSettings, String> {
    let trimmed = code.trim();
    if trimmed.is_empty() {
        return Err("Overlay crop code is empty.".to_string());
    }

    let bytes = STANDARD
        .decode(trimmed)
        .map_err(|err| format!("Overlay crop code is not valid Base64: {err}"))?;
    let document = serde_json::from_slice::<OverlayCropDocument>(&bytes)
        .map_err(|err| format!("Overlay crop code payload is invalid JSON: {err}"))?;

    if document.v != 1 {
        return Err(format!(
            "Unsupported overlay crop code version {}.",
            document.v
        ));
    }

    validate_crop_settings(document.settings.crop)
}

#[cfg(test)]
mod tests {
    use super::{
        decode_crop_code, encode_crop_code, OverlayCropSettings, OverlayDisplayMode,
        OverlaySettingsStore,
    };

    fn sample_crop() -> OverlayCropSettings {
        OverlayCropSettings {
            left: 0.29,
            top: 0.27,
            width: 0.61,
            height: 0.21,
        }
    }

    #[test]
    fn crop_code_round_trip_preserves_values() {
        let crop = OverlayCropSettings {
            left: 0.31,
            top: 0.28,
            width: 0.55,
            height: 0.19,
        };

        let decoded = decode_crop_code(&encode_crop_code(crop)).unwrap();

        assert_eq!(decoded, crop);
    }

    #[test]
    fn store_returns_default_when_file_is_missing() {
        let dir = tempfile::tempdir().unwrap();
        let store = OverlaySettingsStore::new(dir.path().join("missing.json"));

        let loaded = store.load_payload().unwrap();

        assert_eq!(loaded.crop, OverlayCropSettings::default());
        assert_eq!(loaded.display_mode, OverlayDisplayMode::Current);
    }

    #[test]
    fn store_can_save_and_reload_payload() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("overlay.json");
        let store = OverlaySettingsStore::new(path);
        let crop = sample_crop();

        let saved = store.save(crop).unwrap();
        let loaded = store.load_payload().unwrap();

        assert_eq!(saved, loaded);
        assert_eq!(loaded.crop, crop);
        assert_eq!(loaded.display_mode, OverlayDisplayMode::Current);
    }

    #[test]
    fn store_loads_legacy_crop_document_with_current_mode() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("legacy-overlay.json");
        let store = OverlaySettingsStore::new(path.clone());

        std::fs::write(
            path,
            serde_json::json!({
                "v": 1,
                "crop": sample_crop()
            })
            .to_string(),
        )
        .unwrap();

        let loaded = store.load_payload().unwrap();

        assert_eq!(loaded.crop, sample_crop());
        assert_eq!(loaded.display_mode, OverlayDisplayMode::Current);
    }

    #[test]
    fn store_can_save_and_reload_display_mode() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("overlay.json");
        let store = OverlaySettingsStore::new(path);

        store.save(sample_crop()).unwrap();
        let saved = store
            .save_display_mode(OverlayDisplayMode::Herohalf)
            .unwrap();
        let loaded = store.load_payload().unwrap();

        assert_eq!(saved, loaded);
        assert_eq!(loaded.crop, sample_crop());
        assert_eq!(loaded.display_mode, OverlayDisplayMode::Herohalf);
    }
}

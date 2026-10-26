use anyhow::{bail, Context as _};
use base64::{engine::general_purpose::STANDARD, Engine as _};
use serde::{Deserialize, Serialize};
use std::path::PathBuf;

const OVERLAY_SETTINGS_VERSION: u8 = 4;

#[derive(Clone, Copy, Debug, Deserialize, Serialize, PartialEq, specta::Type)]
#[serde(rename = "StreamOverlayCropSettings")]
pub struct OverlayCropSettings {
    pub left: f64,
    pub top: f64,
    pub width: f64,
    pub height: f64,
}

#[derive(Clone, Copy, Debug, Deserialize, Serialize, PartialEq, Eq, Default, specta::Type)]
#[serde(rename = "StreamOverlayDisplayMode")]
#[serde(rename_all = "snake_case")]
pub enum StreamOverlayDisplayMode {
    #[default]
    Current,
    Hero,
    Herohalf,
}

#[derive(Clone, Copy, Debug, Default, Deserialize, Serialize, PartialEq)]
pub struct OverlaySettings {
    pub crop: OverlayCropSettings,
    pub display_mode: StreamOverlayDisplayMode,
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

#[derive(Clone, Debug, Serialize, PartialEq, specta::Type)]
#[serde(rename = "StreamOverlayCropSettingsPayload")]
pub struct OverlayCropSettingsPayload {
    pub crop: OverlayCropSettings,
    pub code: String,
    pub display_mode: StreamOverlayDisplayMode,
}

#[derive(Clone, Debug)]
pub struct OverlaySettingsStore {
    path: PathBuf,
}

impl Default for OverlaySettingsStore {
    fn default() -> Self {
        Self::new(default_settings_path())
    }
}

impl OverlaySettingsStore {
    pub(crate) fn new(path: PathBuf) -> Self {
        Self { path }
    }

    pub fn load(&self) -> anyhow::Result<OverlaySettings> {
        let read_path = &self.path;
        if !read_path.exists() {
            return Ok(OverlaySettings::default());
        }

        let raw = match std::fs::read_to_string(read_path) {
            Ok(raw) => raw,
            Err(err) => {
                crate::services::debug_error!(
                    "Failed to read overlay crop settings from {}: {err}",
                    read_path.display()
                );
                return Ok(OverlaySettings::default());
            }
        };
        let document = match serde_json::from_str::<OverlayCropDocument>(&raw) {
            Ok(document) => document,
            Err(err) => {
                crate::services::debug_error!(
                    "Failed to parse overlay crop settings from {}: {err}",
                    read_path.display()
                );
                return Ok(OverlaySettings::default());
            }
        };
        if document.v != OVERLAY_SETTINGS_VERSION {
            crate::services::debug_error!(
                "Unsupported overlay crop settings version {}.",
                document.v
            );
            return Ok(OverlaySettings::default());
        }

        let crop = match validate_crop_settings(document.settings.crop) {
            Ok(crop) => crop,
            Err(err) => {
                crate::services::debug_error!(
                    "Invalid overlay crop settings in {}: {err}",
                    read_path.display()
                );
                return Ok(OverlaySettings::default());
            }
        };
        Ok(OverlaySettings {
            crop,
            display_mode: document.settings.display_mode,
        })
    }

    pub fn save(&self, crop: OverlayCropSettings) -> anyhow::Result<OverlayCropSettingsPayload> {
        let crop = validate_crop_settings(crop)?;
        let display_mode = self
            .load()
            .map(|settings| settings.display_mode)
            .unwrap_or_default();
        self.write_settings(OverlaySettings { crop, display_mode })
    }

    pub fn save_display_mode(
        &self,
        display_mode: StreamOverlayDisplayMode,
    ) -> anyhow::Result<OverlayCropSettingsPayload> {
        let crop = self
            .load()
            .map(|settings| settings.crop)
            .unwrap_or_default();
        self.write_settings(OverlaySettings { crop, display_mode })
    }

    fn write_settings(
        &self,
        settings: OverlaySettings,
    ) -> anyhow::Result<OverlayCropSettingsPayload> {
        let document = OverlayCropDocument {
            v: OVERLAY_SETTINGS_VERSION,
            settings,
        };
        let raw = serde_json::to_string_pretty(&document)
            .context("Failed to serialize overlay crop settings")?;

        if let Some(parent) = self.path.parent() {
            std::fs::create_dir_all(parent).with_context(|| {
                format!(
                    "Failed to create overlay settings directory {}",
                    parent.display()
                )
            })?;
        }

        std::fs::write(&self.path, raw).with_context(|| {
            format!(
                "Failed to write overlay crop settings to {}",
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

    pub fn load_payload(&self) -> anyhow::Result<OverlayCropSettingsPayload> {
        let settings = self.load()?;
        Ok(self.payload(settings))
    }

    pub fn import_code(&self, code: &str) -> anyhow::Result<OverlayCropSettingsPayload> {
        let crop = decode_crop_code(code)?;
        self.save(crop)
    }
}

fn default_settings_path() -> PathBuf {
    crate::services::paths::overlay_settings_path()
}

pub fn validate_crop_settings(crop: OverlayCropSettings) -> anyhow::Result<OverlayCropSettings> {
    fn valid_ratio(value: f64) -> bool {
        value.is_finite() && value > 0.0 && value < 1.0
    }

    if !valid_ratio(crop.left) {
        bail!("Overlay crop left must be between 0 and 1.");
    }
    if !valid_ratio(crop.top) {
        bail!("Overlay crop top must be between 0 and 1.");
    }
    if !valid_ratio(crop.width) {
        bail!("Overlay crop width must be between 0 and 1.");
    }
    if !valid_ratio(crop.height) {
        bail!("Overlay crop height must be between 0 and 1.");
    }
    if crop.left + crop.width > 1.0 {
        bail!("Overlay crop left + width must stay within the image.");
    }
    if crop.top + crop.height > 1.0 {
        bail!("Overlay crop top + height must stay within the image.");
    }

    Ok(crop)
}

pub fn encode_crop_code(crop: OverlayCropSettings) -> String {
    let document = OverlayCropDocument {
        v: OVERLAY_SETTINGS_VERSION,
        settings: OverlaySettings {
            crop,
            display_mode: StreamOverlayDisplayMode::Current,
        },
    };
    let raw =
        serde_json::to_vec(&document).expect("overlay crop document should serialize to JSON");
    STANDARD.encode(raw)
}

pub fn decode_crop_code(code: &str) -> anyhow::Result<OverlayCropSettings> {
    let trimmed = code.trim();
    if trimmed.is_empty() {
        bail!("Overlay crop code is empty.");
    }

    let bytes = STANDARD
        .decode(trimmed)
        .context("Overlay crop code is not valid Base64")?;
    let document = serde_json::from_slice::<OverlayCropDocument>(&bytes)
        .context("Overlay crop code payload is invalid JSON")?;

    if document.v != OVERLAY_SETTINGS_VERSION {
        bail!("Unsupported overlay crop code version {}.", document.v);
    }

    validate_crop_settings(document.settings.crop)
}

#[cfg(test)]
mod tests {
    use super::{
        decode_crop_code, encode_crop_code, OverlayCropSettings, OverlaySettingsStore,
        StreamOverlayDisplayMode,
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
        assert_eq!(loaded.display_mode, StreamOverlayDisplayMode::Current);
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
        assert_eq!(loaded.display_mode, StreamOverlayDisplayMode::Current);
    }

    #[test]
    fn store_uses_default_for_old_crop_document() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("old-overlay.json");
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

        assert_eq!(loaded.crop, OverlayCropSettings::default());
        assert_eq!(loaded.display_mode, StreamOverlayDisplayMode::Current);
    }

    #[test]
    fn store_can_save_and_reload_display_mode() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("overlay.json");
        let store = OverlaySettingsStore::new(path);

        store.save(sample_crop()).unwrap();
        let saved = store
            .save_display_mode(StreamOverlayDisplayMode::Herohalf)
            .unwrap();
        let loaded = store.load_payload().unwrap();

        assert_eq!(saved, loaded);
        assert_eq!(loaded.crop, sample_crop());
        assert_eq!(loaded.display_mode, StreamOverlayDisplayMode::Herohalf);
    }
}

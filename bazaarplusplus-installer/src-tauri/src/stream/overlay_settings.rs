use anyhow::{bail, Context as _};
use base64::{engine::general_purpose::STANDARD, Engine as _};
use serde::{Deserialize, Serialize};
use std::{
    path::PathBuf,
    sync::{Arc, Mutex, MutexGuard, OnceLock},
    time::SystemTime,
};

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

/// Reads and writes the overlay settings file. Clones share one parse memo,
/// and every `Default` store is a clone of one process-wide store, so the
/// stream service and the Tauri commands clear the same memo when they write.
#[derive(Clone, Debug)]
pub struct OverlaySettingsStore {
    path: PathBuf,
    memo: Arc<Mutex<Option<SettingsMemo>>>,
}

/// The last parse of a regular settings file, reused while the file keeps the
/// same modification time and length. A write through the store clears it, so
/// only an edit from outside the installer relies on that stamp.
#[derive(Debug)]
struct SettingsMemo {
    modified: SystemTime,
    len: u64,
    settings: OverlaySettings,
}

impl Default for OverlaySettingsStore {
    fn default() -> Self {
        static SHARED: OnceLock<OverlaySettingsStore> = OnceLock::new();
        SHARED
            .get_or_init(|| Self::new(default_settings_path()))
            .clone()
    }
}

impl OverlaySettingsStore {
    pub(crate) fn new(path: PathBuf) -> Self {
        Self {
            path,
            memo: Arc::default(),
        }
    }

    /// The saved settings, or the defaults when the file is missing or
    /// invalid. The OBS overlay asks every few seconds, so the file is parsed
    /// again only after its modification time or length changes.
    pub fn load(&self) -> anyhow::Result<OverlaySettings> {
        let Ok(metadata) = std::fs::metadata(&self.path) else {
            return Ok(OverlaySettings::default());
        };
        // Only a regular file has a stamp that follows its contents.
        let stamp = metadata
            .is_file()
            .then(|| metadata.modified().ok())
            .flatten()
            .map(|modified| (modified, metadata.len()));
        if let Some((modified, len)) = stamp {
            if let Some(memo) = self.memo().as_ref() {
                if memo.modified == modified && memo.len == len {
                    return Ok(memo.settings);
                }
            }
        }

        // The stamp is taken before the read: a write racing this parse leaves
        // a different stamp on the file, so the next load parses again.
        let Some(raw) = self.read_raw() else {
            // A failed read may be transient (another process holding the
            // file), so it is not remembered: the next load reads again.
            return Ok(OverlaySettings::default());
        };
        // Invalid contents stay invalid until the file changes, so the
        // defaults they yield are remembered like any parse.
        let settings = self.parse_settings(&raw);
        if let Some((modified, len)) = stamp {
            *self.memo() = Some(SettingsMemo {
                modified,
                len,
                settings,
            });
        }
        Ok(settings)
    }

    fn memo(&self) -> MutexGuard<'_, Option<SettingsMemo>> {
        self.memo.lock().expect("overlay settings memo poisoned")
    }

    fn read_raw(&self) -> Option<String> {
        std::fs::read_to_string(&self.path)
            .inspect_err(|err| {
                crate::services::debug_error!(
                    "Failed to read overlay crop settings from {}: {err}",
                    self.path.display()
                );
            })
            .ok()
    }

    fn parse_settings(&self, raw: &str) -> OverlaySettings {
        let read_path = &self.path;
        let document = match serde_json::from_str::<OverlayCropDocument>(raw) {
            Ok(document) => document,
            Err(err) => {
                crate::services::debug_error!(
                    "Failed to parse overlay crop settings from {}: {err}",
                    read_path.display()
                );
                return OverlaySettings::default();
            }
        };
        if document.v != OVERLAY_SETTINGS_VERSION {
            crate::services::debug_error!(
                "Unsupported overlay crop settings version {}.",
                document.v
            );
            return OverlaySettings::default();
        }

        let crop = match validate_crop_settings(document.settings.crop) {
            Ok(crop) => crop,
            Err(err) => {
                crate::services::debug_error!(
                    "Invalid overlay crop settings in {}: {err}",
                    read_path.display()
                );
                return OverlaySettings::default();
            }
        };
        OverlaySettings {
            crop,
            display_mode: document.settings.display_mode,
        }
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

        let written = std::fs::write(&self.path, raw);
        // Cleared even when the write fails part way: the next load must read
        // whatever reached the file.
        *self.memo() = None;
        written.with_context(|| {
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

    #[test]
    fn store_parses_again_only_when_the_file_stamp_changes() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("overlay.json");
        let reader = OverlaySettingsStore::new(path.clone());
        let writer = OverlaySettingsStore::new(path.clone());
        let document = |mode: &str| {
            serde_json::json!({ "v": 4, "crop": sample_crop(), "display_mode": mode }).to_string()
        };
        let stamp = |modified_secs: u64| {
            std::fs::File::options()
                .write(true)
                .open(&path)
                .unwrap()
                .set_modified(std::time::UNIX_EPOCH + std::time::Duration::from_secs(modified_secs))
                .unwrap();
        };
        std::fs::write(&path, document("hero")).unwrap();
        stamp(1_700_000_000);
        assert_eq!(
            reader.load().unwrap().display_mode,
            StreamOverlayDisplayMode::Hero
        );

        // Same length and modification time: the memoized parse is served.
        std::fs::write(&path, document("hero").replace("\"hero\"", "\"HERO\"")).unwrap();
        stamp(1_700_000_000);
        assert_eq!(
            reader.load().unwrap().display_mode,
            StreamOverlayDisplayMode::Hero
        );

        // An outside edit that moves the stamp is read back.
        std::fs::write(&path, document("herohalf")).unwrap();
        stamp(1_700_000_100);
        assert_eq!(
            reader.load().unwrap().display_mode,
            StreamOverlayDisplayMode::Herohalf
        );

        // So is a save through another store.
        writer
            .save_display_mode(StreamOverlayDisplayMode::Current)
            .unwrap();
        assert_eq!(
            reader.load().unwrap().display_mode,
            StreamOverlayDisplayMode::Current
        );
    }

    #[cfg(unix)]
    #[test]
    fn a_failed_read_is_not_remembered() {
        use std::os::unix::fs::PermissionsExt as _;
        if crate::test_support::permission_bits_are_bypassed() {
            return;
        }
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("overlay.json");
        let store = OverlaySettingsStore::new(path.clone());
        OverlaySettingsStore::new(path.clone())
            .save(sample_crop())
            .unwrap();
        let set_mode = |mode| {
            std::fs::set_permissions(&path, std::fs::Permissions::from_mode(mode)).unwrap();
        };

        // The file keeps its stamp, but its contents cannot be read for now.
        set_mode(0o000);
        let unreadable = store.load().unwrap().crop;
        set_mode(0o600);

        assert_eq!(unreadable, OverlayCropSettings::default());
        assert_eq!(store.load().unwrap().crop, sample_crop());
    }

    #[test]
    fn a_save_clears_the_memo_of_every_store_sharing_it() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("overlay.json");
        let reader = OverlaySettingsStore::new(path.clone());
        let writer = reader.clone();
        writer.save(sample_crop()).unwrap();
        let modified = std::fs::metadata(&path).unwrap().modified().unwrap();
        assert_eq!(reader.load().unwrap().crop, sample_crop());

        // Same length, and the stamp is pinned, so only the cleared memo can
        // show the new crop.
        let moved = OverlayCropSettings {
            left: 0.28,
            ..sample_crop()
        };
        writer.save(moved).unwrap();
        std::fs::File::options()
            .write(true)
            .open(&path)
            .unwrap()
            .set_modified(modified)
            .unwrap();

        assert_eq!(reader.load().unwrap().crop, moved);
    }
}

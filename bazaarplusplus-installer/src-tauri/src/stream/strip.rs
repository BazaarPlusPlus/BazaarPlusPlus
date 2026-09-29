//! Overlay strip rendering: crop a record's screenshot, cache the PNG, and
//! serve it. Every step blocks (SQLite, settings file, image decode), so
//! callers run `render_strip` on a blocking thread.

use super::overlay_settings::{OverlayCropSettings, OverlaySettingsStore};
use super::records::OverlayRecordRepository;
use image::{DynamicImage, ImageFormat};
use std::{
    collections::hash_map::DefaultHasher,
    hash::{Hash, Hasher},
    io::Cursor,
    path::{Path, PathBuf},
    time::UNIX_EPOCH,
};

/// Which cache namespace a strip is written to under the service cache root.
#[derive(Clone, Copy, Debug)]
pub(super) enum StripCache {
    /// The OBS overlay's captured repository, cached at the root.
    Overlay,
    /// A History preview source, cached per source file.
    History,
}

#[derive(Debug)]
pub(super) struct StripRequest {
    pub(super) record_id: String,
    /// A crop supplied by the request; `None` uses the saved overlay settings.
    pub(super) crop: Option<OverlayCropSettings>,
    /// Render without reading or writing the cache.
    pub(super) preview: bool,
    pub(super) cache: StripCache,
}

/// Returns the PNG strip for a record, or `None` when the record or its
/// screenshot is missing.
pub(super) fn render_strip(
    records: &OverlayRecordRepository,
    settings: &OverlaySettingsStore,
    cache_root: &Path,
    request: &StripRequest,
) -> Result<Option<Vec<u8>>, String> {
    let crop = match request.crop {
        Some(crop) => crop,
        None => settings.load()?.crop,
    };
    let Some(path) = records.load_image_path(&request.record_id)? else {
        return Ok(None);
    };

    if request.preview {
        let bytes = std::fs::read(&path)
            .map_err(|err| format!("Failed to read overlay source image: {err}"))?;
        return crop_strip_image(&bytes, crop).map(Some);
    }

    let cache_directory = match request.cache {
        StripCache::Overlay => cache_root.to_path_buf(),
        StripCache::History => {
            // Two installations may have copied screenshot ids and identical file metadata.
            let mut source = DefaultHasher::new();
            path.hash(&mut source);
            cache_root
                .join("history")
                .join(format!("{:016x}", source.finish()))
        }
    };
    load_or_create_strip_cache(&cache_directory, &request.record_id, &path, crop).map(Some)
}

fn sanitized_cache_name(value: &str) -> String {
    value
        .chars()
        .map(|ch| match ch {
            'a'..='z' | 'A'..='Z' | '0'..='9' | '-' | '_' => ch,
            _ => '_',
        })
        .collect()
}

fn crop_cache_path(
    cache_directory: &Path,
    record_id: &str,
    source_path: &Path,
    crop: OverlayCropSettings,
) -> Result<PathBuf, String> {
    let metadata = std::fs::metadata(source_path).map_err(|err| {
        format!(
            "Failed to read source image metadata from {}: {err}",
            source_path.display()
        )
    })?;
    let modified = metadata
        .modified()
        .ok()
        .and_then(|value| value.duration_since(UNIX_EPOCH).ok())
        .map(|value| value.as_secs())
        .unwrap_or(0);
    let cache_name = format!(
        "{}-{}-{}-{}-{}-{}-{}-strip.png",
        sanitized_cache_name(record_id),
        metadata.len(),
        modified,
        (crop.left * 10_000.0).round() as i64,
        (crop.top * 10_000.0).round() as i64,
        (crop.width * 10_000.0).round() as i64,
        (crop.height * 10_000.0).round() as i64
    );

    Ok(cache_directory.join(cache_name))
}

fn load_or_create_strip_cache(
    cache_directory: &Path,
    record_id: &str,
    source_path: &Path,
    crop: OverlayCropSettings,
) -> Result<Vec<u8>, String> {
    let cache_path = crop_cache_path(cache_directory, record_id, source_path, crop)?;
    if cache_path.exists() {
        return std::fs::read(&cache_path).map_err(|err| {
            format!(
                "Failed to read cached overlay strip from {}: {err}",
                cache_path.display()
            )
        });
    }

    let source_bytes = std::fs::read(source_path).map_err(|err| {
        format!(
            "Failed to read overlay source image from {}: {err}",
            source_path.display()
        )
    })?;
    let bytes = crop_strip_image(&source_bytes, crop)?;

    if let Some(parent) = cache_path.parent() {
        std::fs::create_dir_all(parent).map_err(|err| {
            format!(
                "Failed to create overlay cache directory {}: {err}",
                parent.display()
            )
        })?;
    }

    std::fs::write(&cache_path, &bytes).map_err(|err| {
        format!(
            "Failed to write cached overlay strip to {}: {err}",
            cache_path.display()
        )
    })?;

    Ok(bytes)
}

fn crop_strip_image(source_bytes: &[u8], crop: OverlayCropSettings) -> Result<Vec<u8>, String> {
    let image = image::load_from_memory(source_bytes)
        .map_err(|err| format!("Failed to decode overlay source image: {err}"))?;
    let cropped = crop_dynamic_image(image, crop)?;
    let mut output = Cursor::new(Vec::new());
    cropped
        .write_to(&mut output, ImageFormat::Png)
        .map_err(|err| format!("Failed to encode overlay strip image: {err}"))?;
    Ok(output.into_inner())
}

fn crop_dynamic_image(
    image: DynamicImage,
    crop: OverlayCropSettings,
) -> Result<DynamicImage, String> {
    let width = image.width();
    let height = image.height();
    if width == 0 || height == 0 {
        return Err("Overlay source image is empty.".to_string());
    }

    let left = ((width as f64) * crop.left)
        .floor()
        .clamp(0.0, (width - 1) as f64) as u32;
    let top = ((height as f64) * crop.top)
        .floor()
        .clamp(0.0, (height - 1) as f64) as u32;
    let crop_width = ((width as f64) * crop.width)
        .round()
        .clamp(1.0, (width - left) as f64) as u32;
    let crop_height = ((height as f64) * crop.height)
        .round()
        .clamp(1.0, (height - top) as f64) as u32;

    Ok(image.crop_imm(left, top, crop_width, crop_height))
}

#[cfg(test)]
mod tests {
    use super::{crop_cache_path, crop_dynamic_image, load_or_create_strip_cache};
    use crate::stream::overlay_settings::OverlayCropSettings;
    use image::{DynamicImage, GenericImageView, RgbaImage};

    #[test]
    fn crop_dynamic_image_returns_expected_dimensions() {
        let image = DynamicImage::ImageRgba8(RgbaImage::new(1000, 500));
        let crop = OverlayCropSettings {
            left: 0.25,
            top: 0.2,
            width: 0.5,
            height: 0.3,
        };

        let cropped = crop_dynamic_image(image, crop).unwrap();

        assert_eq!(cropped.dimensions(), (500, 150));
    }

    #[test]
    fn strip_cache_hit_does_not_decode_source_image() {
        let temp_dir = tempfile::tempdir().unwrap();
        let source_path = temp_dir.path().join("source.png");
        let cache_directory = temp_dir.path().join("cache");
        let crop = OverlayCropSettings::default();
        std::fs::write(&source_path, b"not an image").unwrap();
        let cache_path = crop_cache_path(&cache_directory, "shot-1", &source_path, crop).unwrap();
        std::fs::create_dir_all(cache_path.parent().unwrap()).unwrap();
        std::fs::write(&cache_path, b"cached strip").unwrap();

        let bytes =
            load_or_create_strip_cache(&cache_directory, "shot-1", &source_path, crop).unwrap();

        assert_eq!(bytes, b"cached strip");
    }
}

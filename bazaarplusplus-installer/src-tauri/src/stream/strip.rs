//! Strip rendering: crop a screenshot, cache the PNG, and serve it. Every step
//! blocks (SQLite, settings file, image decode, cache I/O), so callers run
//! these functions on a blocking thread.
//!
//! Float-to-integer casts here go through `pixel_offset` and `basis_points`,
//! which spell out the saturating behavior. The cast lints keep a new bare
//! `as` from slipping in beside them.
#![warn(clippy::cast_possible_truncation, clippy::cast_sign_loss)]

use super::overlay_settings::{OverlayCropSettings, OverlaySettingsStore};
use super::records::OverlayRecordRepository;
use anyhow::{anyhow, bail, Context as _};
use image::{DynamicImage, ImageFormat};
use std::{
    io::{Cursor, ErrorKind},
    path::{Path, PathBuf},
    sync::atomic::{AtomicU64, Ordering},
    time::{SystemTime, UNIX_EPOCH},
};

/// Suffix of an in-flight cache write; a leftover one is an orphan.
pub(super) const TEMP_SUFFIX: &str = ".tmp";

#[derive(Debug)]
pub(super) struct StripRequest {
    pub(super) record_id: String,
    /// A crop supplied by the request; `None` uses the saved overlay settings.
    pub(super) crop: Option<OverlayCropSettings>,
    /// Render without reading or writing the cache.
    pub(super) preview: bool,
}

/// Returns the OBS overlay's PNG strip for a record, cached at the root of
/// `cache_root`, or `None` when the record or its screenshot is missing.
pub(super) fn render_strip(
    records: &OverlayRecordRepository,
    settings: &OverlaySettingsStore,
    cache_root: &Path,
    request: &StripRequest,
) -> anyhow::Result<Option<Vec<u8>>> {
    let crop = match request.crop {
        Some(crop) => crop,
        None => settings.load()?.crop,
    };
    let Some(path) = records.load_image_path(&request.record_id)? else {
        return Ok(None);
    };

    if request.preview {
        let bytes = std::fs::read(&path).context("Failed to read overlay source image")?;
        return crop_strip_image(&bytes, crop).map(Some);
    }

    cached_strip(cache_root, &request.record_id, &path, crop).map(Some)
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
) -> anyhow::Result<PathBuf> {
    let metadata = std::fs::metadata(source_path).with_context(|| {
        format!(
            "Failed to read source image metadata from {}",
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
        basis_points(crop.left),
        basis_points(crop.top),
        basis_points(crop.width),
        basis_points(crop.height)
    );

    Ok(cache_directory.join(cache_name))
}

/// Returns the strip for `source_path` and `crop` cached in `cache_directory`,
/// rendering and storing it on a miss. A hit refreshes the entry's
/// modification time so an age-based sweep sees it as recently used.
pub(super) fn cached_strip(
    cache_directory: &Path,
    record_id: &str,
    source_path: &Path,
    crop: OverlayCropSettings,
) -> anyhow::Result<Vec<u8>> {
    let cache_path = crop_cache_path(cache_directory, record_id, source_path, crop)?;
    match std::fs::read(&cache_path) {
        Ok(bytes) => {
            touch(&cache_path);
            return Ok(bytes);
        }
        Err(err) if err.kind() == ErrorKind::NotFound => {}
        Err(err) => {
            bail!(
                "Failed to read cached overlay strip from {}: {err}",
                cache_path.display()
            )
        }
    }

    let source_bytes = std::fs::read(source_path).with_context(|| {
        format!(
            "Failed to read overlay source image from {}",
            source_path.display()
        )
    })?;
    let bytes = crop_strip_image(&source_bytes, crop)?;
    write_cache_entry(&cache_path, &bytes)?;
    Ok(bytes)
}

/// Best effort: a file another process holds open (Windows) keeps its time.
fn touch(path: &Path) {
    if let Ok(file) = std::fs::File::options().write(true).open(path) {
        let _ = file.set_modified(SystemTime::now());
    }
}

/// Writes through a uniquely named temporary file, so concurrent requests for
/// one entry never read a partial PNG.
fn write_cache_entry(cache_path: &Path, bytes: &[u8]) -> anyhow::Result<()> {
    static NEXT_TEMP: AtomicU64 = AtomicU64::new(0);

    if let Some(parent) = cache_path.parent() {
        std::fs::create_dir_all(parent).with_context(|| {
            format!(
                "Failed to create overlay cache directory {}",
                parent.display()
            )
        })?;
    }
    let mut temp_name = cache_path.file_name().unwrap_or_default().to_os_string();
    temp_name.push(format!(
        ".{}-{}{TEMP_SUFFIX}",
        std::process::id(),
        NEXT_TEMP.fetch_add(1, Ordering::Relaxed)
    ));
    let temp_path = cache_path.with_file_name(temp_name);

    if let Err(err) = std::fs::write(&temp_path, bytes) {
        let _ = std::fs::remove_file(&temp_path);
        bail!(
            "Failed to write cached overlay strip to {}: {err}",
            temp_path.display()
        );
    }
    match std::fs::rename(&temp_path, cache_path) {
        Ok(()) => Ok(()),
        Err(err) => {
            let _ = std::fs::remove_file(&temp_path);
            // Windows refuses to replace an entry another request holds open;
            // that entry already has the same content.
            if cache_path.exists() {
                Ok(())
            } else {
                Err(anyhow!(
                    "Failed to store cached overlay strip at {}: {err}",
                    cache_path.display()
                ))
            }
        }
    }
}

fn crop_strip_image(source_bytes: &[u8], crop: OverlayCropSettings) -> anyhow::Result<Vec<u8>> {
    let image =
        image::load_from_memory(source_bytes).context("Failed to decode overlay source image")?;
    let cropped = crop_dynamic_image(image, crop)?;
    let mut output = Cursor::new(Vec::new());
    cropped
        .write_to(&mut output, ImageFormat::Png)
        .context("Failed to encode overlay strip image")?;
    Ok(output.into_inner())
}

fn crop_dynamic_image(
    image: DynamicImage,
    crop: OverlayCropSettings,
) -> anyhow::Result<DynamicImage> {
    let width = image.width();
    let height = image.height();
    if width == 0 || height == 0 {
        bail!("Overlay source image is empty.");
    }

    let left = pixel_offset(
        (f64::from(width) * crop.left)
            .floor()
            .clamp(0.0, f64::from(width - 1)),
    );
    let top = pixel_offset(
        (f64::from(height) * crop.top)
            .floor()
            .clamp(0.0, f64::from(height - 1)),
    );
    let crop_width = pixel_offset(
        (f64::from(width) * crop.width)
            .round()
            .clamp(1.0, f64::from(width - left)),
    );
    let crop_height = pixel_offset(
        (f64::from(height) * crop.height)
            .round()
            .clamp(1.0, f64::from(height - top)),
    );

    Ok(image.crop_imm(left, top, crop_width, crop_height))
}

/// Converts a pixel coordinate that the caller has already clamped into the
/// image's bounds. Inside those bounds the value is a whole number that fits
/// `u32`, so nothing is lost. A NaN crop fraction survives `clamp` and becomes
/// 0 here, because float-to-int `as` saturates and maps NaN to 0.
#[expect(
    clippy::cast_possible_truncation,
    clippy::cast_sign_loss,
    reason = "callers clamp into [0, u32 image dimension]; `as` saturates and maps NaN to 0"
)]
fn pixel_offset(clamped: f64) -> u32 {
    clamped as u32
}

/// A crop fraction in ten-thousandths, used only to key the strip cache.
/// Settings keep fractions in `[0, 1]`, so the result is at most 10 000; any
/// other value saturates (NaN becomes 0), which at worst misses the cache.
#[expect(
    clippy::cast_possible_truncation,
    reason = "a cache key; `as` saturates out-of-range values and maps NaN to 0"
)]
fn basis_points(fraction: f64) -> i64 {
    (fraction * 10_000.0).round() as i64
}

#[cfg(test)]
mod tests {
    use super::{
        basis_points, cached_strip, crop_cache_path, crop_dynamic_image, pixel_offset, TEMP_SUFFIX,
    };
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
    fn strip_cache_miss_stores_one_entry_without_a_temporary_file() {
        let temp_dir = tempfile::tempdir().unwrap();
        let source_path = temp_dir.path().join("source.png");
        let cache_directory = temp_dir.path().join("cache");
        let crop = OverlayCropSettings::default();
        RgbaImage::from_pixel(64, 32, image::Rgba([1, 2, 3, 255]))
            .save(&source_path)
            .unwrap();

        let bytes = cached_strip(&cache_directory, "shot-1", &source_path, crop).unwrap();

        let entries = std::fs::read_dir(&cache_directory)
            .unwrap()
            .map(|entry| entry.unwrap().path())
            .collect::<Vec<_>>();
        assert_eq!(
            entries,
            [crop_cache_path(&cache_directory, "shot-1", &source_path, crop).unwrap()]
        );
        assert!(!entries[0].to_string_lossy().ends_with(TEMP_SUFFIX));
        assert_eq!(std::fs::read(&entries[0]).unwrap(), bytes);
    }

    #[test]
    fn float_casts_saturate_and_map_nan_to_zero() {
        assert_eq!(pixel_offset(42.0), 42);
        assert_eq!(pixel_offset(f64::NAN), 0);
        assert_eq!(pixel_offset(-1.0), 0);
        assert_eq!(pixel_offset(1e12), u32::MAX);
        assert_eq!(basis_points(0.25), 2_500);
        assert_eq!(basis_points(f64::NAN), 0);
        assert_eq!(basis_points(f64::INFINITY), i64::MAX);
    }
}

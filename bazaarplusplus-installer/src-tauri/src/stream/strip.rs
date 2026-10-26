//! Strip rendering: crop a screenshot, cache the PNG, and serve it. Every step
//! blocks (SQLite, settings file, image decode, cache I/O), so callers run
//! these functions on a blocking thread. This module also owns the strip cache
//! both routes write and its once-per-process sweep.
//!
//! Float-to-integer casts here go through `pixel_offset` and `basis_points`,
//! which spell out the saturating behavior. The cast lints keep a new bare
//! `as` from slipping in beside them.
#![warn(clippy::cast_possible_truncation, clippy::cast_sign_loss)]

use super::overlay_settings::{OverlayCropSettings, OverlaySettingsStore};
use super::records::OverlayRecordRepository;
use crate::services::paths;
use anyhow::{anyhow, bail, Context as _};
use image::{DynamicImage, ImageFormat};
use sha2::{Digest, Sha256};
use std::{
    collections::HashMap,
    io::{Cursor, ErrorKind, Read as _},
    path::{Path, PathBuf},
    sync::{
        atomic::{AtomicU64, Ordering},
        Arc, Mutex, OnceLock,
    },
    time::{Duration, SystemTime, UNIX_EPOCH},
};

/// Suffix of an in-flight cache write; a leftover one is an orphan.
pub(super) const TEMP_SUFFIX: &str = ".tmp";
/// The History Thumbnail cache beneath the service cache root.
pub(super) const HISTORY_THUMBNAIL_CACHE: &str = "history-thumbnails";
/// The OBS overlay strip cache beneath the service cache root.
pub(super) const OBS_STRIP_CACHE: &str = "obs-strips";
/// Every strip cache `sweep` visits; each holds one namespace per installation.
const STRIP_CACHES: [&str; 2] = [HISTORY_THUMBNAIL_CACHE, OBS_STRIP_CACHE];
/// Suffix of a cached strip; older versions left OBS strips at the cache root.
const STRIP_SUFFIX: &str = "-strip.png";
const CACHE_LIMITS: CacheLimits = CacheLimits {
    unused_for: Duration::from_secs(30 * 24 * 60 * 60),
    max_entries: 1000,
    fresh_for: Duration::from_secs(10),
};
/// A hit rewrites an entry's modification time only once it is this old: a
/// strip served every few seconds is written about once a day, not on every
/// request. Kept short because the count cap removes the oldest times across
/// both caches; a day keeps that order within a day of least recently used,
/// so a strip OBS shows all the time outlives thumbnails viewed once.
const REFRESH_AFTER: Duration = Duration::from_secs(24 * 60 * 60);
/// Screenshot source paths remembered per process and per route.
const SOURCE_PATH_CAPACITY: usize = 512;

/// The cache directory of one installation path inside `cache`, named by a
/// digest of that path so it is stable across restarts.
pub(super) fn cache_namespace(cache_root: &Path, cache: &str, game_path: &Path) -> PathBuf {
    let digest = Sha256::digest(game_path.as_os_str().as_encoded_bytes());
    let namespace = digest[..8]
        .iter()
        .map(|byte| format!("{byte:02x}"))
        .collect::<String>();
    cache_root.join(cache).join(namespace)
}

/// Remembers where a screenshot's source image lives, so a repeated strip
/// request skips SQLite. A screenshot row keeps its image path once written;
/// a remembered file that is gone is forgotten and looked up again. Clones
/// share one bounded, least-recently-used map.
#[derive(Clone, Default)]
pub(super) struct SourcePaths {
    entries: Arc<Mutex<SourcePathEntries>>,
}

#[derive(Default)]
struct SourcePathEntries {
    paths: HashMap<(PathBuf, String), (PathBuf, u64)>,
    clock: u64,
}

impl SourcePaths {
    /// The source image of `screenshot_id` in `game_path`, from memory or from
    /// `lookup`, which returns `None` when the row or its file is missing. A
    /// miss is not remembered.
    pub(super) fn resolve(
        &self,
        game_path: &Path,
        screenshot_id: &str,
        lookup: impl FnOnce() -> anyhow::Result<Option<PathBuf>>,
    ) -> anyhow::Result<Option<PathBuf>> {
        let key = (game_path.to_path_buf(), screenshot_id.to_string());
        if let Some(path) = self.get(&key) {
            if path.is_file() {
                return Ok(Some(path));
            }
            self.lock().paths.remove(&key);
        }
        let found = lookup()?;
        if let Some(path) = &found {
            self.insert(key, path.clone());
        }
        Ok(found)
    }

    fn lock(&self) -> std::sync::MutexGuard<'_, SourcePathEntries> {
        self.entries.lock().expect("strip source paths poisoned")
    }

    fn get(&self, key: &(PathBuf, String)) -> Option<PathBuf> {
        let mut entries = self.lock();
        entries.clock += 1;
        let now = entries.clock;
        entries.paths.get_mut(key).map(|(path, used)| {
            *used = now;
            path.clone()
        })
    }

    fn insert(&self, key: (PathBuf, String), path: PathBuf) {
        let mut entries = self.lock();
        entries.clock += 1;
        let now = entries.clock;
        if entries.paths.len() >= SOURCE_PATH_CAPACITY && !entries.paths.contains_key(&key) {
            let oldest = entries
                .paths
                .iter()
                .min_by_key(|(_, (_, used))| *used)
                .map(|(key, _)| key.clone());
            if let Some(oldest) = oldest {
                entries.paths.remove(&oldest);
            }
        }
        entries.paths.insert(key, (path, now));
    }
}

#[derive(Debug)]
pub(super) struct StripRequest {
    pub(super) record_id: String,
    /// A crop supplied by the request; `None` uses the saved overlay settings.
    pub(super) crop: Option<OverlayCropSettings>,
    /// Render without reading or writing the cache.
    pub(super) preview: bool,
}

/// Returns the OBS overlay's PNG strip for a record, cached in the record
/// installation's `OBS_STRIP_CACHE` namespace, or `None` when the record or
/// its screenshot is missing.
pub(super) fn render_strip(
    records: &OverlayRecordRepository,
    settings: &OverlaySettingsStore,
    source_paths: &SourcePaths,
    cache_root: &Path,
    request: &StripRequest,
) -> anyhow::Result<Option<Vec<u8>>> {
    let crop = match request.crop {
        Some(crop) => crop,
        None => settings.load()?.crop,
    };
    // Without an installation no screenshot path resolves.
    let Some(game_path) = records.game_path() else {
        return Ok(None);
    };
    let Some(path) = source_paths.resolve(game_path, &request.record_id, || {
        records.load_image_path(&request.record_id)
    })?
    else {
        return Ok(None);
    };

    if request.preview {
        let bytes = std::fs::read(&path).context("Failed to read overlay source image")?;
        return crop_strip_image(&bytes, crop).map(Some);
    }

    let cache_directory = cache_namespace(cache_root, OBS_STRIP_CACHE, game_path);
    cached_strip(&cache_directory, &request.record_id, &path, crop).map(Some)
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
/// rendering and storing it on a miss. A hit on an entry older than
/// `REFRESH_AFTER` refreshes its modification time, so the age-based sweep
/// sees it as recently used.
pub(super) fn cached_strip(
    cache_directory: &Path,
    record_id: &str,
    source_path: &Path,
    crop: OverlayCropSettings,
) -> anyhow::Result<Vec<u8>> {
    let cache_path = crop_cache_path(cache_directory, record_id, source_path, crop)?;
    match read_cache_entry(&cache_path) {
        Ok((bytes, modified)) => {
            let age = SystemTime::now()
                .duration_since(modified)
                .unwrap_or_default();
            if age >= REFRESH_AFTER {
                touch(&cache_path);
            }
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

/// An entry's bytes and modification time, read through one handle.
fn read_cache_entry(path: &Path) -> std::io::Result<(Vec<u8>, SystemTime)> {
    let mut file = std::fs::File::open(path)?;
    let modified = file.metadata()?.modified().unwrap_or(UNIX_EPOCH);
    let mut bytes = Vec::new();
    file.read_to_end(&mut bytes)?;
    Ok((bytes, modified))
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

/// Sweeps the strip caches once per process, off the async runtime.
pub(super) fn sweep_cache() {
    static SWEPT: OnceLock<()> = OnceLock::new();
    if SWEPT.set(()).is_err() {
        return;
    }
    let cache_root = paths::overlay_cache_dir();
    tauri::async_runtime::spawn_blocking(move || {
        sweep(&cache_root, SystemTime::now(), &CACHE_LIMITS);
    });
}

struct CacheLimits {
    /// An entry not read or written for longer is removed.
    unused_for: Duration,
    /// The oldest entries beyond this count are removed.
    max_entries: usize,
    /// Entries this recent may be in use and are left alone.
    fresh_for: Duration,
}

/// Every deletion is best effort: Windows refuses to remove an open file.
/// Namespace directories stay, so a concurrent write never loses its parent.
/// The count cap spans every strip cache. Strips that older versions wrote at
/// the cache root are removed outright.
fn sweep(cache_root: &Path, now: SystemTime, limits: &CacheLimits) {
    let age_of = |metadata: &std::fs::Metadata| {
        let modified = metadata.modified().unwrap_or(UNIX_EPOCH);
        (modified, now.duration_since(modified).unwrap_or_default())
    };
    let is_orphan = |path: &Path| path.to_string_lossy().ends_with(TEMP_SUFFIX);

    if let Ok(files) = std::fs::read_dir(cache_root) {
        for file in files.flatten() {
            let Ok(metadata) = file.metadata() else {
                continue;
            };
            let name = file.file_name().to_string_lossy().into_owned();
            let legacy = name.ends_with(STRIP_SUFFIX)
                || (name.contains(&format!("{STRIP_SUFFIX}.")) && name.ends_with(TEMP_SUFFIX));
            if metadata.is_file() && legacy && age_of(&metadata).1 >= limits.fresh_for {
                let _ = std::fs::remove_file(file.path());
            }
        }
    }

    let mut kept = Vec::new();
    for cache in STRIP_CACHES {
        let Ok(namespaces) = std::fs::read_dir(cache_root.join(cache)) else {
            continue;
        };
        for namespace in namespaces.flatten() {
            let Ok(files) = std::fs::read_dir(namespace.path()) else {
                continue;
            };
            for file in files.flatten() {
                let Ok(metadata) = file.metadata() else {
                    continue;
                };
                if !metadata.is_file() {
                    continue;
                }
                let (modified, age) = age_of(&metadata);
                if age < limits.fresh_for {
                    continue;
                }
                let path = file.path();
                if is_orphan(&path) || age > limits.unused_for {
                    let _ = std::fs::remove_file(&path);
                } else {
                    kept.push((modified, path));
                }
            }
        }
    }

    if kept.len() > limits.max_entries {
        kept.sort_by_key(|(modified, _)| *modified);
        let excess = kept.len() - limits.max_entries;
        for (_, path) in kept.into_iter().take(excess) {
            let _ = std::fs::remove_file(path);
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
        basis_points, cache_namespace, cached_strip, crop_cache_path, crop_dynamic_image,
        pixel_offset, sweep, CacheLimits, HISTORY_THUMBNAIL_CACHE, OBS_STRIP_CACHE, TEMP_SUFFIX,
    };
    use crate::stream::overlay_settings::OverlayCropSettings;
    use image::{DynamicImage, GenericImageView, RgbaImage};
    use std::{
        path::{Path, PathBuf},
        time::{Duration, SystemTime},
    };

    /// Writes a cache file last modified `age` seconds before `now`.
    fn cache_file(path: PathBuf, now: SystemTime, age: u64) -> PathBuf {
        std::fs::create_dir_all(path.parent().unwrap()).unwrap();
        std::fs::write(&path, b"png").unwrap();
        std::fs::File::options()
            .write(true)
            .open(&path)
            .unwrap()
            .set_modified(now - Duration::from_secs(age))
            .unwrap();
        path
    }

    #[test]
    fn sweep_removes_stale_entries_orphans_and_root_strips_but_keeps_fresh_ones() {
        let temp = tempfile::tempdir().unwrap();
        let root = temp.path();
        let now = SystemTime::now();
        let limits = CacheLimits {
            unused_for: Duration::from_secs(3600),
            max_entries: 100,
            fresh_for: Duration::from_secs(10),
        };
        let game = Path::new("/Games/The Bazaar");
        let history = cache_namespace(root, HISTORY_THUMBNAIL_CACHE, game);
        let obs = cache_namespace(root, OBS_STRIP_CACHE, game);
        let stale = cache_file(history.join("stale-strip.png"), now, 7200);
        let used = cache_file(history.join("used-strip.png"), now, 60);
        let orphan = cache_file(history.join("used-strip.png.1-0.tmp"), now, 60);
        let writing = cache_file(history.join("used-strip.png.1-1.tmp"), now, 1);
        let just_served = cache_file(history.join("just-served-strip.png"), now, 1);
        let obs_stale = cache_file(obs.join("shot-1-strip.png"), now, 7200);
        let obs_used = cache_file(obs.join("shot-2-strip.png"), now, 60);
        // Older versions cached OBS strips at the root, where nothing swept them.
        let root_strip = cache_file(root.join("shot-1-1-2-3-4-5-6-strip.png"), now, 60);
        let root_orphan = cache_file(root.join("shot-1-strip.png.1-0.tmp"), now, 60);
        let root_writing = cache_file(root.join("shot-2-strip.png"), now, 1);
        let unrelated = cache_file(root.join("notes.txt"), now, 7200);

        sweep(root, now, &limits);

        for removed in [&stale, &orphan, &obs_stale, &root_strip, &root_orphan] {
            assert!(!removed.exists(), "{} survived", removed.display());
        }
        for kept in [
            &used,
            &writing,
            &just_served,
            &obs_used,
            &root_writing,
            &unrelated,
        ] {
            assert!(kept.exists(), "{} was removed", kept.display());
        }
    }

    #[test]
    fn sweep_caps_the_entry_count_across_caches_by_removing_the_oldest() {
        let temp = tempfile::tempdir().unwrap();
        let root = temp.path();
        let now = SystemTime::now();
        let limits = CacheLimits {
            unused_for: Duration::from_secs(3600),
            max_entries: 2,
            fresh_for: Duration::from_secs(10),
        };
        let entries = [
            (HISTORY_THUMBNAIL_CACHE, "/Games/A", 300),
            (OBS_STRIP_CACHE, "/Games/B", 200),
            (HISTORY_THUMBNAIL_CACHE, "/Games/A", 100),
        ]
        .into_iter()
        .enumerate()
        .map(|(index, (cache, game, age))| {
            let namespace = cache_namespace(root, cache, Path::new(game));
            cache_file(namespace.join(format!("shot-{index}-strip.png")), now, age)
        })
        .collect::<Vec<_>>();

        sweep(root, now, &limits);

        assert_eq!(
            entries.iter().map(|path| path.exists()).collect::<Vec<_>>(),
            [false, true, true]
        );
    }

    #[test]
    fn the_count_cap_keeps_a_strip_served_daily_over_thumbnails_viewed_once() {
        let temp = tempfile::tempdir().unwrap();
        let root = temp.path();
        let now = SystemTime::now();
        let limits = CacheLimits {
            unused_for: Duration::from_secs(30 * 24 * 60 * 60),
            max_entries: 2,
            fresh_for: Duration::from_secs(10),
        };
        let source = root.join("source.png");
        RgbaImage::from_pixel(64, 32, image::Rgba([1, 2, 3, 255]))
            .save(&source)
            .unwrap();
        let obs = cache_namespace(root, OBS_STRIP_CACHE, Path::new("/Games/A"));
        let crop = OverlayCropSettings::default();
        cached_strip(&obs, "shot-1", &source, crop).unwrap();
        let served = crop_cache_path(&obs, "shot-1", &source, crop).unwrap();
        let set_age = |path: &Path, age: u64| {
            std::fs::File::options()
                .write(true)
                .open(path)
                .unwrap()
                .set_modified(now - Duration::from_secs(age))
                .unwrap();
        };
        // Written two days ago and served ever since, every few seconds.
        set_age(&served, 2 * 24 * 60 * 60);
        let history = cache_namespace(root, HISTORY_THUMBNAIL_CACHE, Path::new("/Games/A"));
        let viewed_once = [
            cache_file(history.join("shot-2-strip.png"), now, 24 * 60 * 60),
            cache_file(history.join("shot-3-strip.png"), now, 23 * 60 * 60),
        ];
        cached_strip(&obs, "shot-1", &source, crop).unwrap();

        // Swept at the next start, once the refreshed entry is past `fresh_for`.
        sweep(root, SystemTime::now() + Duration::from_secs(60), &limits);

        assert!(served.exists());
        assert!(!viewed_once[0].exists());
        assert!(viewed_once[1].exists());
    }

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

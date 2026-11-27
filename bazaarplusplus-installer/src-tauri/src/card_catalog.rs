//! The card catalog History shows: names, Card Text, art and Enchantment FX, exported from the
//! installed game by the `card-catalog` crate (ADR 0024) and served to the page as `bppcard://`
//! (ADR 0025). Nothing here uses the network.

use crate::services::game_path::GamePathAcceptance;
use crate::services::selected_game_installation::SelectedGameInstallationState;
use anyhow::Context as _;
use serde::Serialize;
use sha2::{Digest, Sha256};
use std::ffi::OsString;
use std::io::{BufRead as _, Read as _, Write as _};
use std::path::{Path, PathBuf};
use std::process::{Child, Command, Stdio};
use std::sync::{Arc, Mutex};
use tauri::http::{header, Response, StatusCode};
use tauri::Manager;

pub(crate) const SCHEME: &str = "bppcard";
/// Changing what an export writes changes this, so every installed catalog exports again.
const EXPORT_FORMAT: &str = "1";
const REVISION_FILE: &str = "revision";

#[derive(Clone, Debug, Default, PartialEq, Eq, Serialize, specta::Type)]
#[serde(rename_all = "camelCase")]
pub struct CardCatalogStatus {
    /// Names the catalog on disk; null before the first export finishes.
    pub revision: Option<String>,
    /// The catalog on disk matches the installed game.
    pub current: bool,
    /// The installed game and its data cache were found.
    pub game_found: bool,
    /// Set while an export runs.
    pub progress: Option<CardCatalogProgress>,
    /// The last export for the installed game failed.
    pub failed: bool,
    /// When the catalog on disk was exported, in Unix milliseconds.
    pub exported_at_ms: Option<i64>,
    pub files: u32,
    pub bytes: u64,
}

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Serialize, specta::Type)]
pub struct CardCatalogProgress {
    pub done: u32,
    pub total: u32,
}

/// The game files one export reads, and the revision they produce.
#[derive(Clone, Debug, PartialEq, Eq)]
struct GameInputs {
    data_dir: PathBuf,
    game_data: PathBuf,
    translations: PathBuf,
    gamedata_etag: Option<String>,
    zh_etag: Option<String>,
    revision: String,
}

#[derive(Default)]
pub(crate) struct CardCatalogState(Mutex<Running>);

#[derive(Default)]
struct Running {
    child: Option<Export>,
    /// A revision whose export failed is not tried again until asked to.
    failed: Option<String>,
}

struct Export {
    revision: String,
    child: Child,
    progress: Arc<Mutex<CardCatalogProgress>>,
}

fn etag(manifest: &Path, entry: &str) -> Option<String> {
    let text = std::fs::read_to_string(manifest).ok()?;
    let value: serde_json::Value = serde_json::from_str(&text).ok()?;
    value["Entries"][entry]["ETag"]
        .as_str()
        .map(|s| s.trim_matches('"').to_owned())
        .filter(|s| !s.is_empty())
}

fn game_inputs(game: &Path, cache: &Path) -> Option<GameInputs> {
    let data_dir = crate::services::paths::game_data_dir(game);
    let catalog_hash = std::fs::read(data_dir.join("StreamingAssets/aa/catalog.hash")).ok()?;
    let game_data = cache.join("GameData.db");
    let translations = cache.join("translations/zh-CN.bytes");
    if !game_data.is_file() || !translations.is_file() {
        return None;
    }
    let gamedata_etag = etag(&cache.join("manifest.json"), "GameData");
    let zh_etag = etag(&cache.join("translations/manifest.json"), "zh-CN");
    let mut hash = Sha256::new();
    for part in [
        EXPORT_FORMAT.as_bytes(),
        &catalog_hash,
        gamedata_etag.as_deref().unwrap_or_default().as_bytes(),
        zh_etag.as_deref().unwrap_or_default().as_bytes(),
    ] {
        hash.update((part.len() as u64).to_le_bytes());
        hash.update(part);
    }
    let revision = hash
        .finalize()
        .iter()
        .take(8)
        .map(|byte| format!("{byte:02x}"))
        .collect();
    Some(GameInputs {
        data_dir,
        game_data,
        translations,
        gamedata_etag,
        zh_etag,
        revision,
    })
}

/// Files, bytes and export time of the catalog on disk.
fn usage(dir: &Path) -> (u32, u64, Option<i64>) {
    let (mut files, mut bytes) = (0u32, 0u64);
    for entry in std::fs::read_dir(dir).into_iter().flatten().flatten() {
        if let Ok(metadata) = entry.metadata() {
            files += 1;
            bytes += metadata.len();
        }
    }
    let exported = std::fs::metadata(dir.join(REVISION_FILE))
        .and_then(|metadata| metadata.modified())
        .ok()
        .and_then(|time| time.duration_since(std::time::UNIX_EPOCH).ok())
        .and_then(|elapsed| i64::try_from(elapsed.as_millis()).ok());
    (files, bytes, exported)
}

fn current_revision(dir: &Path) -> Option<String> {
    let revision = std::fs::read_to_string(dir.join(REVISION_FILE)).ok()?;
    dir.join("index.json")
        .is_file()
        .then(|| revision.trim().to_owned())
}

impl CardCatalogState {
    /// The catalog on disk, starting an export when the installed game has changed since it.
    /// `rebuild` exports again even when the catalog is current or the last export failed.
    pub(crate) fn ensure(&self, app: &tauri::AppHandle, rebuild: bool) -> CardCatalogStatus {
        let dir = crate::services::paths::card_catalog_dir();
        let game = app
            .state::<SelectedGameInstallationState>()
            .resolve(app, None, GamePathAcceptance::Any)
            .map(|resolution| resolution.game_path);
        let inputs =
            game.and_then(|game| game_inputs(&game, &crate::services::paths::game_cache_dir()?));
        self.ensure_with(&dir, inputs, &std::env::current_exe().ok(), rebuild)
    }

    fn ensure_with(
        &self,
        dir: &Path,
        inputs: Option<GameInputs>,
        exe: &Option<PathBuf>,
        rebuild: bool,
    ) -> CardCatalogStatus {
        let Ok(mut running) = self.0.lock() else {
            return CardCatalogStatus::default();
        };
        if let Some(export) = &mut running.child {
            match export.child.try_wait() {
                Ok(None) => {}
                // A finished export recorded its own revision.
                Ok(Some(status)) if status.success() => running.child = None,
                _ => {
                    running.failed = Some(export.revision.clone());
                    running.child = None;
                }
            }
        }
        let revision = current_revision(dir);
        let wanted = inputs.as_ref().map(|inputs| inputs.revision.clone());
        let current = revision.is_some() && revision == wanted;
        if let Some(inputs) = inputs.filter(|_| running.child.is_none()) {
            if rebuild {
                running.failed = None;
            }
            let failed = running.failed.as_deref() == Some(inputs.revision.as_str());
            if (rebuild || !current) && !failed {
                match exe.as_deref().map(|exe| spawn(exe, dir, &inputs)) {
                    Some(Ok(export)) => running.child = Some(export),
                    Some(Err(error)) => {
                        eprintln!("cannot start the card catalog export: {error:#}");
                        running.failed = Some(inputs.revision);
                    }
                    None => running.failed = Some(inputs.revision),
                }
            }
        }
        let (files, bytes, exported_at_ms) = usage(dir);
        CardCatalogStatus {
            revision,
            current,
            game_found: wanted.is_some(),
            progress: running
                .child
                .as_ref()
                .map(|export| export.progress.lock().map(|p| *p).unwrap_or_default()),
            failed: wanted.is_some() && running.failed == wanted,
            exported_at_ms,
            files,
            bytes,
        }
    }
}

const PROGRESS: &str = "progress ";

pub(crate) struct ExportArgs {
    pub data_dir: PathBuf,
    pub game_data: PathBuf,
    pub translations: PathBuf,
    pub out_dir: PathBuf,
    pub revision: String,
    pub gamedata_etag: Option<String>,
    pub zh_etag: Option<String>,
}

impl ExportArgs {
    pub(crate) const CLI_OPTIONS: [&str; 7] = [
        "--data",
        "--game-data",
        "--translations",
        "--out",
        "--revision",
        "--gamedata-etag",
        "--zh-etag",
    ];

    fn from_inputs(dir: &Path, inputs: &GameInputs) -> Self {
        Self {
            data_dir: inputs.data_dir.clone(),
            game_data: inputs.game_data.clone(),
            translations: inputs.translations.clone(),
            out_dir: dir.to_path_buf(),
            revision: inputs.revision.clone(),
            gamedata_etag: inputs.gamedata_etag.clone(),
            zh_etag: inputs.zh_etag.clone(),
        }
    }

    fn to_cli_args(&self) -> Vec<OsString> {
        let mut args: Vec<OsString> = vec![
            "card-catalog".into(),
            Self::CLI_OPTIONS[0].into(),
            self.data_dir.clone().into(),
            Self::CLI_OPTIONS[1].into(),
            self.game_data.clone().into(),
            Self::CLI_OPTIONS[2].into(),
            self.translations.clone().into(),
            Self::CLI_OPTIONS[3].into(),
            self.out_dir.clone().into(),
            Self::CLI_OPTIONS[4].into(),
            self.revision.clone().into(),
        ];
        for (option, value) in [
            (Self::CLI_OPTIONS[5], &self.gamedata_etag),
            (Self::CLI_OPTIONS[6], &self.zh_etag),
        ] {
            if let Some(value) = value {
                args.extend([option.into(), value.into()]);
            }
        }
        args
    }

    pub(crate) fn from_cli_values(values: [Option<OsString>; 7]) -> anyhow::Result<Self> {
        let [data, game_data, translations, out, revision, gamedata_etag, zh_etag] = values;
        let path = |value: Option<OsString>, option: &str| {
            value
                .map(PathBuf::from)
                .with_context(|| format!("card-catalog requires {option}"))
        };
        let text = |value: Option<OsString>| value.map(|v| v.to_string_lossy().into_owned());
        Ok(Self {
            data_dir: path(data, Self::CLI_OPTIONS[0])?,
            game_data: path(game_data, Self::CLI_OPTIONS[1])?,
            translations: path(translations, Self::CLI_OPTIONS[2])?,
            out_dir: path(out, Self::CLI_OPTIONS[3])?,
            revision: text(revision)
                .with_context(|| format!("card-catalog requires {}", Self::CLI_OPTIONS[4]))?,
            gamedata_etag: text(gamedata_etag),
            zh_etag: text(zh_etag),
        })
    }
}

fn spawn(exe: &Path, dir: &Path, inputs: &GameInputs) -> anyhow::Result<Export> {
    std::fs::create_dir_all(dir)?;
    let cli_args = ExportArgs::from_inputs(dir, inputs).to_cli_args();
    let mut command = Command::new(exe);
    // The export ends when its stdin closes, so it never outlives the installer.
    command
        .args(cli_args)
        .stdin(Stdio::piped())
        .stdout(Stdio::piped());
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt as _;
        const BELOW_NORMAL_PRIORITY_CLASS: u32 = 0x0000_4000;
        const CREATE_NO_WINDOW: u32 = 0x0800_0000;
        command.creation_flags(BELOW_NORMAL_PRIORITY_CLASS | CREATE_NO_WINDOW);
    }
    let mut child = command.spawn()?;
    let progress = Arc::new(Mutex::new(CardCatalogProgress::default()));
    if let Some(stdout) = child.stdout.take() {
        let progress = Arc::clone(&progress);
        std::thread::spawn(move || {
            for line in std::io::BufReader::new(stdout).lines() {
                let Ok(line) = line else { break };
                if let Some(next) = parse_progress(&line) {
                    if let Ok(mut current) = progress.lock() {
                        *current = next;
                    }
                }
            }
        });
    }
    Ok(Export {
        revision: inputs.revision.clone(),
        child,
        progress,
    })
}

fn parse_progress(line: &str) -> Option<CardCatalogProgress> {
    let (done, total) = line.strip_prefix(PROGRESS)?.split_once(' ')?;
    Some(CardCatalogProgress {
        done: done.parse().ok()?,
        total: total.parse().ok()?,
    })
}

/// `bppinstaller card-catalog`: one export, run by the installer as its own child process.
/// It reports `progress <done> <total>` lines on stdout.
pub(crate) fn export(args: &ExportArgs) -> anyhow::Result<()> {
    std::thread::spawn(|| {
        let _ = std::io::stdin().read_to_end(&mut Vec::new());
        std::process::exit(2);
    });
    #[cfg(unix)]
    // SAFETY: setpriority only changes this process's scheduling priority.
    unsafe {
        libc::setpriority(libc::PRIO_PROCESS, 0, 10);
    }
    // The game replaces these files when it updates; the export reads copies.
    let staging = tempfile::tempdir()?;
    let game_data_copy = staging.path().join("GameData.db");
    let translations_copy = staging.path().join("zh-CN.bytes");
    std::fs::copy(&args.game_data, &game_data_copy).context("cannot copy GameData.db")?;
    std::fs::copy(&args.translations, &translations_copy).context("cannot copy zh-CN.bytes")?;
    let previous = args.out_dir.join("index.json");
    let reported = std::cell::Cell::new(usize::MAX);
    let report = |done: usize, total: usize| {
        // One line per percent is enough for a progress bar.
        let percent = done * 100 / total.max(1);
        if reported.replace(percent) != percent {
            let mut stdout = std::io::stdout().lock();
            let _ = writeln!(stdout, "{PROGRESS}{done} {total}");
            let _ = stdout.flush();
        }
    };
    let summary = card_catalog::extract(&card_catalog::Inputs {
        data_dir: &args.data_dir,
        game_data: &game_data_copy,
        translations: &translations_copy,
        previous: previous.is_file().then_some(previous.as_path()),
        out_dir: &args.out_dir,
        buildid: None,
        gamedata_etag: args.gamedata_etag.clone(),
        zh_etag: args.zh_etag.clone(),
        publication: false,
        require_fx: false,
        progress: Some(&report),
        // Half the cores: the export runs beside the game and the installer.
        threads: std::thread::available_parallelism().map_or(1, |n| (n.get() / 2).clamp(1, 4)),
    })?;
    prune_text(&args.out_dir, &summary.text)?;
    std::fs::write(args.out_dir.join(REVISION_FILE), &args.revision)?;
    Ok(())
}

/// Card Text files are replaced on every game update; keep only the current locale files.
fn prune_text(
    out_dir: &Path,
    text: &std::collections::BTreeMap<String, String>,
) -> anyhow::Result<()> {
    let keep: Vec<&str> = text.values().map(String::as_str).collect();
    for entry in std::fs::read_dir(out_dir)? {
        let name = entry?.file_name();
        let name = name.to_string_lossy();
        if name.starts_with("text-") && !keep.contains(&name.as_ref()) {
            std::fs::remove_file(out_dir.join(name.as_ref()))?;
        }
    }
    Ok(())
}

/// The catalog's own file names: the index and content-addressed art, FX and Card Text.
fn servable(name: &str) -> Option<&'static str> {
    let hex = |s: &str, lengths: std::ops::RangeInclusive<usize>| {
        lengths.contains(&s.len())
            && s.bytes()
                .all(|b| b.is_ascii_digit() || (b'a'..=b'f').contains(&b))
    };
    if name == "index.json" {
        return Some("application/json");
    }
    if let Some(stem) = name.strip_suffix(".webp") {
        if let Some(tex) = stem.strip_prefix("tex-") {
            return hex(tex, 20..=20).then_some("image/webp");
        }
        return hex(stem, 8..=64).then_some("image/webp");
    }
    let stem = name.strip_suffix(".json")?;
    let stem = stem
        .strip_prefix("fx-")
        .or_else(|| stem.strip_prefix("text-en-"))
        .or_else(|| stem.strip_prefix("text-zh-"))?;
    hex(stem, 20..=20).then_some("application/json")
}

pub(crate) fn serve(dir: &Path, path: &str) -> Response<Vec<u8>> {
    let name = path.trim_start_matches('/');
    let found = servable(name).and_then(|kind| Some((kind, std::fs::read(dir.join(name)).ok()?)));
    let builder = Response::builder().header(header::ACCESS_CONTROL_ALLOW_ORIGIN, "*");
    let response = match found {
        Some((kind, body)) => builder
            .header(header::CONTENT_TYPE, kind)
            .header(
                header::CACHE_CONTROL,
                if name == "index.json" {
                    "no-store"
                } else {
                    "public, max-age=31536000, immutable"
                },
            )
            .body(body),
        None => builder.status(StatusCode::NOT_FOUND).body(Vec::new()),
    };
    response.unwrap_or_else(|_| Response::new(Vec::new()))
}

#[tauri::command]
#[specta::specta]
pub async fn get_card_catalog(app: tauri::AppHandle) -> CardCatalogStatus {
    tauri::async_runtime::spawn_blocking(move || {
        app.state::<CardCatalogState>().ensure(&app, false)
    })
    .await
    .unwrap_or_default()
}

/// Exports the catalog again from the installed game.
#[tauri::command]
#[specta::specta]
pub async fn rebuild_card_catalog(app: tauri::AppHandle) -> CardCatalogStatus {
    tauri::async_runtime::spawn_blocking(move || app.state::<CardCatalogState>().ensure(&app, true))
        .await
        .unwrap_or_default()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn serves_only_catalog_files_with_cors() {
        let dir = tempfile::tempdir().unwrap();
        for name in [
            "index.json",
            "0123456789abcdef0123.webp",
            "tex-0123456789abcdef0123.webp",
            "fx-0123456789abcdef0123.json",
            "text-zh-0123456789abcdef0123.json",
            "revision",
        ] {
            std::fs::write(dir.path().join(name), name).unwrap();
        }
        let index = serve(dir.path(), "/index.json");
        assert_eq!(index.status(), StatusCode::OK);
        assert_eq!(index.headers()[header::ACCESS_CONTROL_ALLOW_ORIGIN], "*");
        assert_eq!(index.headers()[header::CACHE_CONTROL], "no-store");
        let art = serve(dir.path(), "/0123456789abcdef0123.webp");
        assert_eq!(art.headers()[header::CONTENT_TYPE], "image/webp");
        assert_eq!(art.body(), b"0123456789abcdef0123.webp");
        for path in [
            "/tex-0123456789abcdef0123.webp",
            "/fx-0123456789abcdef0123.json",
            "/text-zh-0123456789abcdef0123.json",
        ] {
            assert_eq!(serve(dir.path(), path).status(), StatusCode::OK, "{path}");
        }
        for path in [
            "/revision",
            "/../index.json",
            "/sub/index.json",
            "/missing0.webp",
            "/0123456789ABCDEF0123.webp",
            "/text-de-0123456789abcdef0123.json",
            "/%2e%2e/index.json",
        ] {
            let response = serve(dir.path(), path);
            assert_eq!(response.status(), StatusCode::NOT_FOUND, "{path}");
            assert_eq!(response.headers()[header::ACCESS_CONTROL_ALLOW_ORIGIN], "*");
        }
    }

    fn game(root: &Path, catalog_hash: &str, etag: &str) -> (PathBuf, PathBuf) {
        let game = root.join("game");
        let aa = crate::services::paths::game_data_dir(&game).join("StreamingAssets/aa");
        std::fs::create_dir_all(&aa).unwrap();
        std::fs::write(aa.join("catalog.hash"), catalog_hash).unwrap();
        let cache = root.join("cache");
        std::fs::create_dir_all(cache.join("translations")).unwrap();
        std::fs::write(cache.join("GameData.db"), "").unwrap();
        std::fs::write(cache.join("translations/zh-CN.bytes"), "").unwrap();
        std::fs::write(
            cache.join("manifest.json"),
            format!(r#"{{"Entries":{{"GameData":{{"ETag":"{etag}"}}}}}}"#),
        )
        .unwrap();
        std::fs::write(
            cache.join("translations/manifest.json"),
            r#"{"Entries":{"zh-CN":{"ETag":"zh"}}}"#,
        )
        .unwrap();
        (game, cache)
    }

    #[test]
    fn the_revision_follows_the_game_build_and_its_data() {
        let root = tempfile::tempdir().unwrap();
        let (game, cache) = game(root.path(), "build-1", "data-1");
        let first = game_inputs(&game, &cache).unwrap();
        assert_eq!(first.gamedata_etag.as_deref(), Some("data-1"));
        assert_eq!(first.zh_etag.as_deref(), Some("zh"));
        assert_eq!(first.revision.len(), 16);
        assert_eq!(game_inputs(&game, &cache).unwrap().revision, first.revision);
        // HTTP ETags often carry surrounding quotes; strip them consistently with release/card-art.ts.
        std::fs::write(
            cache.join("manifest.json"),
            r#"{"Entries":{"GameData":{"ETag":"\"data-1\""}}}"#,
        )
        .unwrap();
        let quoted = game_inputs(&game, &cache).unwrap();
        assert_eq!(quoted.gamedata_etag.as_deref(), Some("data-1"));
        assert_eq!(quoted.revision, first.revision);
        let (game, cache) = self::game(root.path(), "build-2", "data-1");
        assert_ne!(game_inputs(&game, &cache).unwrap().revision, first.revision);
        let (game, cache) = self::game(root.path(), "build-1", "data-2");
        assert_ne!(game_inputs(&game, &cache).unwrap().revision, first.revision);
    }

    #[test]
    fn a_current_catalog_starts_no_export_and_a_failed_one_is_not_retried() {
        let root = tempfile::tempdir().unwrap();
        let (game, cache) = game(root.path(), "build", "data");
        let inputs = game_inputs(&game, &cache).unwrap();
        let dir = root.path().join("catalog");
        std::fs::create_dir_all(&dir).unwrap();
        let state = CardCatalogState::default();

        // No program to run: the export fails to start and is remembered as failed.
        let status = state.ensure_with(&dir, Some(inputs.clone()), &None, false);
        assert!(status.game_found && status.failed && !status.current);
        assert_eq!(status.revision, None);
        assert_eq!(status.progress, None);

        std::fs::write(dir.join("index.json"), "{}").unwrap();
        std::fs::write(dir.join(REVISION_FILE), &inputs.revision).unwrap();
        let status = state.ensure_with(&dir, Some(inputs.clone()), &None, false);
        assert_eq!(status.revision, Some(inputs.revision.clone()));
        assert!(status.current);
        assert_eq!((status.files, status.bytes), (2, 2 + 16));
        assert!(status.exported_at_ms.is_some());

        // Without the game, the catalog on disk is still served.
        let status = state.ensure_with(&dir, None, &None, true);
        assert!(!status.game_found && !status.current && !status.failed);
        assert_eq!(status.revision, Some(inputs.revision));
    }

    #[test]
    fn progress_lines_parse() {
        assert_eq!(
            parse_progress("progress 12 1500"),
            Some(CardCatalogProgress {
                done: 12,
                total: 1500
            })
        );
        assert_eq!(parse_progress("skip TCardItem x"), None);
    }

    #[test]
    fn pruning_keeps_the_card_text_the_index_names() {
        let dir = tempfile::tempdir().unwrap();
        let old = "text-zh-00000000000000000000.json";
        let kept = [
            "text-en-11111111111111111111.json",
            "text-zh-22222222222222222222.json",
        ];
        for name in [old, kept[0], kept[1], "0123456789abcdef0123.webp"] {
            std::fs::write(dir.path().join(name), "").unwrap();
        }
        let text = std::collections::BTreeMap::from([
            ("en".to_owned(), kept[0].to_owned()),
            ("zh".to_owned(), kept[1].to_owned()),
        ]);
        prune_text(dir.path(), &text).unwrap();
        assert!(!dir.path().join(old).exists());
        for name in [kept[0], kept[1], "0123456789abcdef0123.webp"] {
            assert!(dir.path().join(name).exists(), "{name}");
        }
    }
}

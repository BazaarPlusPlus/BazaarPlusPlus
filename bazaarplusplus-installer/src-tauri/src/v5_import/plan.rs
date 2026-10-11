//! Turns V5 rows into the rows and file placements the current Data Root gets.
//!
//! Every imported Run is History-Only. A Run already in the current database is
//! skipped with all its children, and so is a Run whose battle, screenshot, or
//! video id the current database already holds. Ghost battles, the Bundle
//! outbox, and seal jobs are never read (see `source.rs`).

use std::collections::{BTreeMap, HashSet};
use std::path::{Path, PathBuf};

use rusqlite::types::Value;
use rusqlite::{Connection, OptionalExtension};
use serde::Serialize;

use super::instant::{parse_v5_instant_ms, parse_v5_offset_minutes};
use super::source::{V5History, V5Row, V5Shape};
use super::ImportError;
use crate::history::cleanup::{normalize_relative_path, replay_payload_path};
use crate::history::files::resolve_cleanup_file_path;
use crate::services::paths::{
    combat_replay_videos_dir_in, combat_replays_dir_in, screenshots_dir_in,
};
use crate::services::placement::PlacementRequest;

const DAY_MS: i64 = 24 * 60 * 60 * 1000;

/// One row for the current database, as `(column, value)` pairs.
pub(crate) type V6Row = Vec<(&'static str, Value)>;

/// The mod's replay payload retention, from the installed mod's contract.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(crate) struct ReplayRetention {
    pub(crate) newest_count: usize,
    pub(crate) age_days: i64,
}

#[derive(Clone, Debug, PartialEq, Eq, PartialOrd, Ord, Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct SkippedRun {
    pub(crate) run_id: String,
    /// `already_present`: the current database has the Run id.
    /// `conflicting_id`: it has one of the Run's battle, screenshot, or video ids.
    pub(crate) reason: &'static str,
}

/// A screenshot or video row and the path it stored.
#[derive(Clone, Debug, PartialEq, Eq, PartialOrd, Ord, Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct FileReference {
    pub(crate) table: &'static str,
    pub(crate) id: String,
    pub(crate) path: String,
    pub(crate) reason: &'static str,
}

#[derive(Debug, Default, Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct PlanReport {
    pub(crate) imported_runs: Vec<String>,
    /// Unfinished Runs closed as `abandoned` at their last-seen time.
    pub(crate) closed_runs: Vec<String>,
    pub(crate) skipped_runs: Vec<SkippedRun>,
    /// Rows not imported because of their file: a path outside the V5 Data
    /// Root, or a video V5 had already deleted or could not have recorded.
    pub(crate) dropped_references: Vec<FileReference>,
    /// Rows imported although their file is gone from V5.
    pub(crate) missing_files: Vec<FileReference>,
    /// Replay payloads the mod's retention would delete; their battles are
    /// imported as `evicted`.
    pub(crate) evicted_replays: Vec<String>,
}

pub(crate) struct ImportPlan {
    /// `(table, row)` in insert order: parents before children.
    pub(crate) rows: Vec<(&'static str, V6Row)>,
    pub(crate) placements: Vec<PlacementRequest>,
    pub(crate) report: PlanReport,
}

struct Context<'a> {
    v5_root: &'a Path,
    v6_root: &'a Path,
    now_ms: i64,
}

pub(crate) fn plan_import(
    history: &V5History,
    target: &Connection,
    v5_root: &Path,
    v6_root: &Path,
    now_ms: i64,
    retention: ReplayRetention,
) -> Result<ImportPlan, ImportError> {
    let context = Context {
        v5_root,
        v6_root,
        now_ms,
    };
    let shape = history.shape.unwrap_or(V5Shape::Lifecycle);
    let by_run = |rows: &[V5Row]| {
        let mut grouped: BTreeMap<String, Vec<V5Row>> = BTreeMap::new();
        for row in rows {
            if let Some(run_id) = row.text("run_id") {
                grouped.entry(run_id).or_default().push(row.clone());
            }
        }
        grouped
    };
    let by_battle = |rows: &[V5Row]| {
        let mut grouped: BTreeMap<String, Vec<V5Row>> = BTreeMap::new();
        for row in rows {
            if let Some(battle_id) = row.text("battle_id") {
                grouped.entry(battle_id).or_default().push(row.clone());
            }
        }
        grouped
    };
    let events = by_run(&history.run_events);
    let battles = by_run(&history.battles);
    let screenshots = by_run(&history.screenshots);
    let snapshots = by_battle(&history.battle_snapshots);
    let videos = by_battle(&history.videos);
    let none = Vec::new();

    let mut report = PlanReport::default();
    let mut imported = Vec::new();
    for run in &history.runs {
        let run_id = run.text("run_id").unwrap_or_default();
        let run_battles = battles.get(&run_id).unwrap_or(&none);
        let run_screenshots = screenshots.get(&run_id).unwrap_or(&none);
        if exists(target, "select 1 from runs where run_id = ?1", &run_id)? {
            report.skipped_runs.push(SkippedRun {
                run_id,
                reason: "already_present",
            });
            continue;
        }
        let mut conflict = false;
        for battle in run_battles {
            let battle_id = battle.text("battle_id").unwrap_or_default();
            conflict |= exists(
                target,
                "select 1 from battles where battle_id = ?1",
                &battle_id,
            )? || exists(
                target,
                "select 1 from battle_snapshots where battle_id = ?1",
                &battle_id,
            )?;
            for video in videos.get(&battle_id).unwrap_or(&none) {
                conflict |= exists(
                    target,
                    "select 1 from combat_replay_videos where video_id = ?1",
                    &video.text("video_id").unwrap_or_default(),
                )?;
            }
        }
        for screenshot in run_screenshots {
            conflict |= exists(
                target,
                "select 1 from run_screenshots where screenshot_id = ?1",
                &screenshot.text("screenshot_id").unwrap_or_default(),
            )?;
        }
        if conflict {
            report.skipped_runs.push(SkippedRun {
                run_id,
                reason: "conflicting_id",
            });
            continue;
        }
        imported.push((run, run_id, run_battles, run_screenshots));
    }

    // Battles first: the replay retention ranks every imported payload together
    // with the payloads the current Data Root already keeps.
    let mut battle_rows = Vec::new();
    let mut candidates = Vec::new();
    for (_, _, run_battles, _) in &imported {
        for battle in run_battles.iter() {
            let (row, ready) = map_battle(&context, shape, battle)?;
            if ready {
                candidates.push((
                    integer(&row, "recorded_at_ms"),
                    battle.text("battle_id").unwrap_or_default(),
                ));
            }
            battle_rows.push((battle, row));
        }
    }
    let retained = retained_payloads(target, v6_root, &candidates, now_ms, retention)?;

    let mut rows = Vec::new();
    let mut placements = Placements::default();
    for (run, run_id, _, _) in &imported {
        let (row, closed) = map_run(run)?;
        rows.push(("runs", row));
        report.imported_runs.push(run_id.clone());
        if closed {
            report.closed_runs.push(run_id.clone());
        }
        for event in events.get(run_id).unwrap_or(&none) {
            rows.push(("run_events", map_run_event(event)?));
        }
    }
    for (battle, mut row) in battle_rows {
        let battle_id = battle.text("battle_id").unwrap_or_default();
        if text_of(&row, "local_payload_state") == Some("ready") {
            if retained.contains(&battle_id) {
                let source = replay_payload_path(&combat_replays_dir_in(v5_root), &battle_id);
                let target = replay_payload_path(&combat_replays_dir_in(v6_root), &battle_id);
                if let (Some(source), Some(target)) = (source, target) {
                    placements.add(source, target);
                }
            } else {
                set(
                    &mut row,
                    "local_payload_state",
                    Value::Text("evicted".into()),
                );
                set(
                    &mut row,
                    "local_payload_maintenance_at_ms",
                    Value::Integer(now_ms),
                );
                report.evicted_replays.push(battle_id.clone());
            }
        }
        rows.push(("battles", row));
        for snapshot in snapshots.get(&battle_id).unwrap_or(&none) {
            rows.push(("battle_snapshots", copy(snapshot, SNAPSHOT_COLUMNS)));
        }
        for video in videos.get(&battle_id).unwrap_or(&none) {
            if let Some(row) = map_video(&context, shape, video, &mut placements, &mut report)? {
                rows.push(("combat_replay_videos", row));
            }
        }
    }
    for (_, _, _, run_screenshots) in &imported {
        for screenshot in run_screenshots.iter() {
            if let Some(row) = map_screenshot(&context, screenshot, &mut placements, &mut report)? {
                rows.push(("run_screenshots", row));
            }
        }
    }

    report.skipped_runs.sort();
    report.dropped_references.sort();
    report.missing_files.sort();
    report.evicted_replays.sort();
    Ok(ImportPlan {
        rows,
        placements: placements.into_requests(),
        report,
    })
}

const SNAPSHOT_COLUMNS: &[&str] = &[
    "battle_id",
    "player_hand_json",
    "player_skills_json",
    "opponent_hand_json",
    "opponent_skills_json",
];

/// V6 `runs` columns copied unchanged.
const RUN_COPIED: &[&str] = &[
    "run_id",
    "hero",
    "game_mode",
    "player_rank",
    "player_rating",
    "day",
    "hour",
    "max_health",
    "prestige",
    "level",
    "income",
    "gold",
    "last_seq",
    "final_day",
    "final_hour",
    "victories",
    "losses",
    "final_player_rank",
    "final_player_rating",
    "final_player_rating_delta",
    "reason",
    "build_channel",
    "player_account_id",
    "bundle_screenshot_requested",
    "mod_version",
];

/// V6 `battles` columns copied unchanged.
const BATTLE_COPIED: &[&str] = &[
    "battle_id",
    "remote_battle_id",
    "uploader_account_id",
    "source",
    "run_id",
    "local_player_account_id",
    "day",
    "hour",
    "encounter_id",
    "combat_kind",
    "player_name",
    "player_account_id",
    "player_hero",
    "player_rank",
    "player_rating",
    "player_level",
    "player_prestige",
    "player_income",
    "player_gold",
    "player_victories",
    "player_hand_item_count",
    "player_skill_count",
    "opponent_name",
    "opponent_account_id",
    "opponent_hero",
    "opponent_rank",
    "opponent_rating",
    "opponent_level",
    "opponent_prestige",
    "opponent_victories",
    "opponent_hand_item_count",
    "opponent_skill_count",
    "result",
    "winner_combatant_id",
    "loser_combatant_id",
    "is_final_battle",
    "bundle_id",
    "download_url",
    "download_url_expires_at_ms",
    "ghost_replay_state",
    "ghost_replay_unavailable_reason",
];

/// V6 `combat_replay_videos` columns copied unchanged.
const VIDEO_COPIED: &[&str] = &[
    "video_id",
    "battle_id",
    "source",
    "width",
    "height",
    "fps",
    "codec",
    "crf",
    "preset",
    "duration_ms",
    "captured_frames",
    "dropped_frames",
    "file_size_bytes",
    "status",
    "error",
];

/// V6 `run_screenshots` columns copied unchanged.
const SCREENSHOT_COPIED: &[&str] = &[
    "screenshot_id",
    "run_id",
    "hero_name",
    "capture_source",
    "is_primary",
    "day",
    "player_rank",
    "player_rating",
    "player_position",
    "victories_at_capture",
];

const PAYLOAD_STATES: [&str; 4] = ["ready", "delete_pending", "evicted", "missing"];

fn copy(row: &V5Row, columns: &[&'static str]) -> V6Row {
    columns
        .iter()
        .map(|column| (*column, row.value(column)))
        .collect()
}

fn set(row: &mut V6Row, column: &'static str, value: Value) {
    match row.iter_mut().find(|(name, _)| *name == column) {
        Some(entry) => entry.1 = value,
        None => row.push((column, value)),
    }
}

fn get<'a>(row: &'a [(&'static str, Value)], column: &str) -> Option<&'a Value> {
    row.iter()
        .find(|(name, _)| *name == column)
        .map(|(_, value)| value)
}

fn text_of<'a>(row: &'a [(&'static str, Value)], column: &str) -> Option<&'a str> {
    match get(row, column) {
        Some(Value::Text(text)) => Some(text),
        _ => None,
    }
}

fn integer(row: &[(&'static str, Value)], column: &str) -> i64 {
    match get(row, column) {
        Some(Value::Integer(value)) => *value,
        _ => 0,
    }
}

fn exists(conn: &Connection, sql: &str, key: &str) -> Result<bool, ImportError> {
    conn.query_row(sql, [key], |_| Ok(()))
        .optional()
        .map(|found| found.is_some())
        .map_err(|error| ImportError::Target(error.to_string()))
}

/// The instant in `column` of a V5 row as Unix milliseconds. NULL stays NULL;
/// text that is no instant fails the import rather than inventing a time.
fn instant(
    row: &V5Row,
    table: &'static str,
    id_column: &str,
    column: &'static str,
) -> Result<Option<i64>, ImportError> {
    match row.value(column) {
        Value::Null => Ok(None),
        Value::Text(text) => {
            parse_v5_instant_ms(&text)
                .map(Some)
                .ok_or_else(|| ImportError::InvalidV5Value {
                    table,
                    id: row.text(id_column).unwrap_or_default(),
                    column,
                    value: text,
                })
        }
        other => Err(ImportError::InvalidV5Value {
            table,
            id: row.text(id_column).unwrap_or_default(),
            column,
            value: format!("{other:?}"),
        }),
    }
}

fn required_instant(
    row: &V5Row,
    table: &'static str,
    id_column: &str,
    column: &'static str,
) -> Result<i64, ImportError> {
    instant(row, table, id_column, column)?.ok_or_else(|| ImportError::InvalidV5Value {
        table,
        id: row.text(id_column).unwrap_or_default(),
        column,
        value: "NULL".into(),
    })
}

fn nullable(value: Option<i64>) -> Value {
    value.map_or(Value::Null, Value::Integer)
}

/// Returns the V6 row and whether the Run was unfinished and is closed here.
fn map_run(run: &V5Row) -> Result<(V6Row, bool), ImportError> {
    let started = required_instant(run, "runs", "run_id", "started_at_utc")?;
    let last_seen = required_instant(run, "runs", "run_id", "last_seen_at_utc")?;
    let ended = instant(run, "runs", "run_id", "ended_at_utc")?;
    let status = run.text("status").unwrap_or_default();
    // As the mod's MarkRunAbandoned closes a Run: abandoned at its last-seen time.
    let finished =
        run.integer("completed") == Some(1) && matches!(status.as_str(), "completed" | "abandoned");
    let mut row = copy(run, RUN_COPIED);
    row.push(("started_at_ms", Value::Integer(started)));
    row.push(("last_seen_at_ms", Value::Integer(last_seen)));
    if finished {
        row.push(("status", Value::Text(status)));
        row.push(("ended_at_ms", nullable(ended)));
    } else {
        row.push(("status", Value::Text("abandoned".into())));
        row.push(("ended_at_ms", Value::Integer(last_seen)));
    }
    row.push(("upload_policy", Value::Text("history_only".into())));
    Ok((row, !finished))
}

fn map_run_event(event: &V5Row) -> Result<V6Row, ImportError> {
    let mut row = copy(event, &["run_id", "seq", "kind", "payload_json"]);
    row.push((
        "ts_at_ms",
        Value::Integer(required_instant(event, "run_events", "run_id", "ts_utc")?),
    ));
    Ok(row)
}

/// The V6 battle row, and whether its payload is a retention candidate:
/// `ready` with the file present in V5. A `ready` battle without its file is
/// `missing`; `delete_pending` is `evicted`, since V5 had chosen to delete it.
fn map_battle(
    context: &Context<'_>,
    shape: V5Shape,
    battle: &V5Row,
) -> Result<(V6Row, bool), ImportError> {
    let mut row = copy(battle, BATTLE_COPIED);
    row.push((
        "recorded_at_ms",
        Value::Integer(required_instant(
            battle,
            "battles",
            "battle_id",
            "recorded_at_utc",
        )?),
    ));
    row.push((
        "deleted_at_ms",
        nullable(instant(battle, "battles", "battle_id", "deleted_at_utc")?),
    ));
    // UpgradeToLifecycleColumns derived the state from has_local_payload; an
    // upgraded table has no CHECK, so an unknown state is derived the same way.
    let stored = match shape {
        V5Shape::First => None,
        V5Shape::Lifecycle => battle
            .text("local_payload_state")
            .filter(|state| PAYLOAD_STATES.contains(&state.as_str())),
    };
    let maintenance = match (shape, &stored) {
        (V5Shape::Lifecycle, Some(_)) => instant(
            battle,
            "battles",
            "battle_id",
            "local_payload_maintenance_at_utc",
        )?,
        _ => None,
    };
    let state = stored.unwrap_or_else(|| {
        if battle.integer("has_local_payload") == Some(1) {
            "ready".into()
        } else {
            "missing".into()
        }
    });
    let battle_id = battle.text("battle_id").unwrap_or_default();
    let (state, maintenance, candidate) = match state.as_str() {
        "ready" => {
            let present = replay_payload_path(&combat_replays_dir_in(context.v5_root), &battle_id)
                .is_some_and(|path| is_regular_file(&path));
            if present {
                ("ready", maintenance, true)
            } else {
                ("missing", Some(context.now_ms), false)
            }
        }
        "delete_pending" => ("evicted", Some(context.now_ms), false),
        "evicted" => ("evicted", maintenance, false),
        _ => ("missing", maintenance, false),
    };
    row.push(("local_payload_state", Value::Text(state.into())));
    row.push(("local_payload_maintenance_at_ms", nullable(maintenance)));
    Ok((row, candidate))
}

/// The payloads the mod's retention keeps among `candidates` and the payloads
/// the current Data Root already holds: the newest `newest_count` by recorded
/// time (battle id breaking ties), plus everything inside the age window
/// (`ReplayPayloadRetentionPolicy.SelectEvictionCandidates`).
fn retained_payloads(
    target: &Connection,
    v6_root: &Path,
    candidates: &[(i64, String)],
    now_ms: i64,
    retention: ReplayRetention,
) -> Result<HashSet<String>, ImportError> {
    let mut statement = target
        .prepare(
            "select recorded_at_ms, battle_id from battles \
             where source = 'LOCAL' and local_payload_state = 'ready'",
        )
        .map_err(|error| ImportError::Target(error.to_string()))?;
    let existing = statement
        .query_map([], |row| {
            Ok((row.get::<_, i64>(0)?, row.get::<_, String>(1)?))
        })
        .and_then(|rows| rows.collect::<Result<Vec<_>, _>>())
        .map_err(|error| ImportError::Target(error.to_string()))?;
    let v6_replays = combat_replays_dir_in(v6_root);
    let mut ranked: Vec<(i64, &str, bool)> = existing
        .iter()
        .filter(|(_, battle_id)| {
            replay_payload_path(&v6_replays, battle_id).is_some_and(|path| is_regular_file(&path))
        })
        .map(|(recorded, battle_id)| (*recorded, battle_id.as_str(), false))
        .chain(
            candidates
                .iter()
                .map(|(recorded, battle_id)| (*recorded, battle_id.as_str(), true)),
        )
        .collect();
    ranked.sort_by(|left, right| right.0.cmp(&left.0).then_with(|| right.1.cmp(left.1)));
    let cutoff = now_ms.saturating_sub(retention.age_days.saturating_mul(DAY_MS));
    Ok(ranked
        .iter()
        .enumerate()
        .filter(|(index, (recorded, _, imported))| {
            *imported && (*index < retention.newest_count || *recorded >= cutoff)
        })
        .map(|(_, (_, battle_id, _))| (*battle_id).to_string())
        .collect())
}

fn map_video(
    context: &Context<'_>,
    shape: V5Shape,
    video: &V5Row,
    placements: &mut Placements,
    report: &mut PlanReport,
) -> Result<Option<V6Row>, ImportError> {
    let video_id = video.text("video_id").unwrap_or_default();
    let raw_path = video.text("video_relative_path").unwrap_or_default();
    let reference = |reason| FileReference {
        table: "combat_replay_videos",
        id: video_id.clone(),
        path: raw_path.clone(),
        reason,
    };
    let (attachment, file_state) = match shape {
        V5Shape::First => ("attached".to_string(), "pending".to_string()),
        V5Shape::Lifecycle => (
            video.text("attachment_state").unwrap_or_default(),
            video.text("file_state").unwrap_or_default(),
        ),
    };
    // V6 keeps no row for a deleted video file; the CHECK limits status.
    if file_state == "deleted" {
        report.dropped_references.push(reference("file_deleted"));
        return Ok(None);
    }
    let status = video.text("status").unwrap_or_default();
    if !matches!(status.as_str(), "RECORDING" | "COMPLETED" | "FAILED") {
        report.dropped_references.push(reference("invalid_status"));
        return Ok(None);
    }
    let directory = combat_replay_videos_dir_in(context.v5_root);
    let Some(relative) = data_relative_path(&raw_path, &directory) else {
        report
            .dropped_references
            .push(reference("outside_data_root"));
        return Ok(None);
    };
    place(
        placements,
        report,
        &directory,
        &combat_replay_videos_dir_in(context.v6_root),
        &relative,
        reference("source_missing"),
    );

    let mut row = copy(video, VIDEO_COPIED);
    row.push(("video_relative_path", Value::Text(relative)));
    row.push((
        "started_at_ms",
        Value::Integer(required_instant(
            video,
            "combat_replay_videos",
            "video_id",
            "started_at_utc",
        )?),
    ));
    row.push((
        "ended_at_ms",
        nullable(instant(
            video,
            "combat_replay_videos",
            "video_id",
            "ended_at_utc",
        )?),
    ));
    // An upgraded table has no CHECK on these; the video belongs to an imported
    // battle, so it is attached, and the mod reconciles an unknown file state.
    let attachment = if matches!(attachment.as_str(), "attached" | "detached") {
        attachment
    } else {
        "attached".into()
    };
    let file_state = if matches!(file_state.as_str(), "pending" | "present" | "missing") {
        file_state
    } else {
        "pending".into()
    };
    row.push(("attachment_state", Value::Text(attachment)));
    row.push(("file_state", Value::Text(file_state)));
    for (v6, v5) in [
        ("detached_at_ms", "detached_at_utc"),
        ("missing_at_ms", "missing_at_utc"),
        ("last_reconciled_at_ms", "last_reconciled_at_utc"),
    ] {
        let value = match shape {
            V5Shape::First => None,
            V5Shape::Lifecycle => instant(video, "combat_replay_videos", "video_id", v5)?,
        };
        row.push((v6, nullable(value)));
    }
    Ok(Some(row))
}

fn map_screenshot(
    context: &Context<'_>,
    screenshot: &V5Row,
    placements: &mut Placements,
    report: &mut PlanReport,
) -> Result<Option<V6Row>, ImportError> {
    let screenshot_id = screenshot.text("screenshot_id").unwrap_or_default();
    let raw_path = screenshot.text("image_relative_path").unwrap_or_default();
    let reference = |reason| FileReference {
        table: "run_screenshots",
        id: screenshot_id.clone(),
        path: raw_path.clone(),
        reason,
    };
    let directory = screenshots_dir_in(context.v5_root);
    let Some(relative) = data_relative_path(&raw_path, &directory) else {
        report
            .dropped_references
            .push(reference("outside_data_root"));
        return Ok(None);
    };
    let local = screenshot.text("captured_at_local").unwrap_or_default();
    let offset = parse_v5_offset_minutes(&local).ok_or_else(|| ImportError::InvalidV5Value {
        table: "run_screenshots",
        id: screenshot_id.clone(),
        column: "captured_at_local",
        value: local.clone(),
    })?;
    let captured = required_instant(
        screenshot,
        "run_screenshots",
        "screenshot_id",
        "captured_at_utc",
    )?;
    place(
        placements,
        report,
        &directory,
        &screenshots_dir_in(context.v6_root),
        &relative,
        reference("source_missing"),
    );

    let mut row = copy(screenshot, SCREENSHOT_COPIED);
    row.push(("image_relative_path", Value::Text(relative)));
    row.push(("captured_at_ms", Value::Integer(captured)));
    row.push(("captured_utc_offset_minutes", Value::Integer(offset)));
    Ok(Some(row))
}

/// The stored path as a `/`-separated path under `directory`, the V5 folder its
/// kind of file lives in. A relative path stays relative (`.` and `..` dropped,
/// as History cleanup resolves it); an absolute path is kept only when it lies
/// under `directory`. `None` for anything else.
fn data_relative_path(raw: &str, directory: &Path) -> Option<String> {
    let trimmed = raw.trim();
    if trimmed.is_empty() {
        return None;
    }
    let relative = if Path::new(trimmed).is_absolute() {
        // A `\\?\` path treats `/` as part of a name, so a root written that
        // way followed by `/`-joined segments never matches; Windows reads
        // either separator the same in any other absolute path.
        let written = if cfg!(windows) {
            trimmed.replace('/', "\\")
        } else {
            trimmed.to_owned()
        };
        // Compare as written and with links resolved: the game directory may be
        // reached through a symbolic link, as macOS temporary folders are.
        let path = Path::new(&written);
        let paths = [Some(path.to_path_buf()), path.canonicalize().ok()];
        let bases = [Some(directory.to_path_buf()), directory.canonicalize().ok()];
        let rest = paths.iter().flatten().find_map(|path| {
            bases
                .iter()
                .flatten()
                .find_map(|base| path.strip_prefix(base).ok().map(Path::to_path_buf))
        })?;
        normalize_relative_path(&rest.to_string_lossy())
    } else {
        normalize_relative_path(trimmed)
    };
    if relative.is_empty() {
        return None;
    }
    // `C:name` is not absolute on Windows, yet joining it replaces the root.
    resolve_cleanup_file_path(directory, &relative)?;
    Some(relative)
}

fn place(
    placements: &mut Placements,
    report: &mut PlanReport,
    source_directory: &Path,
    target_directory: &Path,
    relative: &str,
    missing: FileReference,
) {
    let (Some(source), Some(target)) = (
        resolve_cleanup_file_path(source_directory, relative),
        resolve_cleanup_file_path(target_directory, relative),
    ) else {
        return;
    };
    if is_regular_file(&source) {
        placements.add(source, target);
    } else {
        report.missing_files.push(missing);
    }
}

/// A regular file, not a symbolic link: the import never follows a link out of
/// the V5 Data Root.
fn is_regular_file(path: &Path) -> bool {
    std::fs::symlink_metadata(path).is_ok_and(|metadata| metadata.file_type().is_file())
}

/// Placement requests, one per target: Runs that share a file place it once.
#[derive(Default)]
struct Placements(BTreeMap<PathBuf, PathBuf>);

impl Placements {
    fn add(&mut self, source: PathBuf, target: PathBuf) {
        self.0.entry(target).or_insert(source);
    }

    fn into_requests(self) -> Vec<PlacementRequest> {
        self.0
            .into_iter()
            .map(|(target, source)| PlacementRequest { source, target })
            .collect()
    }
}

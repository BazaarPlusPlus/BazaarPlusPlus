//! What the History callers do with the stored paths of the #989 input table,
//! and the problem each History operation reports. Every caller resolves a
//! stored path through `StoredRelativePath` in `history/files.rs`, so an input
//! the rule rejects names no file: reveal reports it, a delete keeps the file,
//! and a reference still protects every file with its last segment as name.

use super::{
    execute_storage_cleanup_with, CleanupPlans, History, StorageCleanupExecution,
    StorageCleanupPreset, StorageCleanupPreview, StorageCleanupScope,
};
use crate::history::mod_schema::create_mod_schema;
use crate::problem::{SemanticProblem, SemanticProblemCode};
use crate::services::data_maintenance::DataMaintenanceLock;
use crate::services::paths;
use crate::test_support::{
    assert_stored_path_table, describe_path_under, stored_path_inputs, write_stored_path_targets,
    StoredPathInput,
};
use std::path::{Path, PathBuf};

const RUN_SQL: &str = "
    insert into runs (
        run_id, started_at_ms, last_seen_at_ms, status,
        hero, game_mode, ended_at_ms, victories, losses
    ) values (
        'run-1', 1767258000000, 1767261600000,
        'completed', 'Vanessa', 'Normal', 1767261600000, 10, 2
    );
    insert into battles (
        battle_id, source, run_id, recorded_at_ms, combat_kind, player_name,
        opponent_hero, opponent_name, result, local_payload_state
    ) values (
        'battle-1', 'LOCAL', 'run-1', 1767259800000, 'PVP', 'Player',
        'Dooley', 'Opponent', 'win', 'missing'
    );";

const RETRY_RECORD: &str = "installer-cleanup-video-retry.txt";

/// Temporary games for one table, each with an empty history database, and a
/// file outside every one of them that the `absolute` input names.
struct Games {
    temp: tempfile::TempDir,
    outside: PathBuf,
    next: std::cell::Cell<usize>,
}

impl Games {
    fn new() -> Self {
        let temp = tempfile::tempdir().unwrap();
        let outside = temp.path().join("outside.png");
        std::fs::write(&outside, b"outside").unwrap();
        Self {
            temp,
            outside,
            next: std::cell::Cell::new(0),
        }
    }

    fn inputs(&self, dir: &str) -> Vec<StoredPathInput> {
        stored_path_inputs(dir, &self.outside)
    }

    /// A fresh game per input, so no state crosses inputs.
    fn fresh(&self) -> PathBuf {
        let index = self.next.get();
        self.next.set(index + 1);
        let game = self.temp.path().join(format!("game-{index}"));
        let database = paths::database_path(&game);
        std::fs::create_dir_all(database.parent().unwrap()).unwrap();
        create_mod_schema(&rusqlite::Connection::open(&database).unwrap());
        game
    }

    /// The labels of the files among `targets` and the outside file that are
    /// gone, or `none`.
    fn removed(&self, targets: &[(&str, PathBuf)]) -> String {
        let removed = targets
            .iter()
            .map(|(label, path)| (*label, path.as_path()))
            .chain([("outside", self.outside.as_path())])
            .filter(|(_, path)| !path.exists())
            .map(|(label, _)| label)
            .collect::<Vec<_>>();
        if removed.is_empty() {
            "none".to_string()
        } else {
            removed.join(", ")
        }
    }

    /// The labels of `targets` still on disk, or `none`.
    fn kept(targets: &[(&str, PathBuf)]) -> String {
        let kept = targets
            .iter()
            .filter(|(_, path)| path.exists())
            .map(|(label, _)| *label)
            .collect::<Vec<_>>();
        if kept.is_empty() {
            "none".to_string()
        } else {
            kept.join(", ")
        }
    }
}

fn execute(game: &Path, sql: &str, params: impl rusqlite::Params) {
    rusqlite::Connection::open(paths::database_path(game))
        .unwrap()
        .execute(sql, params)
        .unwrap();
}

fn insert_screenshot(game: &Path, id: &str, capture_source: &str, path: &str) {
    execute(
        game,
        "insert into run_screenshots (
            screenshot_id, run_id, capture_source, is_primary,
            image_relative_path, captured_at_ms, captured_utc_offset_minutes
        ) values (?1, null, ?2, 0, ?3, 1767261600000, 480)",
        [id, capture_source, path],
    );
}

fn insert_run_with_video(game: &Path, video_path: &str) {
    rusqlite::Connection::open(paths::database_path(game))
        .unwrap()
        .execute_batch(RUN_SQL)
        .unwrap();
    execute(
        game,
        "insert into combat_replay_videos (
            video_id, battle_id, source, video_relative_path, width, height, fps, codec,
            started_at_ms, file_size_bytes, status
        ) values (
            'video-1', 'battle-1', 'GAME_CAPTURE', ?1, 1920, 1080, 60,
            'h264', 1767259800000, 5, 'COMPLETED'
        )",
        [video_path],
    );
}

fn video_rows(game: &Path) -> i64 {
    rusqlite::Connection::open(paths::database_path(game))
        .unwrap()
        .query_row("select count(*) from combat_replay_videos", [], |row| {
            row.get(0)
        })
        .unwrap()
}

fn history(game: &Path) -> History {
    History::at_with(Some(game.to_path_buf()), || false)
        .unwrap()
        .with_cleanup_game_probe(|_| Ok(false))
}

/// `rejected` for the action problem of `operation`, anything else as found.
fn action_problem(problem: &SemanticProblem, operation: &str) -> String {
    if problem.code == SemanticProblemCode::HistoryActionFailed
        && problem.params.get("operation").map(String::as_str) == Some(operation)
    {
        "rejected".to_string()
    } else {
        format!("{problem:?}")
    }
}

/// Previews `scope` with preset `all` and executes that preview's plan.
fn preview_and_execute(
    history: &History,
    scope: StorageCleanupScope,
) -> Result<StorageCleanupExecution, SemanticProblem> {
    let plans = CleanupPlans::default();
    let plan_id = match history
        .preview_cleanup(scope, StorageCleanupPreset::All, &plans)
        .unwrap()
    {
        StorageCleanupPreview::Screenshots { plan_id, .. }
        | StorageCleanupPreview::RunData { plan_id, .. } => plan_id,
    };
    history.execute_cleanup(scope, StorageCleanupPreset::All, &plans, plan_id)
}

#[test]
fn reveal_screenshot_resolves_each_stored_path() {
    let games = Games::new();
    assert_stored_path_table(
        "reveal screenshot",
        &games.inputs("a"),
        &[
            (
                "a/b.png",
                "pinned by history_facade_owns_paths_queries_reveals_deletes_and_cleanup",
            ),
            (r"a\b.png", "<root>/a/b.png"),
            ("a/../b.png", "rejected"),
            ("..", "rejected"),
            (".", "rejected"),
            ("empty", "rejected"),
            ("blank", "rejected"),
            ("absolute", "rejected"),
            ("UNC", "rejected"),
            ("C:x.png", "rejected"),
        ],
        |input| {
            let game = games.fresh();
            rusqlite::Connection::open(paths::database_path(&game))
                .unwrap()
                .execute_batch(RUN_SQL)
                .unwrap();
            execute(
                &game,
                "insert into run_screenshots (
                    screenshot_id, run_id, capture_source, is_primary,
                    image_relative_path, captured_at_ms, captured_utc_offset_minutes
                ) values ('shot-1', 'run-1', 'end_of_run_auto', 1, ?1, 1767261600000, 480)",
                [&input.raw],
            );
            match history(&game).run_screenshot_path("run-1") {
                Ok(path) => describe_path_under(&paths::screenshots_dir(&game), &path),
                Err(problem) => action_problem(&problem, "reveal_screenshot"),
            }
        },
    );
}

#[test]
fn reveal_video_resolves_each_stored_path_that_exists() {
    let games = Games::new();
    assert_stored_path_table(
        "reveal video",
        &games.inputs("a"),
        &[
            (
                "a/b.png",
                "pinned by history_facade_owns_paths_queries_reveals_deletes_and_cleanup",
            ),
            (r"a\b.png", "<root>/a/b.png"),
            ("a/../b.png", "rejected"),
            ("..", "rejected"),
            (".", "rejected"),
            ("empty", "rejected"),
            ("blank", "rejected"),
            ("absolute", "rejected"),
            ("UNC", "rejected"),
            ("C:x.png", "rejected"),
        ],
        |input| {
            let game = games.fresh();
            write_stored_path_targets(&paths::combat_replay_videos_dir(&game), "a");
            insert_run_with_video(&game, &input.raw);
            match history(&game).battle_video_path("battle-1", "video-1") {
                Ok(path) => describe_path_under(&paths::combat_replay_videos_dir(&game), &path),
                Err(problem)
                    if problem
                        .diagnostic
                        .as_deref()
                        .is_some_and(|text| text.contains("Video file was not found")) =>
                {
                    "missing".to_string()
                }
                Err(problem) => action_problem(&problem, "reveal_video"),
            }
        },
    );
}

#[test]
fn delete_video_removes_the_file_each_stored_path_names() {
    let games = Games::new();
    assert_stored_path_table(
        "delete video",
        &games.inputs("a"),
        // A path the rule rejects keeps every file and loses its row.
        &[
            (
                "a/b.png",
                "pinned by history_facade_owns_paths_queries_reveals_deletes_and_cleanup",
            ),
            (r"a\b.png", "ok; row deleted; removed a/b.png"),
            ("a/../b.png", "ok; row deleted; removed none"),
            ("..", "ok; row deleted; removed none"),
            (".", "ok; row deleted; removed none"),
            ("empty", "ok; row deleted; removed none"),
            ("blank", "ok; row deleted; removed none"),
            ("absolute", "ok; row deleted; removed none"),
            ("UNC", "ok; row deleted; removed none"),
            ("C:x.png", "ok; row deleted; removed none"),
        ],
        |input| {
            let game = games.fresh();
            let targets = write_stored_path_targets(&paths::combat_replay_videos_dir(&game), "a");
            insert_run_with_video(&game, &input.raw);
            let result = match history(&game).delete_battle_video("battle-1", "video-1") {
                Ok(_) => "ok".to_string(),
                Err(problem) => action_problem(&problem, "delete_video"),
            };
            let row = if video_rows(&game) == 0 {
                "deleted"
            } else {
                "kept"
            };
            format!("{result}; row {row}; removed {}", games.removed(&targets))
        },
    );
}

#[test]
fn screenshot_cleanup_unlinks_the_file_each_deleted_row_names() {
    let games = Games::new();
    assert_stored_path_table(
        "cleanup candidate",
        &games.inputs("a"),
        &[
            (
                "a/b.png",
                "pinned by execute_deletes_rows_and_orphans_but_leaves_mod_owned_cache",
            ),
            (r"a\b.png", "ok, 1 row; removed a/b.png"),
            ("a/../b.png", "ok, 1 row; removed none"),
            ("..", "ok, 1 row; removed none"),
            (".", "ok, 1 row; removed none"),
            ("empty", "ok, 1 row; removed none"),
            ("blank", "ok, 1 row; removed none"),
            ("absolute", "ok, 1 row; removed none"),
            ("UNC", "ok, 1 row; removed none"),
            ("C:x.png", "ok, 1 row; removed none"),
        ],
        |input| {
            let game = games.fresh();
            let targets = write_stored_path_targets(&paths::screenshots_dir(&game), "a");
            insert_screenshot(&game, "shot-1", "end_of_run_auto", &input.raw);
            let result =
                match preview_and_execute(&history(&game), StorageCleanupScope::Screenshots) {
                    Ok(StorageCleanupExecution::Screenshots { result }) => {
                        format!("ok, {} row", result.deleted_rows)
                    }
                    Ok(other) => format!("{other:?}"),
                    Err(problem) => action_problem(&problem, "execute_storage_cleanup"),
                };
            format!("{result}; removed {}", games.removed(&targets))
        },
    );
}

#[test]
fn screenshot_cleanup_keeps_the_files_a_surviving_row_references() {
    let games = Games::new();
    assert_stored_path_table(
        "cleanup reference set",
        &games.inputs("a"),
        // A rejected reference protects every file named as its last segment,
        // so it keeps at least the file it kept before the rule.
        &[
            (
                "a/b.png",
                "pinned by execute_preserves_shared_file_referenced_by_a_protected_primary_screenshot",
            ),
            (r"a\b.png", "kept a/b.png"),
            ("a/../b.png", "kept a/b.png, b.png, server/share/b.png"),
            ("..", "kept none"),
            (".", "kept none"),
            ("empty", "kept none"),
            ("blank", "kept none"),
            ("absolute", "kept none"),
            ("UNC", "kept a/b.png, b.png, server/share/b.png"),
            ("C:x.png", "kept none"),
        ],
        |input| {
            let game = games.fresh();
            let targets = write_stored_path_targets(&paths::screenshots_dir(&game), "a");
            // Every target is a deleted row's file; the `manual` capture is not
            // a cleanup candidate, so its row survives and references `raw`.
            insert_screenshot(&game, "drop-a", "end_of_run_auto", "a/b.png");
            insert_screenshot(&game, "drop-b", "end_of_run_auto", "b.png");
            insert_screenshot(&game, "drop-unc", "end_of_run_auto", "server/share/b.png");
            insert_screenshot(&game, "keep", "manual", &input.raw);
            match preview_and_execute(&history(&game), StorageCleanupScope::Screenshots) {
                Ok(StorageCleanupExecution::Screenshots { result }) => {
                    assert_eq!(result.deleted_rows, 3);
                    format!("kept {}", Games::kept(&targets))
                }
                Ok(other) => format!("{other:?}"),
                Err(problem) => format!("{problem:?}"),
            }
        },
    );
}

#[test]
fn screenshot_orphan_scan_spares_the_files_a_row_references() {
    let games = Games::new();
    // The scan reads only past-dated folders, so the table's `a` is one.
    let dir = "2020-01-01";
    assert_stored_path_table(
        "orphan scan",
        &games.inputs(dir),
        // A rejected reference spares every file named as its last segment.
        &[
            ("a/b.png", "referenced"),
            (
                r"a\b.png",
                "pinned by plan_detects_orphan_files_only_in_qualifying_dated_folders",
            ),
            ("a/../b.png", "referenced"),
            ("..", "orphan"),
            (".", "orphan"),
            ("empty", "orphan"),
            ("blank", "orphan"),
            ("absolute", "orphan"),
            ("UNC", "referenced"),
            ("C:x.png", "orphan"),
        ],
        |input| {
            let game = games.fresh();
            write_stored_path_targets(&paths::screenshots_dir(&game), dir);
            insert_screenshot(&game, "keep", "manual", &input.raw);
            let plans = CleanupPlans::default();
            match history(&game)
                .preview_cleanup(
                    StorageCleanupScope::Screenshots,
                    StorageCleanupPreset::All,
                    &plans,
                )
                .unwrap()
            {
                StorageCleanupPreview::Screenshots { preview, .. } => match preview.orphan_files {
                    0 => "referenced".to_string(),
                    1 => "orphan".to_string(),
                    other => format!("{other} orphans"),
                },
                other => format!("{other:?}"),
            }
        },
    );
}

/// Before the rule, the orphan scan dropped the leading separator of an
/// absolute row, so `/2020-01-01/b.png` spared `2020-01-01/b.png`. The rule
/// rejects the row, and its file name still spares the file.
#[test]
fn an_absolute_row_still_spares_the_dated_file_it_names() {
    let games = Games::new();
    for raw in ["/2020-01-01/b.png", r"\2020-01-01\b.png"] {
        let game = games.fresh();
        write_stored_path_targets(&paths::screenshots_dir(&game), "2020-01-01");
        insert_screenshot(&game, "keep", "manual", raw);
        match history(&game)
            .preview_cleanup(
                StorageCleanupScope::Screenshots,
                StorageCleanupPreset::All,
                &CleanupPlans::default(),
            )
            .unwrap()
        {
            StorageCleanupPreview::Screenshots { preview, .. } => {
                assert_eq!(preview.orphan_files, 0, "{raw}")
            }
            other => panic!("expected a screenshot preview, got {other:?}"),
        }
    }
}

#[test]
fn run_data_cleanup_retries_each_recorded_video_path() {
    let games = Games::new();
    assert_stored_path_table(
        "retry record",
        &games.inputs("a"),
        // The plan drops an empty line and a path the rule rejects, so no
        // cleanup session opens and the record stays as written.
        &[
            (
                "a/b.png",
                "pinned by a_failed_unlink_is_retried_by_the_next_cleanup_after_its_rows_are_gone",
            ),
            (r"a\b.png", "ok; removed a/b.png; record gone"),
            ("a/../b.png", "ok; removed none; record kept"),
            ("..", "ok; removed none; record kept"),
            (".", "ok; removed none; record kept"),
            ("empty", "ok; removed none; record kept"),
            ("blank", "ok; removed none; record kept"),
            ("absolute", "ok; removed none; record kept"),
            ("UNC", "ok; removed none; record kept"),
            ("C:x.png", "ok; removed none; record kept"),
        ],
        |input| {
            let game = games.fresh();
            let targets = write_stored_path_targets(&paths::combat_replay_videos_dir(&game), "a");
            let record = paths::bpp_data_dir(&game).join(RETRY_RECORD);
            let written = format!("{}\n", input.raw);
            std::fs::write(&record, &written).unwrap();
            let result = match preview_and_execute(&history(&game), StorageCleanupScope::RunData) {
                Ok(StorageCleanupExecution::RunData { .. }) => "ok".to_string(),
                Ok(other) => format!("{other:?}"),
                Err(problem) => action_problem(&problem, "execute_storage_cleanup"),
            };
            let record = match std::fs::read_to_string(&record) {
                Err(error) if error.kind() == std::io::ErrorKind::NotFound => "gone".to_string(),
                Ok(text) if text == written => "kept".to_string(),
                Ok(text) => format!("{text:?}"),
                Err(error) => panic!("{error}"),
            };
            format!(
                "{result}; removed {}; record {record}",
                games.removed(&targets)
            )
        },
    );
}

/// An empty plan opens no cleanup session, so it never asks whether the game
/// runs. `a_refused_cleanup_keeps_the_preview_plan_for_its_retry` pins the
/// refusal of a plan that is not empty.
#[test]
fn an_empty_cleanup_plan_executes_while_the_game_is_reported_running() {
    let games = Games::new();
    let game = games.fresh();
    let running = History::at_with(Some(game), || true)
        .unwrap()
        .with_cleanup_game_probe(|_| Ok(true));

    match preview_and_execute(&running, StorageCleanupScope::Screenshots).unwrap() {
        StorageCleanupExecution::Screenshots { result } => assert_eq!(
            (
                result.deleted_rows,
                result.deleted_files,
                result.freed_bytes,
                result.skipped_pending_uploads
            ),
            (0, 0, 0, 0)
        ),
        other => panic!("expected a screenshot result, got {other:?}"),
    }
    match preview_and_execute(&running, StorageCleanupScope::RunData).unwrap() {
        StorageCleanupExecution::RunData { result } => assert_eq!(
            (
                result.deleted_runs,
                result.deleted_files,
                result.freed_bytes,
                result.skipped_pending_uploads
            ),
            (0, 0, 0, 0)
        ),
        other => panic!("expected a run-data result, got {other:?}"),
    }
}

#[test]
fn a_video_delete_removes_the_file_before_the_row() {
    let games = Games::new();
    let game = games.fresh();
    let targets = write_stored_path_targets(&paths::combat_replay_videos_dir(&game), "a");
    insert_run_with_video(&game, "a/b.png");
    // The row delete fails, so whatever happened first stays visible.
    rusqlite::Connection::open(paths::database_path(&game))
        .unwrap()
        .execute_batch(
            "create trigger refuse_video_delete before delete on combat_replay_videos
             begin select raise(abort, 'row delete refused'); end;",
        )
        .unwrap();

    let problem = history(&game)
        .delete_battle_video("battle-1", "video-1")
        .unwrap_err();

    assert_eq!(action_problem(&problem, "delete_video"), "rejected");
    assert!(problem
        .diagnostic
        .as_deref()
        .is_some_and(|text| text.contains("row delete refused")));
    assert_eq!(games.removed(&targets), "a/b.png");
    assert_eq!(video_rows(&game), 1);
}

#[test]
fn a_video_delete_deletes_the_row_when_the_file_is_missing() {
    let games = Games::new();
    let game = games.fresh();
    insert_run_with_video(&game, "a/b.png");
    assert!(!paths::combat_replay_videos_dir(&game).exists());

    let detail = history(&game)
        .delete_battle_video("battle-1", "video-1")
        .unwrap();

    assert_eq!(detail.battles[0].video, None);
    assert_eq!(video_rows(&game), 0);
}

/// The cleanup command resolves its installation inside the data maintenance
/// lock; without one it reports History as unavailable, not as a failure.
#[tokio::test]
async fn cleanup_execution_without_an_installation_is_history_unavailable() {
    let problem = execute_storage_cleanup_with(
        &DataMaintenanceLock::default(),
        CleanupPlans::default(),
        || None,
        StorageCleanupScope::RunData,
        StorageCleanupPreset::All,
        0,
    )
    .await
    .unwrap_err();

    assert_eq!(problem.code, SemanticProblemCode::HistoryUnavailable);
    assert!(problem.params.is_empty());
    assert_eq!(problem.diagnostic, None);
}

#[tokio::test]
async fn cleanup_execution_failure_names_its_operation() {
    let games = Games::new();
    let game = games.fresh();
    std::fs::write(paths::database_path(&game), b"not sqlite").unwrap();

    let problem = execute_storage_cleanup_with(
        &DataMaintenanceLock::default(),
        CleanupPlans::default(),
        move || Some(game),
        StorageCleanupScope::RunData,
        StorageCleanupPreset::All,
        0,
    )
    .await
    .unwrap_err();

    assert_eq!(
        action_problem(&problem, "execute_storage_cleanup"),
        "rejected"
    );
    assert!(problem.diagnostic.is_some());
}

/// The read operations name the game when it still runs; the other five never
/// do. `history_page_list_uses_semantic_unavailable_and_read_failed_problems`
/// and `run_detail_page_distinguishes_not_found_read_and_action_problems` pin
/// the same reads while the game is closed.
#[test]
fn history_reads_name_a_running_game() {
    let games = Games::new();
    let game = games.fresh();
    std::fs::write(paths::database_path(&game), b"not sqlite").unwrap();
    let history = History::at_with(Some(game), || true).unwrap();

    for (operation, problem) in [
        (
            "list_runs",
            history
                .list_runs_for_page(
                    50,
                    0,
                    &crate::stream::history_thumbnails::HistoryThumbnails::default(),
                )
                .unwrap_err(),
        ),
        (
            "get_run_detail",
            history.run_detail_for_page("run-1").unwrap_err(),
        ),
    ] {
        assert_eq!(problem.code, SemanticProblemCode::HistoryReadBlockedByGame);
        assert_eq!(
            problem.params.get("operation").map(String::as_str),
            Some(operation)
        );
    }

    for (operation, problem) in [
        (
            "reveal_screenshot",
            history.run_screenshot_path("run-1").unwrap_err(),
        ),
        (
            "reveal_video",
            history
                .battle_video_path("battle-1", "video-1")
                .unwrap_err(),
        ),
        (
            "delete_video",
            history
                .delete_battle_video("battle-1", "video-1")
                .unwrap_err(),
        ),
        (
            "preview_storage_cleanup",
            history
                .preview_cleanup(
                    StorageCleanupScope::RunData,
                    StorageCleanupPreset::All,
                    &CleanupPlans::default(),
                )
                .unwrap_err(),
        ),
        (
            "execute_storage_cleanup",
            history
                .execute_cleanup(
                    StorageCleanupScope::RunData,
                    StorageCleanupPreset::All,
                    &CleanupPlans::default(),
                    0,
                )
                .unwrap_err(),
        ),
    ] {
        assert_eq!(action_problem(&problem, operation), "rejected");
    }
}

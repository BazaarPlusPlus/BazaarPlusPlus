# History And Storage

## History Boundary

- `History` in `src-tauri/src/services/history.rs` resolves the selected installation and privately derives database, screenshot, and video paths. It owns list, detail, reveal, video deletion, cleanup preview, and cleanup execution.
- Commands in `src-tauri/src/commands/history.rs` pass ids, limits, offsets, cleanup scopes, and presets. They do not accept storage paths, cutoffs, or precomputed cleanup plans.
- An absent run detail is a successful nullable result. Unavailable installation, unsupported schema, failed reads, and failed actions remain distinct `SemanticProblem` codes.
- `open_probed` in `src-tauri/src/history/queries.rs` opens the mod database with read/write flags and a busy timeout so SQLite can recover a dirty WAL or create shared-memory files. It probes `user_version`, retries selected transient errors, and rejects unsupported schemas without retry. The supported versions are owned by `src-tauri/history-database-compatibility.json`; the mod's one-time JSON-failure recovery changes queue data, not the History query columns.

## Cleanup Safety

- `History::preview_cleanup` and `History::execute_cleanup` derive cutoffs from the same `StorageCleanupPreset` contract in `src-tauri/src/history/cleanup.rs`.
- `PROTECTED_RUN_PREDICATE` in `src-tauri/src/history/cleanup.rs` protects completed non-PTR Ranked runs awaiting sealing: runs with no outbox yet, or with a scheduled reseal job. Runs with pending/uploaded outboxes, a server-rejected or retention-expired outbox and no reseal job, or terminal seal failures may be cleaned up, so a later mod recovery cannot assume their source data remains. The same predicate drives planning, skipped counts, screenshot protection, and guarded execution.
- Screenshot cleanup deletes eligible database rows before unlinking files, preserves files still referenced by surviving rows, and protects today's local-date folder from the orphan sweep.
- Run-data cleanup validates the required cascade foreign keys before destructive effects. Each planned run is conditionally deleted in a transaction; files still named by guarded-delete misses are excluded from unlinking.
- `resolve_cleanup_file_path` in `src-tauri/src/history/files.rs` confines cleanup paths to their expected roots.

## Data Ownership

The current root name comes from `BAZAAR_DATA_DIRECTORY` in `src-tauri/src/config.rs`. Reset may delete that current root; uninstall does not. Legacy V4 data remains user-owned. `BundleOutbox/` and `bundle_outbox` remain mod-owned: installer cleanup may consult upload status but never deletes their files or rows. [ADR-0005](adr/0005-data-ownership-and-reset.md) records that ownership boundary.

Frontend History List and Run Detail each own their loading lifecycle; cleanup and deletion retain the confirmed-operation seam described in [Frontend Architecture](frontend-architecture.md). Thumbnail preparation is independent of list reads. `createHistoryListWorkflow` requests `ensure_history_preview` on entry, refresh, page changes, and window resume. It publishes no image URLs until the first preparation succeeds. Later requests keep the published URLs until preparation settles, including while maintenance holds the lifecycle gate, so a refresh neither remounts nor refetches loaded thumbnails; a failed preparation, or one that returns no service URL, clears them. Preparation or image failures use the card's ordinary thumbnail fallback without blocking the list, and a card whose image failed retries it after the next applied preparation.

`History::list_runs_for_page` registers the same installation path used for its query with `StreamRuntime::register_history_preview`. Its thumbnail URLs carry an opaque process-local source, so an existing page cannot be rebound to a different installation when the selected directory gains a database. Sources are deduplicated by path and retained until the installer exits. The History image route uses that source and a separate cache namespace for each source file; existing OBS routes retain their captured repository and cache behavior.

`StreamRuntime::ensure_history_preview` starts a stopped service through the lifecycle gate and preserves an already-running OBS session, including its directory and display window. A later History entry or window resume therefore starts it again after a tray stop or completed Reset. Maintenance still excludes startup. `observeHistoryWindowResume` listens to native window focus while the page is mounted, falling back to browser focus and visibility events outside Tauri or when native registration fails, and refreshes both rows and previews. The Windows close control uses a plain hide-to-tray label on History routes.

`list_history_runs` in `src-tauri/src/history/repo.rs` returns a page of runs with a summary across the full database. The displayed ten-win rate counts completed runs with at least ten victories, independently of the mod's outcome tiers. `createHistoryListWorkflow` in `src/features/history/historyListWorkflow.ts` corrects an out-of-range page before publishing rows, and `useHistoryPage` in `src/features/history/useHistoryPage.ts` binds that selection to the URL. `useRouteScroll` in `src/layouts/useRouteScroll.ts` restores the matching list position on return from details, including when the rows load asynchronously; primary navigation starts at the top.

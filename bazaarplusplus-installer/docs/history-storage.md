# History And Storage

## History Boundary

- `History` in `src-tauri/src/services/history.rs` resolves the selected installation and privately derives database, screenshot, and video paths. It owns list, detail, reveal, video deletion, cleanup preview, and cleanup execution.
- Commands in `src-tauri/src/commands/history.rs` pass ids, limits, offsets, cleanup scopes, and presets. They do not accept storage paths, cutoffs, or precomputed cleanup plans.
- An absent run detail is a successful nullable result. Unavailable installation, unsupported schema, failed reads, and failed actions remain distinct `SemanticProblem` codes.
- `open_probed` in `src-tauri/src/history/queries.rs` opens the mod database with read/write flags and a busy timeout so SQLite can recover a dirty WAL or create shared-memory files. It probes `user_version`, retries selected transient errors, and rejects unsupported schemas without retry.

## Cleanup Safety

- `History::preview_cleanup` and `History::execute_cleanup` derive cutoffs from the same `StorageCleanupPreset` contract in `src-tauri/src/history/cleanup.rs`.
- `PROTECTED_RUN_PREDICATE` in `src-tauri/src/history/cleanup.rs` protects completed non-PTR Ranked runs while upload or terminal-seal state still requires their source data. The same predicate drives planning, skipped counts, screenshot protection, and guarded execution.
- Screenshot cleanup deletes eligible database rows before unlinking files, preserves files still referenced by surviving rows, and protects today's local-date folder from the orphan sweep.
- Run-data cleanup validates the required cascade foreign keys before destructive effects. Each planned run is conditionally deleted in a transaction; files still named by guarded-delete misses are excluded from unlinking.
- `resolve_cleanup_file_path` in `src-tauri/src/history/files.rs` confines cleanup paths to their expected roots.

## Data Ownership

The current root name comes from `BAZAAR_DATA_DIRECTORY` in `src-tauri/src/config.rs`. Reset may delete that current root; uninstall does not. Legacy V4 data remains user-owned. `BundleOutbox/` and `bundle_outbox` remain mod-owned: installer cleanup may consult upload status but never deletes their files or rows. [ADR-005](adr/005-data-ownership-and-reset.md) records that ownership boundary.

Frontend History and Run Detail use the shared page-state and confirmed-operation seams described in [Frontend Architecture](frontend-architecture.md). History reads do not own or start the Stream service; preview availability is a separate capability.

`list_history_runs` in `src-tauri/src/history/repo.rs` returns a page of runs with a summary across the full database. The displayed ten-win rate counts completed runs with at least ten victories, independently of the mod's outcome tiers. `History` in `src/pages/History.tsx` keeps the page in the URL and redirects an out-of-range page after cleanup. `useRouteScroll` in `src/layouts/useRouteScroll.ts` restores the matching list position on return from details, including when the rows load asynchronously; primary navigation starts at the top.

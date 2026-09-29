# Stream Service

## Native Lifecycle

- `StreamRuntime` in `src-tauri/src/stream/runtime.rs` is the single owner of service lifecycle, task handles, and captured installation paths. Its lifecycle mutex serializes ensure, restart, stop, window changes, and exclusive maintenance.
- `StreamRuntime::ensure` and `StreamRuntime::restart` resolve one selected-installation snapshot while holding the lifecycle gate. Window changes reuse the captured record path rather than resolving a different installation mid-session.
- `ProductionServer::start` in `src-tauri/src/stream/server.rs` creates the repository and settings store, binds the loopback service, reports database availability, and serves with graceful shutdown. `origin` in the same file is the one service origin every URL is built from; the service binds that fixed port or fails.
- `StreamRuntime::prepare_history_thumbnails` is the History List's entry to the same lifecycle: it starts a stopped service and never rebinds a running one.
- Startup, tray actions, window-close behavior, and Tauri stream commands call through `StreamRuntime`; they do not mutate the server task directly.
- Stream command failures are classified by capability and operation in `src-tauri/src/commands/stream.rs` and returned as `SemanticProblem`.

## Frontend Capabilities

`DefaultStreamWorkflow` in `src/features/stream/streamWorkflow.ts` keeps service state, polling freshness, display window, crop settings, and one-off actions independent. Repeated poll failures retain the last value but mark it stale; actions that require authoritative running state remain gated until a successful refresh. Problems and notices stay semantic until `src/features/stream/streamProblems.ts` and `src/features/stream/streamPresentation.ts` translate them.

## HTTP Surface

`router` in `src-tauri/src/stream/http.rs` serves the overlay, settings, record and crop APIs, record images, and static assets, and merges the History Thumbnail route. CORS is restricted by `is_allowed_cors_origin` to Tauri origins and the configured local Vite development origins.

All database and image reads run on blocking threads through `run_record_task`, never on the async workers shared with Tauri commands: a SQLite open can wait out the busy timeout while the game holds a lock. The OBS strip route makes one blocking hop into `render_strip` in `src-tauri/src/stream/strip.rs`, which reads the saved crop, resolves the record's screenshot, and crops, caches, and encodes the PNG. A crop in the query is validated before that hop and overrides the saved crop; `preview=true` bypasses the cache.

The History Thumbnail route belongs to `src-tauri/src/stream/history_thumbnails/mod.rs`, which also owns its source registry and URL builder. It always uses the saved crop and caches under one namespace per installation path, named by a digest of that path, beside the OBS strips at the cache root. Both routes share `cached_strip`: writes go through a uniquely named temporary file, and a hit refreshes the entry's modification time. `sweep_cache` runs once per process on a blocking thread when the service first starts. It deletes the legacy per-file `history/` cache tree, orphaned temporary files, and History Thumbnails unused past an age limit, then the oldest beyond a count cap; it skips files modified in the last few seconds, and every deletion is best effort because Windows refuses to delete an open file. The limits are `CACHE_LIMITS` in that module. The OBS strips at the cache root are never swept.

Overlay records carry canonical `hero_id` separately from their display title through `to_overlay_record` in `src-tauri/src/stream/records/mapper.rs`. The settings page owns a small zh/en dictionary because it runs on the service origin; `DefaultStreamWorkflow.openSettings` appends the current app locale when opening it.

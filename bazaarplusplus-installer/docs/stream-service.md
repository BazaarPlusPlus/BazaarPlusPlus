# Stream Service

## Native Lifecycle

- `StreamRuntime` in `src-tauri/src/stream/runtime.rs` is the single owner of service lifecycle, task handles, and captured installation paths. Its lifecycle mutex serializes ensure, restart, stop, window changes, and exclusive maintenance.
- `StreamRuntime::ensure` and `StreamRuntime::restart` resolve one selected-installation snapshot while holding the lifecycle gate. Window changes reuse the captured record path rather than resolving a different installation mid-session.
- `ProductionServer::start` in `src-tauri/src/stream/server.rs` creates the repository and settings store, binds the loopback service, reports database availability, and serves with graceful shutdown.
- Startup, tray actions, window-close behavior, and Tauri stream commands call through `StreamRuntime`; they do not mutate the server task directly.
- Stream command failures are classified by capability and operation in `src-tauri/src/commands/stream.rs` and returned as `SemanticProblem`.

## Frontend Capabilities

`DefaultStreamWorkflow` in `src/features/stream/streamWorkflow.ts` keeps service state, polling freshness, display window, crop settings, and one-off actions independent. Repeated poll failures retain the last value but mark it stale; actions that require authoritative running state remain gated until a successful refresh. Problems and notices stay semantic until `src/features/stream/streamProblems.ts` and `src/features/stream/streamPresentation.ts` translate them.

## HTTP Surface

`router` in `src-tauri/src/stream/http.rs` serves the overlay, settings, record and crop APIs, record images, and static assets. CORS is restricted by `is_allowed_cors_origin` to Tauri origins and the configured local Vite development origins.

All database and image reads run on blocking threads through `run_record_task`, never on the async workers shared with Tauri commands: a SQLite open can wait out the busy timeout while the game holds a lock. Both strip routes, OBS and History, make one blocking hop into `render_strip` in `src-tauri/src/stream/strip.rs`, which reads the saved crop, resolves the screenshot, and crops, caches, and encodes the PNG. A crop in the query is validated before that hop and overrides the saved crop; `preview=true` bypasses the cache.

Overlay records carry canonical `hero_id` separately from their display title through `to_overlay_record` in `src-tauri/src/stream/records/mapper.rs`. The settings page owns a small zh/en dictionary because it runs on the service origin; `DefaultStreamWorkflow.openSettings` appends the current app locale when opening it.

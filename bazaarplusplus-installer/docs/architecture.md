# Architecture

## Runtime Ownership

- The `run` function in `src-tauri/src/lib.rs` is the composition root for native plugins, managed state, the command handler, tray setup, startup warm-up, and platform window events.
- `main_window::restore` in `src-tauri/src/main_window.rs` is the shared restore path for tray activation, second-instance activation, and macOS reopen. Close handling consults `StreamRuntime::snapshot` before deciding whether the main window hides to the tray.
- `SelectedGameInstallationState` in `src-tauri/src/services/selected_game_installation.rs` is process-wide but session-scoped. Install, History, and Stream resolve through it; app restart recreates it empty. `resolve_game_path_core` in `src-tauri/src/services/game_path.rs` owns explicit, selected, startup-detected, then fallback priority.
- `InstallerContextState::get_or_initialize` in `src-tauri/src/services/startup.rs` is the shared cold-start boundary for install detection. Startup warm-up and `detect_for_install` await the same initializer.

## Feature Boundaries

- Install effects belong to the Rust `install` service under `src-tauri/src/services/install/`; frontend orchestration belongs to `DefaultInstallWorkflow` in `src/features/install/installWorkflow.ts`. `plan_install` in `src-tauri/src/services/install/plan.rs` derives effects directly from fresh detection facts; `install_state_from_snapshot` in `src-tauri/src/services/install/mod.rs` derives readiness from an empty plan, keeping presentation and execution on the same rule.
- `History` in `src-tauri/src/services/history.rs` owns history paths, SQLite access, reveals, video deletion, and cleanup. Tauri commands pass domain ids and presets rather than storage paths.
- `createRunDetailWorkflow` in `src/features/history/runDetailWorkflow.ts` owns Run Detail loading, action exclusion, target failures, and detail replacement after video deletion. The current operation token rejects late results after a newer load or a stopped lifecycle; `useRunDetailPage` in `src/features/history/useRunDetailPage.ts` binds the workflow to the current run and React subscription. Deletion confirmation retains its separate target and retry owner.
- `StreamRuntime` in `src-tauri/src/stream/runtime.rs` is the only lifecycle-mutation boundary for the local overlay service.
- Updater phase state belongs to `createUpdaterMachine` in `src/features/about/updater.ts`; modal metadata is derived by `getUpdaterUiContract` in `src/features/about/updaterPresentation.ts`.
- Install, History, and Stream native failures cross IPC as `SemanticProblem` from `src-tauri/src/problem.rs`. Each domain owns its stable codes and parameters; diagnostics remain separate from localized user copy.

## Generated IPC

Rust command signatures are the only IPC schema. `builder` in `src-tauri/src/commands/registry.rs` supplies both the production invoke handler and the binding export command list. `runGenerateBindings` in `scripts/generate-bindings.mjs` validates a temporary export before atomically replacing `src/types/generated/`. Its `--if-stale` flag, used only by `npm run dev` and `npm run check`, skips the cargo export when the committed `commands.ts` is newer than every Rust input; `build`, `test`, `prebuild-check`, and `verify` always regenerate.

`CommandAdapter` in `src/api/commandAdapter.ts` is that schema's TypeScript face; `commandClient` in `src/api/commandClient.ts` picks one implementation at module load — generated native commands or the Browser Preview adapter. Feature modules depend on the typed interface rather than command strings. The rationale and upgrade constraint live in [ADR-004](adr/004-tauri-specta-command-bindings.md).

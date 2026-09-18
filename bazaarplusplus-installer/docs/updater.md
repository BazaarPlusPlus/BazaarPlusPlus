# Updater

## State Machine

- `runCheck` in `src/features/about/updater.ts` returns Browser Preview state outside Tauri, an available update with its native handle, or current-version state.
- `UpdaterSnapshot` and `createUpdaterMachine` in the same module make checking, availability, download, install, restart readiness, restart, and failure mutually exclusive. Only download carries progress.
- The native `Update` handle returned by `check` remains alive until `downloadAndInstall`; retry obtains a fresh handle after one is consumed.
- `updaterProblemFromError` in `src/features/about/updaterProblems.ts` converts native errors into stable operation/version parameters plus optional diagnostics. Localized recovery copy is derived separately.

## Presentation

`getUpdaterUiContract` in `src/features/about/updaterPresentation.ts` derives modal priority, dismissal, title, and action from the machine snapshot. Decisions and recovery are dismissible; download, install, and restart work are blocked because the native API exposes no cancellation contract. `ShellUpdateModal` in `src/layouts/ShellUpdateModal.tsx` is the single modal presentation.

Under the zh locale, an available update may also offer the versioned mainland-China mirror built by `buildMainlandDownloadUrl` in `src/features/about/mainlandDownload.ts`. Automatic installation remains the primary path. [ADR-001](adr/001-in-app-updater.md) records that product choice.

Updater endpoint, public key, artifact creation, and runtime permissions are configured in `src-tauri/tauri.conf.json` and `src-tauri/capabilities/default.json`; those environment files remain authoritative for their literal values.

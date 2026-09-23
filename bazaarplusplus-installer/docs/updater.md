# Updater

## State Machine

- `runCheck` in `src/features/about/updater.ts` returns Browser Preview state outside Tauri, an available update with its native handle, or current-version state.
- `UpdaterSnapshot` and `createUpdaterMachine` in the same module make checking, availability, download, install, restart readiness, restart, and failure mutually exclusive. Only download carries progress.
- The native `Update` handle returned by `check` remains alive until `downloadAndInstall`; retry obtains a fresh handle after one is consumed.
- `updaterProblemFromError` in `src/features/about/updaterProblems.ts` converts native errors into stable operation/version parameters plus optional diagnostics. Localized recovery copy is derived separately.

## Presentation

`getUpdaterUiContract` in `src/features/about/updaterPresentation.ts` derives modal priority, dismissal, title, and action from the machine snapshot. Decisions and recovery are dismissible; download, install, and restart work are blocked because the native API exposes no cancellation contract. `ShellUpdateModal` in `src/layouts/ShellUpdateModal.tsx` is the single modal presentation.

Under the zh locale, the modal may also offer the mainland-China mirror page published in the Release Manifest as `downloads[platform].mainlandUrl`. `runCheck` in `src/features/about/updater.ts` reads it from the update's `rawJson`, the Platform Release Manifest the updater already fetched through the first of `UPDATER_ENDPOINTS` in workspace `../release/downloads.ts` (the lockstep Release Manifest is the second endpoint; the plugin moves on to it after a non-2xx status, a transport error, or a body it cannot parse, and then reports the lockstep version as current), through `decodeMainlandDownloadUrl` in workspace `../release/downloads.ts`, choosing the platform with the same `isWindows` host detection the machine injects; a manifest that publishes no address, or a non-https one, offers no link. `offersMainlandMirror` in `getUpdaterUiContract` decides when: for an available update, while its download runs, and after a failed download or install. It is not offered during install or restart, after a restart failure, or after a check failure, where no version is known. Automatic installation remains the primary path. [ADR-0001](adr/0001-in-app-updater.md) records that product choice.

Updater endpoint, public key, artifact creation, and runtime permissions are configured in `src-tauri/tauri.conf.json` and `src-tauri/capabilities/default.json`; those environment files remain authoritative for their literal values.

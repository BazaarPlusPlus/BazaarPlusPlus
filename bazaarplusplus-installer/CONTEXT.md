# BazaarPlusPlus Installer Context

BazaarPlusPlus Installer is a Tauri 2 desktop app for installing and managing the BazaarPlusPlus mod for *The Bazaar*. React/Vite owns presentation and workflow state; Rust owns native integration, filesystem effects, local history access, the OBS overlay service, and packaging boundaries.

## System Map

- `src/` — frontend pages, framework-neutral workflows, localization, and generated-command adapters.
- `src-tauri/src/` — Tauri commands, installer services, history access, stream runtime, and platform integration.
- `scripts/` and `build.sh` — generated bindings, resource validation, release packaging, signing, and upload.
- `src-tauri/resources/` — bundled mod payload inputs and stream web assets.

## Vocabulary

- **Payload** — files owned or bundled by BPP. Ownership determines what install, repair, and uninstall may remove.
- **Selected game installation** — the session-scoped The Bazaar installation shared by Install, History, and Stream.
- **InstallState** — the native contract supplying detected paths, readiness, warnings, and action gates to the Install workflow.
- **Run Detail** — the view of one history run, its battles, screenshot, and recorded videos.
- **Reset local data** — explicit deletion of the current BPP data root, distinct from uninstall, which preserves user data.
- **Semantic problem** — a stable code plus parameters and an optional diagnostic; frontend copy derives from the code rather than native error text.
- **Stream runtime** — the serialized owner of the local overlay service lifecycle and captured installation state.
- **Generated bindings** — the TypeScript command client exported from Rust command signatures; generated files are replaceable artifacts.

## Topics

Each document states current behavior for one task branch. Open the one whose triggers match the task at hand.

- [Architecture](docs/architecture.md) — runtime ownership, subsystem boundaries, selected-installation state, generated IPC bindings.
- [Frontend architecture](docs/frontend-architecture.md) — command adapters, async page state, modal coordination, confirmation lifecycles, localization boundaries.
- [Install and reset](docs/install-reset.md) — detection, payload ownership, install, repair, uninstall, reset, action gates, the macOS trampoline, Steam LaunchOptions, trampoline bundle signing, vanilla restoration.
- [History and storage](docs/history-storage.md) — SQLite access, run history, screenshots, video deletion, destructive cleanup.
- [Stream service](docs/stream-service.md) — service lifecycle, capability polling, overlay routes, settings, CORS.
- [Updater](docs/updater.md) — update checks, download and install phases, restart recovery, the mainland-China fallback.
- [Release](docs/release.md) — versions, bundled resources, native recorder inputs, platform packaging, artifact signing and notarization, manifests, upload, release verification.
- [Decisions](docs/adr/) — the rationale behind a boundary the task is about to challenge.

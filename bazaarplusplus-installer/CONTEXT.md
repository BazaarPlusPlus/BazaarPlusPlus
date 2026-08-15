# BazaarPlusPlus Installer Context

BazaarPlusPlus Installer is a Tauri 2 desktop app for installing and managing the BazaarPlusPlus mod for *The Bazaar*. React/Vite owns presentation and workflow state; Rust owns native integration, filesystem effects, local history access, the OBS overlay service, and packaging boundaries.

This file is the entry map. Open only the topic that matches the work; architectural rationale lives separately in `docs/adr/`.

## System Map

- `src/` — frontend pages, framework-neutral workflows, localization, and generated-command adapters.
- `src-tauri/src/` — Tauri commands, installer services, history access, stream runtime, and platform integration.
- `scripts/` and `build.sh` — generated bindings, resource validation, release packaging, signing, and upload.
- `src-tauri/resources/` — bundled mod payload inputs and stream web assets.

## Vocabulary

- **Payload** — files owned or bundled by BPP. Ownership determines what install, repair, and uninstall may remove.
- **Selected game installation** — the session-scoped The Bazaar installation shared by Install, History, and Stream.
- **InstallState** — the native contract that supplies detected paths, readiness, warnings, and action gates to the Install workflow.
- **Reset local data** — explicit deletion of the current BPP data root. It is distinct from uninstall, which preserves user data.
- **Semantic problem** — a stable code plus parameters and an optional diagnostic; frontend copy is derived from the code rather than native error text.
- **Stream runtime** — the serialized owner of the local overlay service lifecycle and captured installation state.
- **Generated bindings** — the TypeScript command client exported from Rust command signatures; generated files are replaceable artifacts.

## Topics

- [Architecture](docs/architecture.md) — read for runtime ownership, subsystem boundaries, selected-installation state, or generated IPC bindings.
- [Frontend architecture](docs/frontend-architecture.md) — read for command adapters, async page state, modal coordination, confirmation lifecycles, or localization boundaries.
- [Install and reset](docs/install-reset.md) — read before changing detection, payload ownership, install, repair, uninstall, reset, their action gates, the macOS trampoline, Steam LaunchOptions, bundle signing, or vanilla restoration.
- [History and storage](docs/history-storage.md) — read before changing SQLite access, run history, screenshots, video deletion, or destructive cleanup.
- [Stream service](docs/stream-service.md) — read before changing service lifecycle, polling capability state, overlay routes, settings, or CORS.
- [Updater](docs/updater.md) — read before changing update checks, download/install phases, restart recovery, or the manual mainland-China fallback.
- [Release](docs/release.md) — read before changing versions, resources, native recorder inputs, platform packaging, signing, manifests, upload, or release verification.
- [Decisions](docs/adr/) — read the matching ADR when a task may challenge an existing architecture or product choice.

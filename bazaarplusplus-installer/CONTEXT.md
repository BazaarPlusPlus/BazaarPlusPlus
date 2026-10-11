# BazaarPlusPlus Installer Context

BazaarPlusPlus Installer is a Tauri 2 desktop app for installing and managing the BazaarPlusPlus mod for *The Bazaar*. React/Vite owns presentation and workflow state; Rust owns native integration, filesystem effects, local history access, the OBS overlay service, and packaging boundaries.

## System Map

- `src/` — frontend pages, framework-neutral workflows, localization, and generated-command adapters.
- `src-tauri/src/` — Tauri commands, installer services, history access, stream runtime, and platform integration.
- `scripts/` — generated bindings, resource validation and platform signing/packaging; product orchestration lives in the [workspace release flow](../docs/release.md).
- `src-tauri/resources/` — bundled mod payload inputs and stream web assets.

## Vocabulary

- **Payload**, **Payload Inventory**, **Release Manifest**, **Platform Release Manifest**, **Mainland Mirror** — product-wide terms defined in [the root glossary](../CONTEXT.md).
- **Selected game installation** — the session-scoped The Bazaar installation shared by Install, History, and Stream.
- **InstallState** — the native contract supplying detected paths, readiness, warnings, and action gates to the Install workflow.
- **History List** — the paginated view of local runs, with totals across the complete local history. Its selected page is distinct from the Run Detail view and from thumbnail availability.
- **History Thumbnail** — the cropped strip of a run's screenshot on a History List card, served under a process-local source bound to the installation the page was read from. Distinct from the OBS overlay's strip, which follows the captured stream installation.
- **Run Detail** — the view of one history run, its battles, screenshot, and recorded videos.
- **Data Root**, **Legacy Root** — defined in [the root glossary](../CONTEXT.md). The Install page lists and deletes Legacy Roots one at a time ([Install and reset](docs/install-reset.md#legacy-roots)).
- **Reset local data** — explicit deletion of the current Data Root, distinct from uninstall, which preserves every Data Root.
- **Running-game refusal** — the fail-closed check that blocks an operation touching game-directory files while The Bazaar runs or cannot be confirmed closed; each operation has its own problem code.
- **Data maintenance lock** — the installer-wide lock that serializes History cleanup, Reset, BepInEx reset, and Legacy Root deletion; taken before the stream lifecycle gate.
- **Semantic problem** — a stable code plus parameters and an optional diagnostic; frontend copy derives from the code rather than native error text.
- **Stream runtime** — the serialized owner of the local overlay service lifecycle and captured installation state.
- **Generated bindings** — the TypeScript command client exported from Rust command signatures; generated files are replaceable artifacts.

## Topics

Each document states current behavior for one task branch. Open the one whose triggers match the task at hand.

- [Architecture](docs/architecture.md) — runtime ownership, subsystem boundaries, selected-installation state, generated IPC bindings.
- [Frontend architecture](docs/frontend-architecture.md) — command adapters, async page state, modal coordination, confirmation lifecycles, localization boundaries.
- [Install and reset](docs/install-reset.md) — detection, payload ownership, install, repair, uninstall, reset, Legacy Roots, action gates, the macOS trampoline, Steam LaunchOptions, trampoline bundle signing, vanilla restoration.
- [History and storage](docs/history-storage.md) — SQLite access, run history, screenshots, video deletion, destructive cleanup.
- [Stream service](docs/stream-service.md) — service lifecycle, capability polling, overlay routes, settings, CORS.
- [Updater](docs/updater.md) — update checks, download and install phases, restart recovery, the mainland-China fallback.
- [Release](docs/release.md) — versions, bundled resources, native recorder inputs, platform packaging, artifact signing and notarization, manifests, upload, release verification.
- [Decisions](docs/adr/) — the rationale behind a boundary the task is about to challenge.

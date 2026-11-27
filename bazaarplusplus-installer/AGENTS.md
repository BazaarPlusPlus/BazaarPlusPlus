# AGENTS.md

The Tauri desktop installer. React/Vite owns presentation and workflow state; Rust owns native integration, filesystem effects, local history access, the OBS overlay service, and packaging boundaries. Repo-wide rules are in `../AGENTS.md`, and installer vocabulary lives in `GLOSSARY.md`.

## System Map

- `src/` — frontend pages, framework-neutral workflows, localization, and generated-command adapters.
- `src-tauri/src/` — Tauri commands, installer services, history access, stream runtime, and platform integration.
- `scripts/` — generated bindings, resource validation, and platform signing/packaging; product orchestration lives in the [workspace release flow](../docs/release.md).
- `src-tauri/resources/` — bundled mod payload inputs and stream web assets.

## Topics

Open the topic document whose triggers match the task at hand:

- [Architecture](docs/architecture.md) — runtime ownership, subsystem boundaries, selected-installation state, generated IPC bindings.
- [Frontend architecture](docs/frontend-architecture.md) — command adapters, async page state, modal coordination, confirmation lifecycles, localization boundaries.
- [Install and reset](docs/install-reset.md) — detection, payload ownership, install, repair, uninstall, reset, Legacy Roots, action gates, the macOS trampoline, Steam LaunchOptions, trampoline bundle signing, vanilla restoration.
- [History and storage](docs/history-storage.md) — SQLite access, run history, screenshots, video deletion, destructive cleanup.
- [Stream service](docs/stream-service.md) — service lifecycle, capability polling, overlay routes, settings, CORS.
- [Updater](docs/updater.md) — update checks, download and install phases, restart recovery, the mainland-China fallback.
- [Release](docs/release.md) — versions, bundled resources, native recorder inputs, platform packaging, artifact signing and notarization, manifests, upload, release verification.
- [Decisions](docs/adr/) — the rationale behind a boundary the task is about to challenge.

## Verification

`just installer::check` runs the full source gate, tests included; `installer::test` after it only repeats them. Inside the loop, pick the narrower gate that matches what changed:

- Documentation or these instructions: `bun run docs:check`.
- React or TypeScript: `bun run typecheck`; regenerate bindings with `bun run generate:bindings:if-stale` first when Rust signatures changed. Before committing, `just installer::check-fast` runs what the pre-commit hook runs (format, lint, types, cargo fmt, docs).
- `scripts/`: `bun run vp test run <path>.test.ts` for the colocated test, otherwise run the touched script.
- UI styling, tokens, shared `bpp-*` styles, the brand mark or UI copy: read `../docs/design.md`. Edit design tokens in `../design/` or shared Web UI in `../packages/ui/` (never the generated `../packages/ui/styles/tokens.css`), run `just design::build`, and run `just ui::check` and `just site::check` in the same change.
- Versioning, bundled resources, or Tauri config: `just release::check`, then `just installer::check` (its source prebuild step checks the product projections). Release packaging only: `bun run prebuild-check` also verifies the prepared Payload and fails until `just release::prepare <platform>` has run.
- A platform bundle: `just release::build <platform>`. No other kind of task needs it.

Tests prove a behavior seam, an observable outcome of the boundary under test, through its public interface; mock call order and exact source text are not behavior.

Rewrite goldens (`src/__shell_snapshots__/`, `src-tauri/tests/goldens/`) under `BPP_UPDATE_GOLDENS=1` with `bun run vp test run src/shell.snapshot.test.tsx` for the shell and `bun scripts/generate-bindings.ts --with-rust-tests` for Rust. A bare `cargo test` also needs `TAURI_CONFIG='{"bundle":{"resources":[]}}'`, the empty bundle `scripts/tauri-source-env.ts` supplies.

The Rust gates also run on Linux (cloud sessions; prerequisites in `../docs/development.md`). Code only macOS or Windows calls needs a matching `#[cfg(any(target_os = "macos", target_os = "windows"))]`, or Linux Clippy fails it as unused. A test that makes a file unreadable or unwritable returns early on `crate::test_support::permission_bits_are_bypassed()`, since root ignores permission bits.

## Local workflow

`just installer::dev` serves the frontend alone; anything touching a native command needs the full shell from `just installer::app`.

## Stream service

Stream HTTP handlers run every SQLite or filesystem access through `run_record_task` (`src-tauri/src/stream/http.rs`); `docs/stream-service.md` explains why.

## Documentation

- Re-verify every claim in a `docs/*.md` file you change; `bun run docs:check` checks only cited paths, links, and `AGENTS.md` topic coverage, not prose.
- Keep generated audits and review artifacts under gitignored `tmp/`.

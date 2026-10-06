# AGENTS.md

The Tauri desktop installer. Repo-wide rules are in `../AGENTS.md`.

Read `CONTEXT.md` first; it maps each task to its topic document.

## Verification

`just installer::check` runs the full source gate, tests included; `installer::test` after it only repeats them. Inside the loop, pick the narrower gate that matches what changed:

- Documentation or these instructions: `npm run docs:check`.
- React or TypeScript: `npm run check:ts`; regenerate bindings with `npm run generate:bindings:if-stale` first when Rust signatures changed. Before committing, `just installer::check-fast` runs what the pre-commit hook runs (format, lint, types, cargo fmt, docs).
- `scripts/`: `npx vitest run <path>.test.mjs` for the colocated test (they are Vitest files, not `node --test`), otherwise run the touched script.
- UI styling or tokens: read `../docs/design.md`; a token change also runs `just site::check` in the same change.
- Versioning, bundled resources, or Tauri config: `just release::check`, then `just installer::check` (its source prebuild step checks the product projections). Release packaging only: `npm run prebuild-check` also verifies the prepared Payload and fails until `just release::prepare <platform>` has run.
- A platform bundle: `just release::build <platform>`. No other kind of task needs it.

Tests prove a behavior seam, an observable outcome of the boundary under test, through its public interface; mock call order and exact source text are not behavior.

Rewrite goldens (`src/__shell_snapshots__/`, `src-tauri/tests/goldens/`) under `BPP_UPDATE_GOLDENS=1` with `npx vitest run src/shell.snapshot.test.tsx` for the shell and `npm run generate:bindings:test` for Rust. A bare `cargo test` also needs `TAURI_CONFIG='{"bundle":{"resources":[]}}'`, the empty bundle `scripts/tauri-source-env.mjs` supplies.

## Local workflow

`just installer::dev` serves the frontend alone; anything touching a native command needs the full shell from `just installer::app`.

## Stream service

Stream HTTP handlers run every SQLite or filesystem access through `run_record_task` (`src-tauri/src/stream/http.rs`); `docs/stream-service.md` explains why.

## Documentation

- Re-verify every claim in a `docs/*.md` file you change; `npm run docs:check` checks only cited paths, links, and `CONTEXT.md` coverage, not prose.
- Keep generated audits and review artifacts under gitignored `tmp/`.

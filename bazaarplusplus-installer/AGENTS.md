# AGENTS.md

The Tauri desktop installer. Repo-wide rules (commits, pull requests, docs policy, contracts) are in `../AGENTS.md`.

`CONTEXT.md` is the entry map: read it first, then open only the topic document whose trigger matches the task.

## Verification

`just installer::check` runs the full source gate. Inside the loop, pick the narrower gate that matches what changed:

- Documentation or these instructions: `npm run docs:check`.
- React or TypeScript: `npm run check:ts`; regenerate bindings with `npm run generate:bindings:if-stale` first when Rust signatures changed. Complete the change with `just installer::check`.
- `scripts/`: the colocated `*.test.mjs` when one exists, otherwise run the touched script. Product Release modules and tests live in root `release/` and use `just release::test`.
- Versioning, bundled resources, Tauri config, or release packaging: `npm run prebuild-check` before broader validation.
- A platform bundle: `just release::build <platform>`. No other kind of task needs it.

Tests prove a behavior seam, an observable outcome of the boundary under test, through its public interface; mock call order and exact source text are not behavior.

## Local workflow

`just installer::dev` serves the frontend alone at `http://127.0.0.1:14207/`; anything touching a native command needs the full shell from `just installer::app`.

## Documentation

- `docs/*.md` states current behavior, one file per task branch, and `CONTEXT.md` links every one of them. Re-verify every claim in a current-behavior document you change.
- Keep generated audits and review artifacts under gitignored `tmp/`.

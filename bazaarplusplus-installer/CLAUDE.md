# Agent Instructions

`CONTEXT.md` is the entry map: read it first, then open only the topic document whose trigger matches the task.

## Verification

Pick the gate that matches what changed:

- Documentation or these instructions: `npm run docs:check`.
- React or TypeScript: `npm run check`.
- `scripts/`: the colocated `*.test.mjs` when one exists, otherwise run the touched script.
- Versioning, bundled resources, Tauri config, or release packaging: `npm run prebuild-check` before broader validation.
- A platform bundle: `./build.sh --prod`. No other kind of task needs it.

Tests prove a behavior seam — an observable outcome of the boundary under test — rather than mock sequencing or exact source text.

## Local Workflow

`npm run dev` serves the frontend alone at `http://127.0.0.1:14207/`; anything touching a native command needs the full shell from `npm run tauri dev`.

## Commits And Pull Requests

- Use Conventional Commits: `<type>(<scope>): <description>`.
- End every PR body with a `Release Notes:` section, one blank line, then the notes; use `- N/A` for non-user-facing changes.

## Documentation

- `docs/*.md` states current behavior, one file per task branch. Cite code as a root-relative path plus a symbol name, so an unrelated edit above the symbol cannot silently retarget the citation.
- Give each claim one owner. Environment files own every directly readable script, value, path, and dependency list; docs carry the convention, the reason, and the gotcha those files cannot state.
- Re-verify every claim in a current-behavior document you change.
- `docs/adr/` holds decisions that still constrain work, using Context, Decision, Rejected Alternatives, and Consequences. A decision a later ADR replaces gains a `superseded-by` frontmatter pointer to it.
- `docs/plans/` holds active future work; retired material with lasting value moves to `docs/archive/` with the same `superseded-by` pointer.
- Keep generated audits and review artifacts under gitignored `tmp/`.

## Instruction Maintenance

- Add a repository instruction only when it is non-obvious, actionable, and repeatedly useful. Put feature-specific guidance beside the feature.
- Propose repository instruction changes under a `Suggested CLAUDE.md additions` heading for reviewer selection.

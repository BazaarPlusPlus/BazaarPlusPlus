# Agent Instructions

Start every task with `CONTEXT.md`; its topic pointers define when to open each current-behavior document.

## Verification

- Match verification to the changed behavior.
- Docs-only or instruction-only changes: run `npm run docs:check`.
- React or TypeScript UI changes: usually run `npm run check`.
- `scripts/` changes: run the smallest relevant Vitest file when one exists; otherwise run the touched script when practical.
- Versioning, bundled resources, Tauri config, or release packaging: run `npm run prebuild-check` before broader validation.
- Packaging or release work: run `./build.sh --prod` only when the task requires a platform bundle.
- Tests should prove a behavior seam, not mock sequencing or exact source text.

## Local Workflow

- Frontend-only smoke checks: run `npm run dev` and open `http://127.0.0.1:14207/`.
- Full desktop shell: `npm run tauri dev`.

## Commits And Pull Requests

- Use Conventional Commits: `<type>(<scope>): <description>`.
- End every PR body with a `Release Notes:` section, one blank line, then the notes; use `- N/A` for non-user-facing changes.
- A PR whose `last-verified` value names one of its own commits must use a merge commit so that hash remains reachable.

## Documentation

- `docs/*.md` contains current behavior, split by task branch and cited by root-relative file path plus symbol. Use a line citation only for a single-line literal.
- Give each claim one owner. Environment files are the source for directly readable scripts, values, paths, and dependency lists.
- Re-verify every claim in a changed current-behavior document and update its `last-verified` hash in the same change.
- `docs/adr/` holds decisions that still constrain work, using Context, Decision, Rejected Alternatives, and Consequences.
- `docs/plans/` holds active future work; retired material with lasting value moves to `docs/archive/`, using `status`, `topic`, and `superseded-by` frontmatter.
- Keep generated audits and review artifacts under gitignored `tmp/`.

## Instruction Maintenance

- Add a repository instruction only when it is non-obvious, actionable, and repeatedly useful. Put feature-specific guidance beside the feature.
- Propose repository instruction changes under a `Suggested CLAUDE.md additions` heading for reviewer selection.

# Documentation Contract

## Context

Current behavior, future work, historical material, and architectural rationale once appeared as peers. Agents had to read documents before knowing whether their claims were authoritative. A separate index also cached status already owned by document frontmatter.

The first topic layout solved that ambiguity but placed every current document under an extra `truth/` namespace. With a small topic set, that directory added navigation without adding a lifecycle distinction: current topics can be the default at `docs/*.md`, while decisions, plans, and history remain explicit subdirectories.

That layout originally shipped with a per-document `last-verified` commit stamp, refreshed whenever a claim was re-checked. The stamp is retired; the layout is not.

## Decision

Use one entry map and path-based lifecycle boundaries: `CONTEXT.md` is the task-triggered topic map, current behavior is the default at `docs/*.md`, and decisions, plans, and archived history are explicit subdirectories. ADRs own rationale and consequences rather than mirroring the current implementation.

Current documents carry no frontmatter. Location states lifecycle, and `superseded-by` is the one key worth writing — it points a replaced decision or plan at its replacement.

The operational rules — claim ownership, symbol-level citation form, and re-verification on change — are owned by `CLAUDE.md`, the always-loaded policy file. This record does not restate them.

`npm run docs:check` proves three things and nothing more: every root-relative path cited in backticks resolves to a file, every relative Markdown link resolves, and `CONTEXT.md` links every `docs/*.md` topic. Each catches real staleness from the tree alone, with no bookkeeping to keep in sync. Prose being true is not mechanically checkable and is not claimed.

## Rejected Alternatives

- Keep `docs/truth/`. The current root already has an unambiguous meaning because every other lifecycle has a named subdirectory.
- Add `docs/INDEX.md`. It would compete with `CONTEXT.md` and cache metadata owned elsewhere.
- Put all current behavior in `CONTEXT.md`. Always-loaded detail raises context cost and weakens topic triggers.
- Duplicate the operational rules here. Two authoritative statements of one rule drift; `CLAUDE.md` is the single always-loaded owner.
- Archive pruned current documentation. Git retains deleted mechanics without presenting stale claims as current search results.
- Replace symbols with line ranges. Unrelated insertions can silently retarget an otherwise valid range, and the guard against that — checking cited line numbers against each file's length — never fired, because zero line citations were ever written.
- Keep the `last-verified` commit stamp and its reachability check. It taxed every code pull request with a hash refresh, forced merge commits so a branch-local stamp stayed reachable, and still proved nothing about the prose: a stamp says only that someone claimed to have looked. It also failed on its own terms — at commit `7e49fc8` on `master`, `docs/install-reset.md` carried a stamp that a squash had already made unreachable, so `npm run docs:check` reported a red tree over pure bookkeeping while every claim in the document was accurate.
- Require `status` and `topic` frontmatter on current documents. `status: current` was constant, `topic` restated the filename, and topic uniqueness was machinery guarding a property no reader consults.

## Consequences

Agents start with `CONTEXT.md`, open only the topic whose trigger matches the task, and consult an ADR only when its decision is in question. Re-verifying a changed document is a judgment call the author records in the pull request rather than a value stored in the file, so a documentation update costs one edit and a code pull request costs none. Historical mechanics are recovered from Git when explicitly needed.

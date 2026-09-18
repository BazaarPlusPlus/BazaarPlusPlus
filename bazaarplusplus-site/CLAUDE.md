# CLAUDE.md

Repository guardrails for agents (`AGENTS.md` is a symlink to this file).

Read `docs/ARCHITECTURE.md` before changing routes, URL/history behavior, Hero metrics flow, or module ownership. Read `CONTEXT.md` before naming or changing Hero Analysis, its scope, or dataset coverage semantics.

## Verification

- Code changes run `npm test`, `npm run typecheck`, `npm run lint`, and `npm run format:check`.
- Also run `npm run build` when bundling or code-splitting can change. Deploy only when asked.
- Use the existing dev and preview scripts; both reserve port 3000 with `strictPort`.
- Local Hero Analysis needs no credentials: dev and preview both read the production metrics origin directly over CORS. Point `VITE_METRICS_BASE` elsewhere to use a different origin.

## Project Rules

- Put every user-facing string in `src/content/site-copy.ts`, including labels, aria text, loading states, and shell copy; pass it through the existing locale props.
- Implement cross-module changes in the owner named by `docs/ARCHITECTURE.md`; extend the documented boundary explicitly when no owner fits.
- Use the concepts in `CONTEXT.md`. Add a glossary entry when the domain has a real naming gap.
- Type-aware lint runs on `oxlint-tsgolint`, which pins the TypeScript major. Upgrading `typescript` requires a matching `oxlint-tsgolint`; see `docs/adr/0002-oxc-toolchain.md`.

## Decisions and Work Tracking

- Record architectural decisions in `docs/adr/NNNN-slug.md`, starting at `0001` and never reusing a number. State any conflict with an existing ADR.
- Track plans, feature requests, and bugs as GitHub issues, not repository documents. Read `docs/agents/issue-tracker.md` before creating, fetching, or triaging them. Git history holds completed working context.

## Rule Changes

Keep root rules to cross-cutting guardrails and scope module-specific rules locally. During ordinary work, propose additions under **Suggested rule additions** in the wrap-up; add only patterns that are non-obvious, repeated, and actionable. Direct edits are appropriate when the user requests them or an existing rule needs correction.

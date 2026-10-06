# AGENTS.md

bazaarplusplus.com. Repo-wide rules are in `../AGENTS.md`.

| When you are about to | Read |
|---|---|
| Change routes, URL/history behavior, the Hero metrics flow, or code crossing modules (implement in the named owner; if none fits, extend the boundary in that doc in the same change) | `docs/ARCHITECTURE.md` |
| Change how Hero metrics are fetched locally, or reach for a metrics proxy or credentials | `docs/adr/0001-direct-metrics-origin.md` (dev and preview read the production origin over CORS; no credentials) |
| Change visual styling, tokens, or shared UI components | `../docs/design.md` |
| Name or change Hero Analysis, its scope, or dataset coverage semantics | `CONTEXT.md` |
| Upgrade `typescript` or the oxc toolchain (`typescript` and `oxlint-tsgolint` majors move together) | `docs/adr/0002-oxc-toolchain.md` |

## Verification

- UI, route, or data-flow changes also pass `just site::e2e`; its goldens live under `e2e/__snapshots__/`, and an `ICON_BUDGETS` raise in `e2e/chunks.spec.ts` needs its reason in the PR.
- Stop any dev server on the preview port before `just site::e2e`: locally Playwright reuses whatever already listens there, so it would test the dev server, not the preview build. Screenshot goldens (`*-linux.png`) are checked only on Linux; on macOS they are skipped, so CI is the gate, and a failing run uploads its diffs as the `site-e2e-results` artifact.
- The dev and preview scripts fail rather than move when their port is taken (`strictPort`).
- Deploy only when asked; `README.md` § Deploy owns how. The automatic preview deploy that follows a matching `master` push needs no request.

## Project rules

- Put every user-facing string in `src/content/site-copy.ts`, including labels, aria text, loading states, and shell copy; pass it through the existing locale props.
- Chinese (`zh`) copy in `src/content/site-copy.ts` uses full-width punctuation; `test/site-copy.test.ts` rejects ASCII commas.

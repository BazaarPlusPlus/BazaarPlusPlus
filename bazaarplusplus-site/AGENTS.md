# AGENTS.md

bazaarplusplus.com. Repo-wide rules (commits, pull requests, docs policy, contracts) are in `../AGENTS.md`.

| When you are about to | Read |
|---|---|
| Change routes, URL/history behavior, the Hero metrics flow, or module ownership | `docs/ARCHITECTURE.md` |
| Name or change Hero Analysis, its scope, or dataset coverage semantics | `CONTEXT.md` |
| Upgrade `typescript` or the oxc toolchain | `docs/adr/0002-oxc-toolchain.md` |

## Verification

- Code changes pass `just site-check` and `just site-test`.
- Deploy only when asked.
- The dev and preview scripts both reserve port 3000 with `strictPort`.
- Local Hero Analysis needs no credentials: dev and preview both read the production metrics origin directly over CORS.

## Project rules

- Put every user-facing string in `src/content/site-copy.ts`, including labels, aria text, loading states, and shell copy; pass it through the existing locale props.
- Implement cross-module changes in the owner named by `docs/ARCHITECTURE.md`; extend the documented boundary explicitly when no owner fits.
- Type-aware lint runs on `oxlint-tsgolint`, which pins the TypeScript major: a `typescript` upgrade needs a matching `oxlint-tsgolint`.

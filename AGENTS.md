# AGENTS.md

BazaarPlusPlus monorepo. The mod and the installer ship as one Product Release; the server, analyzer, and site are separate deployments. Each project keeps its own toolchain, lockfiles, `AGENTS.md`, and `CONTEXT.md` glossary. Work inside the project the change touches and follow its `AGENTS.md`; this file holds only what spans projects.

| The change touches | Project |
|---|---|
| In-game features, Harmony patches, uploads from the game | `bazaarplusplus-mod/` |
| Install, repair, update, local history, the OBS overlay | `bazaarplusplus-installer/` |
| Bundle ingest, Ghost discovery, BazaarDB delivery | `bazaarplusplus-server/` |
| Hero and build snapshots computed from Bundles | `bazaarplusplus-analyzer/` |
| bazaarplusplus.com, Hero Analysis, the download page | `bazaarplusplus-site/` |
| Product version, Payload Inventory, release pipeline | root `VERSION`, `release.mjs`, `release/`; read `docs/release.md` |

Cross-project terms (Product Release, Payload, Release Manifest, Bundle) are defined in `CONTEXT.md`.

## Contracts

A contract change lands in one pull request together with every consumer it breaks. Each row names the owning source; consumers read it, they do not restate it.

| Contract | Owner | Consumers |
|---|---|---|
| Bundle V5 binary and manifest | `bazaarplusplus-server/contracts/v5/` | mod writes, server ingests, analyzer reads |
| Run segment inside a Bundle | `bazaarplusplus-mod/docs/contracts/run-payload-v5.md` | server stores it opaque, analyzer decodes it |
| Mod API HTTP routes | `bazaarplusplus-server/docs/api-reference.md` | mod |
| Hero and build snapshots | `bazaarplusplus-analyzer/contracts/v5/` and `bazaarplusplus-analyzer/docs/specs/consumer-data-contract.md` | site Hero Analysis, mod build recommendations |
| Payload Inventory | `release/payload.json` | mod MSBuild, installer packaging and cleanup |
| Release Manifest | `release/manifest.mjs`, shared fixture `release/fixtures/latest.json` | site download page, installer updater, mod update check |

Run `just release::sync` after editing `VERSION` or `release/payload.json`; it regenerates the projections the builds validate against.

## Verification

`just` lists every recipe. Gate one project with `just <project>::check` and `just <project>::test`; gate a contract change with the check and test recipes of the owner and every consumer. `just check` and `just test` cover the whole repo. `just fmt` formats everything. Scope and prerequisites: `docs/development.md`.

## Commits and pull requests

- Commit only when the user asks, after reviewing your own diff: done when you can name every file in the commit and why it is there. Revert formatter-only edits to files outside your change.
- Commit messages and PR titles use Conventional Commits, `<type>(<scope>): <description>`. The scope is the project or feature area; root tooling uses `dev` or `release`.
- End every PR body with a `Release Notes:` line, one blank line, then one bullet per user-visible change starting with Added, Fixed, or Improved. Use `- N/A` when nothing is user-visible.
- `master` is protected. The wrap-up flow is: commit on a branch, `gh pr create`, merge, delete merged branches.

## Changing code

- Run an independent red-team review of a large refactor or design plan before implementing. Keep it review-only, landing `file:line` evidence rather than patches, then send the revised plan back for confirmation.
- A replacement ships only the new version: done when the old implementation is deleted and the project's check and test recipes pass.
- Scope a delete or change to its named target plus the members that fed only that target. Unrelated cleanup goes in its own change.

## Documentation

- Each fact has one owner. A term goes in the project's `CONTEXT.md` (in the root one when several projects use it). Current behavior goes in `docs/*.md`. A decision goes in `docs/adr/`. Plans, feature requests, and bugs go in GitHub issues through `gh`; read `docs/agents/issue-tracker.md` first.
- Config files, scripts, and `--help` output own every value they state. A doc carries only the convention, the reason, or the gotcha they cannot state.
- Name domain concepts with the glossary's terms; a real naming gap gets a `CONTEXT.md` entry in the same change. When your output contradicts an ADR, name the ADR and say why it is worth reopening.
- Cite code by path plus symbol name. Line numbers drift.
- ADRs are numbered `NNNN-slug.md` per project, in sequence. A number is never reused or renumbered. A replaced ADR gets a `superseded-by:` frontmatter pointer; a retired one is deleted and lives on in git history.
- Root `README.md`, `docs/development.md`, and `docs/release.md` are in Chinese, with `README_en.md` as the English mirror.
- Docs checks: installer `npm run docs:check`; mod `bazaarplusplus-mod/tests/Architecture.Tests/DocsHygieneTests.cs` enforces byte budgets and resolving links.

## Instructions

Add an instruction to an `AGENTS.md` only when it is non-obvious, keeps coming up, and is actionable; put it in the narrowest `AGENTS.md` it applies to. During ordinary work, propose it under a **Suggested AGENTS.md additions** heading in the wrap-up. Edit directly when the user asks or an existing instruction is wrong.

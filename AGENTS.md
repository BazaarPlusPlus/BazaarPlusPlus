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
| Bundle V5 binary and manifest | `bazaarplusplus-server/contracts/v5/`, shared goldens in `fixtures/` beneath it | mod writes, server ingests, analyzer reads |
| Run segment inside a Bundle | `bazaarplusplus-mod/docs/contracts/run-payload-v5.md`, shared golden `bazaarplusplus-mod/tests/BundleV5Codec.Tests/fixtures/run-payload-v5.fixture.b64` | server stores it opaque, analyzer decodes it |
| Mod API HTTP routes | `bazaarplusplus-server/docs/api-reference.md` | mod |
| Hero and build snapshots | `bazaarplusplus-analyzer/contracts/v5/`, shared goldens in `fixtures/` beneath it and the hero alias table `hero-aliases.json`, plus `bazaarplusplus-analyzer/docs/specs/consumer-data-contract.md` | site Hero Analysis, mod build recommendations, installer History (hero aliases) |
| Seal eligibility | `BundleQueueStore.SealEligibleRunCondition` in `bazaarplusplus-mod/src/BazaarPlusPlus.Storage/`, shared cases `bazaarplusplus-mod/tests/BundleQueueSqliteStore.Tests/fixtures/bundle-seal-eligibility.json` | mod replay maintenance, installer History cleanup (`PROTECTED_RUN_PREDICATE`) |
| Payload Inventory | `release/payload.json` | mod MSBuild, installer packaging and cleanup |
| Local history database schema (`user_version`) and Data Root name | `RunLogSchema.LocalDatabaseSchemaVersion` and `PathConstants.DataRootDirectoryName`, mirrored as `historyDatabaseUserVersion` and `dataRootDirectoryName` in `bazaarplusplus-mod/src/BazaarPlusPlus.Storage/BazaarPlusPlus.history-database.json`; `release::check` gates the pair (`release/history-database.mjs`): same Data Root on both sides, mod schema newest the installer supports; shared golden `bazaarplusplus-mod/tests/RunLoggingPipeline.Tests/fixtures/history-database-v3.schema.sql` | installer History, Reset, cleanup, and overlay reads (`bazaarplusplus-installer/src-tauri/history-database-compatibility.json`, whose `dataRootDirectoryName` a test pins to `BAZAAR_DATA_DIRECTORY`) |
| Release Manifest and Platform Release Manifest | `release/manifest.mjs`, shared fixtures `release/fixtures/latest.json` and `release/fixtures/latest/` | site download page, installer updater, mod update check |

Root `release/` owns Product Release modules and tests. Imports run one way: installer scripts may import release modules, never the reverse.

Run `just release::sync` after editing `VERSION` or `release/payload.json`; it regenerates the projections the builds validate against.

## Verification

`just` lists every recipe. Gate one project with `just <project>::check` and `just <project>::test`; gate a contract change with the check and test recipes of the owner and every consumer. `just check` and `just test` cover the whole repo; `just release::test` gates the root release modules. `just fmt` formats everything. Goldens fail on any difference in every project; `BPP_UPDATE_GOLDENS=1` rewrites them (analyzer: `just analyzer::golden`), and every rewritten golden must be an intended diff in the PR. Scope and prerequisites: `docs/development.md`.

## Commits and pull requests

- Commit only when the user asks, after reviewing your own diff: done when you can name every file in the commit and why it is there. Revert formatter-only edits to files outside your change.
- Commit messages and PR titles use Conventional Commits, `<type>(<scope>): <description>`. The scope is the project or feature area; root tooling uses `dev` or `release`.
- End every PR body with a `Release Notes:` line, one blank line, then one bullet per user-visible change starting with Added, Fixed, or Improved. Use `- N/A` when nothing is user-visible.
- `master` is protected. The wrap-up flow is: commit on a branch, `gh pr create`, merge, delete merged branches.

## Changing code

- Run an independent red-team review of a large refactor or design plan before implementing. Keep it review-only, landing `file:line` evidence rather than patches, incorporate the findings, and proceed within the user's authorized scope without another confirmation.
- A replacement ships only the new version: done when the old implementation is deleted and the project's check and test recipes pass.
- Scope a delete or change to its named target plus the members that fed only that target. Unrelated cleanup goes in its own change.

## Documentation

- Each fact has one owner. A term goes in the project's `CONTEXT.md` (in the root one when several projects use it). Current behavior goes in `docs/*.md`. A decision goes in `docs/adr/`. Plans, feature requests, and bugs go in GitHub issues (see Agent skills).
- Config files, scripts, and `--help` output own every value they state. A doc carries only the convention, the reason, or the gotcha they cannot state.
- Name domain concepts with the glossary's terms; a real naming gap gets a `CONTEXT.md` entry in the same change. When your output contradicts an ADR, name the ADR and say why it is worth reopening.
- In committed docs and comments, cite code by path plus symbol name; line numbers drift. `file:line` is fine in session evidence and reviews.
- ADRs are numbered `NNNN-slug.md` per project, in sequence. A number is never reused or renumbered. A replaced ADR gets a `superseded-by:` frontmatter pointer; a retired one is deleted and lives on in git history.
- Root `README.md`, `docs/development.md`, and `docs/release.md` are in Chinese. `README_en.md` mirrors `README.md` section for section; edit both in one change.

## Instructions

Add an instruction to an `AGENTS.md` only when it is non-obvious, keeps coming up, and is actionable; put it in the narrowest `AGENTS.md` it applies to. During ordinary work, propose it under a **Suggested AGENTS.md additions** heading in the wrap-up. Edit directly when the user asks or an existing instruction is wrong.

## Agent skills

### Issue tracker

GitHub Issues on this monorepo through `gh`; issues only, not PRs. Read `docs/agents/issue-tracker.md` before you create, read, or triage an issue.

### Triage labels

Each of the five canonical triage roles uses its own name as its label. Read `docs/agents/triage-labels.md` before applying one; most do not exist on the repo yet.

### Domain docs

`docs/agents/domain.md` names which `CONTEXT.md` and `docs/adr/` files to read before you explore an area.

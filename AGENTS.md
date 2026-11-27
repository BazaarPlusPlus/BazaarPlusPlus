# AGENTS.md

BazaarPlusPlus monorepo. The mod, the installer and the Android App ship as one Product Release; the server, analyzer, site, UI documentation site, and QQ bot are separate deployments. Each project keeps its own toolchain, lockfiles, `AGENTS.md`, and `GLOSSARY.md`. Work inside the project the change touches and follow its `AGENTS.md`; this file holds only what spans projects.

| The change touches | Project |
|---|---|
| In-game features, Harmony patches, uploads from the game | `bazaarplusplus-mod/` |
| Install, repair, update, local history, the OBS overlay | `bazaarplusplus-installer/` |
| QQ command handling, account binding, on-demand player reports | `bazaarplusplus-bot/` |
| Bundle ingest, Ghost discovery, BazaarDB delivery | `bazaarplusplus-server/` |
| Hero and build snapshots computed from Bundles | `bazaarplusplus-analyzer/` |
| bazaarplusplus.com, Hero Analysis, the download page | `bazaarplusplus-site/` |
| UI kit documentation at ui.bazaarplusplus.com | `bazaarplusplus-ui-docs/` |
| Shared React 19 + Tailwind 4 UI kit (`@bazaarplusplus/ui`) | `packages/ui/` (`just ui::check`, `just ui::test`, `just ui::pack-check`) |
| Shared Run decision and visit projection (`@bpp/runs`) | `packages/runs/` (`just runs::check`, `just runs::test`) |
| The Android App: loads the installed official game, its launcher, the runtime it carries for LemonLoader | `bazaarplusplus-android/` |
| Product version, Payload Inventory, release pipeline | root `VERSION`, `release/` (CLI `release/cli.ts`); read `docs/release.md` |
| Cross-platform design tokens, brand mark, brand book and writing standard | `design/`; read `docs/design.md` |

Cross-project terms (Product Release, Payload, Release Manifest, Bundle) are defined in `GLOSSARY.md`.

## Contracts

A contract change lands in one pull request together with every consumer it breaks. Each row names the owning source; consumers read it, they do not restate it.

| Contract | Owner | Consumers |
|---|---|---|
| Bundle V6 binary and manifest | `bazaarplusplus-server/contracts/v6/`, shared goldens in `fixtures/` beneath it | mod writes and reads Ghost Bundles, server ingests, analyzer reads, bot reads |
| Run segment inside a Bundle V6 | `bazaarplusplus-mod/docs/contracts/run-payload-v6.md`, shared golden `bazaarplusplus-mod/tests/BundleV6Codec.Tests/fixtures/run-payload-v6.fixture.b64` | server stores it opaque (and serves its byte range as a linked account's Record), analyzer, bot and the site's Run detail page decode it |
| Mod API HTTP routes | `bazaarplusplus-server/docs/api-reference.md` | mod, bot |
| Website account API (`WebApi` `/api/*`) | `handleWebApiRequest` in `bazaarplusplus-server/src/modules/web-account-api.ts` | site (`bazaarplusplus-site/src/features/account/account-api.ts` and `bazaarplusplus-site/worker.ts`) |
| Hero and build snapshots | `bazaarplusplus-analyzer/contracts/v6/`, shared goldens in `fixtures/` beneath it and the hero alias table `hero-aliases.json`, plus `bazaarplusplus-analyzer/docs/specs/consumer-data-contract.md` | site Hero Analysis, mod build recommendations, installer History (hero aliases), the QQ hero daily report (`scripts/qqbot/hero-report.ts`), bot hero report |
| Daily Highlights | `build_highlights` in `bazaarplusplus-analyzer/src/bazaarlens/highlights.py`, shared golden `bazaarplusplus-analyzer/tests/fixtures/pipeline-golden.highlights.json` | the QQ daily report (`scripts/qqbot/hero-report.ts`) |
| Battle snapshot JSON in the local history database | `PvpBattleCardSetDocument` in `bazaarplusplus-mod/src/BazaarPlusPlus.Core/Game/PvpBattles/`, shared golden `bazaarplusplus-mod/tests/BundlePipeline.Tests/fixtures/battle-snapshots.golden.txt` | installer History Key Cards (`FINAL_BOARD_ITEMS`, pinned by `final_board_reads_the_mod_snapshot_shape`), installer Run Detail (player and opponent boards and skills) |
| Local Run Message Stream rows | mod RunMessageStreamWriter, RunLogStore.AppendEvent, and the run_events table in the local history schema, pinned by `bazaarplusplus-mod/tests/BundlePipeline.Tests/fixtures/local-stream-command.json` | mod RunPayloadComposer and installer History |
| Run decision and visit projection | `packages/runs/` (`@bpp/runs`, ADR-0023, ADR-0026) | site Run page and installer History Run detail |
| Seal eligibility | `BundleQueueStore.SealEligibleRunCondition` in `bazaarplusplus-mod/src/BazaarPlusPlus.Storage/`, shared cases `bazaarplusplus-mod/tests/BundleQueueSqliteStore.Tests/fixtures/bundle-seal-eligibility.json` | mod replay maintenance, mod History delete (`BundleQueueStore.ProtectedRunCondition`), installer History cleanup (`PROTECTED_RUN_PREDICATE`) |
| Payload Inventory | `release/payload.json` | mod MSBuild, installer packaging and cleanup |
| Local history database schema (`user_version`) and Data Root name | `RunLogSchema.LocalDatabaseSchemaVersion` and `PathConstants.DataRootDirectoryName`, mirrored as `historyDatabaseUserVersion` and `dataRootDirectoryName` in `bazaarplusplus-mod/src/BazaarPlusPlus.Storage/BazaarPlusPlus.history-database.json`; `release::check` gates the pair (`release/history-database.ts`): same Data Root on both sides, mod schema newest the installer supports; shared schema file `bazaarplusplus-mod/src/BazaarPlusPlus.Storage/history-database-v6.schema.sql` | installer History, Reset, cleanup, and overlay reads (`bazaarplusplus-installer/src-tauri/history-database-compatibility.json`, whose `dataRootDirectoryName` a test pins to `BAZAAR_DATA_DIRECTORY`); Android App staging (`AppFiles.DATA_ROOT`, pinned by `AppFilesTest`) |
| Release Manifest and Platform Release Manifest | `release/manifest.ts` (Android: `buildAndroidFragment`, `ANDROID_RELEASE_FIELDS`, `androidAppVersionCode`, `androidNonManagedPayloadDigest`, `androidCarriedRelease`, `ANDROID_GAME_SIGNERS`, `androidManagedPayloadKey`), shared fixtures `release/fixtures/latest.json`, `release/fixtures/latest/` and `release/fixtures/android-carried-release.json` | site download page, installer updater, mod update check, Android App (`bazaarplusplus-android/app/`), bot status and downloads |
| Game Status | `gameStatus` and `androidGameStatus` in `release/game-status.ts`, `decodeGameStatus` and `decodeAndroidGameStatus` in `release/downloads.ts`, shared fixtures `release/fixtures/game-status.json` and `release/fixtures/android-game-status.json` | site download page, bot status |
| Interop Package layout and interop statement | `buildInteropPackage` in `release/android-app.ts`, `interopStatement` and `signInteropStatement` in `release/android-interop.ts`, shared fixtures `release/fixtures/android-interop/` (statement bytes) | Android App (verifies and loads the zip) |
| Android release certificate | `ANDROID_RELEASE_SIGNER_SHA256` in `release/manifest.ts` | the Android App's embedded release certificate, which a test there pins to this digest |
| Managed Payload Manifest line format | `release/managed-payload.ts`, shared golden `release/fixtures/managed-payload/manifest.txt` | mod Updater and Core staging (`bazaarplusplus-mod/src/BazaarPlusPlus.Updater/Shared/ManagedPayloadManifest.cs`), installer Repair (`bazaarplusplus-installer/src-tauri/src/services/bepinex/managed_payload.rs`), Android App staging |
| Managed Payload CDC chunking and chunk index | `release/managed-payload-chunks.ts`, shared goldens `release/fixtures/managed-payload/cdc-vectors.json` and `release/fixtures/managed-payload/chunks.txt` | mod Core incremental staging (`bazaarplusplus-mod/src/BazaarPlusPlus.Core/Infrastructure/SelfUpdate/`), Android App incremental staging (`bazaarplusplus-android/app/src/main/java/com/bazaarplusplus/android/payload/`) |
| Design tokens, shared component styles and the Web component library | `design/tokens.json` and `packages/ui/` (`@bazaarplusplus/ui`, styles under `packages/ui/styles/`); `just design::check` gates generated `packages/ui/styles/tokens.css`, the Tailwind bridge `packages/ui/styles/theme.css`, the Android resources, the brand-mark and cover art copies and the site hero colours; `packages/ui/test/styles-classes.test.ts` and each product's `bundle-classes` test gate the `bpp-*` classes (ADR-0026) | UI documentation site, installer, and site (all workspace consumers of `@bazaarplusplus/ui` and `@bazaarplusplus/ui/game`), external consumers via GitHub Packages, mod (generated `InGameTheme.g.cs` and `InGameSizes.g.cs`), Android App (generated `res/values/bpp_tokens.xml` and `res/drawable/bpp_mark.xml`; its launcher follows `design/components/Launcher/`), bot report colours, server verification emails (`renderVerificationEmail` imports `tokens.json` and the generated mark PNG `email-mark.g.ts`) |
| Release signing public key | `plugins.updater.pubkey` in `bazaarplusplus-installer/src-tauri/tauri.conf.json` | mod self-update (`ReleaseSigningKey.PublicKeyLine` in `bazaarplusplus-mod/src/BazaarPlusPlus.Updater/Shared/`, pinned by `The_embedded_key_is_the_tauri_updater_public_key`) |
| Card Art index, Card Text and Enchantment FX (TemplateId → art file, English title, localized definition and optional material descriptor) | Rust crate `bazaarplusplus-installer/src-tauri/card-catalog/` (`extract::card_index`, `text::write_card_text`, `fx::Describer`), output byte-identical to the Python it replaced (ADR 0024), shapes pinned by the crate's tests and `tests/fixtures/` | bot `src/data.ts` (art), ui-docs `tools/pin-card-art.ts`, `release/card-art.ts` (`cardArtStale` checks the index version, sources, Enchantment FX and both locale files), the card catalog in `packages/ui/src/game/catalog.tsx` (art, names, Card Text and optional materials; ADR 0025, ADR 0026), read by the site Run detail page through the site worker (`/card-art/*`, immutable art, Card Text and Enchantment FX) and by installer Run Detail from its own export (`src-tauri/src/card_catalog.rs`, served as `bppcard://`) |
| Launch Record | `LaunchRecord.toJson` in `bazaarplusplus-android/app/src/main/java/com/bazaarplusplus/android/launch/`, shared golden `bazaarplusplus-android/contracts/launch-record.v1.json` | mod Android Host and Updater (`LaunchRecord` in `bazaarplusplus-mod/src/BazaarPlusPlus.Updater/Shared/`) |

Root `release/` owns Product Release modules and tests. Imports run one way: installer scripts may import release modules, never the reverse. The same holds for `design/` and `packages/*`: products import them, they never import a product.

Run `just release::sync` after editing `VERSION` or `release/payload.json`; it regenerates the projections the builds validate against.

## Verification

`just` lists every recipe. Gate one project with `just <project>::check` and `just <project>::test`; gate a contract change with the check and test recipes of the owner and every consumer. `just check` and `just test` cover every project; `just release::test` gates the root release modules. `just fmt` formats everything. Goldens fail on any difference in every project; `BPP_UPDATE_GOLDENS=1` rewrites them (analyzer: `just analyzer::golden`), and every rewritten golden must be an intended diff in the PR. Scope and prerequisites: `docs/development.md`.

## Commits and pull requests

- Commit only when the user asks, after reviewing your own diff: done when you can name every file in the commit and why it is there. Revert formatter-only edits to files outside your change.
- Commit messages and PR titles use Conventional Commits, `<type>(<scope>): <description>`. The scope is the project or feature area; root tooling uses `dev` or `release`.
- End every PR body with a `Release Notes:` line, one blank line, then one bullet per user-visible change starting with Added, Fixed, or Improved. Use `- N/A` when nothing is user-visible.
- A PR body names an issue only as `Closes #n` or `Refs #n`. GitHub closes the issue on any close, fix, or resolve word followed by `#n`, whatever the sentence around it says, so a partial PR writes `Refs #n` and lists the remaining items in an issue comment.
- `master` is protected. The wrap-up flow is: commit on a branch, `gh pr create`, merge, delete merged branches.

## Changing code

- Run an independent red-team review of a large refactor or design plan before implementing. Keep it review-only, landing `file:line` evidence rather than patches, incorporate the findings, and proceed within the user's authorized scope without another confirmation.
- A replacement ships only the new version: done when the old implementation is deleted and the project's check and test recipes pass.
- Scope a delete or change to its named target plus the members that fed only that target. Unrelated cleanup goes in its own change.

## Documentation

- Each fact has one owner. A term goes in the project's `GLOSSARY.md` (in the root one when several projects use it). Current behavior goes in `docs/*.md`. A decision goes in `docs/adr/`. Plans, feature requests, and bugs go in GitHub issues (see Agent skills).
- Config files, scripts, and `--help` output own every value they state. A doc carries only the convention, the reason, or the gotcha they cannot state.
- Name domain concepts with the glossary's terms; a real naming gap gets a `GLOSSARY.md` entry in the same change. When your output contradicts an ADR, name the ADR and say why it is worth reopening.
- In committed docs and comments, cite code by path plus symbol name; line numbers drift. `file:line` is fine in session evidence and reviews.
- ADRs are numbered `NNNN-slug.md` per project, in sequence. A number is never reused or renumbered. A replaced ADR gets a `superseded-by:` frontmatter pointer; a retired one is deleted and lives on in git history.
- Root `README.md`, `docs/development.md`, and `docs/release.md` are in Chinese. `README_en.md` mirrors `README.md` section for section; edit both in one change.

## Instructions

Add an instruction to an `AGENTS.md` only when it is non-obvious, keeps coming up, and is actionable; put it in the narrowest `AGENTS.md` it applies to. During ordinary work, propose it under a **Suggested AGENTS.md additions** heading in the wrap-up. Edit directly when the user asks or an existing instruction is wrong.

## Agent skills

### Issue tracker

GitHub Issues on this monorepo through `gh`; issues only, not PRs. Read `docs/agents/issue-tracker.md` before you create, read, or triage an issue.

### Triage labels

Each of the five canonical triage roles uses its own name as its label. Read `docs/agents/triage-labels.md` before applying one.

### Domain docs

`docs/agents/domain.md` names which `GLOSSARY.md` and `docs/adr/` files to read before you explore an area.

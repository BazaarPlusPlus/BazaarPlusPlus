# Self-update

The mod updates its own Managed Payload (root `CONTEXT.md`). Core downloads and stages it, and the Updater swaps it in at the next start. ADR-0014 records why the work is split this way.

## Shape

- **Check.** `MainMenuVersionCheckController` (desktop) reads its platform's Platform Release Manifest through `ReleaseManifestClient`. `MainMenuSelfUpdate` passes the result to `SelfUpdatePolicy` together with `RunningManagedPayload`, which is the version, `RunLogSchema.LocalDatabaseSchemaVersion`, and the installed manifest's digest. Each check logs `self_update.release.checked`.
- **Stage.** `SelfUpdateStaging.Ensure` keeps one staging per version in flight and retries after a failure. `ManagedPayloadStager` downloads into `<Data Root>/Updates/.download-<version>-<guid>/`, verifies the zip and then the extracted files, and renames the directory to `Updates/<version>/`. It logs `self_update.payload.staged` or `self_update.payload.stage_failed` with a `reason_code`.
- **Label.** `MainMenuVersionLabelFormatter` appends the `MainMenuUpdateStatus` the check last published; it owns the label text. The status is process-wide and carries over between main-menu visits.
- **Swap.** `ManagedPayloadSwap` in `src/BazaarPlusPlus.Updater/` runs from `BepInExPatcher.Initialize` or `MelonUpdaterPlugin.OnPreInitialization`. It writes its journal and backups under `Updates/.swap/`. Its log lines read `[BPP][Updater] event=updater.*` and go to the loader's log, not through `BppLog`.

## Build and test

The Updater builds both targets. Each host project references one of them with `ReferenceOutputAssembly=false`, and the generated `Payload.targets` places that build. `tests/Updater.Tests` covers verification and the swap. `tests/SelfUpdate.Tests` stages with Core and then swaps with the Updater.

To check a swap by hand:

1. Build a newer Managed Payload zip with `release/managed-payload.mjs`.
2. Stage it with `ManagedPayloadStager` into the game's Data Root.
3. Start the game. The loader log shows `updater.swap.applied`, or `updater.staged.rejected` with its `reason_code`.

## Gotchas

- A development build has no installed manifest. Core therefore never stages for it, and the Updater compares only versions. An installed manifest that exists but does not parse makes the Updater refuse every staged payload (`installed_manifest_invalid`).
- On Android a swapped `MelonLoader/UserLibs/` file loads one launch late (ADR-0014).

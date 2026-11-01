# ADR-0014: The mod stages a signed Managed Payload, and a loader-stage Updater swaps it in

Status: Accepted

## Context

Until now only the installer updated the mod on desktop, and only a new APK updated it on Android. Most product versions change nothing but BPP's own assemblies, yet each one costs an installer run or an APK reinstall. A loaded assembly cannot be replaced in the process that loaded it, and on Windows the file is locked. The files a release may replace must therefore be swapped before any BPP assembly loads. That happens in a component BPP cannot itself replace in the same way. The plan is in issue #362.

## Decision

1. **What self-updates.** The Managed Payload (root `GLOSSARY.md`): the `managed` and `history-contract` Payload entries of one platform. Every other entry stays under the installer or the APK: the loader, native libraries, `bootstrap`, `sqlite-*`, and the Updater itself. Their sha256 values fold into the fragment's `nonManagedPayloadDigest` (`release/managed-payload.mjs` `desktopNonManagedPayloadDigest`, `release/manifest.mjs` `androidNonManagedPayloadDigest`).
2. **What is published.** Each platform's build writes a zip that holds the Managed Payload Manifest (`manifest.txt`), its minisign signature, and the files under `files/<install path>`. The zip is signed with the Tauri updater key. Its fragment records `managedPayload` with `historyDatabaseUserVersion` and `nonManagedPayloadDigest`. The installed copy keeps the manifest beside the mod as `BazaarPlusPlus.managed-manifest.txt`.
3. **Core only stages.** On the main menu the host reads its own Platform Release Manifest. `SelfUpdatePolicy` stages a newer release only when two facts match the running copy: the history database schema it was compiled with, and the digest its installed manifest records. It compares against the running copy, not the previous release, so a copy that skipped versions still sees every change between. Any other newer release asks for the installer or a new APK. Staging checks the size, sha256, and signature of the zip, extracts only the paths the manifest names, and verifies every file hash. It then moves the result by rename into `<Data Root>/Updates/<version>/`. A copy without an installed manifest, which means a development build, never stages.
4. **The Updater swaps.** `BazaarPlusPlus.Updater` is a BepInEx 5 preloader patcher on desktop (`netstandard2.1`, `BepInEx/patchers/`) and a MelonLoader plugin on Android (`net10.0`, `MelonLoader/Plugins/`). Both run before the mod loads. It references only its loader and the BCL, and carries its own Ed25519 and BLAKE2b verification, so it never loads a file it is about to replace. It verifies the staged directory again, and refuses one that is not newer or not compatible with the installed manifest. It moves each old file aside and the new one in under a write-ahead journal, removes the managed files the new manifest no longer lists, and installs the manifest last. On a failure, or after an interrupted run, it rolls back every step.
5. **The Updater never updates itself.** It is built at a fixed version 1.0.0, deterministic and without a PDB, so unchanged source built by the same compiler gives an unchanged file. A changed Updater changes `nonManagedPayloadDigest`, so it reaches users only through the installer or a new APK. A different SDK feature band can change the bytes from unchanged source (`global.json` rolls forward to the latest feature band), and no gate catches that yet; see Consequences.

## Consequences

- On Android, LemonLoader loads `MelonLoader/UserLibs/` before plugins. A swapped UserLib therefore takes effect one launch after the Mods that ship with it. The pinned serializer versions (ADR-0013) keep that set unchanged across ordinary releases.
- A fragment's `selfUpdatable` is a hint for the download page. On both desktop and Android it compares the release with the promoted one. The mod decides from the running copy alone.
- The installer owns the Updater like any other private Payload file. A BPP patcher alone does not count as a third-party BepInEx patcher.
- An installer that bundles N-1 neither asks to repair a self-updated N nor writes N-1 back (installer [ADR-0009](../../../bazaarplusplus-installer/docs/adr/0009-repair-keeps-self-updated-managed-payload.md)).
- An unintended change to the built Updater, such as one from an SDK feature-band bump on a release runner, changes every release's `nonManagedPayloadDigest`. Every desktop copy would then get ManualUpdate and Android a new APK. Pinning the built Updater's sha256 per target, or failing the upload when it changes without a source change, is a follow-up.
- `SelfUpdateStaging` is a fourth remote-data lifecycle beside the three in ADR-0007, and an exception to its generation gate: one staging run per process outlives the main-menu visit that started it, and publishes only `MainMenuVersionUpdateState`, a process-wide status that holds for whichever visit reads it.

## Guardrails

- Do not give the Updater a BPP reference, a NuGet package, or a version that follows the product. A changed Updater file forces every user through the installer.
- The Managed Payload Manifest format is shared by `release/managed-payload.mjs` and `src/BazaarPlusPlus.Updater/Shared/ManagedPayloadManifest.cs`. The golden `release/fixtures/managed-payload/manifest.txt` binds both.
- Core compiles the Updater's `Shared/` sources rather than referencing the Updater, so both verify with the same code.

## Evidence

- Verification, signatures, and swap: `tests/Updater.Tests/`. The known-answer tests run against the release key and a published signature.
- Stage, then swap, end to end: `tests/SelfUpdate.Tests/`
- Release side: `release/managed-payload.test.mjs`, `release/product-release.test.mjs`

2026-11-01 注：root [ADR 0012](../../../docs/adr/0012-android-app.md) 之后，Android 的 Managed Payload 不再由 APK 携带。BPP 的 Android App 在每次启动前按平台清单暂存它，Updater 照本记录换入，只是 `nonManagedPayloadDigest` 改为和 Android App 写的启动记录比较；mod 在 Android App 里运行时不自己暂存，只提示从 Android App 重新启动。桌面不变。

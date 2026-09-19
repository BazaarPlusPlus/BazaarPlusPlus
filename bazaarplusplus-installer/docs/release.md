# Release

Product versioning, isolated Payload preparation, immutable uploads and complete-release promotion are owned by the [workspace release flow](../../docs/release.md). The installer owns platform packaging and official signing.

## Verification Gates

- `npm run verify -- --source-only` is the clean-checkout source gate. `npm run verify -- --release-platform <macos|windows>` adds real Payload validation for one platform.
- `verificationSteps` in `scripts/checks/verify.mjs` checks formatting, oxlint, the locked Cargo graph, and Clippy, generates bindings while running Rust tests, checks generated drift, type-checks, runs Vitest, builds strict Rust docs, applies the selected prebuild guard, and builds the production frontend.
- `npm run prebuild-check` validates versions, generated bindings, platform configuration, Payload build receipts, archives and native inputs. Missing receipts require preparation, not a bypass.
- `./build.sh --prod` delegates to the product coordinator. Its process lock spans preparation, release verification, compilation, signing, bundling and artifact recording. Source integration tests also require the mod's .NET SDK.

## Reproducible Inputs

- Workspace `VERSION` is the authored product version. `synchronizeVersions` in `scripts/release/version-sync.mjs` projects it into npm, Tauri and Cargo; `assertVersionsAreAligned` rejects drift.
- `RELEASE_PLATFORMS` in `scripts/release/release-platforms.mjs` owns target triples, bundle layout, Tauri overlays, resource ZIPs and updater keys.
- The workspace Payload Inventory drives managed copying, ZIP admission and Rust file ownership. Runtime-only and retired entries cannot ship; dependency ownership does not authorize deleting user data or foreign plugins.
- `ensureNativeRecorderInput` in `scripts/release/native-recorder-input.mjs` reuses current native inputs or invokes the mod-owned producer in isolated staging. `verifyNativeRecorderInput` validates the final artifact inventory before the product coordinator promotes any Payload directories.
- `listPayloadFiles`, `buildZipBuffer` and `writeDeterministicZip` in `scripts/release/payload-zip.mjs` create deterministic unsigned ZIPs and checksum manifests. `validatePayloadZip` enforces staging/archive agreement. The coordinator additionally seals source, Managed, seed and output hashes in the Payload build receipt; regenerating a ZIP cannot make stale provenance current.

## Packaging And Signing

`run_release_prechecks` in `build.sh` requires the active product build lock and runs the platform release verification gate. It never chooses a separate version or prepares a second archive.

On macOS, producer inputs are arm64, deployment target 12.0, system-linked, ABI-complete, loadable and ad-hoc signed. The installer verifies their inventory, signs nested Mach-O code and bundles inside-out with the official identity, repacks the signed copy, and packages/notarizes the outer installer. SourceForBuild remains unsigned provenance; final distribution hashes live in the artifact manifest. See [ADR-006](adr/006-native-replay-recorder-signing.md).

`load_updater_signing_env` and `load_macos_developer_id_env` in `build.sh` own local environment and ignored signing-secret conventions. `assertMacosTrampolineStub` in `scripts/checks/prebuild-check.mjs` independently verifies the trampoline architecture and deployment target.

## Artifact And Upload Contract

`createArtifactManifest` in `scripts/release/artifact-manifest.mjs` is called by the product coordinator only after packaging and source/Git revalidation. It records one installer/updater pair, the sealed Payload receipt, Git identity, sizes, hashes and signature content. Failed builds remove the previous receipt; there is no standalone command to create a receipt for leftover bundles.

`validateArtifactManifest` rejects dirty or changed release inputs, mismatched versions/commits, and modified artifacts before upload. The uploader freezes every file before making any remote write. Platform uploads do not advance latest; the workspace coordinator verifies both platforms at the same version and commit before conditional promotion.

R2 access uses S3 credentials and ETag conditional writes, not Wrangler login state. Credentials, first-release migration, retry behavior and website deployment ordering are specified in the [product release guide](../../docs/release.md).

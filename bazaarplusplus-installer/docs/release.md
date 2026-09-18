# Release

## Verification Gates

- `npm run verify -- --source-only` is the authoritative clean-checkout source gate. `npm run verify -- --release-platform <macos|windows>` adds real release-payload validation for one platform.
- `verificationSteps` in `scripts/checks/verify.mjs` runs the sub-second gates first — formatting, Rust formatting, and the locked Cargo graph — then generates bindings while running the Rust suite once, checks generated drift, type-checks, runs Vitest, runs strict Rust checks, applies the selected prebuild guard, and builds the production frontend. `runVerification` stops on the first failed step.
- `npm run prebuild-check` is the focused guard for version alignment, generated bindings, platform configuration, bundled resources, and pinned native inputs.
- `./build.sh --prod` is the only path that produces a platform bundle, and it runs the release prechecks below before packaging.

## Reproducible Inputs

- `package.json` is the version source. `synchronizeVersions` in `scripts/release/version-sync.mjs` updates package-lock, Tauri, and Cargo versions; `collectVersionSnapshot` and `assertVersionsAreAligned` guard drift.
- `RELEASE_PLATFORMS` in `scripts/release/release-platforms.mjs` is the platform source for target triples, bundle layout, Tauri overlays, resource ZIPs, and updater keys.
- `scripts/release/native-recorder-input.lock.json` records each platform's canonical mod input digest, input-file hashes, producer Git provenance, and exact promoted artifact file/tree inventory. `computePlatformRequirement` hashes the catalog policy plus declared worktree bytes; Git commit and dirty state are provenance only. `ensureNativeRecorderInput` reuses a matching current-platform record or runs the mod-owned native build and locally promotes its verified output. The mod then synchronizes managed files and prepares the resource archive only for that same platform. `verifyNativeRecorderInput` makes release-platform prebuild reject any staged artifact addition, removal, or byte drift (`scripts/release/native-recorder-input.mjs`).
- `listPayloadFiles`, `buildZipBuffer`, and `writeDeterministicZip` in `scripts/release/payload-zip.mjs` create deterministic resource ZIPs and entry manifests. `validatePayloadZip` enforces staging/ZIP agreement and rejects unsafe or retired paths.

## Packaging And Signing

`run_release_prechecks` in `build.sh` synchronizes versions, verifies native inputs, prepares resources, and runs the platform release gate before Tauri packaging.

On macOS, the promoted native inputs are already producer-checked as arm64, deployment target 12.0, system-linked, ABI-complete, loadable, and ad-hoc signed. The installer verifies their manifest inventory, signs nested Mach-O components and bundles inside-out with the official identity, rebuilds the payload ZIP, and then packages and notarizes the outer installer. The ownership decision lives in [ADR-006](adr/006-native-replay-recorder-signing.md). `assertMacosTrampolineStub` in `scripts/checks/prebuild-check.mjs` separately proves the bundled trampoline architecture and deployment target.

## Artifact And Upload Boundary

- `discoverArtifacts` and `createArtifactManifest` in `scripts/release/artifact-manifest.mjs` require exactly one coherent installer/updater pair and record commit, cleanliness, paths, sizes, hashes, and signature content.
- `validateArtifactManifest` rechecks those facts before upload. `upload_release_assets` in `build.sh` consumes only the validated paths and has no directory-scan fallback.
- Platform fragments come from `buildPlatformFragment` in `scripts/release/generate-platform-manifest.mjs`. `generate_latest_manifest` in `build.sh` fetches the canonical fragments and rebuilds `latest.json` through `buildLatestManifest` in `scripts/release/generate-latest-manifest.mjs`.
- R2 access runs through the project-pinned Wrangler executable selected by `wrangler_cli` in `build.sh`.

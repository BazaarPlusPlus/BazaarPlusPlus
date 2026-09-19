# Desktop Native Artifact Publishing

The mod repository owns the macOS and Windows native recorder sources, build recipes, ABI declarations, and producer checks. The sibling installer repository owns the staged binaries and its native-recorder-input lock, which is the manifest consumed by installer prebuild verification.

## Freshness contract

`native/artifacts.json` is the catalog for the desktop artifacts. Each platform entry names its native sources, build scripts, platform policy, native headers, managed `DllImport` consumers, required exports, temporary build-output layout, and installer destination. Every policy field (architecture, deployment target, signing) is hashed into the input digest and recorded in the manifest; what a field additionally enforces is decided by whichever build or verify script reads it. The current-platform input digest is SHA-256 over a versioned canonical stream containing the normalized platform policy plus every declared input path, length, and exact worktree bytes in sorted path order. Consequently, uncommitted input changes invalidate an artifact immediately; unrelated repository changes do not.

Git commit and dirty state are recorded only as producer provenance. They are never compared for freshness.

The installer manifest records, per platform, the canonical input digest and input-file hashes alongside an exact file/tree inventory of the promoted artifact bytes. A platform is fresh only when its input digest matches the current catalog inputs and every staged artifact entry still matches the manifest. A platform record without an input digest remains integrity-checkable but is stale when that platform next publishes.

## Product preparation

`just release::prepare <platform>` runs the workspace product coordinator on the native host. It prepares an isolated installer-shaped tree, ensures native inputs there, fetches and validates seeds, builds managed assemblies into private output directories, and checks assembly versions, native inventory and the unsigned archive before replacing any canonical Payload directory.

The native coordinator computes the catalog input digest, reuses matching artifacts, or invokes the mod-owned build scripts into temporary output. Its local promotion is nested inside product preparation: a later managed build or final verification failure discards the entire temporary tree and leaves the previous canonical Payload unchanged.

The product coordinator switches SourceForBuild, the unsigned archive and native input lock under a recovery journal. Packaging refuses a pending journal; the next preparation restores the previous generation before retrying. Only the selected platform changes. See the [product release guide](../../../docs/release.md) for locking, recovery and two-platform release promotion.

## Producer checks

Both macOS build scripts resolve Clang and the SDK through `xcrun`, pass the SDK explicitly with `-isysroot`, compile arm64 with `-mmacosx-version-min=12.0`, and turn unguarded-availability diagnostics into errors. Post-link checks require arm64, `LC_BUILD_VERSION` platform `MACOS` with `minos 12.0`, reviewed system-only dependencies, the catalog's exact exports, successful local load/symbol resolution, and a valid ad-hoc signature. The Core Audio dylib additionally requires `AudioHardwareCreateProcessTap` and `AudioHardwareDestroyProcessTap` to remain weak imports.

The Windows build emits an x64 DLL through the local Visual Studio/MSVC environment, checks PE architecture, exact catalog exports, reviewed system dependencies, the explicit unsigned producer policy, and runs the native smoke program before promotion.

These are producer checks on the machine running `publish`, not remote compatibility attestations. macOS 12.0 is the fixed binary deployment target; newer API use remains guarded by compiler availability enforcement and the Core Audio weak-import check.

## Signing boundary

Promoted macOS inputs are ad-hoc signed, and promoted Windows inputs are unsigned. The installer verifies those producer states before packaging. Its existing production path remains the release authority: nested macOS Mach-O code and bundles are signed inside-out with Developer ID, then the outer installer is signed and notarized. Those release signatures transform the producer bytes, so the installer artifact manifest continues to own final distribution hashes.

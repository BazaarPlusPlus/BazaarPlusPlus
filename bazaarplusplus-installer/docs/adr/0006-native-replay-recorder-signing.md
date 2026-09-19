# Installer Owns Native Replay Recorder Release Artifacts

## Context

Combat Replay records in-process through platform-native Unity plugins. The installer must preload those artifacts at platform-specific locations and establish one release-signing authority.

## Decision

- The mod repository owns native recorder source and builds.
- The mod catalog defines each platform's native/build/ABI inputs, required exports, policy, temporary build-output layout, and installer destination.
- The installer manifest records a canonical content digest over the selected platform's declared mod worktree inputs and exact hashes/tree shape of the promoted artifacts. Git commit and dirty state are provenance only.
- The mod's `publish` command asks the installer-owned coordinator to reuse a fresh current-platform input or locally build, verify, and transactionally promote it before managed packaging. Managed installer-source synchronization and archive preparation are restricted to that same platform. Promotion requires no remote qualification or attestation service.
- macOS inputs arrive ad hoc signed; the installer signs nested code and bundles with the official release identity before outer notarization.
- Windows producer inputs remain unsigned and use the same local freshness/build/promotion model.
- Platform payloads contain only the native recorder artifacts, not an external encoder fallback.

Current input validation, staging, signing, and packaging are specified in [Release](../release.md).

## Rejected Alternatives

- Keep a process-based encoder fallback. It preserves the high-copy path and adds a runtime executable.
- Silently fall back to software encoding. That hides a performance and capability regression.
- Store officially signed inputs in the producer repository. Release signing remains the installer's responsibility.
- Gate local promotion on a separate oldest-OS runner or authenticated remote attestation. The fixed deployment target, compiler availability diagnostics, binary metadata/import/export/dependency checks, local load/smoke checks, and signing checks are the producer boundary.
- Install render plugins as ordinary managed plugins. Unity must preload them before managed startup.

## Consequences

Every declared native source, build recipe, ABI/header/managed interop declaration, or output policy change invalidates the relevant platform digest. The next publish on that platform rebuilds and promotes locally; unrelated commits do not. Native availability and installer prebuild fail closed on manifest or artifact drift, and upgrade cleanup retains retired names only as tombstones rather than release inputs.

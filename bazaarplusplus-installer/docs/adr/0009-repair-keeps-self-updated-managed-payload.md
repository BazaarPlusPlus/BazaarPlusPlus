# Repair Keeps A Self-Updated Managed Payload

## Context

The mod updates its own Managed Payload (root `CONTEXT.md`, mod [ADR-0014](../../../bazaarplusplus-mod/docs/adr/0014-managed-payload-self-update.md)). The installed copy can therefore be newer than the one in the installer's bundled `BepInEx.zip`. Until now readiness required `BazaarPlusPlus.version` to equal the bundled version, and Repair extracted the whole bundled zip. Such an installer would report a self-updated mod as needing Repair, and Repair would write the older mod back. On the next start the mod would stage the newer release again, and the loop would repeat. [ADR-0002](0002-macos-launch-trampoline.md) defined Repair's final state as whatever the installer bundles.

## Decision

- Readiness requires the installed version to be at least the bundled one (`installed_version_is_current`). Versions compare by their leading major.minor.patch, in the same order as the Updater's `ManagedPayloadVersion`. An installed version that does not parse is not ready.
- Repair keeps the installed Managed Payload when it is newer than the bundled one, built for the same platform, history database schema and `nonManagedPayloadDigest`, and every file its installed Managed Payload Manifest lists is present with the recorded size and SHA-256. The bundled zip still writes every non-managed file: BepInEx, Doorstop, native plugins, SQLite natives and the Updater. Otherwise the bundled zip writes everything, as on a first install. The fallback covers a development build without a manifest, a damaged payload, and a payload released with other non-managed files.
- Repair never touches the Data Root, so a payload the mod has staged under `Updates/<version>/` stays there, and the Updater swaps it in at the next start as usual.
- The installer stays the only writer of native files, bootstrap files, the Updater, the macOS trampoline, and the signature repair of the application bundle.

The release this installer bundles is the floor of the final state; a verified newer Managed Payload already on disk raises it. A newer release that is not yet on disk reaches the user through the installer's own updater ([ADR-0001](0001-in-app-updater.md)), which checks at startup, or through the mod's self-update. A desktop Platform Release Manifest names the installer's own version, so a newer one means a newer installer.

## Rejected Alternatives

- Compare readiness against the Platform Release Manifest fetched at detection. Detection would wait on the network and readiness would flip when offline. A newer release the installer does not bundle is also one Repair cannot write without a download path of its own.
- Let Repair download the newer Managed Payload itself. That duplicates the mod's staging and signature checks in a second language. The mod already stages the payload, and the installer's own update brings a newer bundle.
- Verify the minisign signature of the installed manifest. The Updater checked the signature before it swapped the payload in. The installer already trusts the bytes in the game directory, so it checks only that the files still match their manifest.
- Keep any newer installed payload regardless of `nonManagedPayloadDigest` or schema. The bundled non-managed files would then sit beside managed files that were never released with them.

## Consequences

The installer reads the Managed Payload Manifest line format, so it is a consumer of that contract (root `AGENTS.md`). Repair can leave the mod newer than the installer. Readiness stays a version check, so a newer installed payload whose files no longer match its manifest still reads as ready, and the primary action no longer repairs it. Reset BepInEx followed by Install writes the bundled release again, and the mod's self-update then restores the newer one. A development deploy (`6.0.0.t…dev`) is likewise ready against a bundled `6.0.0.prod`. The installer's own update remains the way to reach a release whose non-managed files or history database schema changed. The macOS acceptance repairs a copied game whose mod has self-updated past the bundled one (`acceptance/repaired-tree.json`).

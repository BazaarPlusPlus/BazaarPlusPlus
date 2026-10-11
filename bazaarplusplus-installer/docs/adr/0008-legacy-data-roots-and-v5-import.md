# Legacy Data Roots And V5 Import

## Context

Root [ADR-0006](../../../docs/adr/0006-v6-data-root.md) moves the mod to `BazaarPlusPlusV6/` and makes the installer responsible for importing V5 history once. Game directories can therefore hold several Data Roots: the current one and Legacy Roots (`BazaarPlusPlus/`, `BazaarPlusPlusV4/`, `BazaarPlusPlusV5/`). ADR-0005 covered only one current root and one ignored V4 root.

## Decision

- Reset, History, cleanup, and the OBS overlay read and write only the current Data Root (`BAZAAR_DATA_DIRECTORY`). Reset never touches a Legacy Root. Uninstall preserves every Data Root.
- Legacy Roots are user-owned. The installer lists those present in the selected game directory with their size, and deletes one only after the user confirms that root. Deletion runs in the native backend through the same path as Reset and returns a typed result. Nothing deletes a Legacy Root automatically, including a successful import.
- Every destructive data operation (Reset, BepInEx reset, Legacy Root deletion, V5 import) refuses while the game is running, using a fail-closed check. A check that cannot tell is a refusal.
- `BundleOutbox/` and `bundle_outbox` remain mod-owned: cleanup may read upload status but never deletes outbox files or rows.
- The V5 import is a one-time installer feature with a fixed home, `src-tauri/src/v5_import/`, deleted before 6.2.0; `release::check` refuses a 6.2.0 version while it exists. It writes only History-Only Runs, never queues an upload, and records completion in a marker inside `BazaarPlusPlusV5/`, so a V6 Reset does not trigger a second import and deleting V5 removes the marker with it. Its inputs, filters, and file placement are implementation detail of that module.

## Rejected Alternatives

- Extend Reset to Legacy Roots. Reset targets current data; widening it silently deletes user-owned history.
- Delete `BazaarPlusPlusV5/` right after import. A wrong import mapping would destroy the user's only copy.
- Keep the import marker in installer state or in the V6 root. Installer reinstalls and Steam library moves lose the former; a V6 Reset would erase the latter and re-import data the user already cleaned up.
- Let installer cleanup reclaim the bundle outbox. That duplicates the mod's queue lifecycle.
- Delete from the frontend, or return generic success text. Native code owns running-game refusal and filesystem effects, and callers need removed, empty, blocked, and partial-failure states kept distinct.

## Consequences

Legacy Roots stay on disk until the user removes them. When import places files by hard link or clone, deleting V5 may reclaim far less space than its listed size, so the UI does not promise a figure. Current Reset and Legacy Root behavior is specified in [Install And Reset](../install-reset.md) and [History And Storage](../history-storage.md).

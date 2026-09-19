# Data Ownership And Reset

## Context

BazaarPlusPlus V5 writes a new data contract under `BazaarPlusPlusV5/`. Existing `BazaarPlusPlusV4/` data may contain history, screenshots, or other user artifacts from earlier mod releases. V5 also introduces `BundleOutbox/` and the `bundle_outbox` table as the mod's durable upload queue.

Resetting BazaarPlusPlus data is destructive but distinct from uninstalling the mod. The product needs an explicit target, honest action availability, and a result that distinguishes deletion from an already-empty target.

## Decision

Legacy `BazaarPlusPlusV4/` data remains owned by the user. The installer does not read, delete, migrate, or prompt about it. Installer Reset targets only the current V5 data root.

`BundleOutbox/` and `bundle_outbox` remain exclusively mod-managed. The installer does not delete outbox files or rows and does not include their bytes in cleanup estimates.

Deletion stays in the native backend. Reset availability is exposed through install state, requires an acknowledged confirmation, blocks dismissal while native work is running, and returns a typed result with the refreshed install state.

Current reset and cleanup behavior is specified in [Install And Reset](../install-reset.md) and [History And Storage](../history-storage.md).

## Rejected Alternatives

- Extend Reset to clean `BazaarPlusPlusV4/`. This would silently broaden a current-data reset into deletion of user-owned legacy artifacts.
- Let installer cleanup reclaim the bundle outbox. That would duplicate the mod's queue lifecycle and could remove upload inputs while the mod still owns their retry or retention state.
- Keep reset always enabled. Availability must reflect the detected target and current operation gates.
- Delete from the frontend. Native code owns service exclusion, running-game refusal, filesystem effects, and error classification.
- Return generic success text. Callers need structured removed-versus-empty state and stable failure semantics.

## Consequences

V4 data can remain on disk until the user removes it independently. The installer does not advertise or automate that removal.

Outbox retention and reclamation changes must be implemented by the mod. Installer cleanup may use outbox row status to decide whether run-owned source data is still needed, but it never mutates the outbox itself.

Future reset work must preserve backend-owned deletion and keep empty, blocked, partial-failure, and successful removal states distinct. Uninstall continues to preserve user data.

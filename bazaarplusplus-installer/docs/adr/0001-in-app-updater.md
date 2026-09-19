# In-App Updater

## Context

Users need a primary update path that preserves download, install, failure, and restart state inside the app. Mainland-China network conditions also justify a localized manual fallback without making every user leave the app.

## Decision

Use the Tauri updater as the primary flow. Keep one native update handle through download and install, present the lifecycle in the shell, and restart through the native process integration. Under the zh locale, an available update may also expose a versioned mainland-China mirror; automatic installation remains the primary action.

The current state and presentation boundaries are specified in [Updater](../updater.md); artifact generation belongs to [Release](../release.md).

## Rejected Alternatives

- Send every user to an external download. That discards integrated progress, recovery, install, and restart state.
- Recreate the update between check and install. Download and install must use the native handle returned by the successful check.
- Make the regional mirror the default. It is a network fallback, not a second release authority.

## Consequences

Updater changes must preserve one coherent state machine and test the shell presentation as well as native integration. Release work must keep versions, updater artifacts, fragments, and signatures aligned.

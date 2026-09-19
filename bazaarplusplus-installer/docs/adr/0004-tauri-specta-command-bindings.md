# Generate Tauri Command Bindings With tauri-specta

## Context

The IPC contract was once repeated across Rust signatures, DTO exports, a command-name artifact, and a handwritten TypeScript map. Command payloads could drift even when the name list remained complete.

## Decision

Make Rust Tauri commands the only IPC schema. One Specta builder registers the production invoke handler and exports typed DTOs plus command functions. The native frontend and Browser Preview implement the generated interface at one adapter boundary.

Generation is mandatory for check, test, build, and prebuild validation. A failed generation preserves the previous artifact and fails the invoking command. Current ownership and generation mechanics are specified in [Architecture](../architecture.md).

## Rejected Alternatives

- Keep a second DTO generator as fallback. Two schemas recreate drift.
- Keep a command-name artifact or handwritten command map. Names cannot verify payload or result shapes.
- Generate DTOs but invoke string commands manually. Command names and payload construction would still be duplicated.
- Branch Preview behavior inside features. Runtime selection belongs at the adapter boundary.

## Consequences

The compatible Specta packages are upgraded deliberately as one set. Generated bindings are replaceable and never hand-edited; every IPC DTO and command must satisfy the generator's metadata requirements.

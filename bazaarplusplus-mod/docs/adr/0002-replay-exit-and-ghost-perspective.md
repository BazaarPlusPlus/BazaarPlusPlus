# ADR-0002: Replay exit is explicit and single-owner

Status: Accepted

## Decision

Nothing exits `ReplayState` on a tick or a timer. `CombatReplayRuntime.TryContinueReplay` is the single programmatic exit, and it runs the same chain a real click on the recap "continue" button does.

## Why

A replay remains in `ReplayState` after playback ends. Video finalization depends on the eventual exit, so a tick-driven auto-exit races whoever is orchestrating the replay and can orphan a recording. Exit timing must stay explicit and owned by one caller.

## Guardrails

- `CombatReplayRuntime.TryContinueReplay` is the only programmatic `ReplayState` exit. It rejects while starting, playing, capturing recap post-roll, or already exiting ([runtime](../../src/BazaarPlusPlus/Game/CombatReplay/CombatReplayRuntime.cs)).
- No other file may reach the native exit; an architecture test pins the single-exit boundary ([test](../../tests/Architecture.Tests/CoreLayeringTests.cs) — `Replay_state_exit_stays_owned_by_the_combat_replay_runtime`).
- Keep replay transport primitive and policy-free: one recording per battle; batching and concatenation stay external. Mid-playback skipping remains out of scope; phase races fail safely and retry on a later snapshot.

## Ghost payload perspective contract

The manifest inside a ghost replay payload always uses the recorder perspective: the challenger occupies the `Player` side. `GhostBattlePayload.PerspectiveVersion` identifies the stored convention: `0` is the legacy local perspective and is migrated automatically when loaded; `1` is the recorder perspective. External delivery must send ghost payloads with `PerspectiveVersion = 1`.

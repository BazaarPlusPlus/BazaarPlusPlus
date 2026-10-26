# ADR-0002: Replay exit is explicit and single-owner

Status: Accepted

## Decision

Nothing exits `ReplayState` on a tick or a timer. There is no programmatic `ReplayState` exit: the native recap continue click and the bootstrapped `Exit()` prefix are the only exit paths.

## Why

A replay remains in `ReplayState` after playback ends. Video finalization depends on the eventual exit, so a tick-driven auto-exit races whoever is orchestrating the replay and can orphan a recording. Exit timing must stay explicit and owned by one caller.

## Guardrails

- Do not add a programmatic `ReplayState` exit. Bootstrapped saved replays leave through the `Exit()` prefix, which routes into `CombatReplayRuntime.TryExitBootstrappedSavedReplayToMenu` ([runtime](../../src/BazaarPlusPlus/Game/CombatReplay/CombatReplayRuntime.cs)), and on the Android host into `AndroidCombatReplayRuntime.TryExitBootstrappedSavedReplayToMenu` ([Android runtime](../../src/BazaarPlusPlus.Android/CombatReplay/AndroidCombatReplayRuntime.cs)). Both are facades over Core's `SavedReplayPlayback.TryExitBootstrapped` ([module](../../src/BazaarPlusPlus.Core/Game/CombatReplay/SavedReplayPlayback.cs)), which decides through `SavedReplayLifecycle`. See ADR-0020.
- Keep replay transport primitive and policy-free: one recording per battle; batching and concatenation stay external. Mid-playback skipping remains out of scope; phase races fail safely and retry on a later snapshot.

## Ghost payload perspective contract

The manifest inside a ghost replay payload always uses the recorder perspective: the challenger occupies the `Player` side. `GhostBattlePayload.PerspectiveVersion` stamps that convention: `1` is the only valid value, and a payload carrying any other value fails `GhostBattlePayloadReader.MatchesIdentity`.

# ADR-0020: Saved Replay Playback in Core, behind a per-host port

Status: Accepted

## Context

`SavedReplayLifecycle` (ADR-0005) already decided a saved replay's start, exit and menu return in Core, but each host carried the protocol that feeds it: the desktop's `CombatReplayRuntime` and the Android host's `AndroidCombatReplayRuntime` each held the active operation, its payload lease, the admission ladders, the start sequence, the failure fold, the state exit, the bootstrapped exit, the menu return and the disposal fold, about 700 lines apiece. The two copies had drifted. `combat_replay.playback.request_rejected` logged at Debug on the desktop and at Info on Android. The Android tick ticked the lifecycle only while a menu return was pending, and the desktop's on every frame. Each host also had its own publish and interruption exception types and its own rollback result. ADR-0003 rejects a unification that only looks tidier; this one removes a duplicated protocol that had already diverged.

## Decision

1. Core's `SavedReplayPlayback` (`src/BazaarPlusPlus.Core/Game/CombatReplay/SavedReplayPlayback.cs`) owns one saved replay's admission, payload lease, start, exit and menu return for both hosts. It implements `ISavedReplayRuntime`, so History talks to it directly, and it drives `SavedReplayLifecycle`, which is now internal to Core.
2. A host supplies `ISavedReplayHost` (`ISavedReplayHost.cs`): its main-thread scope, the clock, the `ReplayState` and lobby reads, whether it records video, History's row probe, the cleanup steps of each exit path in its own order (ADR-0003), the menu-return dispatch, and the notices it turns into its own logs and counters. Each start gets an `ISavedReplayStart` handle from `BeginStart`. Host objects (the Mono or IL2CPP combat sequence, the bootstrap context) cross only inside that handle.
3. The hosts differ only in port behavior. The desktop's `DesktopSavedReplayHost` restores the opening state on every path, records video, and resets the managed recording UI when an operation is accepted or completes. The Android host's `AndroidSavedReplayHost` enters `AndroidMainThread`, never records video, caches History's row probe for five seconds, and emits the `android.replay.*` events from notices.
4. A start that stops with a known reason throws Core's `ReplayPlaybackStartException`, whose reason code the failure fold reads. A lobby-bootstrap rollback returns `ReplayBootstrapRollback`.

## Consequences

- One behavior now holds on both hosts. `request_rejected` logs at Info. The tick always ticks the lifecycle but reads the lobby only while a menu return is pending. Before, a throw from the loader already completed the operation, but a throw from the board-UI clear that follows it escaped and left the operation active with its lease held. Now any throw from `BeginStart` completes the operation as a failed start and frees the lease. A replay that displaces another operation frees the displaced lease at once. After disposal every `ISavedReplayRuntime` member answers without throwing.
- `tests/SavedReplayPlayback.Tests/` pins the protocol once for both hosts, through an in-memory host and start over the real publisher, gate and catalog. It runs in `mod::test-core`.
- The adapters' IL2CPP and Mono calls stay in the hosts and are still verified on a device (ADR-0016). The module runs the Android start's awaits on the caller's `AndroidMainThread` context, so `AndroidHostThreadingRuleTests` also scans the module's compiled types: no `Task.Run`, `WhenAll`, `WhenAny` or `ConfigureAwait`.

## Relationship to other decisions

ADR-0002's exit rule is unchanged: both hosts' `TryExitBootstrappedSavedReplayToMenu` statics are facades over `SavedReplayPlayback.TryExitBootstrapped`. ADR-0003's per-path cleanup order is kept, now as port behavior. ADR-0005's pure core is unchanged; `SavedReplayPlayback` is the layer that translates its decisions. ADR-0012's boundary holds: Core gains no game, Unity or IL2CPP reference.

## Evidence

- Module and port: `src/BazaarPlusPlus.Core/Game/CombatReplay/SavedReplayPlayback.cs`, `src/BazaarPlusPlus.Core/Game/CombatReplay/ISavedReplayHost.cs`
- Adapters: `src/BazaarPlusPlus/Game/CombatReplay/DesktopSavedReplayHost.cs`, `src/BazaarPlusPlus.Android/CombatReplay/AndroidSavedReplayHost.cs`
- Tests: `tests/SavedReplayPlayback.Tests/SavedReplayPlaybackTests.cs`, `tests/AndroidHost.Tests/AndroidHostThreadingRuleTests.cs`

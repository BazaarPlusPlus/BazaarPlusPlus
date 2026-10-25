# ADR-0022: PvP Battle Capture Window in Core, behind a per-host reads port

Status: Accepted

## Context

Each host framed a PvP battle from its own net messages: an opening game sim, the combat sim, and the next game sim. The desktop spread this over `CombatReplayCaptureService`, a sequence matcher with its window and candidate types, `PvpBattleSnapshotCollector`, `PvpBattleManifestFactory` and `PvpReplayPayloadFactory`. The Android host's `AndroidPvpBattleCapture` and `AndroidPvpBattleSnapshots` re-implemented it, with only the live-retry rule (`PvpBattleCaptureRule`) shared. The copies had drifted:

- Android hard-coded the `PVPCombat` opening; the desktop also opened on a monster `Combat`.
- Android read the player's profile name and account at the opening; the desktop read them at the close.
- Android never refreshed the run link at the close.
- Android stored card enums as raw `ToString()` names, outside the card facets (ADR-0015).
- Android read the player stats unguarded, so one throw failed the opening and could leave a stale opened battle that the next sims closed.

The desktop had bugs of its own. A null `Data.Run` threw and lost the battle. A throw while building the capture left the window stuck after its combat sim, logging a failure on every later sim. One unreadable opponent field blanked the whole opponent. ADR-0003 rejects a unification that only looks tidier; this one removes a duplicated protocol that had already diverged.

## Decision

1. Core's `PvpBattleCaptureWindow<TMessage>` (`src/BazaarPlusPlus.Core/Game/PvpBattles/PvpBattleCaptureWindow.cs`) owns one battle's window for both hosts:
   - which run states open it (`PvpOpeningKinds`);
   - when each field is read: the opening's battle and opponent facts at the opening, the player's name and account at the close;
   - the live retry of the player boards and their status and source labels;
   - the outcome, and the run link, refreshed at the close;
   - the order of the Replay Payload's parts.
   It returns one `PvpBattleCapture`: the manifest, the replay payload, and a board summary for the host's log.
2. A host supplies `IPvpBattleReads<TMessage>` (`IPvpBattleReads.cs`). A message is valid only during the `Observe` call that delivered it. The window keeps only the host's opaque `PvpReplayPart` per stage: the desktop defers serialization to the close (`DesktopPvpBattleReads`), and Android serializes each IL2CPP proxy as it arrives (`AndroidPvpBattleReads`).
3. Every read is guarded on its own. A field that throws is null and logs `pvp_battles.participant.unreadable`; a board that throws is Missing and logs `pvp_battles.snapshot.degraded`. `Observe` never throws.
4. Where the hosts differed, the desktop's timing is the shared rule, except where it was a desktop bug. Android passes `PvpOpeningKinds.PvpCombat` only, as it did before.
5. Persistence stays per host (ADR-0003): the desktop's async queue and its rollback in `CombatReplayRuntime`; Android's synchronous saves under the persistence lease in `AndroidPvpBattleCapture`, now through `PvpBattleCatalog`. Both hosts map captured cards through their facet adapter's `Captured*` methods.

## Consequences

- Android now follows the desktop:
  - It reads the player's name and account at the close.
  - It refreshes the run link at the close.
  - It maps cards through `AndroidCardFacets`. Mapped members store the same names as before. A type with no facet fails that board to Missing. An unnamed section or socket stores null. The unnamed size 0 still stores `"0"`.
  - It guards each player and opponent field.
  - The opponent hero falls back to the run's opponent.
  - Whitespace text is null.
  - A throw at the opening no longer leaves a stale battle.
  - Once one replay part fails, it no longer serializes the later parts.
  - Its degraded and unreadable logs become the Core ids above. `android.pvp_battle.recorded`, `lease_busy`, `save_failed` and `android.replay.*` stay.
- The desktop's bugs are fixed:
  - A null run gives a null day and hour, and the battle is still captured.
  - The window resets before the close reads.
  - Opponent fields degrade one by one.
  - The opening's degraded logs carry the battle id.
  - A monster combat serializes no replay parts.
  - A PvP capture without its replay logs `ReplayPartMissing` and still neither latches nor enqueues.
  - The player's name, account and rank also normalize whitespace to null.
- Stored `battle_snapshots` JSON for mapped members, replay bytes, the replay routes and the Bundle goldens are unchanged.
- `tests/PvpBattleCaptureWindow.Tests/` pins the window once for both hosts through a scripted port whose messages expire after their `Observe` call, and runs in `mod::test-core`. `tests/BundlePipeline.Tests/DesktopPvpBattleCaptureTests.cs` drives the window over the real desktop reads and Mono messages. The IL2CPP reads are still verified on a device (ADR-0016).

## Relationship to other decisions

ADR-0020 is the precedent: a Core module owns a protocol both hosts ran, and calls a per-host port. Its state transition (`PvpBattleCaptureWindow.Transition`) is an internal pure function inside that module. The window is not on ADR-0005's pure-core list: it calls the port, the run context and the clock. ADR-0012's boundary holds: Core gains no game, Unity or IL2CPP reference. ADR-0015 is amended for Android's captured cards.

## Evidence

- Module and port: `src/BazaarPlusPlus.Core/Game/PvpBattles/PvpBattleCaptureWindow.cs`, `src/BazaarPlusPlus.Core/Game/PvpBattles/IPvpBattleReads.cs`
- Adapters: `src/BazaarPlusPlus/Game/PvpBattles/DesktopPvpBattleReads.cs`, `src/BazaarPlusPlus.Android/GameInterop/AndroidPvpBattleReads.cs`
- Persistence: `src/BazaarPlusPlus/Game/CombatReplay/CombatReplayRuntime.cs`, `src/BazaarPlusPlus.Android/GameInterop/AndroidPvpBattleCapture.cs`
- Tests: `tests/PvpBattleCaptureWindow.Tests/PvpBattleCaptureWindowTests.cs`, `tests/BundlePipeline.Tests/DesktopPvpBattleCaptureTests.cs`, `tests/AndroidHost.Tests/AndroidCardFacetsCapturedTests.cs`

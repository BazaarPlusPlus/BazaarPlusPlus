# arch-c4: PvP Battle Capture Window on Android (smoke)

- Commit: `b50e1d14a291dde3485542bc5c575fd61d61ee6e` (refactor/arch-c4-pvp-capture-window)
- Plugin: `BazaarPlusPlus.Android.dll` sha256 `2824634561b3b8a5ae69ecea5358ac4e8d1c2a418a7be830694c12eb0a7122b0`
  (matches `android.plugin.loaded` in `bpp.log`)
- Device: AVD bpp_api35 (emulator-5556), game 1.0.12825-production-android-d9601ded, 2026-10-25
- `bpp.log`: the `[BPP]` lines of the MelonLoader log for one session (Data Root path redacted)

## Checks

| Check | Result |
|---|---|
| One PvP fight (day 5, hour 6) to its closing sim: `android.pvp_battle.recorded` has run_id and result set, `replay_saved=true` | pass (`result=loss`) |
| Set fields keep the `status/source/count/opening=N[/live]` format | pass (`Captured/OpeningMessage/9/opening=9`, ...) |
| Exactly three `android.replay.message_serialized` (spawn, combat, despawn) | pass |
| No degraded / unreadable / part_failed storm | pass (only the unrelated 404 warnings of the build corpus and version check) |
| New `battles` row: `combat_kind=PVPCombat`, run link set, `player_name` and `player_account_id` non-null | pass |
| History lists the battle; its replay plays and returns to History (`menu_return_observed decision=complete_confirmed`) | pass |

## Note

A first attempt hit "System UI isn't responding" / "The Bazaar isn't responding" dialogs on the
emulator during a PvE fight while the host load average was ~19; the BPP main-thread tick
(`android.hooks.summary`) kept running throughout. Dismissing the dialogs closed the game. After a
relaunch the run resumed and every check above passed in that session.

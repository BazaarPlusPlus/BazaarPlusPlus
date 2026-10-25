# Architecture deepening c6: device evidence

Device evidence for `refactor(mod): own the live card snapshot read outcome in Core` and `refactor(mod): resolve card spans through BppItemBoardSpan`. The checks come from section 10 of the c6 plan. Both sessions ran on 2026-10-25 on AVD `bpp_api35` (`emulator-5556`, 2400x1080 landscape, 420 dpi) with game `1.0.12825-production-android-d9601ded`. The game state was the same in both: a Vanessa run on Day 1, in the Aila weapon shop, with 5 gold.

## Sessions

| Session | Commit | `BazaarPlusPlus.Android.dll` sha256 | Log |
|---|---|---|---|
| before | `master` `b687ba8` | `5a244260b6b1c2db952cf3bfefe26e01a7c9850278f08ba12a69944ee0b94f05` | `master-b687ba8-bpp.log` |
| after | `refactor/arch-c6-live-snapshot-outcome` `cdb5a0b` | `3b3e8d648f2437bab1175c267ca387c55e5ec83db08ab7d0529faa39119d8348` (Core `f9f8c9274fd15f43b42550814e9409b7b5582f2201e78f746289321559aafc20`) | `c6-cdb5a0b-bpp.log` |

Each session was built with `just mod::android-build`, installed with `just mod::android-deploy` and started with `just mod::android-launch`; then the run was resumed from the main menu. The after-session plugin hash was also checked on the device (`sha256sum` of `MelonLoader/Mods/BazaarPlusPlus.Android.dll`).

The logs keep only `[BPP]` lines, with every `run_id=` line and anything account-like removed.

## Checks

| Check (plan section 10) | Result | Evidence |
|---|---|---|
| Before: record `android.live_build.opened` shop, stash, board and leases | `shop=3 stash=0 board=3 leases=6`, twice | `master-b687ba8-bpp.log` 13:43:12 and 13:43:44; `master-game-state.png`, `master-ten-win-shop.png`, `master-ten-win-board.png` |
| 1. After: same counts, same board order on screen | Pass. `shop=3 stash=0 board=3 leases=6`, twice. The board reads axe (Small), rifle (Medium), hook (Small) in both sessions, the same order as in the game. The cropped panel screenshots are pixel-identical across the sessions (same sha256). | `c6-cdb5a0b-bpp.log` 13:46:32 and 13:46:57; `c6-game-state.png`, `c6-ten-win-shop.png` and `c6-ten-win-board.png`, compared with their `master-*` twins |
| 2. No `android.live_build.live_snapshot.degraded` line for a normal board | Pass. Neither session logs one. | `grep live_snapshot` on both logs finds nothing |
| 3. Close with the close button and with the back key: leases go back to 0, no `logging.render.failed` | Pass in both sessions. The `close_button` and `back_key` closes each log `returned=6 rental_leases=0 rental_balanced=true`. The back key logs `android.back_key.consumed target=live_build`. The final `android.hooks.summary` (13:48:40) shows `live_build_opened=2 live_build_closed=2 live_build_leases=0 card_leases=0 card_balanced=true`. Neither session logs `render.failed`. | `c6-cdb5a0b-bpp.log` 13:46:46, 13:47:02 and 13:48:40; `c6-after-back-close.png` |
| 4. Desktop: open Live Build in a run, with no `live_build_panel.live_snapshot.degraded` | **Not done on a device.** This step had no way to control the desktop, and the check would also have changed the user's installed Steam copy. What covers the desktop path instead: `LiveCardSnapshotReadTests.DegradedLinesKeepEachHostsFormat` pins the exact desktop line, and `just mod::test` and `just mod::check` build the desktop host. | none |

## Limitations

- **The stash was empty in both sessions (`stash=0`).** The rewritten walk's stash branch ran only on an empty list. Dragging an item onto the stash bag in the shop did not move it into the stash.
- **The degraded path did not run.** A normal board cannot make a card misfit. The `invalid_placement` line format (`section=board|stash`, `socket_id=Socket_N|null`, `item_size=Small|Medium|Large`) is pinned only by the Core unit tests.

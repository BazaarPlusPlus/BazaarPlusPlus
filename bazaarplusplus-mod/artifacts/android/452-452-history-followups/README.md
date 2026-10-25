# #452 Android History follow-ups: device smoke (re-check)

- Commit under test: `c53c08b1` (branch `android/452-history-followups`, code from `5e0844f4`).
- Plugin: `BazaarPlusPlus.Android.dll` (Release), sha256 `6b059b16baa13aa56421ac1af437260c3b1b6fa507e98c1e023dae94531ec5b9` (the `android.plugin.loaded` line in `bpp.log` names the same hash).
- Sits beside `../452-history-followups/`, the first smoke of `5e0844f4`; this re-check adds the delete prompt, More and the card inspection at 2560x1600.
- Device: emulator (API 35), 2400x1080; `wm size 2560x1600` for the size checks, then `wm size reset`.

## What it shows

- `bpp.log`: the `[BPP]` lines of this launch only, with every line naming an account, user or token dropped.
- Combat with History open: the PvP opponent confirmed, History opened from the touch menu before the fight began. `android.history.opened reason=touch_menu`, `boards_rendered ... rental_leases=13`, then `android.history.closed reason=combat ... returned=13 rental_leases=0`, with no scene load. The next `android.hooks.summary` reports `card_rented=101 card_returned=101 card_leases=0 card_balanced=true history_leases=0`.
- `delete-prompt.jpg`: Delete on a completed run. The prompt reads "Press Delete again within 5s to remove …" with no MP4 sentence. The run was not deleted.
- At 2560x1600: `history-boards-2560x1600.jpg` (both boards; the battle list with opponent names is cropped out), `inspection-2560x1600.jpg` (card inspection), `skills-2560x1600.jpg` (skills sheet), `more-2560x1600.jpg` (More). The log has the matching `android.history.relayout`, `card_inspected`/`inspection_closed`, `skills_opened`/`skills_closed` and `more_opened`/`more_closed` lines.

## Not covered on the device

- Note draft across a non-combat scene change: no such scene change is reachable by touch inside a run (a combat loads no scene on Android; leaving to the main menu does not count). `AndroidHistoryOverlaysTests` covers it.

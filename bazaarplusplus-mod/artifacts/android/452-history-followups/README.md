# #452 Android History follow-ups: device smoke

- Commit under test: `5e0844f4` (fix(mod): finish the Android History follow-ups)
- Plugin: `BazaarPlusPlus.Android.dll` (Release), sha256 `08a457a031a15c34d300a38bb050a2daa9c1aff28d0b4f1578d83d35f3008cda`
- Device: emulator (API 35), 2400x1080, then `wm size 2560x1600`, reset after.

## What it shows

- `bpp.log`: the `[BPP]` lines only, with every line naming an account, user or token dropped.
  - Combat starting while History is open closes it, with no scene load: `android.history.opened reason=touch_menu`, `android.history.boards_rendered ... rental_leases=16`, then `android.history.closed reason=combat ... returned=16`.
  - The cards balance afterwards: the later `android.hooks.summary` reports `card_rented=40 card_returned=40 card_leases=0 card_balanced=true history_leases=0`.
- `history-runs-2560x1600.jpg`: History at 2560x1600, both boards shown. Opponent names in the battle list are masked.
- `history-skills-2560x1600.jpg`: the skills sheet at 2560x1600.

## Not covered on the device

- Delete prompt without MP4 (Core lane test `TestDeletePromptMentionsMp4OnlyWhereTheHostRecords`): Delete is disabled for an in-progress run.
- Note draft across a non-combat scene change (`AndroidHistoryOverlaysTests`): reaching it needs an offline Resume.
- Real Ghost import: moved to #465.

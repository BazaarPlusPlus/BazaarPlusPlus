# arch-c5: smoke of the touch view rebuild through TouchViewSlot

Branch `refactor/arch-c5-touch-view-rebuild` at `76b28a2d`, code commit `d9e252e3`. Built with `just mod::android-build`, deployed with `just mod::android-deploy`, launched with `just mod::android-launch` on AVD `bpp_api35` (emulator-5556, 2400x1080 landscape), game `1.0.12825-production-android-d9601ded`. Plugin sha256 (`android.plugin.loaded`): `9c156177f8d9d5d560fb8a46d9dc2ee199328332c4cb389481fc265cc257b63a`. This differs from the sha in `../452-touch-view-rebuild/` for the same commit because it was built in another worktree path. That directory holds the earlier smoke of the same checks.

`smoke-d9e252e3-bpp.log` holds only `[BPP]` lines, 19:06 to 19:09. Smoke level only: no screenshots, because nothing user-visible changed. No device can make a build throw, so the failure path rests on `TouchViewSlotTests` and `AndroidTouchViewBuildGuardTests`.

| Check | Result | Evidence |
|---|---|---|
| History relayout with a favorite's note draft open keeps the draft | Met | `wm size 900x2000` then `wm size reset`: three `android.history.relayout … note_draft_kept=true note_length=7`. The third relayout is expected, because the size switch also shifts the safe-area inset. No `android.history.layout_failed`, and the panel stayed open with the draft shown |
| Closing History brings the touch launcher back | Met | `android.history.closed reason=close_button returned=15 rental_leases=0`, and the BPP launcher button was visible on the main menu afterwards |
| Collection's first build (the constructor, now through the slot) lays out, opens and closes | Met | `android.collection.laid_out screen=2400x1080@420\|136,0,2264,1080 …`, then `android.collection.opened`, then `android.collection.closed reason=close_button returned=18 rental_balanced=true` |

The first Collection open built the catalog on the main thread (`catalog_built map_ms=35884`). The one `android.portrait.load_failed` line ("main-thread wait did not complete within 30 s") comes from that blocked main thread, not from this change.

# 452-touch-view-rebuild: smoke of the shared touch view rebuild rule

Branch `refactor/arch-c5-touch-view-rebuild`, code commit `d9e252e3` ("rebuild every touch panel's view through one failed-build rule"). Built with `just mod::android-build`, deployed with `just mod::android-deploy`, launched with `just mod::android-launch` on AVD `bpp_api35` (emulator-5556, 2400x1080 landscape), game `1.0.12825-production-android-d9601ded`. Plugin sha256 (`android.plugin.loaded`): `e66f8ab9f2dcc8eb7775d1d60d1ad9184549a8eee50cfd6ba82583cb2a8f3231`.

`smoke-d9e252e3-bpp.log` holds only `[BPP]` lines, 18:56 to 19:01. This is a smoke check only. There are no screenshots because nothing user-visible changed. No device can make a build throw, so the failure path rests on `TouchViewSlotTests` and `AndroidTouchViewBuildGuardTests`.

| Check | Result | Evidence |
|---|---|---|
| History relayout with a note draft open keeps the draft, no failure | Met | `wm size 900x2000` then `wm size reset`: three `android.history.relayout … note_draft_kept=true note_length=7`; no `android.history.layout_failed`; the panel stayed open (`history_open_sessions=1 history_relayouts=3`, `card_balanced=true`) |
| Closing History brings the touch launcher back | Met | `android.history.closed reason=close_button returned=15 rental_leases=0`, then the launcher's `android.ui.panel_opened page=menu reason=launcher` |
| Collection's first build (the constructor, now through the slot) lays out and opens | Met | `android.collection.laid_out screen=2400x1080@420\|136,0,2264,1080 …` then `android.collection.opened` |
| Collection relayout while open | Met | `android.collection.relaid_out … open=true` for each screen, then `android.collection.closed reason=back_key returned=18 rental_balanced=true`; no `relayout_failed` |

The first Collection open built the catalog on the main thread (`catalog_built map_ms=59901`). The first `wm size` switch landed while no frame ran, so it logged nothing. The relayout evidence comes from the second open, with the catalog cached. The one `android.portrait.load_failed` line comes from that blocked main thread, not from this change.

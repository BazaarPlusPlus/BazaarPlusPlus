# #425 Android panels fit the phone: device evidence

Emulator `bpp_api35` (API 35, 420 dpi), game `1.0.12825-production-android-d9601ded`, BPP 6.0.0 from branch `android/425-mobile-fit` deployed as a `build_only` MelonLoader Payload. Two screens: the native 2400x1080 (safe area x 136..2264, a cutout on each side) and `adb shell wm size 2560x1600` (safe area 0,0,2560,1509, the top 91 px unsafe), reset afterwards with `wm size reset`. The conventions are in `docs/architecture/android-mobile-ui.md`, the decision in ADR-0018.

## Redaction

Screenshots are halved. None shows the main menu or its account name: menu and launcher shots are cropped to the BPP button or the menu, and History shots show no other player's name. Logs hold only `[BPP]` lines; any line matching an account, user or player id, a token, or a player name is removed (one per log, the `live_build_panel.corpus.degraded` warning, whose exception text matched the filter). No database is committed: this change touches no stored data.

## Criteria

| Criterion | Screens | Log lines |
|---|---|---|
| Collection grid columns from width | `2400x1080/b-collection-items.png`, `g-collection-skills.png`; `2560x1600/zc-collection.png` | `run1`: `collection.laid_out ... units_per_dp=2.625 ... item_columns=8 skill_columns=6 target_dp=48 body_sp=12`; `run5`: `units_per_dp=1.772 ... item_columns=9`; `run3`: `filtered command=tab tab=skills visible=132 columns=6` |
| Collection filter controls (48 dp, whole labels) | `c-collection-search.png`, `i-rail-widened-chips.png`, `j-rail-end-note.png`, `zg-collection-chipspan.png` ("The Dragons" spans two columns) | `run2`/`run6`: `collection.filtered command=search` |
| Collection tap-to-inspect | `d-inspect-chips.png`, `e-inspect-retarget-scrolled.png`, `h-inspect-skill.png`, `zh-collection-inspect-chips.png`, `zi-collection-inspect-text.png` (card text under the chips); `2560x1600/zd-inspect.png` | `run3`: `inspected ... chip_columns=3`, `inspection_retargeted`; `run5`: `chip_columns=4`; `run6`: `inspected ... tier=diamond ... chip_columns=3` |
| Touch menu and BPP button (safe area, 48 dp, clear of the game's HUD) | `a-launcher.png` (main menu, between the scenery and the MENU button), `a-menu.png`, `y-inrun-launcher.png`; `2560x1600/za-inrun-launcher.png`, `zb-menu.png` (in a run, below the day and hour dial and left of the hero portrait at both ratios) | `run8`: `ui.touch_layout screen=2400x1080@420|136,0,2264,1080 launcher=147,731,168x126` and `panel_opened page=menu ... row_height=147 min_target=126` (2400); `screen=2560x1600@420|0,0,2560,1509 launcher=7,751,113x85`, `row_height=99 min_target=85` (2560, 85 units = 48 dp), then `panel_closed reason=back_key` |
| Enabled and disabled | `o-settings-enabled.png`, `p-settings-two-disabled.png`, `q-menu-history-only.png`, `s-settings-touch-off.png`, `r-launcher-hidden.png`; `2560x1600/zj-settings-two-disabled.png`, `zk-menu-history-only.png` | `run3`: `settings.changed ... CollectionPanel value=false`, `LiveBuildPanel value=false`, `panel_opened ... entries=History`, `TouchMenu value=false`, then all three `true`; `run7`: the same toggles at 2560x1600, `panel_opened ... entries=History menu=545,457,638x227 row_height=99`, then `entries=History,Collection,LiveBuild` |
| History (Runs, Ghost, inspection) | `k-history-runs-boards.png`, `l-history-ghost-before.png`, `t-history-ghost-message-wrapped.png` (board message band grows to fit), `m-history-inspection.png`; `2560x1600/ze-history-boards.png`, `zl-history-filters-scrolled.png` | `history.view_created ... target_dp=48 body_sp=12` at both screens (`run3`, `run5`) |
| Ten-Win Builds | `n-livebuild-menu.png`, `z-livebuild-pick-tags.png`; `2560x1600/zf-livebuild.png` ("Pick" tag sits whole under a one-socket card) | `live_build.opened ... units_per_dp=1.772` (`run5`), `candidate_toggled` |
| Settings rows | `o-`, `p-`, `s-settings-*.png` | `settings.touch_targets ... min_hit_px=126 min_option_px=126` (2400); `run7`: `screen=2560x1600 ... min_hit_px=127 min_option_px=126 target_px=126 hit_overlaps=0` |
| Upgrade and enchant previews | `u-upgrade-preview-pearl.png`, `v-upgrade-preview-hook.png`, `w-enchant-preview-pearl.png`, `x-enchant-preview-scrolled.png` | `run4`: `upgrade_values.leased tier=bronze next_tier=silver`; `enchant_preview.rendered ... font_px=27 effect_percent=100 effect_sp=10.3` |
| Back key closes the top-most surface first | `f-back-closes-inspection-first.png` | `run3`: `collection.inspection_closed reason=back_key`, then `collection.closed reason=back_key`; `history_inspection` then `history`; `touch_menu`; `live_build`; `run5`/`run6` the same chain at 2560 and after the last build; `run7`: `history.closed reason=back_key ... rental_leases=0` and `touch_menu` at 2560 |
| Drag scroll with inertia | `e-inspect-retarget-scrolled.png`, `j-rail-end-note.png` | `collection.scrolled list=rail distance_dp=554.4 ... coast_frames=96 inertia=true`, `list=grid ... coast_frames=83`, `list=inspect`; `live_build.scrolled`; `run7`: `history.scrolled list=filters distance_dp=368.8 ... inertia=true` at 2560 |
| Card rentals balanced | | every `collection.closed`, `live_build.closed` with `rental_leases=0 rental_balanced=true`; `history.closed ... rental_leases=0` |
| Relayout on a screen change | `2560x1600/*` | `run8`: `ui.touch_layout screen=2560x1600@420|0,0,2560,1509 launcher=7,751,113x85`, and back to `2400x1080@420|136,0,2264,1080 launcher=147,731,168x126` after `wm size reset` |

## Logs

| Log | Screen | What it shows |
|---|---|---|
| `logs/run1-2400.log` | 2400x1080 | menu, Collection layout, rail and grid drag scroll, inspection |
| `logs/run2-2400.log` | 2400x1080 | Collection search |
| `logs/run3-2400.log` | 2400x1080 | inspection retarget and back chain, skills tab, History back chain, Ten-Win Builds, settings disabled and enabled, menu back key |
| `logs/run4-2400.log` | 2400x1080, in a run | History, upgrade preview, enchant preview (Upgrade Preview off for the capture, then restored), Ten-Win Builds scroll and back key |
| `logs/run5-2560.log` | 2400x1080 then 2560x1600, in a run | relayout, menu, Collection, History, Ten-Win Builds at 2560x1600 |
| `logs/run6-2400.log` | 2400x1080 | final build: chip span, inspection columns and card text, back chain |
| `logs/run7-2560.log` | 2400x1080 then 2560x1600 | the same process as `run6` continued: settings rows, panels disabled and enabled, History drag scroll and back key, menu back key |
| `logs/run8-2560.log` | 2400x1080 then 2560x1600, main menu then in a run | the BPP button moved below the in-run dial (`TouchLauncherLayout.LauncherCenterShare`): main-menu launcher and menu, Resume, in-run launcher at both ratios, menu at 2560x1600 closed by the back key. `run1`–`run7` predate the move, so their `launcher=` values are the old, centered position |

## Caveats

- The enchant preview renders at 10.3 sp on this emulator, under 12 sp. It is capped at the game's own passive text (27 px here, `TouchMetrics.ReadablePercent` never goes above 100%), so it is as large as the tooltip it sits in; raising it above the native text would break the native look.
- The Ten-Win Builds inspection could not be opened on the device: the remote corpus refresh fails (the device log's `live_build_panel.corpus.degraded reason_code=remote_refresh_failed` warning, left out of the committed logs by the redaction filter), the embedded corpus has one build, and it matches no Vanessa candidate. Its layout is the shared `TouchInspectLayout`, covered by `tests/SettingsDockCore.Tests/TouchChromeTests.cs`.
- History's filter strip clips its last chip at the right edge on purpose: it drag-scrolls horizontally (`zl-history-filters-scrolled.png`). The archive list did not scroll on the device because it holds one Run; the battle rail holds two.
- The upgrade and enchant previews were captured at 2400x1080 only. Both render inside the game's own tooltip; the enchant preview needs a pedestal choice, which the run on the device does not offer at 2560x1600.
- An enlarged skill's art bleeds slightly around its ring frame (`h-inspect-skill.png`); that is the game's prefab at that scale.
- Only `a-launcher.png`, `a-menu.png`, `y-inrun-launcher.png`, `za-inrun-launcher.png` and `zb-menu.png` were recaptured after the BPP button moved below the in-run dial (`run8`). Any other shot that shows the button predates the move and has it at the left edge's vertical center; what those shots evidence (menu entries, settings, the hidden button) does not depend on where it sits.
- The button's band was checked on the device on the main menu and the in-run encounter screen at both ratios. Combat and the end-of-run screens were not reached in this pass; `TouchLauncherLayoutTests` pins the measured bands.

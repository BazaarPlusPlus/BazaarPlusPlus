# #397 Android Ten-Win Builds (Live Build) panel: acceptance on the AVD

AVD `bpp_api35` (emulator-5556, 2400×1080 at 420 dpi, a 136 px cutout on the left edge), game
`1.0.12825-production-android-d9601ded`, BPP 6.0.0 Release build from branch
`android/397-livebuild-panel`. The panel is `src/BazaarPlusPlus.Android/LiveBuild/`, its layout is
Core's `LiveBuildTouchLayout` over `TouchMetrics`.

Screenshots are halved. The full-screen panel and in-run screens show no account name; the two
main-menu shots are cropped to the touch menu. `logs/sessionN.log` is the `[BPP]` log of each game
session, filtered to `android.live_build.*`, `live_build_panel.*`, `android.back_key.*`,
`android.card_rental.released`, `android.settings.changed`, `android.scene.loaded`,
`android.ui.panel_*`, `android.ui.mounted`, `android.plugin.loaded`, the `BackKeyPatches` installs,
and the session's last `android.hooks.summary` (with the `live_build_*` and `back_key_*` counters).

## The corpus

`https://cdn.bazaarplusplus.com/metrics/v6/builds/latest.json` answers 404: the analyzer has not
published a first `builds/latest.json` yet, so desktop and Android both run on the embedded seed,
whose one Vanessa build uses placeholder template ids and can never match. Session 1 shows that
state as it ships. For sessions 2–4, `make_test_corpus.py` wrote a schema-2 corpus of three Vanessa
builds from the real template ids of this run's board (read from the local history database), and
it was pushed into the catalog's cache file (`BazaarPlusPlusV6/builds.json`), which the catalog
reads on start (`corpus.ready source=cache build_count=3`). The file was removed from the device
afterwards. The stats in those screenshots are made up.

## What was exercised

| Step | Evidence | Result |
|---|---|---|
| Catalog on start, seed only | session 1 `corpus.degraded reason_code=embedded_fallback`, then `reason_code=remote_refresh_failed` (404) | the seed stands in; `03`, `04` show `updated 10w ago · 1 builds` |
| Back-key hooks | `android.patch.installed ... BackKeyPatches+HandleEscapePatch`, `+OpenSettingsPatch` | both install |
| Open in the main menu, no run | `live_build.opened source=touch_menu hero=vanessa shop=0 stash=0 board=0`, `03-main-menu-no-run.png` | the remembered hero, empty boards |
| Back key on the panel | `live_build.closed reason=back_key ... rental_balanced=true`, `back_key.consumed source=handle_escape target=live_build` | closes the panel, the game's escape menu stays shut |
| Open in a run (Vanessa, day 5) | `live_build.opened shop=1 stash=8 board=8 ... leases=17`, `04-in-run-seed-corpus.png` | 17 cards rented from the live boards |
| Drag scroll with inertia | `live_build.scrolled from=43 to=754 max=813`, `05-drag-scrolled-to-board.png` | one fling carries the list to the board row |
| List edges | `06-stash-row-clipped-by-bands.png` | cards scrolled past the list's top hide under the pinned row |
| Candidate on the seed | `candidate_toggled candidates=1 matches_state=no_recommendation`, `07` | Core's no-match guidance |
| Refresh | `refresh.failed reason_code=remote_request_failed`, `live_build.refresh_completed succeeded=false outcome=failed effect=status fetched_on_main_thread=false completed_on_main_thread=true` | the fetch ends on a pool thread and the result is completed on the main thread |
| Close button | `live_build.closed reason=close_button returned=17 rental_leases=0 rental_balanced=true` | every lease back |
| Setting off in a run | `card_rental.released reason=switched_off ... card_rented=17 card_returned=17 card_balanced=true`, `settings.changed key=LiveBuildPanel value=false`, `15-setting-off-in-run.png` | the tab disappears; `01` is the same menu with it on |
| Back key on the touch menu | `ui.panel_closed reason=back_key`, `back_key.consumed target=touch_menu` | |
| Relaunch with the setting off | `02-touch-menu-setting-off-after-relaunch.png` | the switch persists; no tab |
| Setting on, test corpus | session 2 `settings.changed value=true`, `corpus.ready source=cache build_count=3` | |
| Pick candidates on the board | `candidate_toggled candidates=1 ... recommendations=2 final_build=8`, `candidates=2 ... recommendations=1`, `08-two-candidates-one-match.png` | the pinned build, Match tags on the build, Candidate tags on the board, the four metric cells |
| Browse | `live_build.browsed index=1 count=2`, `09-browse-2-of-2.png` | |
| Inspect a build card | session 4 `live_build.inspected source=FinalBuild size=medium tier=diamond scale=1.748`, `10-inspect-build-card.png` | the card enlarged with its name and text |
| Back key: inspection, then panel, then the game | session 4 `inspection_closed reason=back_key`, `live_build.closed reason=back_key returned=25 rental_balanced=true`, `back_key.passed`, `14-back-key-passes-to-game.png` | the third press reaches the game's escape menu |
| Another ratio | session 3 `live_build.relayout from=2400x1080@420\|136,0,2264,1080 to=2560x1600@420\|145,0,2415,1600 units_per_dp=1.772 card_scale=0.425 rental_leases=25`, `11`, `12` | the sheet re-lays itself out with its leases kept; no overflow at either size |
| Back to 2400×1080 | `relayout ... to=2400x1080@420`, `13-back-to-2400x1080.png` | |
| Scene change | session 4 `scene.loaded name=MainMenuScene`, `card_rental.released reason=scene_changed ... card_rented=26 card_returned=26 card_lost=0 card_balanced=true` | Exit to Main Menu from the game's escape menu, then Resume |
| Counters | session 4 last summary: `live_build_opened=1 live_build_closed=1 live_build_leases=0 back_key_presses=3 back_key_consumed=2 back_key_passed=1` | |

Sessions 2 and 3 ran the build before the inspection showed the card's text; session 4 ran the
final build (`plugin.loaded sha256=f1b808da…`). The change between them touches only the
inspection's text and its panel's opacity.

## Not exercised

- A scene change while the panel is open: the sheet covers the screen, so no touch reaches the
  game under it. `AndroidTouchUi.OnSceneLoaded` closes the panel before the rental release, the
  path the closed-panel scene change took.
- A device that reports no `Screen.dpi`: the 400 dpi fallback is covered by
  `LiveBuildTouchLayoutTests` only.

Game state left: in Vanessa's run, day 5, on the board with a reward to pick.

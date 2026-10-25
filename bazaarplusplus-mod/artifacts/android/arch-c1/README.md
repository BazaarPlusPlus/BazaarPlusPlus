# arch-c1 evidence: the Core Overlay Panel Host on Android

Branch `refactor/arch-c1-overlay-panel-host` at `ece38f61` ("run every Main Overlay Panel on both hosts through one Core Overlay Panel Host", ADR-0021).
Device: AVD `bpp_api35` (emulator-5556, 2400x1080 landscape), game `1.0.12825-production-android-d9601ded`, BPP `6.0.0`.
Run: Vanessa, Normal, days 1 to 4. Logs hold only `[BPP]` lines, with the MelonLoader `[BazaarPlusPlus]` prefix removed. Screenshots are cropped to the panel or board; History's run list, its PvP list and the main menu show player names and are cut out.

## Rebased build (smoke, 2026-10-25)

The branch was rebased onto `origin/master` (#457, #458, #459) as `17f6cb7a`. #459 gave Collection two back steps before the panel: the inspected card animates closed, then an expanded rail picker collapses. On the rebased branch these are Collection's modal to the Overlay Panel Host (`HasModalBackStep`, `DismissModalBackStep`). Logs 1 to 4 below come from the pre-rebase build `ece38f61`. The checks they prove are combat close and gate, superseding, switch-off, History's back order, scene change and replay return, and the rebase does not change that code. `logs/5-rebased-14753521a933.log` is a smoke run of the rebased build only. Its plugin sha256 is `14753521a933b718c31e8d3b674b7887f967fe76cfe1c619c3ca18668cbdb3f0`, from `just mod::android-build` at `17f6cb7a`. The game was at the main menu and the run was not resumed.

| Check | Result | Evidence |
|---|---|---|
| Back with an inspection open closes it through the animation | Met | 18:15:49 `collection.inspected … animated=true`; 18:16:07 `back_key.consumed target=collection.modal` then `collection.inspection_closed reason=back_key` |
| Back with the hero picker expanded collapses it and leaves the panel open | Met | 18:16:28 `back_key.consumed target=collection.modal`, with no `collection.closed`; screens `rebased-collection-picker-expanded.png` and `rebased-collection-picker-collapsed-by-back.png` (rail crops) |
| The next back closes the panel | Met | 18:16:38 `collection.closed reason=back_key returned=18 rental_balanced=true` then `back_key.consumed target=collection` |
| Balance and no host errors | Met | last summary `card_rented=19 card_returned=19 card_lost=0 card_balanced=true back_key_presses=3 back_key_consumed=3`; no `directive.failed`, `request.reentrant`, `predicate.failed` or `combat_probe.degraded` lines |

## Builds and sessions

| Log | Build | Plugin sha256 (`android.plugin.loaded`) | Window |
|---|---|---|---|
| `logs/1-branch-357480032326.log` | branch `ece38f61`, `just mod::android-build` | `357480032326e239e0adfdf1775da273cac4edcece866db4f95e8d27b2311eca` | 15:26 to 16:39 |
| `logs/2-probe-d9dee437e45b-a.log` | probe on `ece38f61` | `d9dee437e45bb6cb83c84bf54132c03a4a60308f2ad492a39117571a7991ee49` | 16:39 to 16:52; the emulator crashed at 16:52 (see Notes) |
| `logs/3-probe-d9dee437e45b-b.log` | probe on `ece38f61`, after a cold boot | `d9dee437e45b…` (same) | 17:07 to 17:10 |
| `logs/4-branch-redeploy-357480032326.log` | branch `ece38f61`, rebuilt and redeployed | `357480032326…` (same as log 1: the build is deterministic) | 17:10, load to main menu, no probe |

**Probe.** The probe was never committed; `probe/C1Probe.cs.txt` is its whole source plus the two `AndroidMod.cs` hook lines. It polls `<Data Root>/probe-c1` once a second, deletes the file, and runs one command through the same delegates the settings dock calls: `open_history` is the dock's `OpenHistory: () => _historyPanel?.Open("settings")`, and `collection_off|on` and `live_build_off|on` write the `ISetting<bool>.Value` the dock's rows write. It exists because no touch path opens the dock or a second panel while a panel is open: every sheet covers the BPP launcher and the game's gear. Its `android.probe.*` lines exist only in logs 2 and 3. Its `load_scene` command (log 2, 16:41:21) called the navigation menu's `SceneLoader.LoadScene(HeroSelectScene, …)` from the main menu and loaded nothing, so the scene-change check used the player's path instead. After log 3 the probe was deleted, the arm file is absent from the Data Root, and the branch build was redeployed (log 4).

## Checks

| Check | Result | Evidence |
|---|---|---|
| Combat close, History | Met: `reason=combat` | log 1, 16:10:51 `history.closed reason=combat returned=15` right after `pvp_battle.recorded`; screen `history-open-before-pvp.png` is the panel open in the shop before the fight |
| Combat close, Ten-Win Builds | Met: `reason=combat` | log 1, 16:22:09 `live_build.closed reason=combat returned=7 rental_balanced=true` |
| Combat close, Collection | Met: `reason=combat` | log 1, 16:36:29 `collection.opened … day=4` then 16:36:30 `collection.closed reason=combat returned=32 rental_balanced=true` (monster fight) |
| Combat close records `combat`, not `scene_changed` | Met | no fight on Android loads a scene: every combat close above is `combat`, and no `scene.loaded` falls between the open and the close |
| Combat gate | Met: all three panels | log 1, `touch_menu.open_skipped panel=collection reason_code=combat_active` 16:04:37; `panel=live_build` 16:04:46 and 16:17:51; `history.open_skipped reason_code=combat_active` 16:11:08 and 16:26:04; screen `combat-gate-history-pvp.png` is the fight the History tap was refused in |
| Mutual exclusion, Ten-Win Builds then History | Met: `superseded` | log 2, 16:40:56 `live_build.opened`, 16:41:03 `probe.command cmd=open_history` → `live_build.closed reason=superseded` → `history.opened reason=settings`; screen `history-opened-superseding-livebuild.png` |
| Mutual exclusion, Collection then History | Met: `superseded` | log 3, 17:08:22 `collection.opened`, 17:08:37 `probe.command cmd=open_history` → `collection.closed reason=superseded returned=32 rental_balanced=true` → `history.opened reason=settings`; screen `history-opened-superseding-collection.png` |
| Switch-off, panel closed | Met | log 1, Collection closed by the back key at 15:37:57, then the dock's Collection row off: 15:38:43 `card_rental.released reason=switched_off destroyed_now=30`; screen `dock-collection-off.png` |
| Switch-off, Collection open | Met | log 3, 17:08:55 `probe.command cmd=collection_off` → `collection.closed reason=switched_off returned=32` → `card_rental.released reason=switched_off destroyed_now=34 card_balanced=true` |
| Switch-off, Ten-Win Builds open | Met | log 3, in the run with 12 cards on its sheet (`live_build.opened … stash=3 board=9 leases=12`, 17:09:50): 17:09:56 `live_build.closed reason=switched_off returned=12` → `card_rental.released reason=switched_off destroyed_now=12 card_balanced=true`. The main-menu attempt at 17:09:18 had nothing rented (`leases=0`), so it logged the close and no `card_rental.released`: `ReleaseAll` logs only when it releases or destroys something |
| Switch-off, History's More then Turn Off | Met | log 1, 15:45:51 `history.closed reason=switched_off returned=15` → `card_rental.released reason=switched_off destroyed_now=15`; screen `history-more-turn-off.png` |
| Back key order | Met | log 1: 15:29:47 the touch menu first (`back_key.consumed target=touch_menu`); 15:30:03 with nothing of ours open the key passes to the game (`back_key.passed`); 15:43:34 History's note modal before the panel (`target=history.modal`), then 15:43:42 the panel (`target=history`); Collection 15:37:57 `target=collection` |
| Scene change, non-combat load keeps History open and re-renders | Met | log 2: Resume from the main menu, History opened during the run's `LoadingScene` (16:51:27, `history.opened reason=touch_menu`); 16:51:39 `scene.loaded name=Gameplay` → `card_rental.released reason=scene_changed destroyed_now=13` and no `history.closed`; the next summaries go `history_opened=2 history_leases=13 history_board_renders=2` (16:51:37) → `history_board_renders=3`, leases still 13 (16:51:53); closed by the back key at 16:52:05. Screen `history-open-after-scene-load.png` |
| Scene change into combat closes History with `scene_changed` | Not reachable on Android | a fight starts inside `Gameplay` with no scene load, so this path is covered by `tests/AndroidHost.Tests/AndroidOverlayPanelsTests.cs` and `tests/OverlayPanelLifecycle.Tests/`, not on the device |
| Replay return | Met | log 1: 15:48:31 `history.closed reason=replay_started return_after_replay=true`; the first start failed (`combat_replay.playback.failed … start_exception`, a 30 s main-thread wait timeout on this emulator) and History came back anyway at 15:49:50 `history.opened reason=replay_returned resume_selection=true section=favorites`; second try 15:50:26 `replay_started`, playback 15:51:41, exit 15:59:35, 15:59:44 `history.opened reason=replay_returned resume_selection=true section=favorites`. Screen `history-reopened-after-replay.png` |
| Card balance | Met for every session end | last `hooks.summary` of each log: `card_lost=0 card_leases=0 card_balanced=true` (log 1 `card_rented=558 card_returned=558`; log 3 `89/89`) |
| End of a run | Not reached | `run_ended_hits=0` in every log; the run is still on day 4, so the balance above is at session end, not at a run's end |
| No storm of `overlay_panels.directive.failed`, `overlay_panels.request.reentrant`, `combat_probe.degraded` | Met | zero lines of any of the three in all four logs |

Other screens: `collection-open-main-menu.png` is Collection in the main menu (log 3, 17:08:22) before the superseding History open.

## Notes

- `[WARNING]` and `[ERROR]` lines in the logs are unrelated to the host: `lobby.version_check.degraded` (Android is not released, so the Platform Release Manifest is not published) and `live_build_panel.corpus.degraded` (the emulator's network timed out on the remote build library; the embedded corpus served). The one `combat_replay.playback.failed` is the 30 s main-thread wait above.
- The first Collection open of log 1 spent `map_ms=271511` in `collection.catalog_built` (15:37:33) on a cold emulator; log 3's first open took `map_ms=5250`. The stall is in building the catalog, before the host is involved.
- The emulator process crashed at about 16:52 while Collection was opening in log 2 (its own log ends with an adb protocol fault and a Netsim ping timeout); log 2's last `[BPP]` lines are ordinary summaries with `card_balanced=true`. It was cold-booted with no snapshot and no data wiped.
- Emulator touch drags are unreliable: a card drag sometimes leaves a hover state and the encounter does not start. Fights were started by a select tap, then a drag to the board gap row, with the panel taps 0.4 s after the drag.
- Doc observation, not changed here: `docs/architecture/android-mobile-ui.md` "Off switch" says a feature's switch must be reachable while its surface is open. On the device only History meets it (its More dialog). Collection and Ten-Win Builds cover the BPP launcher and the gear, so their switch, and the second-panel open that would supersede them, has no touch path while they are open. That is why the probe was needed for those checks.

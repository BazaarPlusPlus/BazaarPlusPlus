# #398 Android History: the Ghost section and replay, acceptance on the AVD

AVD `bpp_api35` (emulator-5556, 2400×1080 at 420 dpi, safe area x 136–2264), game
`1.0.12825-production-android-d9601ded`, BPP 6.0.0 Release build of commit `45c522b` on branch
`android/398-399-history-finish`. History is `src/BazaarPlusPlus.Android/Ui/AndroidHistoryPanel*`, the
replay is `src/BazaarPlusPlus.Android/CombatReplay/`.

Every session below ran that one build. Commits after `45c522b` change only this evidence.

| Session | Log | `android.plugin.loaded sha256` | Commit | What it covers |
|---|---|---|---|---|
| 1 | `logs/1-enabled-history-ghost-replay.log` | `8b0739c9c5ac02ac6275e49060612ef6302eead0b3c417a52fb4f8cd63858c0b` | `45c522b` | run A and run B, the Ghost section and its filters, three Ghost downloads and replays, the return to History, Favorites, a relayout with a note draft open, More, the off switch from More and from settings during a replay, an offline Resume with History open across the game's scene change, a network cut during a replay, History in a run |
| 2 | `logs/2-patchmode-disabled.log` | `8b0739c9c5ac02ac6275e49060612ef6302eead0b3c417a52fb4f8cd63858c0b` | `45c522b` | `[Android] PatchMode = Disabled` |
| 3 | `logs/3-patchmode-enabled-again.log` | `8b0739c9c5ac02ac6275e49060612ef6302eead0b3c417a52fb4f8cd63858c0b` | `45c522b` | `PatchMode = Enabled` again |
| 4 | `logs/4-seeded-history.log` | `8b0739c9c5ac02ac6275e49060612ef6302eead0b3c417a52fb4f8cd63858c0b` | `45c522b` | the #399 seeded rows; here only for the local saved replay and its return |

`logs/ghost-bundle-double-http.log` is the local test double's access log for session 1 (below).
Sessions 1 and 4 are the same files as the #399 folder's.

## Redaction

Screenshots are halved. The main-menu shots are cropped to the BPP touch menu or the settings rows,
so the account name is out of frame; the real opponents' names on the run rail and on the one
pre-existing favorite are pixelated. Each log is the `[BPP]` lines of one game session, the chunks
marked `# ---- fNN ----`, with every line naming an account, user or player id removed (none
matched, apart from a stack-trace frame naming a parameter, which was kept). No line carries an id
or a player name. `bazaarplusplus.redacted.db` is the history database pulled after session 4, with
the seeded and Ghost rows still in it: every account id is `redacted-account`, every real player and
opponent name `redacted-player` (in the `pvp_combat_recorded` events too), and a byte search finds
none of the original ids or names.

## Ghost: the server and the test double

The server is reachable: every Ghost section entry runs a real sync,
`history_panel.ghost_sync.succeeded imported_count=0 reason_code=completed`. It returns no Ghost
battle for this account. Writing one to the production server is out of bounds, so the Ghost rows
came from a **test double**:

- Three discovery rows were seeded into the pulled history database (`source='GHOST'`, battle ids
  `gh_70b7a…` D7, `gh_b60d5…` D6, `gh_b1204…` D5, challenger `ghost_double`), each pointing at the same
  Bundle V6, `01M5QR2HG8A9Q96CBRD219B27V.bundle`, of this account's run 2810a43b. Their download URL is `http://127.0.0.1:8765/<bundle>`, reached from the device through
  `adb reverse tcp:8765` and served by `python3 -m http.server` on this Mac.
- Nothing in the mod was overridden: no setting, no seam. The rows are what the real sync writes, the
  download is the shipped `DownloadGhostBundleAsync` (a plain GET of the row's URL), and
  `RunBundleV6Contract.Open`, the identity checks and `GhostBattlePayloadStore.Save` are the shipped
  path. The access log shows one GET per Download Replay (04:34:42, 04:43:25, 04:49:03); the rows
  then read `ghost_replay_state='local_ready'` in the redacted database, the button changes from
  Download Replay to Replay, and each replay starts with `source=imported_ghost`.
- The real sync ran over the seeded rows (`imported_count=0`) and left them alone.
- The three rows share one Bundle, so D7 and D6, whose snapshots carry the same items, draw the same
  board; D5 differs.

Afterwards the seeded rows, their payload files and the favorite made from one of them were deleted
from the device, the reverse and the server removed; the device database again has its 8 local
battles, 2 runs and 1 favorite.

## Criteria

| Criterion | Evidence | Result |
|---|---|---|
| Enabled | session 1: 22 `android.patch.installed`, `android.history.composed ghost_sync=true`, History in the touch menu, `01-touch-menu.png` | met |
| Disabled | session 2: `plugin.loaded mode=disabled`, no `patch.installed`, no `history.composed` and no `ui.mounted`; `hooks.summary mode=disabled` with every hit at 0; no BPP button and no `\| BPP 6.0.0` on the version label, `25-patchmode-disabled.png`. Session 3, enabled again: `version_label.hit ... \| BPP 6.0.0`, `ui.mounted`, `26-patchmode-enabled.png` | met. The composition still starts (`record_chain.started`, `combat_replay.maintenance.completed`), with no patch installed to feed it |
| Run A, then run B | `boards_rendered section=runs both_boards=true day=5 ... rental_leases=15`, `day=7`, back to `day=5`, each 7 + 8 leases; `02-runs-run-b.png`, `03-runs-run-a.png` | met: each run draws its own boards and rail |
| Ghost section, sync and filters | 04:33:58 `ghost_sync.succeeded imported_count=0`; `filter_tapped filter=ghost_outcome value=IWon`, `ILost`, `ghost_day_min_10`, `All`; `04-ghost-section.png` to `07-ghost-won-10d-empty.png` | met: the three seeded Ghost battles listed; Lost and Won + ≥10d show the empty state |
| Ghost download and replay | 04:34:42 GET, 04:34:43 `replay.start_requested battle_id=gh_70b7a source=imported_ghost from_lobby=true`, `history.closed reason=replay_started returned=0 rental_leases=0`, `combat_replay.playback.started`, `08-ghost-replay-playing.png`, `09-ghost-replay-recap.png` | met: the challenger's board plays against the account's own board |
| Return to History (ADR-0002) | 04:37:00 `menu_return_observed decision=complete_confirmed scene=GameScene:lobby_loaded`, `playback.succeeded ... degradation_count=0`, `history.opened reason=replay_returned resume_selection=true section=ghost`, `10-ghost-returned.png` | met: History reopens on the Ghost section and the battle that played |
| No residue across the round trip | `card_rental.released reason=scene_changed ... card_rented=75 card_returned=75 card_lost=0 card_balanced=true`; the run rows keep their rank badges, `12-runs-badges-before-replay.png`, `11-runs-badges-after-replay.png` | met on the final build. The dry run on an earlier build lost the badges after a replay (the scene load unloaded the badge atlases); `45c522b` keeps them (`AndroidHistoryBadges.LoadAtlas`, `Historys_badge_artwork_survives_a_scene_loads_asset_unload`) |
| The History switch off during a replay | D6: 04:43:25 GET, `start_requested battle_id=gh_b60d5`, `13-d6-before-download.png`; `settings.changed key=HistoryPanel value=false` from the game's settings in the replay, `history.return_cancelled reason=switched_off`, `14-d6-replay-settings-history-off.png`; after Continue `menu_return_observed`, `playback.succeeded`, no `replay_returned`, `15-d6-no-return-main-menu.png` | met: the replay ends normally into the main menu and History stays closed. Switched on again at 04:46:46 |
| A scene change the game starts, History open | offline Resume (airplane mode): `scene.loaded name=Gameplay` with no run, History opened there at 04:47:10 (`ghost_sync.failed reason_code=query_failed`, the host unresolved), `16-offline-resume-loading.png`, `17-offline-history-open-in-gameplay-scene.png`; 54 s later the game loads `MainMenuScene`: `card_rental.released reason=scene_changed leases=0 ... card_balanced=true`, then `boards_rendered day=6 rental_leases=8`, `18-offline-history-after-main-menu-scene.png` | met: History stays open across a scene change that is not a combat, as the desktop's `OnlyWhenInCombat` keeps it, and re-rents its cards in the new scene. The network was restored at once |
| A network cut during a Ghost replay | D5: 04:49:03 GET, `start_requested battle_id=gh_b1204`; airplane mode on in the replay brings the game's Connection Issues alert, `19-net-cut-replay-modal.png`; OK, the replay finishes, `20-net-cut-replay-recap.png`; network back, Continue: `menu_return_observed complete_confirmed`, `playback.succeeded`, `history.opened reason=replay_returned` on D5, `21-net-cut-replay-returned.png` | met: the alert is the game's own; BPP's return is unaffected |
| History in a run | online Resume into the run, History opened 04:50:44 (`boards_rendered section=runs day=7 rental_leases=15`), Replay gated as "In Run", `22-in-run-history-replay-gated.png`; `history.closed reason=close_button returned=15 rental_leases=0` | met |
| Local saved replay | session 4: `start_requested battle_id=ad42ebdb source=local_saved`, `playback.started`, `23-local-replay-recap.png`, `exit_requested decision=proceed`, `menu_return_observed decision=complete_confirmed`, `playback.succeeded ... degradation_count=0`, `history.opened reason=replay_returned section=runs`, `24-local-replay-returned.png` | met |
| Ghost favorite | `favorite_tapped section=ghost`, then `boards_rendered section=favorites both_boards=false day=7 upper_leases=8 lower_leases=0`, "Saved to Favorites with its replay.", `27-ghost-favorite-one-board.png` | met: a Ghost favorite draws the challenger's board only |
| Counters | session 1 last summary: `history_opened=8 history_open_sessions=0 history_leases=0 replay_starts=3 replay_start_failures=0 replay_exits=3 replay_menu_returns=3 replay_session=false payload_leases_held=0 main_thread_failures=0 card_rented=288 card_returned=288 card_lost=0 card_balanced=true`; session 4: `replay_starts=1 replay_menu_returns=1 card_rented=169 card_returned=169 card_balanced=true` | met |
| Gates | `just mod::check`, `mod::test`, `mod::test-core`, `just with-config release just mod::matrix staging ptr`, `mod::android-build`, `just commands-check` green on `45c522b`; desktop goldens unchanged | met |

## Covered by a test instead of the device

- **A scene change into a combat with History open.** History closes only when the game loads a
  scene into a combat. On the emulator a combat loads no scene, and a run's scene change is started
  from a game menu the full-screen panel covers. `A_scene_change_returns_historys_cards_and_closes_it_only_into_a_combat`
  (`tests/AndroidHost.Tests/AndroidSceneChangeTeardownTests.cs`) holds the compiled branch: the cards
  go back with `scene_changed`, then `AndroidCombatProbe.IsInCombat` decides the `scene_combat` close.

## Findings

- Badge atlases unloaded by a scene load: found in the dry run, fixed in `45c522b` (above).
- In the second of two back-to-back relayouts (`wm size reset`, 04:40:00), `relayout ... rental_leases=16`
  read 16 with 8 cards on screen: the previous view's cards were still in flight, and `CardRentalCore.Return` settles them on the next
  tick. The close returned 8 with `rental_leases=0`, and the session ended balanced.

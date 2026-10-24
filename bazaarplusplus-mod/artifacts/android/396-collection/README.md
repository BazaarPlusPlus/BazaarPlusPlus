# #396 Android Collection panel: acceptance on the AVD

AVD `bpp_api35` (emulator-5556, 2400×1080 at 420 dpi), game `1.0.12825-production-android-d9601ded`,
LemonLoader `v0.7.4-android.4`, BPP 6.0.0 Release build of commit `6ca9695` on branch
`android/396-397-rental-finish`. The rental adapter is `AndroidCardRental` over Core's
`CardRentalCore`; the panel is `src/BazaarPlusPlus.Android/Collection/`.

Every session ran that one build. Commits after `6ca9695` add only this evidence. Session numbers
match the #397 folder (`../397-livebuild/`), where session 1 holds no Collection step.

| Session | Log | `android.plugin.loaded sha256` | Commit | What it covers |
|---|---|---|---|---|
| 2 | `logs/session2.log` | `15a0f1a9e84ff57abf8f067712980421c3fddb780a224657a753e7e5b9d8baec` | `6ca9695` | keyword icons in the card text, Collection then Ten-Win Builds on one pool, a scene change the game starts while Collection is open (no setup was in flight at it, below), reopening after it |
| 3 | `logs/session3.log` | `15a0f1a9e84ff57abf8f067712980421c3fddb780a224657a753e7e5b9d8baec` | `6ca9695` | the setting on/off contrast with its `switched_off` release, two different cards inspected in a row with tier and enchantment chips, the back key |
| 4 | `logs/session4.log` | `15a0f1a9e84ff57abf8f067712980421c3fddb780a224657a753e7e5b9d8baec` | `6ca9695` | an inspected card returned by the back key while its setup is still running, and that lease settling into the pool |

Screenshots are halved. The full-screen panel and in-run screens show no account name; the
settings and touch-menu shots are crops, and the Connection Issues alert is cropped to the alert.
Each log is the `[BPP]` lines of one game session, filtered to the Collection, rental, card-text,
settings, scene, back-key and touch-menu events, and closed by the session's last
`android.hooks.summary`. No test double is involved: Collection reads only the game's own card
templates.

The earlier `panel/` sessions (builds `6e25f64d…` and before) and the interop `spike/` (a temporary
build, `2e3d3b7b…`) were removed; they are in git history, and the spike's findings are in the
issue's comments.

## What was exercised

| Step | Evidence | Result |
|---|---|---|
| Setting on, then off, in a run | session 3 `10-touch-menu-setting-on.png`, `07-native-setting-on.png`; `settings.changed key=CollectionPanel value=false`, `08-native-setting-off.png`, `09-touch-menu-setting-off.png` (`ui.panel_opened entries=History,LiveBuild`) | the Card Collection tab is offered only with the switch on |
| The `switched_off` release | `card_rental.released reason=switched_off leases=0 destroyed_now=34 card_rented=49 card_returned=49 card_instantiated=37 card_pool_hits=12 card_destroyed=37 card_lost=0 card_balanced=true` | the pooled cards are destroyed with every lease back |
| Switch back on | `settings.changed key=CollectionPanel value=true`, `entries=History,Collection,LiveBuild` | |
| Open in the run | `collection.opened cached=false in_run=true hero=vanessa day=6`, `catalog_built templates=3375 accepted=1850 unmapped=0 failed=0`, `04-grid-in-run-day6.png` | the run's hero and the Day 6 chip are selected; the game's own cards at their native widths |
| Two different cards in a row | `inspected` Grenade, `inspection_retargeted tier=diamond`, `05-inspect-grenade-diamond.png`; `inspection_closed reason=close_button rental_leases=32`; `inspected` Grappling Hook, `inspection_retargeted tier=bronze enchantment=shiny`, `06-inspect-second-card-shiny.png` | Diamond frame and 400 damage on the Grenade; the Hook at Bronze Shiny with nothing of the Grenade left |
| Close | `collection.closed reason=close_button returned=32 rental_leases=0 rental_balanced=true` | |
| Back key | `inspection_closed reason=back_key`, `back_key.consumed target=collection`, `collection.closed reason=back_key returned=32 rental_balanced=true`, `back_key.consumed target=collection`, `back_key.passed` | inspection, then panel, then the game's escape menu |
| Keyword icons in card text | session 2 `card_text.sprites icon=Damage asset=Text_Icon_Atlas_w_Shadows`, `01-inspect-keyword-icon.png` | the Damage icon is drawn from the game's sprite atlas, not a missing-sprite box |
| One pool for Collection and Ten-Win Builds | `collection.closed returned=32 rental_balanced=true`, then `live_build.opened leases=15`, then `card_rental.released reason=switched_off card_rented=80 card_returned=80 card_instantiated=41 card_pool_hits=39 card_balanced=true` | Ten-Win Builds took 39 of its rents from cards Collection had returned |
| A scene change the game starts, Collection open | Resume from the main menu in airplane mode: the game loads `Gameplay` with no run; Collection was opened there (`collection.opened cached=true in_run=true hero=vanessa day=1`) and a card inspected, and from 40 s on a tier or enchantment chip was tapped every 0.4 s, each re-running the rented card's setup (58 `inspection_retargeted` lines). About a minute after the Resume the game returned to `MainMenuScene` with its Connection Issues alert (`02-game-connection-alert-after-offline-resume.png`) | `scene.loaded name=MainMenuScene`, `collection.inspection_closed reason=scene_changed rental_leases=32`, `collection.closed reason=scene_changed returned=32 rental_leases=0 rental_balanced=true`, `card_rental.released reason=scene_changed leases=0 destroyed_now=30 card_rented=113 card_returned=113 card_instantiated=74 card_destroyed=74 card_lost=0 card_balanced=true`. BPP closes the panel and releases every card on the game's scene load. **No setup was in flight** at that close: `rental_leases` and `card_leases` count every lease not yet settled (`CardRentalCore.ActiveLeases`), and a lease returned or released during its setup stays counted until the setup ends. `rental_leases=32` at the inspection's close is the grid alone, and `0` after the panel's close means every lease had settled before the release ran. The release-during-setup branch of a scene change did not run here (below) |
| A card returned while its setup runs | session 4, in the run: Grenade inspected, then a tap on its Gold chip and the back key sent from one device shell script (`inspection_retargeted tier=gold`, then `inspection_closed reason=back_key rental_leases=33` in the same second) | the grid holds 32 leases, so the 33rd is the inspected card, still counted because its re-setup was running when the back key returned it. The next summary has it settled into the pool: `card_rented=33 card_returned=1 card_leases=32 card_held=32 card_pooled=1 card_lost=0 card_balanced=true` (the summary before had `card_pooled=0`), and the grid shows no Gold Grenade left over (`11-grid-after-in-flight-back-close.png`). That is `CardRentalCore.Return`'s detach-then-settle path; `collection.closed reason=back_key returned=32 rental_leases=0 rental_balanced=true` follows |
| No leftover card after it | the network restored at once, the alert dismissed; `collection.opened cached=true in_run=false`, a fresh card inspected (`03-reopen-after-scene-change.png`, Coconut at Bronze with no chip of the earlier card), `collection.closed returned=32 rental_balanced=true`; the next scene load releases `card_rented=146 card_returned=146 card_lost=0 card_balanced=true` | nothing of the closed inspection or grid survives into the next scene |

## Covered by tests instead of the device

- **A scene change while a setup is still running.** The device never reached it: at session 2's
  game-started scene change the counters show zero setups in flight (above), likely because the
  scene-load frames let every pending setup finish before the scene-loaded callback runs, and every
  other scene change starts from a game menu the full-screen panel covers. Only
  `CardRentalCoreTests` (`tests/AndroidHost.Tests/`) covers `ReleaseAll` on an in-flight lease, the
  `DestroyOnArrival` branch:
  `A_release_all_during_a_setup_detaches_the_card_and_destroys_it_on_arrival`,
  `Release_all_destroys_held_and_pooled_cards_and_an_in_flight_one_when_it_arrives` and
  `Pooling_resumes_after_a_release_all_while_its_in_flight_card_is_still_destroyed`.
  The detach-then-settle half it shares with `Return` ran on the device in session 4.
  `AndroidSceneChangeTeardownTests` holds the compiled close with `scene_changed` from
  `AndroidTouchUi.OnSceneLoaded`.

Game state left: in Vanessa's run a7ef4347, day 6, at the choice screen, the screen at its own
2400×1080 and 420 dpi.

# #396 Android Collection panel: acceptance on the AVD

AVD `bpp_api35` (emulator-5556, 2400×1080), game `1.0.12825-production-android-d9601ded`,
LemonLoader `v0.7.4-android.4`, BPP 6.0.0 Release build from branch
`android/396-collection-panel`. `spike/` holds the earlier interop spike that measured card rental;
this folder records the shipped adapter (`AndroidCardRental`) and the panel built on it
(`src/BazaarPlusPlus.Android/Collection/`).

Screenshots are cropped or taken over the full-screen panel, so no account name shows.
`panel/bpp-panel.log` is the `[BPP]` log of the session, filtered to the `android.collection.*`,
`android.card_rental.*`, `android.ui.panel_*`, `android.scene.loaded` and
`android.settings.changed` events.

## What was exercised

| Step | Evidence | Result |
|---|---|---|
| Touch menu with the setting on | `panel/01-touch-menu.png` | a Card Collection tab and a `Collection panel: On` switch |
| First open in the main menu | `collection.opened cached=false in_run=false hero=vanessa`, `catalog_built` | the remembered hero is selected; the catalog builds once, `templates=3375 accepted=1850 unmapped=0 failed=0`, `map_ms=28843` (the game's first `GetCardMap`) and `project_ms=2024` over 171 frames |
| Grid of the game's own cards | `panel/02-items-vanessa.png` | Items(141) for Vanessa, item cards at their native widths |
| Scroll the grid | `panel/03-grid-scrolled-clipped.png` | cards scroll under the header and stop at the grid's edges |
| Open a card, change tier | `inspected ... tier=bronze`, `inspection_retargeted tier=diamond`, `panel/04-inspect-diamond.png` | the same rented Grenade set up again at Diamond (400 damage) |
| Open another card, enchant | `inspection_retargeted tier=bronze enchantment=heavy`, `panel/05-inspect-enchant.png` | Jellyfish set up again as Heavy |
| Hero and size filters | `filtered command=hero visible=143`, `command=size visible=65`, `panel/06-filter-dooley-medium.png` | Dooley + Medium |
| Search | `filtered command=search visible=4`, `panel/07-search-bot.png` | "bot" applies after the keyboard's OK |
| Keyword with no match | `filtered command=keyword visible=0`, `panel/08-keyword-no-matches.png` | Core's no-match message |
| Skills tab, reset, clear search | `command=tab`, `command=reset visible=133`, `command=search visible=133`, `panel/09-skills-tab.png` | skill cards render round; a selection made on Items carries over, as on desktop, until Reset |
| Inspect a skill | `inspected type=skill`, `panel/10-skill-inspect.png` | tier chips only; skills take no enchantment |
| Close | `collection.closed reason=close_button returned=35 rental_leases=0 rental_balanced=true` | every grid and inspection lease returned |
| Reopen | `collection.opened cached=true`, `panel/11-reopen-cached.png` | instant: the catalog is kept for the game session |
| Switch the setting off | `card_rental.released reason=switched_off leases=0 destroyed_now=80 ... card_rented=211 card_returned=211 card_lost=0 card_balanced=true`, `panel/12-setting-off.png` | the tab disappears (`12a-setting-on.png` is the same rows before) and the pooled cards are destroyed |
| Switch back on, open, close, change scene | `settings.changed value=true`, `collection.closed ... rental_balanced=true`, `scene.loaded name=LoadingScene`, `card_rental.released reason=scene_changed destroyed_now=30 card_rented=251 card_returned=251 card_balanced=true` | Resume from the main menu loads the run; the pool is destroyed on the scene load |
| Open in a run | `collection.opened cached=true in_run=true hero=vanessa day=5`, `panel/13-in-run-day5.png` | the run's hero is selected and the Day 5 chip is on; closed with `returned=38 rental_balanced=true` |

## Not exercised

- A scene change while the panel is open. The panel covers the screen, so nothing in the game can
  load a scene under it by touch; `AndroidTouchUi.OnSceneLoaded` closes it with
  `reason=scene_changed` before the rental release, the same path the closed-panel run took.

Game state left: in Vanessa's run, day 5, at a merchant.

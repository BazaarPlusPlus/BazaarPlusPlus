# #397 Android Ten-Win Builds (Live Build) panel: acceptance on the AVD

AVD `bpp_api35` (emulator-5556, 2400×1080 at 420 dpi, a 136 px cutout on the left edge), game
`1.0.12825-production-android-d9601ded`, BPP 6.0.0 Release build of commit `6ca9695` on branch
`android/396-397-rental-finish`. The panel is `src/BazaarPlusPlus.Android/LiveBuild/`, its layout is
Core's `LiveBuildTouchLayout` over `TouchMetrics`.

Every session below ran that one build. Commits after `6ca9695` add only this evidence.

| Session | Log | `android.plugin.loaded sha256` | Commit | What it covers |
|---|---|---|---|---|
| 1 | `logs/session1.log` | `15a0f1a9e84ff57abf8f067712980421c3fddb780a224657a753e7e5b9d8baec` | `6ca9695` | the shipped corpus path (CDN, embedded seed), the setting on/off contrast, the failed refresh's main-thread handoff, CJK supporter names |
| 2 | `logs/session2.log` | `15a0f1a9e84ff57abf8f067712980421c3fddb780a224657a753e7e5b9d8baec` | `6ca9695` | the remote corpus through a local test double, Collection then Ten-Win Builds on the shared rental pool, match/browse/inspect, a successful refresh, the `switched_off` release, a scene change the game starts with the panel open, reopening after it |
| 3 | `logs/session3.log` | `15a0f1a9e84ff57abf8f067712980421c3fddb780a224657a753e7e5b9d8baec` | `6ca9695` | the back key on the panel and past it to the game, in a run, without the test double |

Screenshots are halved. The full-screen panel and in-run screens show no account name; the
main-menu shots are cropped to the touch menu or the settings rows. Each log is the `[BPP]` lines
of one game session, filtered to the Ten-Win Builds, rental, settings, scene, back-key and touch-menu
events, and closed by the session's last `android.hooks.summary`.

## The corpus and the test double

`https://cdn.bazaarplusplus.com/metrics/v6/builds/latest.json` answers 404: the analyzer has not
published a first `builds/latest.json` yet, so desktop and Android both run on the embedded seed,
whose one Vanessa build uses placeholder template ids and can never match. Session 1 shows that
state as it ships (`corpus.degraded reason_code=embedded_fallback`, then `remote_refresh_failed`).

Session 2 fetched the corpus from a **local test double**. `make_test_corpus.py` wrote a schema-2
corpus of three Vanessa builds from real template ids: the first build is the board of the evidence
run (a7ef4347, day 6, read from the local history database), the others use Vanessa cards of an
earlier run. A `python3 -m http.server` bound to 127.0.0.1 on this Mac served it at `/metrics/v6/builds/latest.json`,
`adb reverse tcp:18397 tcp:18397` reached it from the device, and `[AndroidDev] BuildCorpusPort` was
set to `18397`. That setting is the existing dev-only seam: it defaults to `0`, has no row in any
settings dialog, logs `android.live_build.dev_corpus_override` as a warning at startup, and changes
only the URL; the fetch, the parser, the cache and the matching are the shipped
`AndroidTenWinBuildCatalog` and `BuildRecommendationRepository` code. The log shows that code taking
the served corpus (`corpus.recovered source=remote build_count=3`) and a later refresh replacing it
(`refresh.succeeded result=updated`, `effect=recommendations`). The stats in screenshots 08–10 are
made up. Afterwards the setting went back to `0`, the reverse and the server were removed, and the
cached `BazaarPlusPlusV6/builds.json` was deleted; session 3 starts with no `dev_corpus_override`
line and on the seed again.

## What was exercised

| Step | Evidence | Result |
|---|---|---|
| Setting off, then on, in the main menu | session 1 `settings.changed key=LiveBuildPanel value=true`, `live_build.data_warmed source=setting_on`; `01-touch-menu-setting-off.png`, `02-native-setting-off.png`, `03-native-setting-on.png`, `04-touch-menu-setting-on.png` | the tab appears only with the switch on, and the catalogs load only then |
| Open in the main menu on the seed | `live_build.opened ... hero=vanessa shop=0 stash=0 board=0 supporters=4 leases=0`, `05-panel-main-menu-seed.png` | the remembered hero, empty boards, `updated 10w ago · 1 builds` |
| Supporters | `06-supporters-cjk-and-hint.png`, `fonts.fallbacks_ready locale=zh-CN fallbacks=3` | Chinese supporter names drawn, the tap hint fits its row beside Sponsor |
| Refresh against the CDN | `refresh.failed reason_code=remote_request_failed` (404), `live_build.refresh_completed succeeded=false outcome=failed effect=status fetched_on_main_thread=false completed_on_main_thread=true`, `07-refresh-404-status.png` | the fetch ends on a pool thread and the result is completed on the main thread |
| Collection, then Ten-Win Builds, in the run | session 2 `collection.opened cached=false in_run=true hero=vanessa day=6`, `collection.closed returned=32 rental_balanced=true`, `live_build.opened ... stash=7 board=8 leases=15` | both panels rent from the one `AndroidCardRental` pool (below) |
| Pick candidates | `candidate_toggled candidates=1 ... recommendations=2 final_build=8`, `candidates=2 ... recommendations=1`, `08-one-candidate-two-matches.png`, `09-two-candidates-one-match.png` | Match tags on the build, Pick tags on the board, the four metric cells |
| Browse | `live_build.browsed index=1 count=2`, `10-browse-2-of-2.png` | |
| Two different cards inspected in a row | `inspected source=FinalBuild size=medium tier=diamond`, `inspection_closed reason=close_button rental_leases=22`, `inspected source=FinalBuild size=medium tier=gold`, `11-inspect-first-card.png`, `12-inspect-second-card.png` | the second card shows its own name and text and nothing of the first; keyword icons (Poison, Haste, Charge) are drawn |
| Refresh against the test double | `refresh.succeeded result=updated`, `refresh_completed succeeded=true outcome=updated effect=recommendations fetched_on_main_thread=false completed_on_main_thread=true` | the successful path takes the same main-thread handoff |
| Close | `live_build.closed reason=close_button returned=22 rental_leases=0 rental_balanced=true` | every lease back, the inspected card's included |
| Setting off in the run | `settings.changed key=LiveBuildPanel value=false`, `card_rental.released reason=switched_off leases=0 destroyed_now=38 card_rented=80 card_returned=80 card_instantiated=41 card_pool_hits=39 card_destroyed=41 card_lost=0 card_balanced=true`, `13-setting-off-in-run.png` | the pooled cards are destroyed; `card_pool_hits=39` are rents Ten-Win Builds served from cards Collection had returned |
| A scene change the game starts, panel open | Resume from the main menu in airplane mode: the game loads `Gameplay` with no run, the panel was opened there (`live_build.opened hero=common board=0 leases=0`), and about a minute later the game returns to `MainMenuScene` with its Connection Issues alert; `scene.loaded name=MainMenuScene` then `live_build.closed reason=scene_changed returned=0 rental_leases=0 rental_balanced=true` | BPP closes the panel on the game's scene load; the network was restored at once |
| Reopen after it | `live_build.opened hero=common shop=0 stash=0 board=0 matches_state=no_candidates`, `14-reopen-after-game-scene-change.png`, `live_build.closed reason=close_button returned=0` | no candidates or cards carried over. `Common` is the hero the game reports in that run-less Gameplay scene (Collection logs `in_run=false hero=null` in the same window), not BPP state |
| Reopen in the resumed run | online Resume, `live_build.opened hero=vanessa stash=7 board=8 matches_state=no_candidates leases=15`, `15-reopen-in-resumed-run.png`, `live_build.closed returned=15 rental_leases=0 rental_balanced=true` | the run's hero and boards again, candidates cleared |
| Back key | session 3 `live_build.closed reason=back_key returned=15 rental_leases=0 rental_balanced=true`, `back_key.consumed source=handle_escape target=live_build`, then `back_key.passed`, `16-back-key-passes-to-game.png` | the first press closes the panel, the next reaches the game's escape menu |
| Counters | session 2 last summary `live_build_opened=4 live_build_closed=4 live_build_leases=0 live_build_refreshes_completed=1 card_balanced=true`; session 3 `back_key_consumed=3 back_key_passed=3` | |

## Covered by tests instead of the device

- **A game-started scene change while a Ten-Win Builds card is inspected.** On this emulator the
  game changes scene by itself only after an offline Resume, and that Gameplay scene has no run, so
  the panel has no boards, no match and no card to inspect. Every other scene change is started
  from a game menu the full-screen panel covers. `AndroidSceneChangeTeardownTests`
  (`tests/AndroidHost.Tests/`) holds the compiled chain instead: `AndroidTouchUi.OnSceneLoaded` closes
  the panel with `scene_changed`, `AndroidLiveBuildPanel.Close` calls `AndroidLiveBuildView.ReturnAll`,
  and that closes `AndroidLiveBuildInspection`. The rental's release of a lease still in setup is
  covered by `CardRentalCoreTests`, and the device shows the same close returning the inspected
  card's lease on a normal close (`inspection_closed ... rental_leases=22`, then `returned=22`).
- **Other screen ratios and dense screens.** `LiveBuildTouchLayoutTests`
  (`tests/LiveBuildPanel.Tests/`) lays the sheet out at 2560×1600@420 and at 2400×1080@480, @560 and
  @640, and holds every socket at 48dp; the fallback density for a device that reports no
  `Screen.dpi` is covered there and in `TouchMetricsTargetTests`. This AVD reports its dpi, so the
  fallback cannot be reached on it. `TouchRelayoutDebounceTests` covers the
  relayout debounce while cards set up.

Game state left: in Vanessa's run a7ef4347, day 6, at the choice screen.

# #399 evidence: Android History favorites, delete, filters, More dialog, native boards, phone fit

Device: AVD `bpp_api35` (emulator-5556), 2400x1080 at 420 dpi (safe area x 136–2400), plus `wm size 2560x1600` for the second ratio (reset afterwards); game `1.0.12825-production-android-d9601ded`, BPP `6.0.0.prod`.
Account state: one real Run (Vanessa, Normal, in progress on day 5) with one recorded PvP battle (day 5, against a Jules opponent), not linked to BazaarDB, no Ghost battle on the server.

## Seeding

The account has one Run and one battle, so `seed399.py` added rows to the pulled history database for the session and removed them afterwards (`--remove`), leaving the real Run as it was:

- `seed399-eligible`: Dooley, Unranked, `history_only`, completed, 12 local battles whose snapshots are variations of the real one, with `local_payload_state='missing'` (no replay file). Deletable.
- `seed399-protected`: Vanessa, Ranked, `bundle`, completed, 2 battles, a `waiting` seal job with a 2099 input deadline, and no player account anywhere, so `BundleSealCoordinator` waits on the player-account gate and never composes or uploads. Seal-protected.
- eight `seed399-fillerN` Runs (Unranked, `history_only`, no battles) so the archive scrolls.
- four Ghost rows `seed399-ghostN` (days 3, 7, 11, 12; two won, two lost) with `ghost_replay_state='unavailable_payload'` and an `invalid.invalid` download URL, so nothing is downloaded.

The pulled database before removal had the seed seal job still `waiting` with 0 attempts and no `bundle_outbox` row: nothing was sealed or uploaded. `[BazaarDB] UploadScreenshots` was set to `true` for the More dialog's link form and restored to `false`.

## Redaction

Screenshots are halved. None shows the main menu or its account name; the real opponent's name is pixelated on the D5 rail chip and on the Favorites row. Logs hold only `[BPP]` lines, with every line naming an account, user or player id, or a player name, removed (1 removed in `logs/1-*`, none in the others). `bazaarplusplus.redacted.db` is the pulled database with the seed rows still in it, every account id set to `redacted-account`, real player names to `redacted-player` (in the `pvp_combat_recorded` event too) and download URLs replaced; a byte search finds none of the original ids or names.

## Builds and logs

| Log | Build | What it shows |
|---|---|---|
| `logs/1-previous-build-delete-scroll-back.log` | branch before the last two row-layout fixes | delete of an eligible Run (`history_panel.run_delete.succeeded`), archive drag scroll, back key over inspection, skills, More and the panel |
| `logs/2-final-build.log` | final branch | favorites note and removal, More, back key, close button, the scene change into the run with History in-run, the History setting off and on, 2560x1600 and back, archive and rail drag scroll, the protected refusal and the eligible delete |
| `logs/3-final-build-filters-favorite.log` | final branch, reseeded (a different assembly hash from log 2, see each log's line 2; neither build is mapped to a commit) | favorite from Runs and removal, hero filter, Ghost outcome and day filters applied and cleared |

## Criteria

| Criterion | Result | Evidence |
|---|---|---|
| Enabled / disabled | met: History panel On shows History in the BPP panel; Off hides it (`android.settings.changed section=Android key=HistoryPanel value=false`, `android.card_rental.released reason=switched_off ... card_balanced=true`); On restores it | `screens/2400x1080/01-*`, `35-*`, `36-*`; `logs/2-*` |
| Favorite and unfavorite | met: favorite a real battle from Runs ("Saved to Favorites with its replay."), the Favorites tab, a 35-character note saved through the keyboard (`note_saved saved=true note_length=35`), Remove with confirm ("Favorite removed.") | `22-*` to `28-*`; `logs/2-*`, `logs/3-*` |
| Delete an eligible Run | met: two-tap confirm ("Sure?"), `run_delete.succeeded battle_count=12 cleanup_failed_count=0`; an active Run's Delete stays disabled | `04-*`, `05-*`, `02-*`; `logs/1-*`, `logs/2-*` |
| A protected Run refuses deletion | met: "This ranked run is waiting to upload to BazaarDB and cannot be deleted yet.", `delete_tapped section=runs enabled=false`; `HistoryPanelRepository.DeleteRun` re-checks `BundleQueueStore.ProtectedRunCondition` in its write transaction | `06-*`; `logs/2-*` |
| Filters apply and clear | met: hero chip Dooley narrows Runs to 2 and clears back to 10; Ghost Won, Won + ≥10d, Lost + ≥10d, All (`filter_tapped filter=ghost_outcome value=IWon`, `ghost_day_min_10`) | `07-*`, `12-*`, `17-*` to `21-*`; `logs/3-*` |
| More dialog | met: DB Connected, Check Server, the BazaarDB link form (code field, Link account, I already linked), scrollable; not linked, so no account info is on screen | `29-*` to `31-*`, `2560x1600/05-*` |
| Native boards for several battles in a row | met: D12, D11, D10, D4, D3 of the seeded Run and D5 of the real one, each `boards_rendered ... rental_leases=` equal to upper + lower | `08-*` to `11-*`, `13-*`, `33-*`; `logs/2-*` |
| Inspect and skills | met: a card tap opens the native card with its text, the Skills sheet shows both sides' skills, a skill tap inspects it | `14-*` to `16-*`, `2560x1600/06-*`, `07-*` |
| Close, scene switch, toggle off | partly met (the scene change and the setting switch both happened with History already closed, so the open-panel scene path, `closed reason=scene_combat:*` and `closed reason=switched_off` were not exercised): X (`closed reason=close_button returned=10 rental_leases=0`), back key (`closed reason=back_key ... rental_leases=0`), the main menu → run scene change (`android.card_rental.released reason=scene_changed ... card_rented=39 card_returned=39 card_lost=0 card_balanced=true`), History reopened in-run with the replay gated as "In Run", and the setting switched off (`reason=switched_off ... card_rented=72 card_returned=72 card_balanced=true`) | `32-*` to `36-*`; `logs/2-*` |
| Back key closes the top-most first | met: `android.back_key.consumed ... target=history_inspection`, `history_skills`, `history_more`, then `history`, then `touch_menu` | `logs/1-*`, `logs/2-*` |
| Drag scroll with inertia | met: `android.history.scrolled list=archive distance_dp=471.9 peak_velocity_dp=694 coast_frames=79 inertia=true`, `list=rail ... coast_frames=65`, `list=filters ...` (the hero chip strip at 2560x1600) | `03-*`, `13-*`, `2560x1600/03-*`; `logs/2-*` |
| Phone fit at two ratios | met: 2400x1080 (`view_created ... units_per_dp=2.625 archive_dp=226.5 target_dp=48 body_sp=12`) and 2560x1600 with its 91 px top inset (`screen=2560x1600@420|0,0,2560,1509 ... archive_dp=320`); no control overlaps another and long text ends in an ellipsis; after `wm size reset` the next open lays out for 2400x1080 again | `screens/2400x1080/*`, `screens/2560x1600/*`, `37-*` |

## Fixed on the device

- The skills sheet threw in its constructor: `AndroidHistoryUi.TapArea` added a second `Image` to a rect that already had one, and a `GameObject` holds one `Graphic`. `TapArea` now takes the fill.
- The archive's hero column wrapped hero names ("Vaness a"); Run rows now carry the hero as their title and battle rows read name / hero · day / time, as `screens/2400x1080/17-*` shows.
- The page label's second line (the time range) was cut to an ellipsis at 32 dp; it is 40 dp.
- A favorite's "Favorited <date> <time>" line was cut; the line now runs under the empty rating column.

## Not exercised or not met

- Ghost boards: the server has no Ghost battle for this account, and the seeded Ghost rows have no payload, so they show "This battle has no usable replay data"; native one-board Ghost rendering was not seen.
- An inspected card's text keeps the game's `{aura.N}` tokens (`screens/2560x1600/07-*`), as Live Build's and Collection's do ([android-host.md](../../../docs/architecture/android-host.md)).
- Android has no touch switch for `[BazaarDB] UploadScreenshots`: with it off, the More dialog's link card says to enable uploads in the desktop's settings dock. It was set with `just mod::android-setting`.
- Replay playback is #398's evidence; the Replay button was not pressed here.
- The Ghost tab's "Cloud: latest 5 days" note sits at the end of the filter strip and is cut at its edge at 2560x1600 (`2560x1600/04-*`); at 2400x1080 it is past the edge entirely.

## Game state left

Main menu with the Vanessa Run resumable on day 5 (Resume), seed rows removed, `[BazaarDB] UploadScreenshots = false`, `[Android] HistoryPanel = true`, display size reset to 1080x2400.

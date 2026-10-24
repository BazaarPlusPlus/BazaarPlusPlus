# #399 Android History: favorites, delete, filters, More, native boards and phone fit, acceptance on the AVD

AVD `bpp_api35` (emulator-5556, 2400×1080 at 420 dpi, safe area x 136–2264; `wm size 1600x2560`
for the 2560×1600 ratio, reset afterwards), game `1.0.12825-production-android-d9601ded`, BPP 6.0.0
Release build of commit `45c522b` on branch `android/398-399-history-finish`. History is
`src/BazaarPlusPlus.Android/Ui/AndroidHistoryPanel*` over Core's `HistoryPanelCoordinator`, its boards
rent the game's card prefabs through `GameInterop/CardRental/AndroidCardRental`, and its layout is
Core's `HistoryTouchLayout` over `TouchMetrics`.

Every session below ran that one build. Commits after `45c522b` change only this evidence.

| Session | Log | `android.plugin.loaded sha256` | Commit | What it covers |
|---|---|---|---|---|
| 1 | `logs/1-enabled-history-ghost-replay.log` | `8b0739c9c5ac02ac6275e49060612ef6302eead0b3c417a52fb4f8cd63858c0b` | `45c522b` | the device's own rows plus the #398 Ghost double: Favorites (one board and two), a note draft across a relayout to 2560×1600 and back, the back key, More, the link form, Turn off from More, the History setting off and on |
| 2, 3 | in the #398 folder | `8b0739c9c5ac02ac6275e49060612ef6302eead0b3c417a52fb4f8cd63858c0b` | `45c522b` | `PatchMode = Disabled`, then `Enabled` again |
| 4 | `logs/4-seeded-history.log` | `8b0739c9c5ac02ac6275e49060612ef6302eead0b3c417a52fb4f8cd63858c0b` | `45c522b` | the seeded rows: hero filter, card and skill inspection, favorite from Runs, note, Remove, the eligible delete, the protected refusal, archive and rail scroll, Ghost filters |

Both logs are the same files as the #398 folder's. Screenshots are under `screens/2400x1080/` and
`screens/2560x1600/`.

## Seeding

The account has two runs, so for session 4 `seed399.py` added rows to the pulled history database
and removed them afterwards (`seed399.py DB --remove`), leaving the real runs as they were:

- `seed399-eligible`: Dooley, Unranked, `history_only`, completed, 12 local battles whose snapshots are
  variations of a real one, with no replay file. Deletable.
- `seed399-protected`: Vanessa, Ranked, `bundle`, completed, 2 battles, a `waiting` seal job with a
  2099 input deadline and no player account anywhere, so `BundleSealCoordinator` waits on the
  player-account gate and never composes or uploads. Seal-protected.
- eight `seed399-fillerN` runs (Unranked, `history_only`, no battles), so the archive scrolls.
- four Ghost rows `seed399-ghostN` (days 3, 7, 11, 12; two won, two lost) with
  `ghost_replay_state='unavailable_payload'` and an `invalid.invalid` download URL, so nothing is
  downloaded.

The database pulled at the end of session 4 still had the seal job `waiting` with 0 attempts and no
`bundle_outbox` row, and the log has no seal, outbox or upload event: nothing was sealed or uploaded.
For the link form, `[BazaarDB] UploadScreenshots` was switched on and then off again in session 1.
Session 1's Ghost rows are the #398 test double (see that folder's README); they were removed with
the seeds.

## Redaction

Screenshots are halved. The settings and touch-menu shots are cropped to the BPP rows, so the
account name is out of frame; the real opponents' names on the run rail and on the pre-existing
favorite are pixelated. Logs hold only `[BPP]` lines, with every line naming an account, user or
player id removed (none matched). `bazaarplusplus.redacted.db` is the database pulled after session
4, with the seeded rows and the Ghost double still in it: every account id is `redacted-account`,
every real player and opponent name `redacted-player` (in the `pvp_combat_recorded` events too); a
byte search finds none of the original ids or names. It is the same file as the #398 folder's.

## Criteria

| Criterion | Evidence | Result |
|---|---|---|
| Enabled / disabled | sessions 2 and 3 in the #398 folder (`25-patchmode-disabled.png`, `26-patchmode-enabled.png`). The History setting on its own: Turn off in More, 04:42:34 `settings.changed key=HistoryPanel value=false source=history_more`, `more_closed reason=switched_off`, one `history.closed reason=switched_off returned=8 rental_leases=0`, `card_rental.released reason=switched_off ... card_rented=217 card_returned=217 card_lost=0 card_balanced=true`; the touch menu then lists Collection and Ten-Win Builds only (`11-turned-off-menu-without-history.png`), the settings row reads off (`12-settings-history-off.png`), on again 04:42:58 (`13-settings-history-on.png`) | met |
| Favorites | 04:37:25 `favorite_tapped section=ghost`; `boards_rendered section=favorites both_boards=false day=7 upper_leases=8 lower_leases=0`, `01-favorites-ghost-one-board.png`; a local favorite `both_boards=true day=5` 7 + 8, `02-favorites-local-two-boards.png`; back to the Ghost one, `03-favorites-ghost-again.png`. Session 4: favorite a seeded battle from Runs ("Its replay was not available, so only the boards were kept."), `22-favorited-from-runs.png`, `23-favorites-three.png` | met: a Ghost favorite draws the challenger's board, a local one both |
| Note | session 4: `note_opened note_length=0`, `note_saved saved=true note_length=15`, "Note saved.", `24-note-saved.png`. Session 1: a draft across a relayout, below | met |
| Remove a favorite | session 4: Remove, "Confirm remove" and "Press Remove again to delete this favorite, its note and its replay copy.", `25-remove-confirm.png`; the second tap within 5 s: "Favorite removed.", `boards_rendered section=favorites day=7`, `26-favorite-removed.png` | met. A first attempt with the second tap 6.6 s later (04:57:47, 04:57:54) correctly did nothing |
| Delete an eligible run | 04:58:42 Delete, "Sure?" and "Press Delete again within 5s to remove seed399-eligib…", `27-delete-sure.png`; 04:58:43 `run_delete.succeeded battle_count=12 cleanup_failed_count=0`, `28-run-deleted.png` | met |
| A protected run refuses deletion | 04:59:06 `delete_tapped section=runs enabled=false`, "This ranked run is waiting to upload to BazaarDB and cannot be deleted yet.", `29-protected-refusal.png`; the run and its seal job are still in the redacted database | met; `HistoryPanelRepository.DeleteRun` re-checks `BundleQueueStore.ProtectedRunCondition` in its write transaction |
| Filters apply and clear | `filter_tapped filter=hero value=Dooley` narrows Runs to 2 (`18-hero-filter-dooley.png`); Ghost ≥10d shows D12 and D11, Won + ≥10d shows D12 (`31-ghost-10d.png`, `32-ghost-won-10d.png`), All with ≥10d off lists all 7 (`33-ghost-filters-cleared.png`) | met |
| Native boards, several objects in a row | `boards_rendered` for D5 of run A, D7 of run B, D12 and D2 of the seeded runs, each `rental_leases` equal to upper + lower (`17-seeded-runs.png`, `34-rail-scrolled.png`, `30-archive-scrolled.png`) | met |
| Inspect and skills | `card_inspected source=upper ... tier=gold`, `19-card-inspected.png`; back key `inspection_closed reason=back_key`; `skills_opened rows=2 skills=4`, `20-skills-sheet.png`; `card_inspected source=skills skill=true`, `21-skill-inspected.png`; back key twice, `skills_closed reason=back_key returned=4` | met |
| Scroll | `scrolled list=archive distance_dp=356.1 ... inertia=true` (`30-archive-scrolled.png`), `scrolled list=rail distance_dp=74.6` (`34-rail-scrolled.png`) | met |
| More and the link form | `more_opened`, DB Connected, "0 ghost battles synced.", Turn off, `06-more-dialog.png`; with BazaarDB upload on (`07-settings-upload-on.png`) the link form, `08-more-link-form.png`; code `BPPTEST0`, Link account tapped twice within a second: one `history_panel.account_link.failed method=redeem reason_code=invalid_or_expired`, "Code invalid or expired - generate a new one", `09-link-invalid-code.png`; upload off again, `10-settings-upload-off.png` | met: one request for two taps; not linked, so no account info is on screen |
| Phone fit, another ratio | the note draft open, `wm size 1600x2560`: `history.relayout from=2400x1080@420\|136,0,2264,1080 to=2560x1600@420\|145,0,2415,1600 note_draft_kept=true note_length=9 rental_leases=8`, `2560x1600/01-note-draft-after-relayout.png`; the back key closes the note (`note_closed reason=back_key`), `2560x1600/02-back-key-closed-note.png`; the Ghost section's cloud note fits beside the list, `2560x1600/03-cloud-note.png` and at 2400×1080 `05-cloud-note.png`; `wm size reset` relayouts back (`note_draft_kept=false`, the note was closed), `history_relayouts=3`; `view_created ... units_per_dp=2.625 target_dp=48 body_sp=12` | met: the panel re-lays itself out with its cards re-rented, and a draft survives the relayout |
| Back key | `note_closed`, `more_closed`, `history.closed reason=back_key returned=8 rental_leases=0`, each with `back_key.consumed`; session 1 `back_key_presses=6 back_key_consumed=6`, session 4 `4` and `4` | met |
| Open, close and scene change without residue | session 1 last summary `history_opened=8 history_open_sessions=0 history_leases=0 card_rented=288 card_returned=288 card_lost=0 card_balanced=true`; session 4 `card_rented=169 card_returned=169 card_balanced=true`; History open across the game's own scene change in `14`–`16` (#398 README) | met |
| Gates | `just mod::check`, `mod::test`, `mod::test-core`, `just with-config release just mod::matrix staging ptr`, `mod::android-build`, `just commands-check` green on `45c522b`; desktop goldens unchanged | met |

The 2400×1080 screens `14-in-replay-settings-history-off.png`, `15-offline-history-open-in-gameplay-scene.png`
and `16-offline-history-after-main-menu-scene.png` are #398's switch-off and scene-change shots, kept
here for the rental side of those criteria.

## Findings

- The delete prompt and its result say "Completed MP4 recordings will be kept" / "were kept" on
  Android, which records no video. The text is Core's `HistoryPanelText.Runs`, shared with the desktop; it is harmless (there are none
  to keep) but could be dropped when the host offers no recording.
- One relayout logged `rental_leases=16` with 8 cards on screen; see the #398 README.

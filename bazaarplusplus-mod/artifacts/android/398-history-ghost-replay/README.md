# #398 evidence: Android History, Ghost section, and saved-replay playback

Device: AVD `bpp_api35` (emulator-5556), game `1.0.12825-production-android-d9601ded`, BPP `6.0.0.prod`.
Account state: one Run (Vanessa, Normal, now day 6) with one recorded PvP battle (day 5, a loss against a Jules opponent).

Base: every build here was the branch on `750e2b0`. Before merge the branch was rebased onto `fd5d5cc`, which brings the Android Collection panel (#422). That rebase hand-merged `Ui/AndroidTouchUi.cs` (the Collection tab, the Collection setting row and the History launcher side by side) and `Host/AndroidMod.cs` (History's scene handling before `AndroidCardRental.ReleaseAll`). The merged build passed the gates but was not run on the device.

Screenshots are halved and cropped so no account name is in frame; the opponent's name in the History battle chip is blurred. Logs hold only `[BPP]` lines with every line naming an account, user, or player id removed (none matched). `bazaarplusplus.redacted.db` is the pulled history database with player and opponent names and account ids nulled, and `opponent_name` removed from the `pvp_combat_recorded` event.

## Builds and logs

| Log | Build | What it shows |
|---|---|---|
| `logs/1-before-fix-26s-index-then-sigsegv.log`, `logs/1-sigsegv-crash-excerpt.logcat.txt` | branch as rebased (`c5264c6` content) | History open: `view_created` at 22:40:34, `template_index_built` 26 s later (main-thread `GetCardMap`), then a SIGSEGV in `RectTransform.GetWorldCorners` from `AndroidHistoryPanelView.WorldBounds`, fault addr `0x1c` (array pointer 4 + header) |
| `logs/2-pvp-recorded-and-replay-portrait-degraded.log` | template index fix only | `android.pvp_battle.recorded ... replay_saved=true`; first saved replay end-to-end, ending `combat_replay.playback.degraded reason_code=opponent_portrait_unavailable` (`Il2CppStringArray(0)` bound to the pointer constructor). Also Ghost section evidence: Runs → Ghost → Runs (`history_panel.ghost_sync.succeeded` at :44 after a `section=runs` open, a reopen at :49 with `section=ghost` and a second sync at :50, then the next open at :132 back on `section=runs`) |
| `logs/3-array-fixed-nullable-ctor-nre.log` | + interop array sizes | replay still degraded, now from `SimPvpOpponent..ctor` unboxing a `null` `Il2CppSystem.Nullable` argument; one more Ghost section entry (`history_panel.ghost_sync.succeeded` at :43) before the replay was started from Runs |
| `logs/4-enabled-final-build.log` | final branch | C1 enabled, C2 without the Ghost section, C4 local replay. The Ghost section was not opened in this session: every `android.history.opened` is `section=runs` and there is no `history_panel.ghost_sync` event (a sync starts only on entering the Ghost section, `HistoryPanelCoordinator.RefreshSectionOnEntry`); its only Ghost line is `android.history.composed ghost_sync=true` (:24) |
| `logs/5-disabled-mode.log` | final branch, `[Android] PatchMode = Disabled` | C1 disabled |

## Criteria

| Criterion | Result | Evidence |
|---|---|---|
| C1 enabled | met: 17 `android.patch.installed`, the replay patches among them; `android.history.composed ghost_sync=true`; History opens from the BPP panel | `logs/4-*`, `screens/bpp-touch-panel-settings.png`, `screens/history-runs.png` |
| C1 disabled | met: `android.plugin.loaded mode=disabled`, 0 `patch.installed`, no `history.composed`, no BPP button, no BPP suffix on the version label | `logs/5-*`, `screens/disabled-no-bpp-button.png` |
| C2 no cross-talk or residue | met on the final build for replay → return, close, enter the run (History in-run, replay gated as "In Run"), back to the main menu, reopen, close. Final summary: `history_open_sessions=0 history_opened=4 history_view_roots=1 payload_leases_held=0 main_thread_failures=0 replay_session=false`; `android.main_thread.port_off_thread` never fires. Only one Run exists on this account, so "run A vs run B" was not exercised. Runs → Ghost → Runs was exercised only on the pre-fix builds (`logs/2-*:44,49,50`, `logs/3-*:43`), not on the final build | `logs/4-*`, `screens/history-in-run-replay-gated.png`; Ghost switch: `logs/2-*`, `logs/3-*` |
| C4 local battle replay | met: `start_requested source=local_saved` → bootstrap stages → `stage=replay` → native recap Continue → `exit_requested decision=proceed` → `menu_return_observed decision=complete_confirmed` → `combat_replay.playback.succeeded degradation_count=0` → `history.opened reason=replay_returned resume_selection=true` | `logs/4-*`, `screens/replay-after-fix-opponent-portrait.png`, `screens/replay-after-fix-recap.png`; before: `screens/replay-before-fix-no-opponent-portrait.png` |
| C4 Ghost sync | partial, pre-fix builds only: `history_panel.ghost_sync.succeeded imported_count=0 reason_code=completed`; the server returned no Ghost battle for this account, so no Ghost replay was played. The final build was not seen opening the Ghost section or completing a Ghost sync | `logs/2-*:44,50`, `logs/3-*:43`, `screens/history-ghost-synced-zero.png` (taken in one of those two sessions) |

## Flagged IL2CPP assumptions

| Assumption | Result |
|---|---|
| `Task.CompletedTask` as a prefix `__result` (`FinalizeRunInitialization`) | holds: `replay_finalize_skips=1`, bootstrap continues |
| Scene `ReturnToMainMenu` lands on | the lobby: `menu_return_observed scene=GameScene:lobby_loaded`, main menu on screen, History reopens |
| `Events.StateChanged` through `DelegateSupport` | holds: `replay.state_observed in_replay_state=true`, `replay_exits_handled=1` |
| Generic statics, `Services.Get<CollectionManager>` | holds: the opponent hero skin resolves and the portrait loads |
| `Il2CppSystem.Nullable` setters and constructor arguments | **broken, fixed**: a `null` absent value throws in `Il2CppObjectBaseToPtrNotNull`; now `AndroidGameIds.Optional` |
| Interop array sizes | **broken, fixed**: `new(4)` and `Il2CppStringArray(0)` bound to the `(IntPtr)` constructor; now `4L`/`0L`, held by `AndroidHostInteropRuleTests` |
| Interface casts, `Trigger(null)` | no failure observed in the replay path (boards, skills, enchantments render) |
| Timeouts vs real bootstrap | `bootstrap_managers` 12–18 s (two awaits, 30 s each); `wait_bootstrap_ready` under 1 s of 20 s; `wait_presentation` 3–6 s of two sequential 5 s waits |
| Badge atlas `LoadImage` | holds: `view_created ... badges=true`, rank badge drawn |
| Template index | **was a 26 s main-thread freeze, fixed**: now read from the game-data file's `cards` table on a pool thread, `templates=3375 elapsed_ms=80–86` |
| Font availability | holds: every History label renders |
| `busy_timeout` under DELETE-journal bursts | not exercised on device: no page read overlapped a battle write in this session |
| Touch targets | every control used here was hit on the first tap at 2400x1080 |

## Not met or out of scope

- No Ghost battle fetched (server returned 0 for this account), so no Ghost replay.
- The final build was not seen opening the Ghost section or syncing Ghost battles; that evidence is from the pre-fix builds of `logs/2-*` and `logs/3-*` only.
- Native UI gap: History draws flat (hero, rank and trophy as text, BPP badge atlases only), is entered from the BPP touch button rather than the game's own MENU, and the board rects are empty; native boards, favorites, delete, filters and the More dialog are #399. A **Record** button shows though the Android host cannot record.
- Settings is still the BPP-drawn touch panel (`screens/bpp-touch-panel-settings.png`) beside the game's own Options dialog (`screens/native-options-dialog.png`), which an injected native section could join.

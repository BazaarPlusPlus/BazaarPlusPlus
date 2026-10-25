# Architecture deepening c3: Saved Replay Playback in Core, device evidence

The device checks of plan §13 (ADR-0016) for branch `refactor/arch-c3-saved-replay-session`:
Core's `SavedReplayPlayback` behind `AndroidSavedReplayHost`, which replaces the saved-replay half
of `AndroidCombatReplayRuntime`.

- AVD `bpp_api35` (emulator-5556, 2400×1080 at 420 dpi, safe area x 136–2264).
- Game `1.0.12825-production-android-d9601ded`.
- BPP 6.0.0 Release build. `just mod::android-build` / `android-deploy` / `android-launch`;
  session 1 began with `just mod::android-smoke enabled`.

| Session | Log | `android.plugin.loaded sha256` | Commit | What it covers |
|---|---|---|---|---|
| 1 | `logs/1-branch-smoke-replays-pause-exit.log`, `logs/1-smoke.md` | `7c17a705daec6a829a8b3c2edb86e337124fb83548e221f9b1ab032335413965` | `80d6c6a` (branch) | smoke; History saved replay from the lobby to the recap; recap Continue exit and menu return; a second History replay; the pause-menu "Exit to main menu" at the recap |
| 2 | `logs/2-master-pause-exit.log` | `32a24a64102b09d64380098a4109893029779516ca367c22b69396146129cc0f` | `a98b73d` (origin/master) | baseline only: is the session 1 pause-menu finding already on master? |
| 3 | `logs/3-branch-back-to-back.log` | `7c17a705daec6a829a8b3c2edb86e337124fb83548e221f9b1ab032335413965` | `80d6c6a` (branch) | the branch rebuilt after session 2 (same sha256); a replay, then a second replay started as soon as History reopened, idle counters at the end |

Commits after `80d6c6a` on the branch add only this evidence.

## Redaction

- **Screenshots.** Each one is halved.
  - The History shots are cropped to the boards and the action row. The battle list, which carries
    opponent names, is cut out.
  - No main-menu shot is committed.
- **Logs.** Each log holds the `[BPP]` lines of one game session, with every line naming an
  account, user or player id removed. No line matched.

## Android checks

| Check | Evidence | Result |
|---|---|---|
| 1. Smoke at idle: `PayloadLeasesHeld=0`, `HasSavedReplaySession=false` | `1-smoke.md`: Injection, Hook hit and Feature levels pass. Every session 1 summary before the first replay reads `replay_session=false payload_leases_held=0`. | met |
| 2. History saved replay from the lobby: `start_requested from_lobby=true`, plays to the recap | 14:18:59 `android.replay.start_requested battle_id=bc825991 source=local_saved from_lobby=true`, then the bootstrap stages, `history.closed reason=replay_started`, and `combat_replay.playback.started` at 14:19:10. `01-s1-history-replay-ready.png`, `02-s1-replay-recap.png`; session 3's replay mid-playback, `06-s3-replay-playing.png` | met |
| 3. Exit at the recap: `exit_requested` Proceed, `menu_return_started`, `menu_return_observed`, lobby; Starts, ExitsHandled and MenuReturns +1; lease released; no session | Recap Continue at 14:20:20 logs `exit_requested decision=proceed`, then `menu_return_started end_reason_code=saved_replay_exit`, then 14:20:23 `menu_return_observed decision=complete_confirmed scene=GameScene:lobby_loaded`, then `playback.succeeded ... reason_code=none rollback_status=not_required`, then `history.opened reason=replay_returned`. Summaries: 14:18:51 `replay_starts=0 replay_exits=0 replay_menu_returns=0`, 14:20:37 `replay_starts=1 replay_exits=1 replay_menu_returns=1 replay_session=false payload_leases_held=0` (`replay_exit_hits=1`: the `ReplayState.Exit` prefix took it). Session 3 repeats it twice, at 14:43:55 and 14:46:01. `07-s3-returned-to-history.png` | met |
| 4. In-game `Exit()` during playback: lobby, lease released, History can start a second replay right away | **On Android, `ReplayState.Exit()` cannot be reached during playback.** The pause menu greys out "Exit to main menu" until the recap (`03-s2-pause-menu-exit-disabled-during-playback.png`). The `Exit()` the patch prefixes is the recap Continue, check 3 above. "Right away" holds after that exit: in session 3, History reopened at 14:43:59.491 and the second replay's `start_requested` followed 1.5 s later at 14:44:01.046 (no `request_rejected`). It played, exited and returned, and at 14:46:20 the summary reads `replay_starts=2 replay_exits=2 replay_menu_returns=2 replay_session=false payload_leases_held=0`. The pause-menu exit at the recap is a different path; see the finding below. | met through Continue, the only `Exit()` path on Android. The pause-menu path fails on the branch and on master alike. |
| 5. Second start with bootstrap ready: `from_lobby=false` | **Not reachable from History on this device.** All 5 starts in sessions 1 to 3 log `from_lobby=true`, including the one started 1.5 s after the menu return. That start's first bootstrap stage reads `active_scene=HeroSelectScene:lobby_loaded`. The value is `!AndroidReplayBootstrap.IsBootstrapReady()`, which needs GameScene loaded with its managers. History offers Replay only from the lobby: in a run it gates Replay as "In Run", and during a replay `HasSavedReplaySession` blocks it. The computation is unchanged: `AndroidSavedReplayHost` `StartBegun` notice versus master `AndroidCombatReplayRuntime.ReplaySaved`, the same expression. Every start in the 398 evidence also logs `from_lobby=true`. | not reachable; value unchanged by construction |
| 6. No `android.replay.tick_failed` / `state_exit_failed`, no IL2CPP crash | The full logcat of sessions 1, 2 and 3 was grepped for `tick_failed\|state_exit_failed`, `SIGSEGV\|SIGABRT\|Fatal signal\|FATAL EXCEPTION`, and `ObjectDisposedException`: 0 matches in each. `main_thread_failures=0` and `card_balanced=true` in every summary. | met |

Two other summary fields matched as well:

- `history_leases` returns to 0 after every History close.
- `card_rental.released reason=scene_changed ... card_lost=0 card_balanced=true` appears across each replay.

## Finding: the pause-menu "Exit to main menu" at the recap leaks the payload lease (pre-existing)

**Path.** At the recap, the pause menu (gear) enables "Exit to main menu"
(`04-s2-pause-menu-exit-enabled-at-recap.png`). The game then loads `MainMenuScene` without
calling `ReplayState.Exit()`: `replay_exit_hits` stays unchanged and no `exit_requested` is logged.
No `StateChanged` with `PreviousState=ReplayState` reaches the listener either: no
`playback.succeeded`/`failed` and no `menu_return_started`.

**Result.** The saved-replay operation never completes. The summaries then read
`replay_session=true payload_leases_held=1` until the process restarts. Every History replay is
rejected:

```
combat_replay.playback.request_rejected source=local_saved reason_code=payload_operation_busy
```

The panel shows "Replay rejected for battle …" (`05-s1-pause-exit-then-history-replay-rejected.png`).

| Build | Pause exit | State after | History replay |
|---|---|---|---|
| Branch, session 1 | 14:21:40 | `in_replay_state=false session=true`, summaries `replay_session=true payload_leases_held=1` | 14:22:26 `payload_operation_busy` |
| master `a98b73d`, session 2 | 14:38:14 | the same: `replay_exits=0 replay_menu_returns=0 replay_session=true payload_leases_held=1` | 14:39:00 `payload_operation_busy` |

**Pre-existing, not a regression of this branch.**

- Master's `AndroidCombatReplayRuntime` reaches `HandleReplayStateExited` only from `OnStateChanged`,
  on a ReplayState to non-ReplayState transition. Its `Tick` only clears the exit latch
  (`ObserveReplayStateGone`).
- The branch keeps that wiring: `OnStateChanged` calls `SavedReplays.OnReplayStateExited()`, and
  `SavedReplayPlayback.Tick` calls `ObserveReplayStateGone`.
- Plan §12 requires this behavior to stay unchanged, so it is not fixed in this refactor.

**A fix needs its own change.** A naive "ReplayState gone while an op is active means state exit"
in `Tick` would misfire during the bootstrap. There the op is active while `in_replay_state=false`
for about 9 s: session 1 started at 14:18:59 and reached `in_replay_state=true` at 14:19:08.

## Desktop checks (plan §13 Desktop 1–4): not run

This host cannot run them.

- The macOS Steam install of The Bazaar has no BepInEx, and no desktop deploy of this branch exists
  (`just mod::build --deploy` needs the installer's trampoline repair).
- This session has no desktop GUI driver to click History, replays and the recording controls.
- Signing the desktop game in may also end the AVD's signed-in session of the test account.

These four checks are still open:

- History replay to the end and back to hero-select.
- The managed-recording restart and its failure path.
- A current-native replay's state-exit cleanup.
- Quitting during a saved replay, with no `ObjectDisposedException`.

They need a desktop run, on Windows or on a macOS install with BepInEx.

## State left on the device

The main menu, with the Vanessa run (Normal, D1) still resumable. `wm size` is the physical
1080x2400, with no override.

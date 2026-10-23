# #402 evidence: the upgrade preview's `scene_changed` release

Device: AVD `bpp_api35` (emulator-5556), game `1.0.12825-production-android-d9601ded`, BPP `6.0.0`, `[Android] UpgradePreview = true`.
Run: Vanessa, day 5, at a shop. Screenshots are cropped to the tooltip column; logs hold only `[BPP]` lines, with the MelonLoader prefix removed.

The release under test is `AndroidMod.OnSceneWasLoaded` → `AndroidUpgradePreview.Release("scene_changed")`. Before #402 it had never fired with a preview held: opening the in-run menu closes the tooltip first.

## Result

- **Exercised on the device by a probe that calls the game's own loader** (`logs/5-…`, `logs/4-…`): a scene load that starts while the tooltip's card-to-card transition runs carries the hold into the new scene, and the hold ends as `reason=scene_changed alive=true`. The counts that test the release are `upgrade_preview_entered`/`exited`, `0/0` before and `1/1` after, with `alive=true` meaning `ExitUpgradePreview` ran on the held controller. `upgrade_values_leased`/`returned` also go `0/0` → `1/1`, but that lease is scoped to one render (`CardTooltipRenderPatch` prefix and finalizer) and was returned when the render ended, before the scene load, so it is not a second proof of this release.
- **No player-reachable path observed**: no player or game sequence was found that lands a `LoadScene` inside that window. The only theoretical candidate is an asynchronous disconnect return (`GameInstance.GoBackToMainMenu`) arriving within 0.25 s of a tap; it was not shown.
- **Not reachable outside that window**: every game scene change hides the tooltip first, so the hold ends as `tooltip_hidden` before the scene loads (`logs/2-…`, `logs/6-…`). Pausing the app does the same (`logs/1-…`). No game-driven scene change fires on its own while the player idles on the board.
- **Decision: keep the release branch defensively** (no code change; one Gotcha in `docs/architecture/android-host.md`). Without it, a hold that survives into a new scene keeps its controller in upgrade preview, leaves `AndroidUpgradePreview` holding a controller of the unloading scene, and leaves `entered` one ahead of `exited` until the next tap.

## Builds

- **base**: `origin/master`, no probe. `186d6b4` for `logs/1-…`.
- **probe**: a temporary build that adds `Host/ScenePreviewProbe.cs` (commit `f46dfb4`; its branch `android/402-scene-change-probe` was deleted after the runs, so that commit is on no branch) and one `ScenePreviewProbe.Tick()` call in `AndroidMod.OnUpdate`. It reads a mode from `<Data Root>/probe-402` and, once per launch, calls the game's own `SceneLoader.LoadScene(SceneID.HeroSelectScene, true, true, …)`, the same call the navigation menu's Home button makes (`NavigationMenuController.OnHomeSelected`). `held` fires after a hold has stood 2 s with no card-to-card transition; `race` fires on the first frame a hold exists while `TooltipParentComponent.IsCardToCardTransitionActive` is true. It logs `android.probe.scene_load` with the transition flag (`transition_active`, the tooltip's card-to-card transition) and whether the tooltip still shows the held card; its `in_transition` field is `SceneLoader.IsInTransition()`, the scene loader's own state, not the tooltip transition. Unity reports the scene this load brings in as `scene.loaded name=MainMenuScene`. Built on `186d6b4` (`logs/2-4`) and again on `9ad3a86` (#418, which added the value lease; `logs/5-6`). The probe is not in the branch, the arm file was removed from the device, and the base build was deployed back.

## Logs

| File | Build | What it shows |
|---|---|---|
| `logs/1-no-probe-pause-and-offline.log` | base `186d6b4` | Hold on Pufferfish (entered 5, 19:10:57), Home key and back at 19:11:31: `exited reason=tooltip_hidden` (`TooltipParentComponent.OnApplicationFocus(false)`), no scene load. Hold on Chocolate Bar (entered 6, 19:12:09), airplane mode for about three minutes (19:12–19:15) and back: no release, no scene load, the game never noticed. The final summary's `upgrade_preview_entered=6 exited=5` is that Chocolate Bar hold still standing when the capture ends, not a leak. |
| `logs/2-probe-held-186d6b4.log` | probe `held` | `scene_load transition_active=false entered=1 exited=0` → `exited reason=tooltip_hidden` inside the `LoadScene` call → `scene_load_called entered=1 exited=1` → `scene.loaded name=MainMenuScene`. No `scene_changed`. |
| `logs/3-probe-race-two-taps-186d6b4.log` | probe `race` | Discarded attempt: a second tap landed on the loading screen and hid the tooltip (`tooltip_hidden`) before the scene loaded. Kept to show why the race was rerun with one tap. |
| `logs/4-probe-race-scene-changed-186d6b4.log` | probe `race` | One tap. `scene_load transition_active=true tooltip_shows_held=true entered=1 exited=0` → `scene_load_called entered=1 exited=0` (the game's hide returned early) → `scene.loaded name=MainMenuScene` → `exited reason=scene_changed alive=true count=1`. Summary after: `entered=1 exited=1`. |
| `logs/5-probe-race-scene-changed-9ad3a86.log` | probe `race` on `9ad3a86` | The same hit on the current master: `upgrade_values.leased leased=1 returned=0` → `scene_load transition_active=true` → `scene_load_called exited=0` → `scene.loaded` → `exited reason=scene_changed alive=true`. Summaries before: `upgrade_preview_entered=0 exited=0 upgrade_values_leased=0 returned=0`; after: `1 1 1 1`. |
| `logs/6-probe-held-9ad3a86.log` | probe `held` on `9ad3a86` | The control on the current master: `tooltip_hidden` inside the call, then the scene load, no `scene_changed`. |

Screens: `screens/held-preview-pufferfish.png` is the held preview the probe runs act on (Pufferfish, cooldown 8.0»7.0, Poison 18»28; probe build on `9ad3a86`, before the `held` load). `screens/after-app-resume-tooltip-closed.png` is the same card after the Home-key round trip in `logs/1`: the tooltip is gone.

`android.probe.*` ids exist only in the probe build. `lobby.version_check.degraded http_status=404` is the Android Platform Release Manifest not being published (Android is not released), unrelated.

## Why only that window (game source)

Line numbers are from `just mod::decompile online` of the desktop `TheBazaarRuntime` `1.0.12651` (the Android Interop Snapshot has no method bodies; the device behavior above matches it).

- Every scene change through `SceneLoader.LoadScene` and `LoadPreviousScene` calls `LoadingStarted()` before its first await (`SceneLoader.cs:244`, `:164`), which raises `Events.LoadAddressableSceneStarted` (`:104-110`). `TooltipParentComponent.OnLoadAddressableSceneStarted` (`TooltipParentComponent.cs:678`) unlocks every locked tooltip and calls `HideCardTooltipController`, whose postfix (`CardTooltipHidePatch`) releases the hold as `tooltip_hidden` because the tooltip's `CurrentCard` was cleared. That is `logs/2` and `logs/6`.
- `HideCardTooltipController` returns early while `IsCardToCardTransitionActive` (`TooltipParentComponent.cs:361`), leaving `CurrentCard` on the held card, so the postfix keeps the hold. The transition lasts 0.25 s by default (`:243`), and it also runs when the first tooltip opens (`transition_active=true` on a single tap in `logs/4` and `logs/5`). A load that starts inside it reaches `OnSceneWasLoaded` with the hold intact.
- The new scene loads before the old one unloads (`SceneLoader.cs:250` then `:258`), so at `OnSceneWasLoaded` the held controller is still alive (`alive=true`) and the release really exits its preview.
- Game-driven scene changes from the board: the run's return to the main menu (`RunManager.ReturnToMainMenu` `:70` → `MoveToScene` `:128` → `LoadScene` `:141`), reached from the menu or from `GameInstance.GoBackToMainMenu` (`GameInstance.cs:376`) on a connection or recovery failure (`NetworkManager.cs:170`, `:202`); and the end of a run (`EndRunDefeatState.cs:17`, `EndRunVictoryState.cs:17`), which follows combat, where `PvpTransitionBegan` already hid the tooltip (`TooltipParentComponent.cs:720`) and tooltips stay blocked (`:304`). All go through `LoadScene`, so all hide first.
- The client is request/response over HTTP (`HttpGameClient.SendCommand`): it learns of a lost connection only when a player command fails, and a command during a run is treated as a recoverable disconnect with a popup (`HttpGameClient.cs:123`, `NetworkManager.NotifyRecoverableDisconnect` `:134`), not a scene change. That is why the airplane-mode attempt in `logs/1` changed nothing.
- `SceneLoader.LoadSceneAdditive` (`SceneLoader.cs:322`) skips `LoadingStarted`, but its callers run where no board item can be held: run start (`StartRunAppState.cs:109`, the `LoadingScene` load in every log) and the collection screen.

So the release is reached exactly when a scene load starts within a quarter second of a tooltip opening or switching cards; the probe reproduced that with the Home button's own call, but no player-driven sequence was seen to do it, so the branch is kept as a defensive release.

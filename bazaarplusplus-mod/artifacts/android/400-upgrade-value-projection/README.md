# #400 evidence: BPP next-tier values in the Android upgrade preview

Device: AVD `bpp_api35` (emulator-5556), game `1.0.12825-production-android-d9601ded`, BPP `6.0.0`.
Run: Vanessa, day 7, at a merchant after Kev's Armory. Screenshots are cropped to the tooltip column, the settings section, or the game's connection UI; logs hold only `[BPP]` lines.

## Sessions

Each session is one launch of one build; its first log line is `android.plugin.loaded ... sha256=...`.

| Session | Plugin `BazaarPlusPlus.Android.dll` sha256 | Code | Files |
|---|---|---|---|
| final (20:01–20:33) | `4907f84037815228a54326712911606c3d0f327739bfcb599d7f8ccc83d8f5eb` | commit `16afc3c`, unchanged | `screens/final-*.png`, `logs/session-final.log`, `logs/session-final.upgrade.log` |
| probe (19:56–20:01) | `126e35549b87089af365e10f227c22cb98c1f83c644306e3cf0d1ecac01f693c` | commit `16afc3c` plus `probe/probe-sea-shell.patch`, uncommitted | `screens/item1-*.png`, `logs/session-probe.log`, `logs/session-probe.upgrade.log` |

The commits after `16afc3c` on this branch change docs and this directory only, so the code under test is still `16afc3c`. The `*.upgrade.log` files are the full session logs filtered to the plugin, scene, settings, back-key, net-message and upgrade lines, plus the last summary.

Test doubles: the probe session is the only one. Nothing in either session wrote to a production server, R2, or the analyzer beyond what the game and BPP do in normal play; no override is committed.

## 1. A value the game leaves unprojected and BPP projects (probe session)

The game fuses a value only when `CardTooltipData.RenderStylizedAttributeValue` passes `_canFuse && NextTierHasAttribute(attribute)`, and a tier's template entry lists only what that tier changes. Sea Shell's `Shield {aura.1.mod} for each Aquatic item` scales by `Custom_0` (10, 15, 20, 25), but the gate reads `ShieldApplyAmount`, which no tier entry carries. So the game prints `10` and BPP prints `10»15`.

No Sea Shell came up in the run, so this uses the fallback the task allows: a temporary probe (`probe/probe-sea-shell.patch`, never committed) that, when the board's Chum tooltip opens, renders the game's own tooltip for a copy of the card re-templated to Sea Shell at Bronze (`CardExtensions.CreateCopy`, `BuildAttributeDictionaryForTier`, a fresh `CardTooltipData` with `CanFuse = true`). The copy keeps Chum's Deadly enchantment and its tags, which is why the title reads "DEADLY Sea Shell"; that is the probe's copy, not a BPP bug.

| BPP `UpgradePreview` on | off |
|---|---|
| `screens/item1-on-sea-shell.png`: `Shield 10»15` | `screens/item1-off-sea-shell.png`: `Shield 10` |

- On (19:58:51): `android.upgrade_values.projected attribute=shield_apply_amount current=10 next=15`, the `native=` segment printing `10` and the `bpp=` segment `10<sprite name=Fusion>15`. Then `render_compared same=true first_difference=-1 bpp_differs=true projected_values=1 length=336`: the managed loop with the game's values renders exactly what the game's own `RenderTooltip` does, and BPP's text differs from it by that one value.
- Off (20:00:07 `settings.changed key=UpgradePreview value=false`, frames at 20:00:34): the probe still hands the game a fusing tooltip (`CanFuse = true`), and the game itself still prints `10`.

## The once-only render comparison on the final build

`render_compared` logs on the first leased render of a launch and on the first render in which BPP's text differs from the game's. The final session logged it once, for Handaxe at 20:03:12: `same=true first_difference=-1 bpp_differs=false projected_values=0 length=254`. The seven later holds logged none, since no tooltip in the run differs from the game's.

## Enabled and disabled on the final build

| Item | On | Off |
|---|---|---|
| Deadly Handaxe (Silver): Damage 50 → 50»90, aura +10 → +10»15 | `screens/final-on-handaxe.png` (20:03:12, `upgrade_preview.entered count=1`, `leased tier=silver next_tier=gold`) | `screens/final-off-handaxe.png` (20:04:39, `tooltip.hit` with no `entered` or `leased`) |

The switch went off through the game's settings dialog: `screens/final-settings-before-off.png`, then `screens/final-settings-off.png` (`settings.row_tapped key=UpgradePreview value=false`, 20:04:15). It went back on at 20:05:53.

## 2. Releasing during a hold

**(b) Switching the feature off during a hold is not reachable on the device; a test covers it.** The setting lives in the game's settings dialog, and opening that dialog hides the tooltip first. With Handaxe held, the back key opened the pause menu and kept the hold (`back_key.passed source=handle_escape`, 20:03:26, no `exited`); tapping SETTINGS then released it as `upgrade_preview.exited reason=tooltip_hidden` (20:03:42), before any row could be reached. `AndroidSettingsDock` calls `ReleaseUpgradePreview("switched_off")` when the row turns off; `tests/AndroidHost.Tests/AndroidSettingsDockTests.cs` `Switching_upgrade_preview_off_releases_its_tooltip` pins that call.

**(a) A network cut does not change scene; the game reconnects in place and the hold ends as `tooltip_hidden`.** Four cuts with airplane mode on the final build, each restored as soon as the wait ended; the session's scene count stayed at 6 (`Gameplay` at 20:02:21 is its last `scene.loaded`), and no LOG IN screen appeared.

| Cut | Offline | What happened |
|---|---|---|
| 1 | ~20:07–20:10:53, idle | Pufferfish held (entered 20:06:34). The game did not notice the cut; the hold stayed until the next card replaced it (20:11:22). |
| 2 | ~20:11:3x–20:15:02 | An encounter tap while offline brought up the game's "Connection Issues" OK modal and "RECONNECTING..."; on restore the resync (`net_message.hit NetMessageGameSim`, 20:15:06) arrived. |
| 3 | ~20:15:5x–20:18:42 | Pufferfish held while offline (entered 20:15:58). On restore the resync (20:18:46) hid the tooltip: `exited reason=tooltip_hidden count=4` the same second, and the game re-showed it (`entered count=5`, 20:18:50). |
| 4 | 20:19:28–20:28:51 (9 min) | Same as 3 with a longer wait: `screens/final-held-pufferfish-before-cut.png`, the modal `screens/final-cut-connection-dialog.png`, the tooltip still held offline `screens/final-cut-held-offline.png` with `screens/final-cut-reconnecting-badge.png`. Restore: `exited reason=tooltip_hidden count=7` (20:28:56), re-shown `entered count=8` (20:28:58), `screens/final-cut-after-restore.png`. Closed by a tap outside: `exited count=8` (20:30:36). |

So in this session every held preview ended through `tooltip_hidden`. None of the cuts is a scene switch; section 3 covers scene switches.

**Leased equals returned.** Every summary in `logs/session-final.log` has `upgrade_values_leased == upgrade_values_returned`, and `upgrade_preview_entered == upgrade_preview_exited` whenever no tooltip is held. The last one (20:33:04): `upgrade_preview_entered=8 upgrade_preview_exited=8 upgrade_values_leased=8 upgrade_values_returned=8 card_balanced=true`.

## 3. Scene switch (from #402, same release code)

The final session switched no scene after its first hold: its last `scene.loaded` is `Gameplay` at 20:02:21, before the first `entered` at 20:03:12. The scene-switch evidence is the #402 runs on build `9ad3a86` (plugin sha256 `4eba6433794d…`). The release those runs exercise is unchanged in `16afc3c`.

- `artifacts/android/402-scene-changed/logs/5-probe-race-scene-changed-9ad3a86.log`: the #402 probe calls the game's own `SceneLoader.LoadScene` during the card-to-card transition, so the hold reaches the new scene. Lines: `upgrade_preview.entered controller=-63108 count=1` (19:32:22), then `scene.loaded name=MainMenuScene` and `upgrade_preview.exited reason=scene_changed alive=true count=1` (19:32:25). The summaries go from `upgrade_preview_entered=0 upgrade_preview_exited=0` to `1/1`. `upgrade_values_leased`/`returned` also go from `0/0` to `1/1`. That lease covers a single render and is returned when the render ends, so it does not prove this release.
- `artifacts/android/402-scene-changed/logs/6-probe-held-9ad3a86.log`: the probe loads a scene outside the transition while a card is held. The game hides the tooltip inside the load, so `exited reason=tooltip_hidden alive=true count=1` (19:36:11) comes before `scene.loaded name=MainMenuScene` (19:36:14). The counts are again `1/1`, with no `scene_changed`.

Why the `9ad3a86` runs still cover `16afc3c`:

- `git diff 9ad3a86 16afc3c -- bazaarplusplus-mod/src/BazaarPlusPlus.Android/GameInterop/AndroidUpgradePreview.cs` is empty.
- In `AndroidMod.OnSceneWasLoaded`, `AndroidUpgradePreview.Release("scene_changed")` is still the first call after the `android.scene.loaded` log line. The later commits only added the History panel's `OnSceneLoaded` and `AndroidCardRental.ReleaseAll("scene_changed")` after it.
- In `Patches/CardTooltipPatch.cs`, `CardTooltipRenderPatch` is unchanged: its prefix calls `AndroidUpgradePreviewValues.Prepare` and its finalizer calls `AndroidUpgradePreviewValues.Return`. The only change to that file in the range is in `EnchantPreviewPassivePatch`.

No in-run exit-to-menu round trip was recorded on `16afc3c`.

## Shared acceptance (#361)

- Enabled and disabled on the AVD: the Handaxe pair above.
- Objects opened in a row: Pufferfish to another board card (`exited reason=replaced` 20:11:22, new controller `-47480`) and again at 20:19:22 (controller `-94174`). Each new card released the previous controller before entering its own; no controller appears in two open holds.
- Close with no cross-use (final session): every hold ended `replaced` or `tooltip_hidden`, the last one by a tap outside (`exited count=8`, 20:30:36), and each next `entered` names its own controller.
- Scene switch with no cross-use: section 3. `artifacts/android/402-scene-changed/logs/5-probe-race-scene-changed-9ad3a86.log` shows `scene_changed alive=true` with entered/exited going from `0/0` to `1/1`, and `artifacts/android/402-scene-changed/logs/6-probe-held-9ad3a86.log` shows `tooltip_hidden` before the scene load. The release code those runs exercise is unchanged in `16afc3c`. The network cuts are not counted as scene switches.
- Rental and lease counts: `upgrade_values_leased` 8 = `returned` 8; the upgrade preview rents no cards (`card_rented=0`, `card_balanced=true`).

## Not committed

- logcat and the whole `Latest.log`.
- The BPP database: its `runs` row carries `player_account_id`, and this feature writes nothing to it.
- Main-menu frames, which show the account name.

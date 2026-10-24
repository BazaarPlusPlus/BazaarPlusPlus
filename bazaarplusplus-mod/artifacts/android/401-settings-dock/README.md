# Android settings in the game's native settings dialog (#401)

Device evidence for the Android BPP settings section inside the game's own settings dialog. The rows are clones of the dialog's native toggle, dropdown and button. They are driven by Core's `SettingsDockEntryRegistry` and `BppSettingsDockCatalog`, the same as on the desktop. The BPP touch button only opens the panels that the desktop opens by hotkey.

Everything here was captured on one final build. Older captures from builds before the final commit were removed.

## Build and sessions

| | |
|---|---|
| Commit | `2c1a0a8` on `android/401-settings-finish` |
| `BazaarPlusPlus.Android.dll` sha256 | `14f62232615d262af72165de55970cf9e8bb762018fa266e5dac7f3dfe695c6f` |
| `BazaarPlusPlus.Core.dll` sha256 | `45096f73c408b96faf95e68cd55e1b7260d4c9c247fb4646e3d63a14082d562b` |
| Device | emulator `bpp_api35` (420 dpi), game `1.0.12825-production-android`, BPP 6.0.0 |
| Screens | 2400x1080 (safe area 136..2264); 2560x1600 through `adb shell wm size 2560x1600`, reset with `wm size reset` afterwards |

Every session logs `android.plugin.loaded ... sha256=14f62232…`, the hash above.

| Session | Started | `[Dev] ReleaseManifestPort` | What it covers | Screenshots |
|---|---|---|---|---|
| S1 | 21:11:16 | 0 (default, production manifest) | Every row on and off. Enchant Preview OFF, ON and AUTO. Both screen sizes, drag scroll and the back key. Rentals across panel close, switch-off and scene change. The game update row after a failed check. | `s1-*.png` |
| S2 | 21:56:34 | 8765 (local test double) | Game update row ACTIVE, and its link | `s2-*.png` |
| S3 | 22:02:00 | 0 (default restored) | Production manifest back (404, degraded). The AUTO enchant probe across run days 1 to 6. | none |
| S4 | 23:23:32 | 0 | Settings restored to their values before the work. The touch menu closes on an outside tap. | none |

`bpp.log` holds the `[BPP]` lines of these four sessions, each headed `### game session S<n>`. The lines come from `grep '\[BPP\]'` of MelonLoader's `Latest.log` and carry no account or user ids. The `android.hooks.summary` lines are periodic and their counters are cumulative per session.

Screenshots are cropped to the BPP region. No main-menu shot shows the account header.

## Rows: on/off contrast

The times are `bpp.log` timestamps.

| Row | Setting | Off | On | Log |
|---|---|---|---|---|
| Upgrade Preview | `[Android] UpgradePreview` | `s1-upgrade-preview-off.png` | `s1-upgrade-preview-on.png` | On: `upgrade_preview.entered` (21:13:48). `changed ... UpgradePreview value=false` (21:14:49), with no `entered` while it is off. `value=true` (21:20:44), then `entered` again (21:21:08, 21:21:24). |
| Enchant Preview | `[EnchantPreview] Mode` | `s1-enchant-off.png` | `s1-enchant-on-rendered.png` | `Mode value=always` (21:20:58), then `enchant_preview.rendered count=1 lines=11` (21:22:07) in a run with Upgrade Preview off (21:22:01), since the upgrade preview wins over the enchant preview. `value=off` (21:22:37). `value=auto_on_pedestal_choice` (21:23:16). The dropdown is shown in `s1-enchant-dropdown-options.png`. |
| Collection | `[Android] CollectionPanel` | `s1-touch-menu-all-off.png` | `s1-collection-open.png`, `s1-touch-menu-collection-only.png` | `value=false` (21:17:44), `value=true` (21:18:46) |
| Ten-Win Builds | `[Android] LiveBuildPanel` | `s1-touch-menu-all-off.png` | `s1-ten-win-builds-open.png`, `s1-touch-menu-collection-tenwin.png` | `value=false` (21:17:46), `value=true` (21:19:15) |
| History | `[Android] HistoryPanel` | `s1-section-panels-off-game-history-dimmed.png` (the game history row is dimmed with its reason) | `s1-history-boards.png`, `s1-touch-menu-all-on.png` | `value=false` (21:17:47), `value=true` (21:19:43) |
| BPP Touch Button | `[Android] TouchMenu` | `s1-touch-button-off.png` | `s1-touch-button-on.png` | `value=false` (21:20:10), `value=true` (21:20:42) |
| Game update | button | `s1-game-update-check-failed.png` ("could not check", S1) | `s2-game-update-active.png`, `s2-game-update-link.png` (S2) | See "Game update row" below |

The whole section with every row on: `s1-section-all-on-2400x1080.png`.

### Enchant Preview AUTO

AUTO hides the preview away from enchant pedestals:

- S1: `enchant_preview.pedestal_changed screen=choice ... kind=none` (21:13:48), and no preview in `s1-enchant-auto-off-pedestal.png`.
- S3 played run days 1 to 6 in AUTO and logged 19 `pedestal_changed`: choice ×9, encounter ×5 and level_up ×4, all `kind=none`, plus one `level_up ... kind=upgrade` (22:45:30).
  - The session ended with `enchant_preview_pedestal_shown=0 enchant_preview_pedestal_hidden=39`.

The shown half (the preview at an enchant pedestal) was not captured on the final build. In six days the game offered no enchant pedestal, and pedestal offers are random. Meanwhile the unit tests cover it:

- The AUTO rule in `SettingsDockCore.Tests`.
- The pedestal catalog names in `AndroidHost.Tests`.

A follow-up issue tracks the device capture.

## Layout, drag scroll and the back key

| Screen | Logged sizes | Drag | Back key |
|---|---|---|---|
| 2400x1080 | `section_visibility state=opened ... min_hit_px=126 ... nav_list_height=1000 nav_inside=true` (21:12:20) | `settings.scrolled ... dragging=true` (21:12:26) | `back_key.passed source=handle_escape` (21:12:52) |
| 2560x1600 | `min_hit_px=127 ... nav_list_height=1481 nav_inside=true` (21:24:31). The dropdown spacing shrinks to fit: `touch_targets reason=refreshed ... canvas_scale=0.741 spacing_before=127.5 spacing=47` (21:24:30). | `dragging=true` (21:25:20, 21:25:32) | `back_key.passed` (21:25:34) |

Screenshots: `s1-section-2560x1600.png` and `s1-collection-2560x1600.png`. Collection was closed by the back key at 2560x1600: `collection.closed reason=back_key returned=40 rental_leases=0 rental_balanced=true` (21:26:23).

## No leftovers, rentals balanced

- Panel closes:
  - Collection: `returned=32` (21:16:39), `40` (21:26:23) and `32` (21:55:08), each `rental_balanced=true`.
  - Ten-Win Builds: `live_build.closed ... returned=18 rental_leases=0 rental_balanced=true` (21:17:01).
  - History: `history.closed ... returned=16 rental_leases=0` (21:17:27).
- Switch-off: `card_rental.released reason=switched_off ... card_rented=66 card_returned=66 ... card_leases=0 ... card_balanced=true` (21:17:44).
- Scene change: `scene.loaded name=EndOfRunRevampScene`, then `card_rental.released reason=scene_changed ... card_rented=106 card_returned=106 ... card_leases=0 ... card_balanced=true` (21:31:09). The MainMenuScene and Gameplay loads after it take no new leases until a panel opens.
- Collection relays out on a screen change without holding cards: `collection.relaid_out ... open=false rental_leases=0` (21:26:14, 21:40:22).

The scene changes here start with every panel closed. A user cannot change scene while a panel is open, because an outside tap closes the touch menu first: `ui.panel_closed reason=outside_tap` (S4, 23:27:27). A scene change that the game starts while a panel is open is covered by the `scene_changed` release path, and the follow-up issue tracks its device capture.

## Game update row

The production Android manifest returns 404, so S1 and S3 show the check-failed state:

- `lobby.version_check.degraded reason_code=http_failure_status http_status=404` (21:12:07, 22:03:18).
- `s1-game-update-check-failed.png` shows "Could not check for a game update", not "up to date".

S2 used a local test double for the ACTIVE state:

- `[Dev] ReleaseManifestPort = 8765` points `ReleaseManifestClient` at `http://127.0.0.1:8765/latest/android-aarch64.json`. On the emulator, `adb reverse tcp:8765 tcp:8765` reached a local static server. Nothing was written to production, R2 or the analyzer.
- The double served a copy of the Release Manifest with `game.gameVersion` set to `1.0.12900-production-android-testdbl` and an example.com APK URL.
- Log lines:
  - `version_check.dev_manifest manifest=http://127.0.0.1:8765/...` (WARNING, 21:56:43)
  - `lobby.game_update.checked ... published_game_version=1.0.12900-production-android-testdbl decision=download_apk manifest=dev_loopback` (21:58:20)
  - `ui.game_update_opened` (22:00:12), followed by the browser opening the APK URL (`s2-game-update-link.png`)

Why this still proves the BPP code path: the dev port changes only the manifest's base URL. The parsing, the game-version compare, the row's state and label, and `Application.OpenURL` all ran the same production code that the real manifest drives. The port defaults to 0 (off).

After S2:

- The port was set back to 0, the default.
- The `adb reverse` list was emptied and the server stopped.
- S3 and S4 log the production 404 again (22:03:18, 23:25:32).

The config still contains the line `[Dev] ReleaseManifestPort = 0`, which is the default.

# Android settings in the game's native settings dialog (#401)

Device evidence for moving every Android BPP setting into the game's own settings dialog. The rows are clones of the dialog's native toggle, dropdown and button, driven by Core's `SettingsDockEntryRegistry` and `BppSettingsDockCatalog`, the same as on the desktop. The custom Settings page of the touch UI is gone. The BPP touch button now only opens the panels that the desktop opens by hotkey.

Setup:

- Emulator `bpp_api35` (420 dpi), game `1.0.12825-production-android`, BPP 6.0.0, built from this branch.
- Screens: 2400x1080 (safe area 136..2264), then 2560x1600 via `adb shell wm size 2560x1600`. The size was reset afterwards with `wm size reset`.
- Screenshots are cropped to the BPP region. Main-menu shots leave out the account header. The History shot keeps only the tab bar.

`bpp.log` holds the `[BPP]` lines of every game session from this work. Each session is headed `### game session <start>`. Per-patch install lines are dropped, and only each session's last `android.hooks.summary` is kept, since its counters are cumulative.

## Rows

The dialog groups the rows by control: toggles, then the Enchant preview dropdown, then the action buttons. Within each group the order is `BppSettingsDockOrder`.

| Row | Setting | Screenshot | Log (`bpp.log`) |
|---|---|---|---|
| Upgrade Preview | `[Android] UpgradePreview` | `settings-all-on.png`, `settings-previews-panels-off.png` | `row_tapped key=UpgradePreview` and `settings.changed ... UpgradePreview value=false/true` (04:15:47, 04:20:40, 04:38:16, 04:41:18) |
| Collection Panel | `[Android] CollectionPanel` | `settings-previews-panels-off.png` | `changed ... CollectionPanel value=false` (04:20:41) and `true` (04:21:40). The menu while it was off: `touch-menu-history-only.png` |
| Ten-Win Builds Panel | `[Android] LiveBuildPanel` | `settings-previews-panels-off.png` | `changed ... LiveBuildPanel value=false` (04:20:42) and `true` (04:21:41) |
| History Panel | `[Android] HistoryPanel` | `settings-history-off-game-history-dimmed.png` | `changed ... HistoryPanel value=false` (04:22:24), then `card_rental.released reason=switched_off ... card_rented=13 card_returned=13 card_leases=0` |
| BPP Touch Button | `[Android] TouchMenu` | `settings-touch-menu-off.png`, `touch-menu-off-no-launcher.png` (no BPP button), `touch-launcher.png` (on) | `changed ... TouchMenu value=false` (04:21:42) and `true` (04:22:08) |
| Enchant Preview | `[EnchantPreview] Mode` (OFF / AUTO / ON) | `enchant-preview-dropdown-open.png` (48 dp options), `enchant-preview-auto-selected.png` | `row_tapped key=EnchantPreview control=choice value=2` and `changed ... Mode value=always` (04:56:43), then `value=1` and `auto_on_pedestal_choice` (04:57:05) |
| Game History | action, Core's `HistoryPanelSettingsDockEntry` with no hotkey | `settings-all-on.png`. Dimmed while History is off: `settings-history-off-game-history-dimmed.png`. The History panel, cropped to its tab bar: `history-tabs.png` | `row_tapped key=GameHistory control=action` then `history.opened reason=settings` (04:22:09–10). The dialog closes: `section_visibility state=closed` |
| Game update | action, opens the APK URL when the manifest names a newer game | `settings-scrolled-game-update.png` (dimmed "THE GAME IS UP TO DATE") | Not tapped. The emulator runs the newest game, so the row cannot be shown active. `AndroidSettingsDockTests.Game_update_is_offered_only_for_a_newer_game` covers the active state |

The section's entry in the dialog's navigation is shown in `settings-nav-entry.png`.

## Touch fit

- **2400x1080:** `settings.touch_targets ... min_hit_px=126 min_option_px=126 target_px=126 hit_overlaps=0`. 48 dp at 420 dpi is 126 px.
- **2560x1600:**
  - `touch_targets reason=screen_changed screen=2560x1600@420 ... min_hit_px=126 hit_overlaps=0` (04:23:05). The geometry re-syncs on the size change.
  - Screenshots: `settings-2560x1600.png` and `touch-menu-2560x1600.png`.
- **Drag scroll:** `settings.scrolled ... dragging=true` (04:22:38 and 04:22:51).
- **Back key:**
  - Closes the top-most BPP surface first, logged as `back_key.consumed target=touch_menu` (04:21:35), `target=history` (04:22:18), `target=collection` (04:42:18, 04:43:38) and `target=live_build` (04:43:51).
  - With none open, it passes to the game, whose escape closes the settings dialog: `back_key.passed` then `section_visibility state=closed` (04:16:03, 04:41:25).
- **Unit tests:** the Unity-free layout math is tested in `tests/SettingsDockCore.Tests`. That covers `TouchLauncherLayout` across five screens and `TouchMetrics.MissingTargetPixels`.

## Enchant preview in AUTO (`AutoOnPedestalChoice`)

- **Probe:** `GameInterop/AndroidChoicePedestals` reads the template ids of the current choice screen. Core's `ChoiceScreenPedestalResolver` and `EnchantPreviewVisibility` then decide, the desktop's rule.
- **Two pedestals, encounter screen:**
  - Log: `pedestal_changed screen=encounter template_ids=2 kind=enchant enchantments=Radiant,Deadly` (04:36:56), then `enchant_preview.rendered`.
  - Screenshot: `enchant-auto-encounter-two-pedestals.png` (Chum shows the Deadly and Radiant lines).
- **One pedestal:**
  - Log: `pedestal_changed screen=pedestal ... kind=enchant enchantments=Deadly` (04:39:55), then `enchant_preview.rendered`.
  - Screenshot: `enchant-auto-at-deadly-pedestal.png` (Deadly only).
- **No pedestal:**
  - Log: other screens log `kind=none` or `kind=upgrade` and render no preview. Examples: choice at 04:40:50, upgrade pedestal at 04:27:16.
  - Screenshot: `enchant-auto-no-pedestal-no-preview.png` (day 7, Piranha, no enchant lines).
- **Precedence:** the upgrade preview takes precedence over the enchant preview on the item whose upgrade preview is open. That item shows its next tier, not the enchantment lines.

## Scene switches

At the switch to the main menu, the log shows `card_rental.released reason=scene_changed leases=0 ... card_rented=68 card_returned=68 ... card_balanced=true` (04:44:12). When their panels close, Collection and Live Build log `rental_leases=0 rental_balanced=true`, and History logs `returned=13 rental_leases=0`.

## Desktop rows not on Android

Each of these rows sets a feature the Android host does not have yet, so a row would change nothing:

- Name override, fixed supporter list, cosmetic names.
- Bilingual item names.
- Event preview, quest preview.
- End-of-run screenshot. Android Bundles are Run-only.
- BazaarDB upload. The touch switch for `[BazaarDB] UploadScreenshots` waits on the account-link form.
- Combat status bar, legendary position.
- Voice subtitles and their position and font scales.
- Graphics upscaling and sharpness.
- Upgrade preview activation. It sets whether Shift is held or toggled, and Android has no Shift. Android uses the Upgrade Preview switch instead, and a tap opens the tooltip.

`[Localization] ChineseLocaleMode` is read on Android for BPP's own strings, but it has no row yet, because it is not among this issue's features.

## Known notes

- The Chinese OFF status of the Enchant preview is "按键显示" (show on key). That is the desktop wording, which implies a hold key Android does not have.
- The ENCHANT PREVIEW label wraps to two lines in English (see the screenshots). It does not overflow the row.
- The first Collection open in a session builds the catalog, which takes several seconds: `collection.catalog_built` at 04:42:53.
- The launcher's old "BPP !" update marker is gone. The update now shows as the Game update row in the dialog.

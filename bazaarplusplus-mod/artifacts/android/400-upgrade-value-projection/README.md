# #400 evidence: BPP next-tier values in the Android upgrade preview

Device: AVD `bpp_api35` (emulator-5556), game `1.0.12825-production-android-d9601ded`, BPP `6.0.0.prod`.
Run: Vanessa, day 5, at the Mittel shop (Level Up taken). Screenshots are cropped to the tooltip column or the BPP panel; logs hold only `[BPP]` lines.

Builds:

- **final**: branch `android/400-upgrade-value-projection` at `638d7c5` (rebased on `origin/master` `186d6b4`, gate set green). Everything under `a-*`, `c-*`, and `logs/session-final-build*.log` comes from one launch of this build (18:46–18:53).
- **game-only**: `origin/master`, whose touch upgrade preview only enters the game's own `CardController.EnterUpgradePreview` and projects nothing itself. Used as the control for (b).
- **pre-fix**: the branch before the fix below (`746ba4d` on the first rebase, the same code as `20a8802`). Used only for the `fix-before-*` frames.
- **probe**: temporary, uncommitted diagnostic builds on top of pre-fix (extra log events `android.upgrade_values.probe_*`). Used only for the `logs/probe-*.log` files; no probe is in the branch.

## (a) The same held item, `[Android] UpgradePreview` off vs on

The run launched with the setting off; the BPP panel then switched it on (`a-panel-upgrade-preview-on.png`, `android.settings.changed ... value=true` at 18:48:18), and the same three items were opened again.

| Item | Off | On |
|---|---|---|
| Pufferfish (Silver): cooldown 8.0 → 8.0»7.0, Poison 18 → 18»28 | `screens/a-off-pufferfish.png` | `screens/a-on-pufferfish.png` |
| Deadly Handaxe (Bronze): Damage 25 → 25»45, aura +5 → +5»10 | `screens/a-off-handaxe.png` | `screens/a-on-handaxe.png` |
| Chum (Bronze): aura 3% → 3»6% Crit Chance | `screens/a-off-chum.png` | `screens/a-on-chum.png` |

Off: no `upgrade_preview.entered` or `upgrade_values.leased` (summaries 18:46:23–18:48:23 all 0). On: each open logs `android.upgrade_preview.entered` and `android.upgrade_values.leased tier=... next_tier=...` (18:48:24, 18:48:30, 18:48:37).

## (b) A value the game does not project and BPP does: **not met on this game build**

No such value was found. The game build on the device fuses every value token in every tooltip opened:

- `upgrade_values_projected` is 0 in every `android.hooks.summary` of every branch session. `android.upgrade_values.projected` is logged only when BPP's segment differs from the game's own, so not one value differed.
- About 13 different items across board and shop, Bronze and Silver: Deadly Handaxe, Piranha, Grappling Hook, Chum, Pearl, Chocolate Bar, Sharkclaws, Pufferfish (bought, with Toxic Weapons' +8 Poison runtime delta: 18»28), Sextant, Cauterizing Blade, Scimitar of the Deep, Shovel, Katana.
- The game-only build renders the same next-tier values: `screens/b-game-handaxe.png`, `b-game-chum.png`, and `b-game-pufferfish.png` match `a-on-handaxe.png`, `a-on-chum.png`, and `a-on-pufferfish.png`, including the cooldown clock (8.0»7.0) and the runtime-delta Poison.
- `logs/probe-0-plain-tokens.log` (probe): every value token resolved through the projection (`probe_value ... resolved=true`, 24 values). All but one came with the game's own component (`native_component=true`), which the game fuses itself. The one that did not, Deadly's `This has +50% Crit Chance`, does not change between tiers.
- `logs/probe-1-text-vs-native.log` (probe): BPP's whole rendered tooltip text against the game's own `RenderTooltip` for the same builder. The only differences were the keyword icons fixed below (`same=false`); the values were identical.

So on `1.0.12825` the classes the projection exists for (literal ability or aura values the next tier's template does not carry, multiplicative auras) did not occur in this run's items. The projection runs on every value and agrees with the game everywhere it was observed.

## Fix found by this evidence (`638d7c5`)

With the feature on, the pre-fix build drew a second keyword icon after a value BPP had rendered: `Deal 25»45 ✸Damage` and `gain 3»6% ⊕Crit Chance`, where the game draws `Damage` and `Crit Chance` without an icon.

- Before: `screens/fix-before-handaxe.png`, `screens/fix-before-chum.png`. After: `screens/a-on-handaxe.png`, `screens/a-on-chum.png`. Game-only: `screens/b-game-handaxe.png`, `screens/b-game-chum.png`.
- `logs/probe-1-text-vs-native.log`: `probe_text same=false` lines. The only difference is the `<sprite name=Damage>` / `<sprite name=CritChance>` that BPP's `ColorKeywords` call added.
- `logs/probe-2-bool-variants.log`: `probe_variants v1=false v2=true v3=true v4=true`. The same `ColorKeywords` call drew no icon when `hasIconAlready` was a literal `true` (v1), but drew one when it was the `renderedWithIcon` value read through the IL2CPP `ValueTuple` proxy (v2–v4), although C# read that value as true (`probe_twice has=true`).
- Fix: `AndroidGameIds.Canonical` stores that bool as 0 or 1 before it goes back to the game. Test: `tests/AndroidHost.Tests/AndroidGameIdsTests.cs`. Gotcha: `docs/architecture/android-host.md`.

## (c) Different items in a row, closing, a scene switch, toggling off: no cross-use, no leftovers

All lines are in `logs/session-final-build.upgrade.log`.

- **Items in a row**: ten opens 18:48:24–18:49:33 across board and shop items (Silver→Gold and Bronze→Silver alternate in `upgrade_values.leased`). Each new item released the previous one: `upgrade_preview.exited reason=replaced` ×6, `reason=tooltip_hidden` ×4. The last frame is `screens/c-row-last-shop-item.png`.
- **Leases balanced**: in every summary `upgrade_values_leased` equals `upgrade_values_returned` (3/3, 7/7, 10/10, 11/11, 12/12). A lease lives only for one tooltip render. `upgrade_preview_entered` equals `upgrade_preview_exited` whenever no tooltip is open (10/10 at 18:49:53, 11/11 at 18:50:23, 12/12 at 18:52:24).
- **Scene switch**: Pufferfish held (`screens/c-held-before-scene-switch.png`, entered 18:50:09). The in-game menu opened through the gear and closed the tooltip (`exited reason=tooltip_hidden`, 18:50:19). Exit to main menu (`MainMenuScene` 18:50:56), Resume (`Gameplay` 18:51:31): counters 11/11/11/11 before and after. Opening Pufferfish again (`screens/c-after-scene-switch.png`) leased a fresh projection (`leased=12 returned=11`, then 12/12). The tooltip was already closed when the scene loaded, so this run did not exercise the `scene_changed` release.
- **Toggle off**: opening the BPP panel hides the open tooltip (`ui.panel_opened` and `exited reason=tooltip_hidden` at 18:52:13), so the switch lands between uses, not during a hold. The setting turned off at 18:52:16 (`screens/c-panel-upgrade-preview-off.png`). Opening Pufferfish again shows the plain tooltip (`screens/c-after-toggle-off-pufferfish.png`): no `entered`, no `leased`. Afterwards `tooltip_hits` rises (30→32) while `upgrade_preview_entered` and `upgrade_values_leased` stay at 12.

## Not committed

- logcat and the whole `Latest.log`.
- The BPP database: its `runs` row carries `player_account_id`, and this feature writes nothing to it.
- Main-menu frames, which show the account name.

The Desktop UI's controls, drawn flat on one native shape so the game's own art (cards, boards, portraits, trophies, rank badges) leads the screen and BPP's controls sit behind it.

Source: `bazaarplusplus-mod/src/BazaarPlusPlus/Infrastructure/DesktopKit/` (`DesktopKit`, `KitButton`, `KitPalette`), sizes from `InGameSizes.g.cs` and colours from `InGameTheme.g.cs`, both generated from `design/tokens.json`. The shape is owned by `GameInterop/NativeSkin/DesktopNativeSkin.cs`. Debug builds show every control in every state in the Kit Gallery (`just mod::ui-shots kit-gallery`). Panels move to the kit one at a time; until then they keep the look `InGamePanel/` describes.

## Hierarchy

The game ships its UI font in one weight, so weight never separates levels. Size and colour do:

| Role | Size | Colour | Use |
|---|---|---|---|
| Title | `ingame-text-title` 26 | `ingame-ink` | One per panel. |
| Section | `ingame-text-section` 18 | `ingame-ink` | A block's heading. |
| Body | `ingame-text-body` 16 | `ingame-ink` | Row titles, md button labels. |
| Meta | `ingame-text-meta` 14 | `ingame-ink-muted` | Secondary facts, sm button labels, chips. |
| Caption | `ingame-text-caption` 12 | `ingame-ink-muted` | Counts, page labels, the supporter line. |

Surfaces step up in lightness and take no outline: `ingame-backdrop` is the ground, `ingame-panel` a rail or well, `ingame-button` a control's plate, `ingame-button-selected` a selected control or row, `ingame-dialog` a floating surface. A field is the reverse, `ingame-backdrop` on `ingame-panel`, so it reads as sunk.

Emphasis has a budget:

- One primary button per view.
- `ingame-accent` marks only the primary button, a selected control's label and a key number.
- `ingame-success`, `ingame-failure`, `ingame-warning` and `ingame-pending` colour only status text beside a word.
- A destructive action is a danger button: a secondary plate with `ingame-failure` text. The `ingame-failure` plate is only the confirm button of its dialog.

## Shape

Every plate is the game's `Btn_Rct_Frame_Fill_Solid_TUI` (white, 9-sliced), tinted by `Image.color` with one token, at `DesktopNativeSkin.ShapePixelsPerUnit` (its 30 px slice draws 8 units deep). There is no frame sprite and no texture. A state changes the tint, never the sprite. Until the shape loads, or if the game build lacks it, a plate is a plain rectangle in the same colour.

## Components

| Component | Size | Rest | Hover | Pressed | Selected ("on") |
|---|---|---|---|---|---|
| Button primary | `control-md` / `control-sm` | `ingame-accent`, `ingame-backdrop` label | `ingame-accent-bright` | hover × `ingame-tint-pressed` | — |
| Button secondary | same | `ingame-button`, `ingame-ink` label | `ingame-chip` | hover × `ingame-tint-pressed` | `ingame-button-selected`, `ingame-accent` label |
| Button quiet | same | no plate, `ingame-ink-muted` label | `ingame-plate-hover`, `ingame-ink` label | hover × `ingame-tint-pressed` | — |
| Button danger | same | secondary plate, `ingame-failure` label | as secondary | as secondary | — |
| Button danger confirm | same | `ingame-failure`, `ingame-backdrop` label | lighter | hover × `ingame-tint-pressed` | — |
| Icon button | square `control-md` / `control-sm` | as its kind, with a drawn icon | | | |
| Segmented | track `control-md` / `control-sm` | `ingame-button` track; options without a plate, `ingame-ink-muted` | `ingame-plate-hover` | | `ingame-button-selected`, `ingame-accent` |
| Chip | `badge-height` | `ingame-chip`, `ingame-ink-muted` (or a tier colour) | between chip and active | | `ingame-chip-active`, `ingame-ink` |
| Checkbox | 20 box | `ingame-chip` box | | | `ingame-accent` box, `ingame-backdrop` check |
| Select, Search | `control-md` | `ingame-backdrop` plate; value `ingame-ink`, placeholder `ingame-ink-muted` | `ingame-plate-hover` | | |
| Section header | Section text | Caption at the right; a hairline of `ingame-rule` at 0.4 below | | | |
| List row | the consumer's height | no plate; a hairline of `ingame-rule` at 0.22 between rows | `ingame-plate-hover` | | `ingame-button-selected`, no hairline touching it |
| Day strip | one `ingame-button` track | each day: Caption day, a 34 portrait, an outcome word | `ingame-plate-hover` | | `ingame-button-selected`, day in `ingame-accent` |
| Tooltip, dialog | content | `ingame-dialog` one unit inside an `ingame-rule` plate at 0.55 | | | |

Every control has two more states. **Disabled** multiplies the rest plate by `ingame-tint-disabled` and fades the label. **Dimmed** (shown as unavailable but still pressable, so the press can say why) uses the disabled plate with an `ingame-ink-muted` label, and `ingame-tint-dimmed-hover` under the pointer.

Button labels sit 18 (md) or 13 (sm) from the plate's sides, with `space-6` between an icon and its label.

## The consumer provides

Its layout (rects or layout groups), its labels from `LocalizedTextSet`, its actions, which control is on, and the native art for content (portraits, trophies, rank badges). The kit draws only plates, text, hairlines and its own stroke icons.

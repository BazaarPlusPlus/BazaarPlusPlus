The Desktop UI's controls, drawn with the game's own button sprites and icons, so a BPP panel reads as a screen of the game. The sprites are the game's thinnest set (the `_L` centres and the hairline frame), so its art for content (cards, boards, portraits, trophies, rank badges) still leads.

Source: `bazaarplusplus-mod/src/BazaarPlusPlus/Infrastructure/DesktopKit/` (`DesktopKit`, `KitButton`, `KitPalette`), sizes from `InGameSizes.g.cs` and colours from `InGameTheme.g.cs`, both generated from `design/tokens.json`. The sprites are owned by `GameInterop/NativeSkin/DesktopNativeSkin.cs` (`DesktopKitArt`). Debug builds show every control in every state in the Kit Gallery (`just mod::ui-shots kit-gallery`). Panels move to the kit one at a time; until then they keep the look `InGamePanel/` describes.

## Hierarchy

The game ships its UI font in one weight, so weight never separates levels. Size and colour do:

| Role | Size | Colour | Use |
|---|---|---|---|
| Title | `ingame-text-title` 26 | `ingame-ink` | One per panel. |
| Section | `ingame-text-section` 18 | `ingame-ink` | A block's heading. |
| Body | `ingame-text-body` 16 | `ingame-ink` | Row titles, md button labels. |
| Meta | `ingame-text-meta` 14 | `ingame-ink-muted` | Secondary facts, sm button labels, chips. |
| Caption | `ingame-text-caption` 12 | `ingame-ink-muted` | Counts, page labels, the supporter line. |

Surfaces are game sprites, not colours. The panel's ground stays `ingame-backdrop`, since boards and cards sit on it. A rail or a well is the ground of the game's match-history run card. A control's plate is the game's brown button centre. A track is that centre darkened by `ingame-tint-pressed`, and a field is the centre darkened by `ingame-art-input-tint` in the frame dimmed by `ingame-art-panel-tint`, so it reads as a sunk control on a ground. A floating surface is the run-card ground in the thin gold frame.

Emphasis has a budget:

- One primary button per view.
- The thin gold frame at full marks only the primary button, a selected control or row, a checkbox and a floating surface; a field carries it dimmed. `ingame-accent` text marks a selected control's label and a key number.
- `ingame-success`, `ingame-failure`, `ingame-warning` and `ingame-pending` colour only status text beside a word.
- A destructive action is a danger button: a secondary plate with `ingame-failure` text. The game's red centre is only the confirm button of its dialog.

## Sprites

`DesktopNativeSkin` loads each sprite by address on its own. The game slices them; a button centre or a frame draws at the scale where the sprite's own height is one `control-md`, so a chamfer or a frame line is the same size on every plate. `Shape` and `Ground` have their own fixed scales (`ShapePixelsPerUnit`, `GroundPixelsPerUnit`).

| `DesktopKitArt` | Game sprite | Use |
|---|---|---|
| `Plate`, `PlateHover`, `PlatePressed` | `Btn_Rct_Brown_L_Center_{Active,Highlighted,Pressed}_TUI` | Button plates; a state is the game's sprite for it |
| `DangerPlate`, `DangerPlateHover`, `DangerPlatePressed` | `Btn_Rct_Red_L_Center_{Active,Highlighted,Pressed}_TUI` | The danger confirm button |
| `Edge`, `EdgeBright` | `Btn_Rct_Frame_L_TUI`, `Btn_Rct_Frame_L_Highlighted_TUI` | The thin gold frame over a plate |
| `Ground` | `Btn_MatchHistory_Run_Common_Background_TUI` | Rails, wells, floating surfaces. The game slices it top and bottom only, so the skin slices its sides the same |
| `Divider` | `Btn_MatchHistory_Run_Common_Divider_TUI` | Rules under section headers and between rows |
| `IconClose`, `IconSearch`, `IconArrow` | `Icon_RingBtn_X_TUI`, `Icon_RingBtn_Search_TUI`, `Icon_Menu_Dropdown_02_TUI` | Close, search, and the arrow (turned for left and right) |
| `Shape` | `Btn_Rct_Frame_Fill_Solid_TUI` | The hover wash, and the fallback of every plate |

Until a sprite loads, or if the game build lacks its address, the plate draws the solid shape (or a plain rectangle) in the token colour the tables below give as its fallback, a rule draws a 1 px `ingame-rule` line, and the close and arrow icons draw from strokes; the search glyph has no stroke form, so the field then shows none. A frame draws only over its plate's own sprite. The Kit Gallery's diagnostics line lists every missing sprite with the reason. The check mark has no game sprite and always draws from strokes.

## Components

| Component | Size | Rest | Hover | Pressed | Selected ("on") |
|---|---|---|---|---|---|
| Button primary | `control-md` / `control-sm` | `PlateHover` in `EdgeBright`, `ingame-ink-bright` label | `PlateHover` × `OverBrightHover` in `EdgeBright` | `PlatePressed` in `Edge` | — |
| Button secondary | same | `Plate`, `ingame-ink` label | `PlateHover` × `ingame-tint-pressed`, `ingame-ink-bright` label | `PlatePressed` | `Plate` in `Edge`, `ingame-accent` label |
| Button quiet | same | no plate, `ingame-ink-muted` label | `ingame-plate-hover` wash, `ingame-ink` label | wash × `ingame-tint-pressed` | — |
| Button danger | same | secondary plate, `ingame-failure` label | as secondary, `ingame-ink-bright` label | as secondary, `ingame-ink-bright` label | — |
| Button danger confirm | same | `DangerPlate`, `ingame-ink-bright` label | `DangerPlateHover` | `DangerPlatePressed` | — |
| Icon button | square `control-md` / `control-sm` | as its kind, with the game's glyph | | | |
| Segmented | track `control-md` / `control-sm` | darkened track; options without a plate, `ingame-ink-muted` | wash | | `Plate` in `Edge`, `ingame-accent` |
| Chip | `badge-height` | `Plate` × `ingame-tint-rest`, `ingame-ink-muted` (or a tier colour) | `Plate` | `PlatePressed` | `PlateHover` × `ingame-tint-pressed`, `ingame-ink-bright` |
| Checkbox | 20 box | `Plate` in `Edge` × `ingame-art-button-tint` | `Edge` at full | | `Plate` in `EdgeBright`, `ingame-accent-bright` check |
| Select, Search | `control-md` | sunk field; value `ingame-ink`, placeholder `ingame-ink-muted`; the arrow or the search glyph in `ingame-ink-muted` | wash | | |
| Section header | Section text | Caption at the right; the `Divider` rule at 0.7 below | | | |
| List row | the consumer's height | no plate; the `Divider` rule at 0.5 between rows | wash | | `Plate` in `Edge`, no rule touching it |
| Day strip | one darkened track | each day: Caption day, a 34 portrait, an outcome word | wash | | `Plate` in `Edge`, day in `ingame-accent` |
| Tooltip, dialog | content | `Ground` in `Edge` | | | |

Fallback colours while a sprite is missing: `ingame-button` for a secondary plate and `ingame-chip` under the pointer; `ingame-button-selected` for a selected control and the primary button, `ingame-chip-active` for the primary button under the pointer and a selected chip; `ingame-failure` for `DangerPlate`; `ingame-panel` for `Ground`; `ingame-dialog` for a floating surface; `ingame-backdrop` for a field.

Every control has two more states. **Disabled** multiplies the rest plate and its frame by `ingame-tint-disabled` and fades the label. **Dimmed** (shown as unavailable but still pressable, so the press can say why) uses the disabled plate with an `ingame-ink-muted` label, and `ingame-tint-dimmed-hover` under the pointer.

Button labels sit 18 (md) or 13 (sm) from the plate's sides, with `space-6` between an icon and its label.

## The consumer provides

Its layout (rects or layout groups), its labels from `LocalizedTextSet`, its actions, which control is on, and the native art for content (portraits, trophies, rank badges). The kit draws only plates, text, rules and icons.

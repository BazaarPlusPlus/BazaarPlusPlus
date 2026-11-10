The Desktop UI's controls, drawn on BPP's own plate art: one soft rounded silhouette in a painted material, with the game's own rule and glyphs. The plates stay dark and low in contrast, so the game's art for content (cards, boards, portraits, trophies, rank badges) leads the screen.

Source: `bazaarplusplus-mod/src/BazaarPlusPlus/Infrastructure/DesktopKit/` (`DesktopKit`, `KitButton`, `KitPalette`), sizes from `InGameSizes.g.cs` and colours from `InGameTheme.g.cs`, both generated from `design/tokens.json`. The sprites are owned by `GameInterop/NativeSkin/DesktopNativeSkin.cs` (`DesktopKitArt`); the plate PNGs are in `bazaarplusplus-mod/src/BazaarPlusPlus/Resources/DesktopKit/` and are drawn by `design/kit-art.ts` (`just design::kit-art`). Debug builds show every control in every state in the Kit Gallery (`just mod::ui-shots kit-gallery`). Panels move to the kit one at a time; until then they keep the look `InGamePanel/` describes.

## Hierarchy

The game ships its UI font in one weight, so weight never separates levels. Size and colour do:

| Role | Size | Colour | Use |
|---|---|---|---|
| Title | `ingame-text-title` 26 | `ingame-ink` | One per panel. |
| Section | `ingame-text-section` 18 | `ingame-ink` | A block's heading. |
| Body | `ingame-text-body` 16 | `ingame-ink` | Row titles, md button labels. |
| Meta | `ingame-text-meta` 14 | `ingame-ink-muted` | Secondary facts, sm button labels, chips. |
| Caption | `ingame-text-caption` 12 | `ingame-ink-muted` | Counts, page labels, the supporter line. |

Surfaces are sprites, not colours. The panel's ground stays `ingame-backdrop`, since boards and cards sit on it. A rail or a well is `Ground`. A control's plate is `Plate`. A track is `Plate` darkened by `ingame-tint-pressed`, and a field is `Plate` darkened by `ingame-art-input-tint` inside `Edge` dimmed by `ingame-art-panel-tint`, so it reads as a sunk control on a ground. A floating surface is `Ground` inside `Edge`.

Emphasis has a budget:

- One primary button per view.
- The gold plate is only the primary button. The soft gold line (`Edge`) marks only a selected control or row, a checkbox and a floating surface; a field carries it dimmed. `ingame-accent` text marks a selected control's label and a key number.
- A destructive action is a danger button: a secondary plate with `ingame-failure` text. The red plate is only the confirm button of its dialog.

## Sprites

Each `DesktopKitArt` slot has one source. A plate slot is a PNG of BPP's own, embedded in the plugin and decoded once. A rule or glyph slot is a game sprite, loaded by address on its own. To change the look, replace the PNGs; the slots, the kit and this table's states stay.

| `DesktopKitArt` | Source | Use |
|---|---|---|
| `Plate`, `PlateHover`, `PlatePressed` | `plate.png`, `plate-hover.png`, `plate-pressed.png` | Control plates |
| `PrimaryPlate`, `PrimaryPlateHover` | `plate-primary.png`, `plate-primary-hover.png` | The primary button |
| `DangerPlate`, `DangerPlateHover`, `DangerPlatePressed` | `plate-danger.png`, `plate-danger-hover.png`, `plate-danger-pressed.png` | The danger confirm button |
| `Edge`, `EdgeBright` | `edge.png`, `edge-bright.png` | A soft gold line along the silhouette, over a plate |
| `Ground` | `ground.png` | Rails, wells, floating surfaces |
| `Shape` | `shape.png` | The silhouette in solid white, tinted: the hover wash |
| `Divider` | the game's `Btn_MatchHistory_Run_Common_Divider_TUI` | Rules under section headers and between rows |
| `MarkCompleted`, `MarkAbandoned`, `MarkActive`, `MarkWon`, `MarkLost` | `mark-*.png` | A run's state when it has no trophy, and a battle's outcome in the day strip |
| `IconClose`, `IconSearch`, `IconArrow` | the game's `Icon_RingBtn_X_TUI`, `Icon_RingBtn_Search_TUI`, `Icon_Menu_Dropdown_02_TUI` | Close, search, and the arrow (turned for left and right) |

### The plate art

`just design::kit-art` draws every plate PNG from one material image, `art/material.png` (the grain of a generated image of worn dark leather, in greyscale). `just design::check` fails when a committed plate differs from what `design/kit-art.ts` draws. The script owns what must agree across plates:

- **One silhouette.** A rounded rectangle, radius 6 canvas units, with a soft painted rim inside the edge and no hard line. A plate is one `control-md` tall at 3 pixels per unit (`KitPixelsPerUnit` in `DesktopNativeSkin`); the sliced border is the radius plus 4 units (`KitBorderPixels`). Change `RADIUS` in the script and that constant together.
- **States by grading.** Each state is the same grain over a fill colour near its token (`ingame-button`, `ingame-chip`, `ingame-chip-active`, `ingame-failure`, `ingame-panel`), so a centre can stretch without showing a seam.
- **Clean edges.** Sprites are drawn at 4× and reduced, and colour is bled under the transparent pixels, so bilinear filtering leaves no dark fringe.

The same command cuts the five marks from `art/marks.png` (one generated sheet of five painted emblems on a clear ground, in the order of `markNames` in the script) and reduces each to 128 pixels. A mark is content art, like a trophy: it is not sliced and not tinted.

If a plate PNG cannot be decoded, the plate draws a plain rectangle in the token colour given below. Until a game sprite loads, or if the game build lacks its address, a rule draws a 1 px `ingame-rule` line and the close and arrow icons draw from strokes; the search glyph has no stroke form, so the field then shows none. The Kit Gallery's diagnostics line lists every missing sprite with the reason. The check mark has no sprite and always draws from strokes.

## Components

| Component | Size | Rest | Hover | Pressed | Selected ("on") |
|---|---|---|---|---|---|
| Button primary | `control-md` / `control-sm` | `PrimaryPlate`, `ingame-ink-bright` label | `PrimaryPlateHover` | `PrimaryPlate` × `ingame-tint-pressed` | — |
| Button secondary | same | `Plate`, `ingame-ink` label | `PlateHover`, `ingame-ink-bright` label | `PlatePressed` | `Plate` in `Edge`, `ingame-accent` label |
| Button quiet | same | no plate, `ingame-ink-muted` label | `ingame-plate-hover` wash, `ingame-ink` label | wash × `ingame-tint-pressed` | — |
| Button danger | same | secondary plate, `ingame-failure` label | as secondary, `ingame-ink-bright` label | as secondary, `ingame-ink-bright` label | — |
| Button danger confirm | same | `DangerPlate`, `ingame-ink-bright` label | `DangerPlateHover` | `DangerPlatePressed` | — |
| Icon button | square `control-md` / `control-sm` | as its kind, with the game's glyph | | | |
| Segmented | track `control-md` / `control-sm` | darkened track; options without a plate, `ingame-ink-muted` | wash | | `Plate` in `Edge`, `ingame-accent` |
| Chip | `badge-height` | `Plate` × `ingame-tint-rest`, `ingame-ink-muted` (or a tier colour) | `Plate` | `PlatePressed` | `PlateHover`, `ingame-ink-bright` |
| Checkbox | 20 box | `Plate` in `Edge` × `ingame-art-button-tint` | `Edge` at full | | `Plate` in `EdgeBright`, `ingame-accent-bright` check |
| Select, Search | `control-md` | sunk field; value `ingame-ink`, placeholder `ingame-ink-muted`; the arrow or the search glyph in `ingame-ink-muted` | wash | | |
| Section header | Section text | Caption at the right; the `Divider` rule at 0.7 below | | | |
| List row | the consumer's height | no plate; the `Divider` rule at 0.5 between rows | wash | | `Plate` in `Edge`, no rule touching it |
| Run row | a list row | a 44 portrait well, Body title over Meta, then three fixed slots at the right: rank badge 30, trophy or mark 30, result (Meta, 46 wide, right-aligned) | wash | | `Plate` in `Edge`, result in `ingame-accent` |
| Portrait well | square, 44 in a row and 36 in a day | a sunk field; the hero's portrait cut to the plate's silhouette | | | |
| Day strip | one darkened track | each day: Caption day, a 36 portrait well, a 24 `MarkWon` or `MarkLost` | wash | | `Plate` in `Edge`, day in `ingame-accent` |
| Tooltip, dialog | content | `Ground` in `Edge` | | | |

Fallback colours when a plate PNG is missing: `ingame-button` for a secondary plate and `ingame-chip` under the pointer; `ingame-button-selected` for a selected control; `ingame-chip-active` for the primary button and a selected chip, `ingame-accent` for the primary button under the pointer; `ingame-failure` for `DangerPlate`; `ingame-panel` for `Ground`; `ingame-dialog` for a floating surface; `ingame-backdrop` for a field.

Every control has two more states. **Disabled** multiplies the rest plate and its frame by `ingame-tint-disabled` and fades the label. **Dimmed** (shown as unavailable but still pressable, so the press can say why) uses the disabled plate with an `ingame-ink-muted` label, and `ingame-tint-dimmed-hover` under the pointer.

Button labels sit 18 (md) or 13 (sm) from the plate's sides, with `space-6` between an icon and its label.

### Run rows and optical edges

- **Fixed slots.** A run row always has its rank, mark and result slots, empty or not, so trophies and marks stand on one axis down the list and a rank badge stands beside the trophy at the same size. A ranked run shows both; the badge is not shrunk into the meta line.
- **Trophy or mark.** A finished run with a trophy tier shows the game's trophy. A run without one shows the kit's mark for its state: abandoned, in progress, or completed below the first tier. An abandoned or active run has no result text.
- **Head and shoulders.** A portrait well shows the art at 1.9 wells tall, hung from the top edge, so a face has a readable size and every hero fills the well in the same way. Without a sprite the well shows `ingame-art-fallback`.
- **Edges belong to the ink.** A control with no plate at rest aligns by its content, not by its hidden plate. A list row's plate bleeds `space-12` out of the row on each side, so the portrait sits under the section header and the result under the header's caption; the column needs that room around it. A row that ends in a quiet button pulls the button out by its label inset. The Kit Gallery's rail shows both; every right-aligned item there ends on one line within 1 px.

## The consumer provides

Its layout (rects or layout groups), its labels from `LocalizedTextSet`, its actions, which control is on, and the native art for content (portraits, trophies, rank badges). The kit draws only plates, text, rules, icons and its marks.

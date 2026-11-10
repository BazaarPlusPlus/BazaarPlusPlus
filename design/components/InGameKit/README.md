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

The same command cuts the five marks from `art/marks.png` (one generated sheet of five painted emblems on a clear ground, in the order of `markNames` in the script) and reduces each to 128 pixels. A mark is content art, like a trophy: it is not sliced and not tinted. The completed mark is graded to dull iron, so its cup does not read as one of the game's trophies.

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
| Run row | a list row | a 44 portrait well, Body title over Meta, then two fixed slots at the right: rank badge 30, trophy or mark 30 | wash | | `Plate` in `Edge` |
| Portrait well | square, 44 in a row and 36 in a day | a sunk field under the hero's medallion | | | |
| Day strip | one darkened track; the days share its width and it scrolls sideways when they do not fit at 64 each | each day: Caption day, a 36 portrait well, a 24 `MarkWon` or `MarkLost` | wash | | `Plate` in `Edge`, day in `ingame-accent` |
| Tooltip, dialog | content | `Ground` in `Edge` | | | |

Fallback colours when a plate PNG is missing: `ingame-button` for a secondary plate and `ingame-chip` under the pointer; `ingame-button-selected` for a selected control; `ingame-chip-active` for the primary button and a selected chip, `ingame-accent` for the primary button under the pointer; `ingame-failure` for `DangerPlate`; `ingame-panel` for `Ground`; `ingame-dialog` for a floating surface; `ingame-backdrop` for a field.

Every control has two more states. **Disabled** keeps the rest plate, darkened by `ingame-tint-pressed`, and fades the label, so a row of controls keeps its shape. **Dimmed** (shown as unavailable but still pressable, so the press can say why) uses the disabled plate with an `ingame-ink-muted` label, and `ingame-tint-dimmed-hover` under the pointer.

Button labels sit 18 (md) or 13 (sm) from the plate's sides, with `space-6` between an icon and its label.

### The supporter strip

Every Desktop panel thanks its supporters in one form (`BPPSupporterNativeAttributionRow`): one sunk strip (a track, one md control high) under the panel's title with "Thanks", up to four names in their tier colours, and the sponsor button at the strip's right end: a secondary sm button with BPP's heart and its label in `ingame-supporter-4`. A narrow rail shows only the names that fit whole. A panel does not draw its own variant.

### Contrast

A panel's surfaces are three steps apart, so each reads against the one under it: `ingame-backdrop` (the sheet), the ground of a rail or a card (`ground.png`, lighter, inside a visible gold rim), and a control's plate (`plate.png`, lighter again, with a stronger rim). A disabled control keeps a plate. `design/kit-art.ts` owns the fills.

### Arrows

A button that pages (a list's pager, the Ten-Win Builds browser) has a plate and a large arrow: the game's arrow glyph has wide margins, so its box is 30 (md) or 24 (sm) where another icon's is 16 or 14. Where paging is a panel's main action, the consumer makes the two arrows share the rail's width and stand 48 high.

### Fields and modals

`DesktopKit.Field` is the sunk text field: one line, or several lines high for a note; `Search` is the one-line field with the glass. `DesktopKit.Modal` is a scrim over the whole panel on its own canvas in front of the game's boards, with a floating card as wide as the consumer says and as high as its children. The consumer shows and hides the modal, and decides whether a press on the scrim dismisses it (a menu) or not (a dialog with a draft).

### Run rows and optical edges

- **The list is on the left.** A panel with a list and a detail puts the list's rail on the left and what the selected item shows on the right: the list is read first and leads to everything else. The Kit Gallery has this shape.
- **Fixed slots.** A run row always has its rank and mark slots, empty or not, so trophies and marks stand on one axis at the row's right edge and a rank badge stands beside the trophy at the same size. A ranked run shows both; the badge is not shrunk into the meta line. The row has no result text: the mark says how the run ended, and the detail says the rest.
- **Trophy or mark.** A finished run with a trophy tier shows the game's trophy. A run without one shows the kit's mark for its state: abandoned, in progress, or completed below the first tier.
- **Hero medallion.** A portrait well shows the hero as a medallion (`KitArtReduction.Medallion`): a rounded plate with the hero's colour (`hero-*`) glowing behind the head and fading to `ingame-backdrop`, the head and shoulders, and a thin `ingame-accent` rim. In a run row the head rises above the plate's top edge by 0.16 of the well, so the figure breaks the frame and the sides and bottom stay inside the rim; in a day cell, under its caption, it stays inside and sinks into shadow at the bottom. `HeroPortraitFocus` names each hero's face square, chosen by eye on the default portrait, and the hero's colour; the consumer passes both to `DesktopKit.ShowPortrait`. A hero without a square gets the kit's rule (`KitArtReduction.HeadSquare`). The well is a sunk field without a sprite and while the medallion is drawn, and shows the whole portrait if it cannot be drawn.
- **Small copies.** The game's portraits, trophies and rank badges are large textures without mipmaps and shimmer at 30 or 44 units. `KitSmallArt` makes one area-averaged copy of each with its own mipmaps (a portrait's copy is its medallion); `DesktopKit.Show` and `ShowPortrait` change to the copy when it is ready.
- **Edges belong to the ink.** A control with no plate at rest aligns by its content, not by its hidden plate. A list row's plate bleeds `space-12` out of the row on each side, so the portrait sits under the section header and the mark under the header's caption; the column needs that room around it, and `DesktopKit.ScrollList` gives a scrolling list a mask that wide. A row that ends in a quiet button pulls the button out by its label inset, and one that starts in a quiet arrow by the arrow's inset. The Kit Gallery's rail shows both; every right-aligned item there ends within 1 px of the column's edge.
- **Rows and days are reused.** `KitRunRow` and `KitDay` give the consumer the texts, the portrait and the art slots, so a list rebinds its rows to other runs without building them again.

## The consumer provides

Its layout (rects or layout groups), its labels from `LocalizedTextSet`, its actions, which control is on, and the native art for content (portraits, trophies, rank badges). The kit draws only plates, text, rules, icons and its marks.

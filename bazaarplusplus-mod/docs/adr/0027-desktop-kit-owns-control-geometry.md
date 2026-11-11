# ADR-0027: The Desktop UI's controls come from one kit, drawn on art slots

Status: Accepted

## Context

ADR-0026 gave each UI system one owner for its native skin and its colours, and left the rest to each panel: "Panels keep their own geometry and state." History, Ten-Win Builds and Collection then each drew their own controls:

- Three button helpers (`HistoryPanelView.Button`, `LiveBuildPanelView.Button`, Collection's `CreateButton`), three close buttons, three supporter credits (`BPPSupporterNativeAttributionRow`, `BPPSupporterAttributionRow` and History's own), each panel's own section heading and empty state, and each its own font sizes.
- Every button stacks the gold `Btn_Rct_Frame_S` frame on a textured brown or blue centre. Beside the game's boards, portraits and trophies, each control competes with the content it frames.

A design review in game settled the look in `../design/components/InGameKit/README.md`: hierarchy comes from size and colour, since the game's font has one weight; emphasis has a budget.

The look went through three rounds in the Kit Gallery, each judged in game:

1. Every plate as one solid game shape tinted by a token. It read as blocks of colour laid over the game.
2. Plates as the game's own thin button sprites (`Btn_Rct_*_L` centres, the hairline `Btn_Rct_Frame_L` frame). The angular chamfer and the hard gold line read as too sharp, muted or not.
3. Plates as BPP's own art: one soft rounded silhouette in a painted material, with the game's rule and glyphs kept.

The lesson is that the look will change again, so the kit should not know it.

## Decision

1. **One kit owns control geometry and state painting.** `Infrastructure/DesktopKit/` builds the Desktop UI's controls: text in five roles, buttons (primary, secondary, quiet, danger, danger confirm; md and sm), icon buttons, segmented controls, chips, checkboxes, selects, search fields, section headers, list rows, the day strip and floating surfaces. Each takes a role and paints its own states; the consumer places it in its own rects or layout groups. A panel keeps its layout, data, interaction state and lifecycle. For controls a panel draws through the kit, this replaces ADR-0026 §3's "Panels keep their own geometry".
2. **A control draws art slots; each slot has one source.** `DesktopKitArt` names the slots: plates per state, the edge line, the ground, the wash shape, the rule, the glyphs and the run and battle marks. `DesktopNativeSkin` fills a plate or mark slot from a PNG of BPP's own, embedded in the plugin (`Resources/DesktopKit/`), and a rule or glyph slot from a game sprite loaded by address on its own, so a game build without one leaves the rest untouched. A state is its own slot where the kit has one; a tint token only darkens a sprite (a track, a field, a disabled control). A missing sprite falls back per part: a plate to a plain rectangle in a token colour, a rule to a 1 px line, the close and arrow icons to strokes. Changing the look is replacing the PNGs (`design/kit-art.ts` draws them from one material image, and `just design::check` gates them); the kit and its consumers do not change. The frame and centre sprites of ADR-0026, `OverBrightHover` and the `Btn_Rectangle` literal rule stay while anything draws them (the combat status bar does). Amended 2026-11-11: nothing drew `OverBrightHover` any more, so it is deleted.
3. **Sizes are tokens.** `design/build.ts` generates `InGameSizes.g.cs` from the shared `control-*`, `badge-height` and `space-*` tokens and the `ingame-text-*` type sizes, which only the Desktop UI reads; the Mobile UI keeps `TouchMetrics` (ADR-0018). The kit's few derived sizes (button side padding, icon strokes) are named constants beside it. A member of `src/BazaarPlusPlus/Infrastructure/UiTokens/Sizes.cs`, `UiSpacing.cs` or `Radii.cs` is deleted with the last panel that reads it.
4. **The colour rule covers the kit.** `InGameUiColourTests` scans `Infrastructure.DesktopKit` and the Kit Gallery like the panels (ADR-0026 §4). The desktop's dimmed-but-pressable button stays a kit state.
5. **The kit is inspectable in game.** A Debug-build Main Overlay Panel, the Kit Gallery (`kit-gallery`), draws every control in every state beside native portraits, trophies and rank badges. It has no hotkey; the Walkthrough Control opens it. The Walkthrough Control also takes `shot <name>`: the desktop host captures the game window from inside the game to `<Data Root>/ui-shots/<name>.png`, so `just mod::ui-shots` captures panels without screen-recording permission. The Android host logs the verb as unsupported.

Each panel's canvas, its shell and the IL rule against font-size literals outside the kit are decided when that panel moves to the kit, since a panel's canvas carries layout constraints of its own (Ten-Win Builds scales by height to fit four boards).

## Relationship to ADR-0003, ADR-0025 and ADR-0026

ADR-0003 rejects a unification that only looks tidier, and visual repetition alone is not evidence. The kit shares what ADR-0026 already shares, values and one runtime resource with one owner, plus the mapping from a control's role to those values; it owns no panel behaviour, no state machine and no lifecycle. The evidence is the drift the review found: the same control drawn with three paddings, three font sizes and three state rules.

ADR-0025 stands: the Mobile UI keeps `UiKit` and `TouchChrome`.

ADR-0026 stands except §3's geometry clause for kit controls.

## Consequences

- A panel moves to the kit by replacing its helpers with kit calls; its layout tests keep pinning rects.
- A kit change is checked in game: `just mod::ui-shots` captures the Kit Gallery before and after.
- The plates do not depend on the game's build. The rule and the glyphs depend on game sprites addressed by path; the Kit Gallery's diagnostics line counts the loaded sprites and names each missing one with the reason.
- The plate art's colours live in PNGs, outside the in-game colour rule, which reads code. `design/kit-art.ts` fills each plate toward a token's colour by hand; a token change does not move the art.

## Evidence

- Spec: `../design/components/InGameKit/README.md`
- Kit: `src/BazaarPlusPlus/Infrastructure/DesktopKit/`, `GameInterop/NativeSkin/DesktopNativeSkin.cs` (`DesktopKitArt`, `KitArt`, `RequestKitArt`), `src/BazaarPlusPlus/Resources/DesktopKit/`, `../design/kit-art.ts`, `../design/components/InGameKit/art/material.png`
- Sizes: `../design/tokens.json`, `../design/build.ts` (`buildInGameSizes`), `src/BazaarPlusPlus/Infrastructure/UiTokens/InGameSizes.g.cs`
- Gallery and captures: `Game/KitGallery/`, `Game/OverlayPanels/DesktopUiShot.cs`, `src/BazaarPlusPlus.Core/Game/OverlayPanels/WalkthroughControl.cs`, `scripts/ui-shots.sh`
- Rule: `tests/Architecture.Tests/InGameUiColourTests.cs`

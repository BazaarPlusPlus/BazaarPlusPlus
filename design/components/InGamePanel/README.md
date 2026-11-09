The look of a BPP panel in game: native frames and buttons on a warm brown ground, parchment text, gold emphasis. This preview stands in for the game's sprites with tinted outlines; the real panels borrow them at runtime.

Source: `InGameTheme.g.cs` (generated from the `ingame-*` tokens) for the Desktop UI and the Mobile UI. Desktop: `bazaarplusplus-mod/src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelView*.cs`, `Game/LiveBuildPanel/Ui/LiveBuildPanelView.cs`, `Game/CollectionPanel/Ui/CollectionPanelView*.cs`, `GameInterop/NativeSkin/DesktopNativeSkin.cs`. Mobile: `bazaarplusplus-mod/src/BazaarPlusPlus.Mobile/History/TouchHistory*.cs`, `bazaarplusplus-mod/src/BazaarPlusPlus.Mobile/Ui/UiKit.cs`, `NativeSkin.cs`.

## Use

- The panel ground is `ingame-backdrop`; a section inside it is `ingame-panel`, and a dialog card `ingame-dialog` over `ingame-scrim`. Ten-Win Builds sits on `ingame-backdrop-raised` with `ingame-veil` over the native Collection backdrop.
- The generic skin's buttons, tabs and frames are the game's `Btn_Rct_*` sprites: brown for a normal button, blue for the selected one, the frame tinted `ingame-art-frame-tint` on panels and `ingame-art-button-tint` on buttons. Until a sprite loads, the button shows `ingame-button` / `ingame-button-selected` and other art `ingame-art-fallback`.
- A button's hover, press and disabled states tint its sprite with `ingame-tint-*`; a resting History record sits on `ingame-tint-rest`, and an unselected tab is clear until hovered (`ingame-tint-quiet-hover`).
- Text is `ingame-ink`; metadata, timestamps and inactive tabs are `ingame-ink-muted`. Over the live combat scene use `ingame-ink-soft` with an `ingame-outline`.
- Gold is the only emphasis: `ingame-accent` for selection and key numbers, `ingame-rule` hairlines at 0.3 to 0.5 opacity between sections, and `ingame-rule-strong` for the one line that leads.
- A battle's outcome and an action's result are `ingame-success`, `ingame-failure`, `ingame-warning` (failed but kept older data) or `ingame-pending`, beside a word, never colour alone.
- The same panel has two layouts. The desktop arranges it for a mouse (`HistoryPanelLayout`, `Sizes.cs`); Android for touch (`HistoryTouchLayout` on `TouchChrome`: 48dp targets, 12sp minimum text). Colours never differ between them.

History uses the warm-brown/gold `HistoryHeaderArtwork` controls and one direct sponsor button. Desktop tabs share one textured backing; inactive records keep a subdued textured surface and edge, with a brighter gold frame for the selected record. Native portraits, rank and result badges carry the content. Its structure and responsive navigation are owned by [the panel architecture](../../../bazaarplusplus-mod/docs/architecture/panels.md). Other native content keeps its game art.

## The consumer provides

The panel's content and the native sprites; the panel draws only the colours above.

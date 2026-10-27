The look of a BPP panel in game: native frames and buttons on a warm brown ground, parchment text, gold emphasis. This preview stands in for the game's sprites with tinted outlines; the real panels borrow them at runtime.

Source: `InGameTheme.g.cs` (generated from the `ingame-*` tokens) in both mod hosts. Desktop: `bazaarplusplus-mod/src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelView*.cs`, `Game/LiveBuildPanel/Ui/LiveBuildPanelView.cs`. Android: `bazaarplusplus-mod/src/BazaarPlusPlus.Android/Ui/AndroidHistory*.cs`, `AndroidUiKit.cs`, `AndroidNativeSkin.cs`.

## Use

- The panel ground is `ingame-backdrop`; a section inside it is `ingame-panel`, and a dialog card `ingame-dialog` over `ingame-scrim`. Ten-Win Builds sits on `ingame-backdrop-raised` with `ingame-veil` over the native Collection backdrop.
- Buttons, tabs and frames are the game's `Btn_Rct_*` sprites: brown for a normal button, blue for the selected one, the frame tinted `ingame-art-frame-tint` on panels and `ingame-art-button-tint` on buttons. Until a sprite loads, the button shows `ingame-button` / `ingame-button-selected` and other art `ingame-art-fallback`.
- Text is `ingame-ink`; metadata, timestamps and inactive tabs are `ingame-ink-muted`. Over the live combat scene use `ingame-ink-soft` with an `ingame-outline`.
- Gold is the only emphasis: `ingame-accent` for selection and key numbers, `ingame-rule` hairlines at 0.3 to 0.5 opacity between sections, and `ingame-rule-strong` for the one line that leads.
- A battle's outcome and an action's result are `ingame-success`, `ingame-failure`, `ingame-warning` (failed but kept older data) or `ingame-pending`, beside a word, never colour alone.
- The same panel has two layouts. The desktop arranges it for a mouse (`HistoryPanelLayout`, `Sizes.cs`); Android for touch (`HistoryTouchLayout` on `TouchChrome`: 48dp targets, 12sp minimum text). Colours never differ between them.

## The consumer provides

The panel's content and the native sprites; the panel draws only the colours above.

# ADR-0018: Android touch surfaces share one frame in Core

Status: Accepted

## Context

The Android host grew its touch surfaces one at a time: the touch menu, History, Ten-Win Builds, and Collection. Each laid itself out with its own numbers. History used a 4 dp margin and an 8 dp padding, Ten-Win Builds 8 dp and 12 dp. The inspected card's share of the sheet and the font of its name differed between the two. The menu's close button was a square as tall as a menu row. Collection still used the desktop's fixed canvas units: on the 2400x1080 emulator its text came to about 9–13 sp and its close button and chips to about 23–26 dp. The back key closed the Collection panel even while a card was inspected. Issue #425 asks for the same entry, close button, header, button sizes, back key and safe-area handling on every surface, at the 48 dp and 12 sp floors.

## Decision

1. One Unity-free frame, `Core/Ui/TouchChrome`, owns what every touch surface shares: the sheet inside the safe area, its margin, padding and gap, the header row, the type scale, the close button's size and corner (`CloseAt`), and the tap-to-inspect sheet (`TouchInspectLayout`). Each panel's layout (`CollectionTouchLayout`, `HistoryTouchLayout`, `LiveBuildTouchLayout`, `TouchLauncherLayout`) starts from it and owns only its own content.
2. A card grid on touch chooses its column count from its width (`CollectionTouchLayout.GridColumns`) and passes it to Core's `CollectionGridLayout.Build`. The desktop keeps its fixed display case through the overload without a count, and its geometry is not derived from the touch layout.
3. The host draws the shared parts through shared helpers: `AndroidUiKit.CloseButton` and `CloseGlyph`, `AndroidUiKit.VerticalScroll`, and `Ui/AndroidScrollTracker`. History keeps its own palette and button type.
4. The back key closes a surface's top-most layer first (a dialog or inspection, then the panel). Amended 2026-10-25: the surfaces no longer keep their own back-key handlers. The Overlay Panel Host dismisses the open panel's modal layer, then closes the panel, and its Android adapter closes the touch menu last (ADR-0022).

The conventions themselves are in [android-mobile-ui.md](../architecture/android-mobile-ui.md).

## Relationship to ADR-0003

ADR-0003 rejects a unification that only looks tidier. This one is not about appearance. The surfaces share a behaviour: each one is a full sheet on the same canvas, in the same safe area, held to the same target and text floors, and closed the same way. Issue #425 asks for that sameness. The shared part is geometry; each panel keeps its own state and rendering, and History keeps its own palette. Amended 2026-10-25: this replaces "The shared part is geometry only. Each panel keeps its own state, lifecycle and rendering." Lifecycle is now shared too, through the Overlay Panel Host, because the two hosts had diverged on rules a player sees; ADR-0022 holds that evidence.

## Consequences

- A new touch surface starts from `TouchChrome` and is added to `tests/SettingsDockCore.Tests/TouchChromeTests.cs`. Those tests check that every close button has the same size and corner and that all text is at least 12 sp.
- Changing a shared margin, size or font in `TouchChrome` moves every surface at once. History's, Ten-Win Builds' and the launcher's own layout tests must still pass.

## Evidence

- Core: `src/BazaarPlusPlus.Core/Ui/TouchChrome.cs`, `src/BazaarPlusPlus.Core/Game/CollectionPanel/Ui/CollectionTouchLayout.cs`, `CollectionGridLayout.Build` in `src/BazaarPlusPlus.Core/Game/CollectionPanel/Grid/CollectionGridLayout.cs`
- Host: `src/BazaarPlusPlus.Android/Ui/AndroidUiKit.cs`, `src/BazaarPlusPlus.Android/Ui/AndroidScrollTracker.cs`, `src/BazaarPlusPlus.Android/Collection/`
- Tests: `tests/SettingsDockCore.Tests/TouchChromeTests.cs`, `tests/CollectionGridLayout.Tests/`, `tests/HistoryPanelDecisions.Tests/HistoryTouchLayoutTests.cs`, `tests/LiveBuildPanel.Tests/`
- Device evidence: `artifacts/android/425-mobile-fit/`

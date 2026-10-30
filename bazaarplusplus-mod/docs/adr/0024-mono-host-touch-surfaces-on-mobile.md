# ADR-0024: The Mono host draws touch surfaces on a mobile platform

Status: Accepted

## Context

ADR-0023 runs the desktop Mono host on iOS. The game's Mac build already carries its mobile code: `ApplicationUtils.IsMobilePlatform()` reads `Application.platform`, and the iOS engine reports `IPhonePlayer`, so the game itself shows its mobile HUD, mobile tooltips, mobile options dialog, and safe area on iOS. BPP's own desktop surfaces do not adapt: they size in desktop canvas units, open on hover, right-click and hotkeys, and label settings rows with keys a phone does not have. The Android host already solved the same fit with Core's touch layouts (`TouchMetrics`, `TouchChrome`, `CollectionTouchLayout`) and an IL2CPP uGUI kit.

## Decision

1. On a mobile platform (`GameInterop/Touch/TouchPlatform.IsMobile`, which asks the game's `ApplicationUtils.IsMobilePlatform()`), the Mono host draws a touch surface in place of a desktop one, inside `BazaarPlusPlus.dll`. There is no iOS fork and no new host assembly; ADR-0023 stands.
2. A touch surface takes its layout from Core, exactly as on Android. Its Unity side is the Android host's code ported to Mono types under `Game/Touch/`, `GameInterop/Touch/` and the panel's own `Touch/` folder. A piece both hosts can share without Unity moves to Core (the card rental ledger `CardRentalCore`, `InspectionTransition`, `TouchCollectionGridPort`).
3. The touch surface registers with the Overlay Panel Host (ADR-0022) under the desktop panel's id, so dock buttons, scene changes and the one-open-panel rule behave the same.
4. A settings row that names a key changes on a mobile platform: History drops its hotkey, and a switch (`Touch.UpgradePreview`) stands in for holding Shift.

## Consequences

- Collection is the first touch surface. History and Ten-Win Builds still draw their desktop layout on iOS until they are ported the same way.
- A change to a touch layout in Core reaches both hosts; a change to the Android uGUI kit must be mirrored in the Mono port by hand, because the two kits bind different Unity type surfaces (IL2CPP proxies and Mono).

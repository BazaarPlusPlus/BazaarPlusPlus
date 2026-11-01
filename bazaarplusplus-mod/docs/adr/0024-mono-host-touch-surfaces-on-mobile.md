# ADR-0024: The Mono host draws touch surfaces on a mobile platform

Status: Accepted. Decision 2's hand port is superseded by [ADR-0025](0025-one-mobile-ui-source-for-both-hosts.md); the other decisions stand.

## Context

ADR-0023 runs the desktop Mono host on iOS. The game's Mac build already carries its mobile code: `ApplicationUtils.IsMobilePlatform()` reads `Application.platform`, and the iOS engine reports `IPhonePlayer`, so the game itself shows its mobile HUD, mobile tooltips, mobile options dialog, and safe area on iOS. BPP's own desktop surfaces do not adapt: they size in desktop canvas units, open on hover, right-click and hotkeys, and label settings rows with keys a phone does not have. The Android host already solved the same fit with Core's touch layouts (`TouchMetrics`, `TouchChrome`, `CollectionTouchLayout`) and an IL2CPP uGUI kit.

## Decision

1. On a mobile platform (`GameInterop/Touch/TouchPlatform.IsMobile`, which asks the game's `ApplicationUtils.IsMobilePlatform()`), the Mono host draws a touch surface in place of a desktop one, inside `BazaarPlusPlus.dll`. There is no iOS fork and no new host assembly; ADR-0023 stands.
2. A touch surface takes its layout from Core, exactly as on Android. Its Unity side is the Android host's code ported to Mono types under `Game/Touch/`, `GameInterop/Touch/` and the panel's own `Touch/` folder. A piece both hosts can share without Unity moves to Core (the card rental ledger `CardRentalCore`, `InspectionTransition`, `TouchCollectionGridPort`). Amended 2026-10-31 (ADR-0025): the Unity side is the one Mobile UI tree, `src/BazaarPlusPlus.Mobile/`, which both hosts compile; the Mono host keeps only thin adapters in `Host/`, `GameInterop/Touch/` and a feature's `Game/` folder ([mobile-ui.md](../architecture/mobile-ui.md)).
3. The touch surface registers with the Overlay Panel Host (ADR-0022) under the desktop panel's id, so dock buttons, scene changes and the one-open-panel rule behave the same.
4. A settings row that names a key changes on a mobile platform: History drops its hotkey, and the Shift-mode and enchant-mode rows give way to each tooltip's own Upgrade preview and Enchantments toggles (`Game/Touch/TouchGameplayPreviews`, since ADR-0025 the Mobile UI's `Previews/TouchPreviewSwitches`), whose start state is one switch (`Touch.UpgradePreview`).
5. Game input that the Mac build compiles out of its mobile branches comes back through mobile-only Harmony patches (`[HarmonyPrepare]` on `TouchPlatform.IsMobile`) that call the game's own mobile members: the card press, hold-to-lock and swipe-up (`GameInterop/Touch/TouchCardPress` over `CardController.MainPointerUp` and `LockTooltipToggle`) and the cosmetics shelf drag (`CollectionsShelfController.ProceedMobileDrag`).
6. A desktop feature with no touch form and no Android counterpart is not mounted on a mobile platform: the held-key music note preview, the hover-and-Shift post-combat impact, and replay video recording.

## Consequences

- Every BPP surface on iOS uses the Android layout: Collection, History, Ten-Win Builds, the combat clock, the settings rows and the tooltip toggles. The saved-replay presentation repair needs no port: the Android files are the IL2CPP forms of the desktop's own repair.
- A change to a touch layout in Core reaches both hosts. The hand mirroring this record first asked for, of the Android uGUI kit and touch surfaces into a `Touch`-prefixed Mono port, is replaced by one Mobile UI source tree compiled into both hosts (ADR-0025).

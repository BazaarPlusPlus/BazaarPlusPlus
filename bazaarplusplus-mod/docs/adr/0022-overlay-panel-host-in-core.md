# ADR-0022: One Overlay Panel Host in Core for both hosts

Status: Accepted

## Context

The desktop host already ran its Main Overlay Panels through a pure lifecycle core under one MonoBehaviour, but both lived in the desktop project, so the Android host re-implemented every rule by hand, and the two hosts drifted on rules a player sees:

- Only History closed for combat on Android; Collection and Ten-Win Builds neither closed for it nor refused to open in it.
- Nothing kept the Android panels exclusive: Collection, Ten-Win Builds and History could all be open at once.
- Scene teardown was a hand-ordered chain of guarded steps in `AndroidMod.OnSceneWasLoaded`, whose comments warned that the order (panels let go of their cards, then the rental releases the rest) was load-bearing.
- Switch-off was split between a History poll and dock closures that paired each panel's close with the card release, across nine hook delegates.
- The back key went through a static list of named handlers whose priority was the registration order across three files.

The tests that pinned this read compiled IL order and registration lists, not behavior.

## Decision

1. Core owns Main Overlay Panel lifecycle for both hosts in `src/BazaarPlusPlus.Core/Game/OverlayPanels/`. `OverlayPanelHost` is a public shell that holds the panels' delegates. It keeps at most one panel open. It closes panels for a scene change by each one's `SceneChangeClosePolicy`, and closes for combat and refuses an open in it. It runs Panel Switch-Off: on the true-to-false edge of a registration's `Enabled` predicate it closes the panel if open, then notifies it. Escape goes to the open panel's modal layer first, then to the panel. Every registrant ticks each frame. Host resources are released once per frame on a scene change or a switch-off, after every close and notification. The rules stay in the internal, effect-free `OverlayLifecycleCore`, which returns directives the shell executes (ADR-0005). `OverlayPanelHostLogState` guards every callback.
2. A panel registers an `OverlayPanelRegistration` and opens or closes only through its handle's `RequestOpen` and `RequestClose`, which return an `OverlayRequestOutcome`. A request made while a lifecycle callback runs is `Busy`. An open whose callback returns false or throws rolls back as `OpenFailed`. Register adopts the predicate's first value, so a switch-off before the next frame is still an edge.
3. Each host keeps one thin adapter that feeds frames and scene tokens in. `DesktopOverlayPanels` is a MonoBehaviour that reads Escape, the toggle hotkeys and the scene. `AndroidOverlayPanels` is a plain class: the melon injects combat, the frame count and the card release as delegates, so it calls no IL2CPP (ADR-0016). It advances a scene generation on each load and answers the back key synchronously inside the game's escape hook, because the Harmony prefix decides whether the game's handler runs. It also owns the touch menu's back slot: the menu is not a Main Overlay Panel and is asked after the panels.

## Why this reopens ADR-0003

ADR-0003 asks for code evidence that two peers share behavior and ownership before they share a seam. The drift above is that evidence: the hosts were meant to follow the same rules and did not, and the order the source warned about was held by hand. The shared part is lifecycle only. Each panel keeps its own state and rendering, and each host its own input reading.

## Consequences

- A lifecycle rule changes in one place and holds on both hosts. Collection and Ten-Win Builds on Android gain the combat gate and mutual exclusion; the desktop's hotkey guards are read even while a modal is active.
- The rules are tested through behavior: `tests/OverlayPanelLifecycle.Tests/` drives the Core host, and `tests/AndroidHost.Tests/AndroidOverlayPanelsTests.cs` drives the Android adapter with fake panels. IL-order tests of the scene teardown are gone.
- A new Main Overlay Panel registers with its host's adapter and implements no close, combat or back rule of its own.

## Evidence

- Core: `src/BazaarPlusPlus.Core/Game/OverlayPanels/OverlayPanelHost.cs`, `OverlayLifecycleCore.cs`, `OverlayPanelRegistration.cs`
- Desktop: `src/BazaarPlusPlus/Game/OverlayPanels/DesktopOverlayPanels.cs`
- Android: `src/BazaarPlusPlus.Android/Ui/AndroidOverlayPanels.cs`, `src/BazaarPlusPlus.Android/Patches/BackKeyPatches.cs`, `src/BazaarPlusPlus.Android/Host/AndroidMod.cs`
- Tests: `tests/OverlayPanelLifecycle.Tests/`, `tests/AndroidHost.Tests/AndroidOverlayPanelsTests.cs`

# ADR-0025: One Mobile UI source tree compiled into both hosts

Status: Accepted

## Context

BPP has two UI forms: the desktop surfaces and the touch surfaces. The touch surfaces exist twice. The Android host has them under `src/BazaarPlusPlus.Android/Ui/`, `Collection/` and `LiveBuild/`. ADR-0024 ported them by hand to the Mono host for iOS, under `Game/Touch/`, `GameInterop/Touch/` and each desktop panel's `Touch/` folder, and asked that every later Android change be mirrored by hand. That rule did not hold. Four of the five Android commits before this record (#686, #689, #693, #694) touched no Mono twin. #693 lists enchantments in the native tooltip's main text (`AndroidEnchantPreview.InlineCandidates`); `TouchEnchantPreview` has no such member, so iOS lacks the feature.

The Android changes that adjust the game's own UI never reached iOS either. `AndroidScreenFill` lowers `LetterBoxManager._minResolution` from 16:10 to 1:1 and widens the camera's field of view (`ScreenFillFieldOfViewPatch`). `AndroidScreenFillClock` moves the day clock into the band above the board. The Mono host has neither, so an iPad (4:3, and the iOS App targets it) shows the game letterboxed.

The twins do not differ only in text. A red-team review of this plan found that small kit files differ in a few lines (`UiKit`, `ChipFlow`, `ScrollTracker`), but panel files differ structurally:

- Frame driving: one injected `AndroidTouchRoot` on Android against three `MonoBehaviour`s on Mono.
- Main-thread scopes: `AndroidMainThread.Host.Enter()` against `TouchMainThread`.
- Object liveness: `WasCollected` against `== null`.
- Casts: `Cast<T>()` and `TryCast<T>()` against C# casts.
- Overlay registration: a different adapter API on each host, with panel ids `history` against `HistoryPanel`.
- Global-namespace game types, which appear under `Il2Cpp.` on Android and cannot be aliased.
- IL2CPP containers (`Il2CppStructArray`), and BCL members netstandard2.1 lacks (`ArgumentNullException.ThrowIfNull`).
- Behavior that has already forked: encounter ids, the catalog's frame budget, and the screen-fill board top.

ADR-0012 forbids the Android host to compile linked sources, and `AndroidHostAssemblyBoundaryTests.Android_host_defines_only_its_own_types` enforces it. That rule protects Core: a linked Core file would duplicate a public Core type. The Mobile UI is not Core. It touches Unity and the game, so it cannot be one binary for an IL2CPP host and a Mono host.

## Decision

1. **Two UI forms.** The Desktop UI stays in the Mono host's `Game/` panels and mounts only on a desktop platform. The Mobile UI is one source tree, `src/BazaarPlusPlus.Mobile/`, which both hosts compile through `<Compile Include>`: the Mono host for iOS (when `TouchPlatform.IsMobile` is true), and the Android host always. Its namespaces are `BazaarPlusPlus.Mobile.*`. It is never a project or an assembly of its own, and no test project links it in addition to a host.
2. **This narrows ADR-0012 for the Mobile tree only.** Each host may compile `src/BazaarPlusPlus.Mobile/` and no other linked BPP source. `Android_host_defines_only_its_own_types` accepts `BazaarPlusPlus.Mobile.*` and still rejects everything else. Core keeps every other ADR-0012 rule.
3. **One Mobile Platform Port per host.** Shared code reaches the runtime only through the port, never through `#if`:
   - frame driving
   - the main-thread scope
   - object liveness
   - casts
   - overlay registration
   - the game font
   - text wrapping
   - button listeners
   
   Each host also supplies a `GlobalUsings.cs` that maps `TMPro`/`Il2CppTMPro` and the game's namespaced types. Code that names a global-namespace game type stays in the host.
4. **Host services are Core ports.** Supporters, the Collection catalog and encounter state reach the Mobile UI through Core interfaces that each composition binds. Where the hosts behave differently today, the migration picks one behavior and records it in the PR.
5. **Native Adjustments split in three.** A Native Adjustment is a change BPP makes to the game's own UI: screen fill, the field of view, the day clock, HUD shortcuts beside the settings button, tooltip toggles, menu entries.
   - The decision is pure geometry in Core, tested there.
   - The flow lives in the Mobile tree.
   - The read and write of game objects (`LetterBoxManager`, the camera, `ClockViewController`) is a thin host adapter whose signatures use only Core and Unity engine types.
6. **Screen shape stays continuous.** Layouts and Native Adjustments decide from `TouchMetrics` (size, density, safe area, and the aspect ratio derived from them), not from a list of device classes. Aspect thresholds that live in a host today (`AndroidScreenFill.GathersHudAbove`, `ReferenceAspect`) move to Core beside the layouts that use them.
7. **Rules follow the code.** Each IL rule test that finds Mobile code by namespace or type name is pointed at the new namespace in the same PR that moves the code. Examples: `AndroidHostThreadingRuleTests`, `AndroidNativeArtBoundaryTests`, `AndroidTouchViewBuildGuardTests`, and `CoreLayeringTests` on the Mono side. Each such test also asserts a minimum number of types it inspected, so a move cannot make it pass empty.
8. **Both hosts build every change to the tree.** A change under `src/BazaarPlusPlus.Mobile/` must build both hosts in CI. Both host projects set `LangVersion` 12, and the tree uses no BCL member that netstandard2.1 lacks.
9. **One log scope.** Mobile UI events use `touch.*` ids on both hosts. The Android `android.history.*`, `android.collection.*` and `android.live_build.*` ids of moved code are renamed in the PR that moves it.
10. **Migration is pair by pair** (issue #701). Each PR moves one Android/Touch pair into the tree and deletes both twins. The order:
    1. Native Adjustments first: screen fill and the day clock on the Mono host, which fixes iPad letterboxing.
    2. A leaf-file pilot that lands decisions 2, 3, 7, 8 and 9: `ScrollTracker`, `ArtImage`, `InspectionText`.
    3. The kit: `UiKit`, `ChipFlow`, `NativeSkin`.
    4. The panels, each after its port members exist.

## Consequences

- A Mobile UI change lands once and reaches iOS and Android. ADR-0024's hand-mirroring consequence is void. Its other decisions stand: the Mono host draws touch surfaces on a mobile platform, restores mobile input through mobile-only patches, and mounts no hover- or key-only feature.
- ADR-0023 stands: iOS keeps the desktop Mono host, its `GameInterop/` and its `Patches/`.
- A Mobile UI change costs two game snapshots to build: the Mono managed snapshot and the Android interop snapshot.
- iOS screen fill needs a Mono reference to `Unity.Cinemachine.dll`. It also needs a device check that a `CinemachineBrain.LateUpdate` postfix fires under `InterpreterMethodPatcher`.
- Until the migration ends, both the old twins and the tree exist. Each pair is in exactly one place at any time.

## Evidence

- Linked-source rule this narrows: `src/BazaarPlusPlus.Android/BazaarPlusPlus.Android.csproj` (comment above the Core reference), `tests/AndroidHost.Tests/AndroidHostAssemblyBoundaryTests.cs`
- Hand-mirrored twins: `src/BazaarPlusPlus.Android/Ui/`, `src/BazaarPlusPlus/Game/Touch/`, `src/BazaarPlusPlus/Game/*/Touch/`
- Drift: `AndroidEnchantPreview.InlineCandidates` against `src/BazaarPlusPlus/Game/Touch/TouchEnchantPreview.cs`
- Android-only Native Adjustments: `src/BazaarPlusPlus.Android/GameInterop/AndroidScreenFill.cs`, `AndroidScreenFillClock.cs`, `src/BazaarPlusPlus.Android/Patches/ScreenFillFieldOfViewPatch.cs`
- Continuous screen metrics: `src/BazaarPlusPlus.Core/Ui/TouchMetrics.cs`, `TouchHudLayout.cs`

# ADR-0023: The iOS port runs the desktop Mono host

Status: Accepted

## Context

There is no official iOS build of the game. `bazaarplusplus-ios/` runs the game's Mac build on iOS: the iOS Unity engine with a static Mono that interprets the Mac assemblies. The mod there sees the same Mono game types as on the desktop, not the IL2CPP proxies of the Android host. Two things differ from the desktop: the BepInEx loader is absent, and the device cannot write native code, so Harmony's native detours cannot run. ADR-0012 split hosts because the IL2CPP surface differs; that reason does not apply here.

## Decision

1. The iOS port loads `BazaarPlusPlus.dll` itself. There is no third host assembly and no copy of the desktop `GameInterop/` or `Patches/`.
2. The host lifecycle that was `Plugin` is `Host/BppMonoHost.cs`, driven by `MonoHostOptions` (log sink, settings store, Data Root, plugin path, self-update). `Plugin` is the BepInEx adapter; `Host/Ios/IosEntry.cs` is the iOS adapter, started from the game's RuntimeInitializeOnLoads. `DesktopComposition` is `MonoHostComposition`.
3. Only the adapters touch BepInEx. The App ships `BepInEx.dll` only so the desktop adapter type loads; nothing starts BepInEx on iOS. The iOS adapter uses Core's `IniSettingsStore` (moved from the Android host), the Unity log, and the App's Documents directory as the Data Root parent.
4. On iOS, Harmony's patcher is `Host/Ios/InterpreterMethodPatcher.cs`. It builds the patched method with Harmony's own manipulator and redirects the original to it through Mono patch 0008 in `bazaarplusplus-ios/`. Every desktop patch, transpilers included, applies unchanged.
5. `HostPlatformKey` replaces `DesktopPlatformKey`. The iOS adapter sets it to `ios-aarch64` before startup, because the port's Mono runs the macOS class libraries and reports macOS.
6. The mod ships inside the App, so the iOS host turns self-update off.

## Consequences

- A desktop patch is an iOS patch. A feature that cannot work on iOS turns itself off by `UnityEngine.Application.platform` or a `MonoHostOptions` flag, not by an iOS fork.
- Once an official iOS build exists it will be IL2CPP. Its host then follows the Android host, and this decision covers only the Mac-build port.

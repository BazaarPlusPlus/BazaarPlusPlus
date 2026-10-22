# ADR-0012: A shared core under per-host adapters

Status: Accepted

## Context

The mod is gaining a second host: an Android build loaded by MelonLoader on CoreCLR next to an IL2CPP game, beside the desktop BepInEx/Mono plugin. The desktop plugin was one assembly whose `Core/` directory held the pure abstractions, guarded only by a namespace-scoped architecture test. Three more assemblies shipped beside it, each justified by "no Unity or BepInEx reference". Localization was one of them, with every type `internal` and opened to the plugin and eleven test projects through `InternalsVisibleTo`. The IL2CPP surface differs systematically from Mono (proxy types, `TryCast` instead of `is`, `Il2Cpp*` namespaces), so the two hosts cannot share one binary or their game adapters. They can share source that touches no game or engine type. The plan is in issue #367.

## Decision

1. `BazaarPlusPlus.Core` is the shared core every host references: settings and host ports, the event bus and BPP-owned snapshots, probe interfaces, run context, `CoreComposition`, the `BppLog` pipeline, localization, and pure infrastructure. Its project references no Unity, loader, game, MelonLoader, or Il2CppInterop assembly. `CoreAssemblyBoundaryTests` checks both compiled targets.
2. Hosts own their adapters and composition roots. The desktop host keeps `GameInterop/`, `Patches/`, the Unity mountables, and `DesktopComposition`, and binds the desktop ports into `CoreComposition`. Its assembly name stays `BazaarPlusPlus.dll`.
3. A shared assembly exists only because it owns an external contract and carries that contract's dependency class. Storage owns the local database contract and SQLite. ModApi owns the wire contract and MessagePack/HTTP. Localization owned neither, so it merged into Core with its namespace unchanged. Dependencies run host → Core → {Storage, ModApi}.
4. Core types are `public`. `InternalsVisibleTo` names only test projects, never a host assembly name. The desktop host does not publicize Core.
5. Core targets `netstandard2.1` and `net10.0` from the same sources into same-named, same-version outputs. This narrows ADR-0010 to the Mono target. `netstandard2.1` keeps `BppReferenceGameLibraries` and binds the game's Newtonsoft.Json. `net10.0` references the NuGet package pinned to the game's version. Its central `PackageVersion` is conditioned on `net10.0`, so a `netstandard2.1` `PackageReference` still fails restore with NU1010. The reference is private to Core, so each host supplies its own runtime copy and a test that binds the game's copy keeps only that one. MessagePack stays out of Core.

## Guardrails

- Move a file into Core only when Core still compiles without it reaching a game or Unity type. Fully qualified names and `BazaarGameShared` DTOs count. Adding a project reference to make it compile is the wrong fix.
- Do not add an `InternalsVisibleTo` for a host. Make the member `public`, or keep it in the host.
- Test projects that reference Core do not also Compile-Include Core sources. A public Core type and its linked copy conflict (CS0436).
- Not decided here: whether Storage and ModApi multi-target, and which features move into Core and when. Issue #367 tracks both.

## Evidence

- Boundary and Newtonsoft binding: `tests/Architecture.Tests/CoreAssemblyBoundaryTests.cs`
- Composition split: `CoreComposition` in `src/BazaarPlusPlus.Core/Runtime/CoreComposition.cs`, `DesktopComposition` in `src/BazaarPlusPlus/DesktopComposition.cs`

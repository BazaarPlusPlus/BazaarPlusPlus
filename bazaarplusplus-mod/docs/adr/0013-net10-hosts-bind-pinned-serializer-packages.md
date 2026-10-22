# ADR-0013: CoreCLR hosts bind serializer packages pinned to the game's versions

Status: Accepted

## Context

ADR-0012 §5 made Core multi-target and narrowed ADR-0010 to the Mono target for Core's Newtonsoft.Json. Storage and ModApi still built only `netstandard2.1` against the game's `Managed/` copies of Newtonsoft.Json and MessagePack. The Android host runs BPP on CoreCLR beside an IL2CPP game. There the game's serializers are compiled into `libil2cpp.so`, and the interop assemblies (`Il2CppMessagePack.dll`, `Il2CppNewtonsoft.Json.dll`) are proxies over IL2CPP objects that cannot serialize CLR objects. The two runtimes share no managed library, so ADR-0010's one-copy-per-process reason does not apply on that host. The plan is in issue #367 §2.4.

## Decision

1. Storage and ModApi target `netstandard2.1` and `net10.0` from the same sources into same-named, same-version outputs, as Core does (ADR-0012 §5). The `netstandard2.1` target keeps `BppReferenceGameLibraries`, so ADR-0010 holds unchanged for the Mono host.
2. The `net10.0` target references NuGet `Newtonsoft.Json` (Storage, ModApi) and `MessagePack` (ModApi) at the game's versions. Their central `PackageVersion` entries are conditioned on `net10.0`, so a `netstandard2.1` `PackageReference` still fails restore with NU1010. The references are private: each host supplies its own runtime copy.
3. The Android host's compatibility check is the Run segment golden, not the compiler. `BundleV6Codec.Tests`, `ModApi.Tests`, and `Storage.Tests` run against the `netstandard2.1` output bound to the game's files. Their `*.Net10.Tests` siblings compile the same sources against the `net10.0` output bound to the packages, and must reproduce the same goldens byte for byte.
4. `MessagePackAnalyzer` stays pinned to the game's MessagePack version for both targets.
5. MessagePack advisories fixed only after the game's version are suppressed in `Directory.Packages.props`, which names them. Following the game's version keeps both hosts on one decoder, and the Mono host runs the game's copy regardless.

## Guardrails

- Move the `net10.0` pin only together with the game's `Managed/` copy. `SharedLibraryBindingTests` fails when either target or the restored package names another version.
- A test that needs the Mono binding pins the shared project with `SetTargetFramework="TargetFramework=netstandard2.1"`. Without it, a `net10.0` test silently resolves the `net10.0` output and loses the game-file coverage.
- Not decided here: how the Android host produces a Replay Payload, since `PvpReplayPayloadFactory` serializes the game's own `INetMessage` objects (issue #367 §2.4).

## Evidence

- Bindings on both targets: `tests/Architecture.Tests/SharedLibraryBindingTests.cs`
- Goldens on both targets: `tests/BundleV6Codec.Tests/` and `tests/BundleV6Codec.Net10.Tests/`

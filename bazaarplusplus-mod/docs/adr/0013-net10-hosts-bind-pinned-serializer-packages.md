# ADR-0013: CoreCLR hosts bind serializer packages pinned to the game's versions

Status: Accepted

## Context

ADR-0012 §5 made Core multi-target and narrowed ADR-0010 to the Mono target for Core's Newtonsoft.Json. Storage and ModApi still built only `netstandard2.1` against the game's `Managed/` copies of Newtonsoft.Json and MessagePack. The Android host runs BPP on CoreCLR beside an IL2CPP game. There the game's serializers are compiled into `libil2cpp.so`, and the interop assemblies (`Il2CppMessagePack.dll`, `Il2CppNewtonsoft.Json.dll`) are proxies over IL2CPP objects that cannot serialize CLR objects. The two runtimes share no managed library, so ADR-0010's one-copy-per-process reason does not apply on that host. The plan is in issue #367 §2.4.

## Decision

1. Storage and ModApi target `netstandard2.1` and `net10.0` from the same sources into same-named, same-version outputs, as Core does (ADR-0012 §5). The `netstandard2.1` target keeps `BppReferenceGameLibraries`, so ADR-0010 holds unchanged for the Mono host.
2. The `net10.0` target references NuGet `Newtonsoft.Json` (Storage, ModApi) and `MessagePack` (ModApi) at the game's versions. Their central `PackageVersion` entries are conditioned on `net10.0`, so a `netstandard2.1` `PackageReference` still fails restore with NU1010. The references are private: each host references the same packages and ships their runtime closure, which for MessagePack includes `MessagePack.Annotations` and `Microsoft.NET.StringTools` (`src/BazaarPlusPlus.ModApi/packages.lock.json`).
3. The Android host's compatibility check is the Run segment golden, not the compiler. Every test project resolves Core, Storage, and ModApi to the `netstandard2.1` output bound to the game's files, the build the desktop host ships, whether it references them directly or through `BazaarPlusPlus.csproj` (`tests/Directory.Build.targets`). The seal-eligibility cases, the history-database goldens, and the Bundle pipeline goldens therefore run on that build, including the run logging and Bundle sealing and upload logic that lives in Core. The `*.Net10.Tests` siblings of `BundleV6Codec.Tests`, `ModApi.Tests`, `Storage.Tests`, and `RunLoggingPipeline.Tests` compile the same sources against the `net10.0` output bound to the packages, and must reproduce the same goldens byte for byte.
4. `MessagePackAnalyzer` stays pinned to the game's MessagePack version for both targets.
5. MessagePack advisories fixed only after the game's version are suppressed in `Directory.Packages.props`, which names them. The `net10.0` pin stays on the game's version because the game's `Managed/MessagePack.dll` is the NuGet 3.1.4 `netstandard2.1` file byte for byte, so both hosts decode the Run segment with one library; moving it would break guardrail 1. The suppressions cover a decoder the Android host ships, so the argument is the trust boundary, not the Mono host's copy. Both codecs use `ContractlessStandardResolverAllowPrivate`, which falls back to the dynamic contractless resolvers. `RunPayloadV6Codec` reads Ghost Bundles from the server, so it adds `MessagePackSecurity.UntrustedData` and a decompressed-size cap. The public `MessagePackGzipCodec` runs at default security and decodes only payloads BPP wrote under its own Data Root (ghost, PvP replay, and favorite payload files). No code path uses Typeless, LZ4, or ExpandoObject.

## Guardrails

- Move the `net10.0` pin only together with the game's `Managed/` copy. `SharedLibraryBindingTests` fails when either target or the restored package names another version.
- A test that needs the `net10.0` output of Core, Storage, or ModApi names it with `SetTargetFramework="TargetFramework=net10.0"`, as the `*.Net10.Tests` projects and `Architecture.Tests` do. Without the default in `tests/Directory.Build.targets`, a `net10.0` test silently resolves the `net10.0` output and loses the game-file coverage. `BundlePipeline.Tests` and `RunLoggingPipeline.Tests` fail when the Core, Storage, or ModApi they load is not the `netstandard2.1` output, and `RunLoggingPipeline.Net10.Tests` when it is not the `net10.0` output (`tests/Shared/SharedTargetPin.cs`).
- Hand `MessagePackGzipCodec` only bytes BPP wrote itself. Anything from the network goes through a codec with `MessagePackSecurity.UntrustedData`.
- Not decided here: a `net10.0` sibling for the Bundle pipeline goldens. Core's `net10.0` output of Bundle sealing and upload runs in no golden until the Android host seals Bundles.
- Not decided here: how the Android host produces a Replay Payload, since the desktop's `DesktopPvpBattleReads.KeepReplayPart` serializes the game's own `INetMessage` objects (issue #367 §2.4).

## Evidence

- Bindings on both targets: `tests/Architecture.Tests/SharedLibraryBindingTests.cs`
- Goldens on both targets: `tests/BundleV6Codec.Tests/` and `tests/BundleV6Codec.Net10.Tests/`; the history-database goldens in `tests/RunLoggingPipeline.Tests/` and `tests/RunLoggingPipeline.Net10.Tests/`
- Tests default to the Mono target: `BppPinSharedProjectsToMonoTarget` in `tests/Directory.Build.targets`

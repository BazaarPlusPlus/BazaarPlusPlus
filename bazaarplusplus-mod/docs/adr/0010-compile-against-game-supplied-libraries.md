# ADR-0010: Compile against game-supplied libraries

Status: Accepted

## Context

The game's `Managed/` directory loads its own Newtonsoft.Json and MessagePack, and the mod binds to those at runtime. The mod used to compile against NuGet packages instead. 5.5.0 picked a Newtonsoft overload the game's copy lacks and failed at runtime with `MissingMethodException`. Pinning the package to the same version did not help: the game's Newtonsoft 13.0.2 is a different binary from NuGet's, and ModApi compiled against MessagePack 3.1.9 while the game has 3.1.4. A metadata validator (#92) compared the IL references with the game file, but it ran outside every gate and rebuilt part of the compiler's binding rules. `PublicizeAll` also publicized these libraries, so members the game keeps internal compiled cleanly.

## Decision

1. `build/GameLibraries.props` is the only list of game-supplied libraries. A project that sets `BppReferenceGameLibraries=true` references each one from `$(ManagedPath)` and excludes it from the publicizer. The Payload Inventory ships none of them.
2. The libraries have no central `PackageVersion`, so any future `PackageReference` fails restore (NU1010).
3. `ValidateBppGameLibraries` fails when a library is missing under `ManagedPath`. `VerifyBppGameLibraryCopies` fails when the copy in a build output is not the game's file.
4. The MessagePack source generator comes from `MessagePackAnalyzer`, pinned to the game's runtime version, 3.1.4. Dropping the NuGet `MessagePack` package would otherwise drop the generator, and the Run segment would silently fall back to runtime-built formatters. The generated code compiles against the game's MessagePack, so a generator that emits APIs missing from the game fails to build.

The compiler is the compatibility check in every gate that already runs: `mod::check`, `mod::test`, `mod::matrix`, and the release `produce` build, which always takes an explicit `ManagedPath`. Windows, PTR, and archived snapshots are covered by building against their own Managed directory.

## Guardrails

- Add a library that the game loads for the mod to `BppGameLibraries`, never as a package.
- `server::test` builds `ModApi.Tests`, so it needs the game's Managed directory too.
- Not covered: reflection and runtime semantics, and any third-party Payload DLL built against a different Newtonsoft. None ships today.
- Deferred: `UnityEngine.Modules` 2022.3.40 still stands in for the game's Unity 6 modules. Switching needs the `FindObjectsOfType` call sites migrated first.

## Evidence

- Access: `tests/RuntimeIntegration.Tests/GameSuppliedLibraryAccessTests.cs`
- Generated formatters and Run goldens: `RunPayloadFormattersAreSourceGenerated` in `tests/BundleV5Codec.Tests/Program.cs`

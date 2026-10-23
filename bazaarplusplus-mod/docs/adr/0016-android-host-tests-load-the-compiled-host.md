# ADR-0016: Android host tests load the compiled host

Status: Accepted

## Context

`tests/AndroidHost.Tests` scanned the compiled Android host as metadata and never loaded it (`ReferenceOutputAssembly="false"`), because the host's references resolve only inside the game process. The one piece of host code it ran, the History reader, it compiled a second time from a linked source. That left the Bundle path the host owns untested: `AndroidPvpBattleEvidence` resolving card enums from the interop enums, `AndroidReplayMessages.VerifyWellFormed`, and Core's `net10.0` `RunPayloadComposer` over a database opened with the host's DELETE journal mode (issue #389).

The interop assemblies are managed: an interop enum is a plain CLR enum with the game's names and values, and loading one needs no IL2CPP runtime. A host method that never calls into IL2CPP runs in a test process once the assemblies its types name are on the probing path.

## Decision

1. `AndroidHost.Tests` references the host project with its output assembly and the host grants it `InternalsVisibleTo` (`src/BazaarPlusPlus.Android/Properties/AssemblyAttributes.cs`). Tests call the host's own types, never a linked copy of a host source.
2. The test project imports `build/InteropPath.props` and references the Interop Snapshot and the Loader Release for compilation. It copies beside itself only `Il2CppBazaarGameShared.dll`, which holds the card enums, and `Il2CppInterop.Runtime.dll`, which the host's replay types name, with their dependencies. A test that needs another interop assembly at run time adds that one file.
3. A test runs only host code that never calls into the IL2CPP runtime: no interop object is constructed and no `Il2Cpp*` method is invoked. Code that does, such as `AndroidReplayMessages.Serialize` or the board captures, is verified on a device.
4. The card enum values both hosts write are one table, `tests/Shared/fixtures/pvp-card-enums.json`. `AndroidHost.Tests` holds the interop enums and `AndroidPvpBattleEvidence` to it, and `BundlePipeline.Tests` holds the game's Mono enums and `DesktopPvpBattleEvidence` to it, so a game update that renumbers a card enum on one platform fails a test instead of shipping different numbers in the Run segment.

## Guardrails

- The boundary tests still read the host's metadata from its build output (`BppAndroidHostAssembly`), so a forbidden reference fails whether or not a test loads the type.
- The suite stays in `just mod::test-core`, which needs the Interop Snapshot (`just mod::fetch android online`) and no Managed snapshot.

## Evidence

- `tests/AndroidHost.Tests/AndroidPvpBattleEvidenceTests.cs`, `tests/AndroidHost.Tests/AndroidRunPayloadComposeTests.cs`, `tests/AndroidHost.Tests/AndroidReplayMaintenanceTests.cs`
- `tests/BundlePipeline.Tests/CardEnumTableTests.cs`

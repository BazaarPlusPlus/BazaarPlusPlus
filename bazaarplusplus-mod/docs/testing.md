# Test projects

How a test project reaches the code it tests. `scripts/test.sh` owns the lanes (`just mod::test`, `just mod::test-core`), and `tests/Architecture.Tests/TestProjectRegistryTests.cs` holds every project to a registry that runs it.

## Reference Core, or link sources

- **Code in Core, Storage or ModApi that needs no game assembly** is tested by an xUnit project that references the assemblies. Set `BppRequiresManagedPath=false`, and give each of the three a `ProjectReference` with `SetTargetFramework="TargetFramework=net10.0"`. Name all three, even when the test uses Core alone. Core brings Storage and ModApi in as transitive references, and `BppPinSharedProjectsToMonoTarget` (`tests/Directory.Build.targets`) moves each reference that names no target framework to `netstandard2.1`, which needs the Managed snapshot. Put the project in `tests/BazaarPlusPlus.CoreTests.slnx` and in `tests/BazaarPlusPlus.Tests.slnx`, so both lanes run it. Add the `Newtonsoft.Json` package only when the code under test reaches JSON, because Core's own reference to it is private (ADR-0012). `tests/MainMenuVersionLabel.Tests/MainMenuVersionLabel.Tests.csproj` is the smallest example.
- Use the public surface of Core. Reach an `internal` type through the `InternalsVisibleTo` list in `src/BazaarPlusPlus.Core/Properties/AssemblyAttributes.cs`; do not make the type public for a test.
- **Link sources with `Compile Include` only** for a Desktop Host file that does not compile without the game assemblies, or for a compile-time stub that replaces a real type (`BppLog`, the internal Updater types that Core also compiles). A project that references Core never also links a Core source (ADR-0012).
- **A test of the desktop binding** (the `netstandard2.1` output bound to the game's files) names no target framework on its reference. The goldens in ADR-0013 run that way.
- One capsule check becomes one `[Fact]` with the same call, so the defaults the check relied on stay under test.

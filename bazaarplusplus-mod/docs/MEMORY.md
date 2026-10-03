# BazaarPlusPlus — Durable Memory

What an agent cannot recover by reading the code in front of it, for any change: domain invariants, the decisions already settled, and the cross-feature traps that fail silently. A trap that bites only one feature lives in the Gotchas section of that feature's [architecture/](architecture/) doc; [ARCHITECTURE.md](ARCHITECTURE.md) routes to it. Rationale is in [adr/](adr/), process in [../AGENTS.md](../AGENTS.md).

## Rules

Constraints and patterns every change keeps.

- MessagePack-serialized DTOs in the Unity/Mono runtime must keep their whole serialized graph `public`.
- Key entities by template GUID; package cards are `EHiddenTag.Package` via `PackageIdentity.IsPackage` — never display name or `ArtKey`. [`src/BazaarPlusPlus/GameInterop/Cards/PackageIdentity.cs`]
- Bump both `RunLogSchema` version constants only for a column change; a data repair never bumps them, and there is no upload-payload version. Index and trigger DDL needs no bump; open rebuilds an index whose SQL drifted from `BootstrapSql`, but a retired index or changed trigger needs a `DROP`. [`src/BazaarPlusPlus.Storage/RunLog/RunLogSchema.cs` | ADR-0006 | ADR-0011]
- Mod-authored user-facing strings use `LocalizedTextSet` (en + zh-Hans, optional zh-Hant + de/pt/ko/it; anything else falls back to English). [`src/BazaarPlusPlus.Localization/LocalizedTextSet.cs`]
- A categorized degradation event includes the category field in its `BppLogStormPolicy` key — a shared key lets one category's failure suppress every later category during the storm window. [`src/BazaarPlusPlus/Infrastructure/Logging/Core/BppLogSchema.cs`]
- Adding or removing a `BppLogEventSource` event means updating its locked manifest test in the same change; find them with `rg -l the_locked tests/`. Field `Order` must be strictly increasing within an event, not contiguous. [`src/BazaarPlusPlus/Infrastructure/Logging/Core/BppLogEventCatalog.cs` | `tests/Architecture.Tests/PluginLoggingTests.cs`]
- Anchor mod file-write paths on `BepInEx.Paths.GameRootPath` or `<GameRoot>/BazaarPlusPlusV5/`, which BepInEx special-cases on macOS to the directory containing the `.app`. A path built from `Application.dataPath` writes unsealed files inside the `.app` bundle, breaking `codesign` re-signing and the trampoline repair — and therefore `just mod::build --deploy` after every game update. [`src/BazaarPlusPlus/Core/Paths/BepInExPathProvider.cs`]
- Reuse the game's native UI components (`CardPreviewBase.SetUp` and the like) and existing prior art instead of hand-rolling a render or upload chain.
- Keep Unity-adjacent logic free of Unity types and Compile-Include it into a test project, so the test needs no InternalsVisibleTo and no ManagedPath; a core that touches `BazaarGameShared` types still needs the game assemblies. [ADR-0005]

## Architecture decisions

One line each, full record in [adr/](adr/). A line here exists to stop a settled question from being reopened; the ADR says why.

- ADR-0001: Expose run/encounter state via on-demand `IEncounterStateProbe`, not an event-sourced timeline tracker.
- ADR-0002: Replay exit is explicit — there is no programmatic `ReplayState` exit; ghost payloads are stored in recorder perspective, stamped by `PerspectiveVersion`.
- ADR-0003: Keep behavior-specific seams; a unification that only looks tidier is rejected.
- ADR-0004: One Collection `Destroy` chip covers the whole destroy-mechanic cluster on base templates; `TTriggerOnCardRepaired` is deliberately excluded.
- ADR-0005: Timing invariants live in pure decision cores, not MonoBehaviour glue.
- ADR-0006: Outbound Mod API rules each have one protocol or persistence owner.
- ADR-0007: Remote data separates runtime catalogs, the release manifest, and build-time seed fetch into three lifecycles.
- ADR-0008: Combat Impact numbers are ledger entries with typed residuals; attribution is graph-driven, never card-GUID constants.
- ADR-0009: Tests assert behavior or compiled artifacts, never source text; RS0030 bans file-to-text reads.
- ADR-0010: Compile against the game-supplied libraries in `build/GameLibraries.props`: no NuGet package, publicizing, or shipped copy.
- ADR-0011: The local `user_version` names the column shape the installer reads; data repairs never bump it.


## Gotchas

Each of these failed silently, or reported something misleading, at least once.

### Game runtime

- Runtime `Card` tags do not carry static template tags — hidden *and* public (`DTOUtils.CreateCard` never copies them and snapshot updates overwrite) — derive identity from template tags and static data, merging runtime+template+enchantment as `CombatImpactEntityTags` does. [`decompiled/TheBazaarRuntime/TheBazaar/DTOUtils.cs` | `src/BazaarPlusPlus/Game/PostCombatImpact/Data/CombatImpactEntityTags.cs`]
- Static-data lookup is fallible on degraded and test paths: catch and return a safe default, as the current resolvers do. [`src/BazaarPlusPlus/GameInterop/GameBuildInfoResolver.cs`]
- A Harmony postfix on an `async Task` game method runs at the first await suspension, not at completion — bind pre-state in a **prefix**.
- A programmatic native `Button.onClick` invoke can return silently through interaction gates such as `AllowInteraction` without throwing — verify the expected game-state transition before treating the action as successful.
- `PublicizeAll` makes `ConfigEntry<T>.SettingChanged` ambiguous (CS0229), so no BPP code subscribes to it; invalidate config-derived caches by raw-value compare. [`src/BazaarPlusPlus/Game/Input/BppHotkeyService.cs`]
- Portrait providers negative-cache exceptions past their service-readiness gates, so one transient asset-load exception caches a null portrait until restart. Preserved by design through the `AsyncLoadCache` migration — changing it is a behavior change. [`src/BazaarPlusPlus/GameInterop/HeroPortraits/`]
- `AssetLoader` signatures vary by game build; route them through `NativeAssetLoaderInvocation`. Process-static caches must load `Global` because scene-scope handles die across lobby↔run; smoke native cards and hero chips across that transition. [`src/BazaarPlusPlus/GameInterop/AssetLoading/`]

### Build and tests

- Source-shadow scenario capsules pin production files through explicit Compile-Include and may define mutually incompatible runtime shims. Keep them in the closed `BppScenarioRunnerProjects` list and execute them only through the process-isolated `ScenarioRunner.Tests` host. [`Directory.Build.props` | `tests/ScenarioRunner.Tests/`]
- Architecture-test file sweeps (absence assertions, reference scans) must enumerate from the `src/` and `tests/` roots, never recurse from the repo root — embedded worktrees such as `.claude/worktrees/` hold stale checkouts that still contain removed code and turn the sweep red. [`tests/Architecture.Tests/CoreLayeringTests.cs`]
- `rg 'new TypeName('` misses target-typed `new(...)` call sites, so a "zero call sites" grep proves nothing; prove a deletion by deleting (AGENTS.md, Build & Test). [`tests/PureBehavior.Tests/PureBehavior.Tests.csproj`]
- `src/` and `tests/` both set `BppEnableWarningGate`, so `TreatWarningsAsErrors` turns an unused using (IDE0005) or an unread private field (CS0414) into a build error. [`Directory.Build.props` | `src/Directory.Build.props`]
- Some `*.Tests` directories own no `.csproj`; directory wildcards in three host projects absorb their sources. Deleting or renaming one leaves its wildcard matching zero Compile items instead of failing, so those tests silently stop running. [`tests/PureBehavior.Tests/PureBehavior.Tests.csproj` | `tests/RuntimeIntegration.Tests/RuntimeIntegration.Tests.csproj` | `tests/FeatureLogging.Tests/FeatureLogging.Tests.csproj`]

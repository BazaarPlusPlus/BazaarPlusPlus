# BazaarPlusPlus — Durable Memory

What the code in front of you cannot tell you, for any change: invariants, settled decisions, and cross-feature traps that fail silently. A single-feature trap goes in that feature's [architecture/](architecture/) Gotchas; rationale is in [adr/](adr/), process in [../AGENTS.md](../AGENTS.md).

## Rules

- MessagePack-serialized DTOs in the Unity/Mono runtime must keep their whole serialized graph `public`.
- Key entities by template GUID; package cards are `EHiddenTag.Package` via `PackageIdentity.IsPackage` — never display name or `ArtKey`. [`src/BazaarPlusPlus/GameInterop/Cards/PackageIdentity.cs`]
- Bump `RunLogSchema.LocalDatabaseSchemaVersion` only for a column change, never for a data repair; there is no upload-payload version. Index and trigger DDL needs no bump; open rebuilds an index whose SQL drifted from `BootstrapSql`, but a retired index or changed trigger needs a `DROP`. [`src/BazaarPlusPlus.Storage/RunLog/RunLogSchema.cs` | ADR-0006 | ADR-0011]
- Run Logging writes `runs.player_account_id` through its queue, so an active Run may silently read NULL; only a completed or abandoned Run's value is final (those writes drain the queue first). [`src/BazaarPlusPlus.Storage/RunLog/Replication/QueuedRunLogStore.cs`]
- Mod-authored user-facing strings use `LocalizedTextSet` (en + zh-Hans, optional zh-Hant + de/pt/ko/it; anything else falls back to English). [`src/BazaarPlusPlus.Core/Localization/LocalizedTextSet.cs`]
- A categorized degradation event names the category field in its call-site storm key, `storm: ["reason_code", ...]`, or one category's failure suppresses every other category for the storm window. A key naming a field with a correlation policy never suppresses. [`src/BazaarPlusPlus.Core/Logging/BppLogPipeline.cs`]
- An event id needs at least three dotted segments, `<scope>.<area>.<event>`, as a literal under its scope's prefix; any other id renders as `logging.render.failed`, which `LogEventIdArchitectureTests` and `AndroidHostLogEventIdTests` catch in the compiled assemblies. [`tests/Architecture.Tests/LogEventIdRule.cs`]
- Anchor mod file-write paths on `BepInEx.Paths.GameRootPath` or `<GameRoot>/BazaarPlusPlusV6/`, which BepInEx special-cases on macOS to the directory containing the `.app`. A path from `Application.dataPath` writes unsealed files inside the `.app`, breaking `codesign` re-signing and so `just mod::build --deploy`. [`src/BazaarPlusPlus/Host/BepInExPathProvider.cs`]
- Reuse the game's native UI components (`CardPreviewBase.SetUp` and the like) and prior art over a hand-rolled render or upload chain.
- Keep Unity-adjacent logic free of Unity types and Compile-Include it into a test project, so the test needs no InternalsVisibleTo and no ManagedPath; a core that touches `BazaarGameShared` types still needs the game assemblies. [ADR-0005]

## Architecture decisions

One line each, to stop a settled question from being reopened; the ADR says why.

- ADR-0001: Expose run/encounter state via on-demand `IEncounterStateProbe`, not an event-sourced timeline tracker.
- ADR-0002: Replay exit is explicit — there is no programmatic `ReplayState` exit; ghost payloads are stored in recorder perspective, stamped by `PerspectiveVersion`.
- ADR-0003: Keep behavior-specific seams; a unification that only looks tidier is rejected.
- ADR-0004: One Collection `Destroy` chip covers the whole destroy-mechanic cluster on base templates; `TTriggerOnCardRepaired` is deliberately excluded.
- ADR-0005: Timing invariants live in pure decision cores, not MonoBehaviour glue.
- ADR-0006: Outbound Mod API rules each have one protocol or persistence owner.
- ADR-0007: Remote data separates runtime catalogs, the release manifest, and build-time seed fetch into three lifecycles.
- ADR-0008: Combat Impact numbers are ledger entries with typed residuals; attribution is graph-driven, never card-GUID constants.
- ADR-0009: Tests assert behavior or compiled artifacts, never source text; RS0030 bans file-to-text reads.
- ADR-0010: Mono targets compile against the game-supplied libraries in `build/GameLibraries.props`: no NuGet package, publicizing, or shipped copy.
- ADR-0011: The local `user_version` names the column shape the installer reads.
- ADR-0012: Shared code goes in `BazaarPlusPlus.Core` (public types, IVT only for tests) under per-host adapters; a separate shared assembly exists only for an external contract and its dependency class.
- ADR-0013: Storage and ModApi also build `net10.0`, bound to NuGet Newtonsoft.Json/MessagePack pinned to the game's versions; the Run segment goldens must pass on both targets.
- ADR-0014: Core only stages a signed Managed Payload; the dependency-free Updater swaps it in before the mod loads and is itself updated only by the installer or APK.
- ADR-0015: Core speaks BPP card facets, never game card enums; each host maps by member name in one adapter, and a facet's integer is never persisted.
- ADR-0016: `AndroidHost.Tests` loads the compiled host and runs only code that never calls into IL2CPP; both hosts' card enum values are one shared table.
- ADR-0017: Item boards and live cards live in Core on facets, with sockets as indexes 0..9 and attributes keyed by game member name; an unmapped tier or size renders as Bronze or Small.
- ADR-0018: Android touch surfaces lay out in Core's one `TouchChrome` frame (safe area, 48 dp/12 sp floors, one close button, shared inspect sheet); a touch grid picks its columns from its width.
- ADR-0019: The Combat Impact producer lives in Core on BPP combat facets that copy the game's names and values; the host only copies a `CombatSim` and reads templates.
- ADR-0020: Saved Replay Playback lives once in Core behind `ISavedReplayHost`; hosts differ only in port behavior.
- ADR-0021: The PvP Battle Capture Window lives once in Core behind `IPvpBattleReads`; persistence stays per host.
- ADR-0022: Main Overlay Panel lifecycle runs through Core's one `OverlayPanelHost` on both hosts; each host keeps a thin adapter.
- ADR-0024: on a mobile platform the Mono host draws touch surfaces over Core's touch layouts, not desktop ones; History and Ten-Win Builds are not ported yet.

## Gotchas

Each of these failed silently, or reported something misleading, at least once.

### Game runtime

- Runtime `Card` tags lack the static template tags, hidden *and* public (`DTOUtils.CreateCard` never copies them; snapshot updates overwrite): merge runtime, template and enchantment tags as `CombatImpactEntityTags` does. [`decompiled/TheBazaarRuntime/TheBazaar/DTOUtils.cs` | `src/BazaarPlusPlus/Game/PostCombatImpact/Data/CombatImpactEntityTags.cs`]
- Static-data lookup is fallible on degraded and test paths: catch and return a safe default, as the current resolvers do. [`src/BazaarPlusPlus/GameInterop/GameBuildProbe.cs`]
- A Harmony postfix on an `async Task` game method runs at the first await suspension, not at completion — bind pre-state in a **prefix**.
- A programmatic native `Button.onClick` invoke can return silently through interaction gates such as `AllowInteraction` without throwing — verify the expected game-state transition before treating the action as successful.
- `ISetting<T>` has no change event (`PublicizeAll` makes BepInEx `ConfigEntry<T>.SettingChanged` ambiguous, CS0229); invalidate config-derived caches by raw-value compare. [`src/BazaarPlusPlus/Game/Input/BppHotkeyService.cs`]
- Portrait providers negative-cache exceptions past their readiness gates: one transient asset-load exception caches a null portrait until restart. Deliberate; changing it is a behavior change. [`src/BazaarPlusPlus/GameInterop/HeroPortraits/`]
- Call `AssetLoader` via `NativeAssetLoaderInvocation` (signatures vary by build). Scene-scope handles die across lobby↔run: process-static caches use `Global`, per-scene loads (card previews, replay warmup) `Current` with no process-wide key set. Smoke cards and hero chips across it. [`src/BazaarPlusPlus/GameInterop/AssetLoading/`]

### Build and tests

- Source-shadow scenario capsules Compile-Include production files and may define mutually incompatible runtime shims: list them in the closed `BppScenarioRunnerProjects` and run them only through the process-isolated `ScenarioRunner.Tests` host (`dotnet run` on one capsule only diagnoses it). [`Directory.Build.props` | `tests/ScenarioRunner.Tests/`]
- Architecture-test file sweeps enumerate from the `src/` and `tests/` roots: recursing from the repo root reaches stale checkouts under `.claude/worktrees/` and turns the sweep red. [`tests/Architecture.Tests/CoreLayeringTests.cs`]
- `rg 'new TypeName('` misses target-typed `new(...)` call sites, so a "zero call sites" grep proves no deletion safe. [`tests/PureBehavior.Tests/PureBehavior.Tests.csproj`]
- `src/` and `tests/` both set `BppEnableWarningGate`, so `TreatWarningsAsErrors` turns an unused using (IDE0005) or an unread private field (CS0414) into a build error. [`Directory.Build.props` | `src/Directory.Build.props`]
- Some `*.Tests` directories own no `.csproj`; directory wildcards in two host projects absorb their sources. Deleting or renaming one leaves its wildcard matching nothing, so those tests silently stop running. [`tests/PureBehavior.Tests/PureBehavior.Tests.csproj` | `tests/RuntimeIntegration.Tests/RuntimeIntegration.Tests.csproj`]

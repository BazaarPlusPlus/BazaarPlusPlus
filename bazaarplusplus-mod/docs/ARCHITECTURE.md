# BazaarPlusPlus Architecture

How the plugin is assembled: startup and teardown order, the layer and assembly boundaries, the seams that more than one feature consumes, and where runtime data lives. Per-feature detail is disclosed under [architecture/](architecture/); this file covers only what holds across features.

Terms used here are defined in [../CONTEXT.md](../CONTEXT.md). Why a boundary is where it is lives in [adr/](adr/), indexed one line each in [MEMORY.md](MEMORY.md).

## Runtime Shape

`Plugin.Awake()` resolves the game build channel via `GameBuildInfoResolver`, creates `BppComposition`, installs static facades such as `BppLog`, `BppPatchHost`, localization, settings dock entries, supporters, and hotkeys, applies Harmony patches per patch class, starts feature modules, builds the owner-scoped Mod API session and separate BazaarDB-link client, then mounts Unity components onto the plugin `GameObject` (`src/BazaarPlusPlus/Plugin.cs`).

`BppComposition` is the manual composition root; there is no DI container. It receives the resolved `IGameBuildInfo` and wires feature modules through `BppFeatureRegistry`, Unity components through `BppMountableRegistry`, and in-game settings rows through `SettingsDockEntryRegistry` (`src/BazaarPlusPlus/BppComposition.cs`). Simple components mount through `ComponentMount<T>`; a bespoke mount exists only where a feature owns additional dependencies or object lifecycles, and `CombatReplayRuntime` is the bootstrap exception because other modules need it before mountables run.

Three ordering facts in this file are load-bearing, because each one fails silently when reordered:

- **Patches apply per class** via `CreateClassProcessor(type).Patch()`, never `PatchAll()`. One broken game target (a game update, a PTR branch) then degrades only its own feature instead of aborting the whole plugin.
- **Teardown unmounts Unity components, disposes the composition (stopping pure features in reverse registration order), and only then destroys `CombatReplayRuntime`.** That lets Run Logging settle a deferred completion while replay persistence state is still readable.
- **The run-log database is released only after every writer is gone.** Microsoft.Data.Sqlite pools connections, so without an explicit release the native handle, the `-wal`/`-shm` files, and an uncheckpointed WAL all survive the process, and the installer would have to recover the database rather than read it. `SqliteShutdown.ReleaseRunLogDatabase` clears the pool, truncates the WAL, and clears again (`src/BazaarPlusPlus.Storage/Sqlite/SqliteShutdown.cs`). Static teardown then releases native font handles, scopes, and cloned font assets through `NativeGameTypography.Reset()`.

## Game Build Channel And PTR Isolation

`GameBuildInfoResolver` classifies the running client as Online, Ptr, or Unknown from the bundleVersion `-ptr` token plus a corroborating `TheBazaar.Config.ServerOption` type probe (`src/BazaarPlusPlus/GameInterop/GameBuildInfoResolver.cs`).

Disagreement resolves to **Ptr**, and the asymmetry is the whole reason. Classifying a PTR client as Online silently pollutes the production dataset and cannot be undone, while classifying an Online client as Ptr only pauses its uploads. The channel is injected into the composition, stamped onto recorded runs by `RunLoggingModule`, and `BundleSealCoordinator` excludes PTR runs before a bundle can enter the outbox.

Newer-only combat events and enum members stay behind runtime adapters in `GameInterop/CombatSimulation`, so a Production build lacking them skips only the optional metrics. Premise tests live in `tests/PtrCompatibility.Tests/`.

## Assemblies And Boundaries

Four assemblies ship.

| Assembly | Contents |
|---|---|
| `BazaarPlusPlus.dll` | the BepInEx plugin: Unity/game integration, feature modules, Harmony patches, embedded resources, composition root |
| `BazaarPlusPlus.ModApi.dll` | HTTP client, routes, DTOs, MessagePack/gzip helpers for the mod backend |
| `BazaarPlusPlus.Storage.dll` | SQLite schema, repositories, path interfaces, local persistence models |
| `BazaarPlusPlus.Localization.dll` | localization resolution engine, language and mode helpers |

`ModApi`, `Storage`, and `Localization` keep **zero game, Unity, and BepInEx references**, an invariant enforced by architecture tests, not by the compiler.

Inside the main assembly:

| Directory | Owns |
|---|---|
| `Core/` | pure mod abstractions: config, event bus, runtime service interfaces, run context, path contracts, the run-snapshot contract |
| `GameInterop/` | adapters over game, Unity, or publicized runtime surfaces |
| `Game/` | feature workflows, UI, user-facing policy, filtering, upload orchestration, storage use |
| `Patches/` | Harmony patches, reaching services through `BppPatchHost` rather than constructor injection |
| `Infrastructure/` | cross-cutting logging, UI tokens, stable text helpers, generic seams |

The main project targets `netstandard2.1`, uses C# 12, and publicizes game assemblies, so `internal` game members are accessible. Remote seed data is declared in `RemoteEmbeddedData.targets` and delegates transport to `build/RemoteEmbeddedDataFetcher`; `BppVersion` in `Directory.Build.props` reads the workspace-root `VERSION`. The shared Payload Inventory generates the MSBuild copy items; production assembly versions are checked from compiled metadata. `just release::prepare <platform>` runs isolated preparation through the [product release coordinator](../../docs/release.md).

Architecture tests ratchet dependency boundaries, shared ownership, and test/build safety contracts over compiled assemblies (`tests/Architecture.Tests/`, ADR-0009). Concrete composition behavior remains in `tests/CompositionRuntime.Tests/` and is compiled by `RuntimeIntegration.Tests`: feature start/stop is fault-isolated, while `BppMountableRegistry.MountAll` deliberately is not.

## Shared Seams

Each of these is consumed by two or more features. Reaching around one to re-implement its behavior inside a feature is the failure this section exists to prevent.

| Seam | Owner | Consumers |
|---|---|---|
| Overlay panel lifecycle | `Game/OverlayPanels/OverlayPanelHost.cs` | Collection, History, LiveBuild |
| Native asset loading compatibility | `GameInterop/AssetLoading/` | Card preview, portraits, replay warmers |
| Native card preview | `GameInterop/CardPreview/` | Collection, combat recap |
| Native tooltip suppression | `GameInterop/Tooltips/NativeTooltipSuppression` | replay recording, end-of-run capture |
| Item board planning / native boards | `GameInterop/ItemBoardPreview/`, `GameInterop/MonsterBoardPreview/` | History, LiveBuild |
| Day tier resolution | `GameInterop/DayTiers/` | Collection's Day gate, Encounter Preview |
| Hero portraits | `GameInterop/HeroPortraits/` | History, LiveBuild, ghost rows |
| Text rendering | `GameInterop/Fonts/NativeGameTypography` | every BPP surface |
| Remote embedded catalogs | `Infrastructure/RemoteEmbeddedCatalog/` | voice lines, ten-win builds, supporters |
| Atomic payload files | `Infrastructure/` `FileBackedPayloadStore<T>` | combat replay, ghost payloads |
| Deduped async loads | `Infrastructure/` `AsyncLoadCache<TKey,TValue>` | hero and encounter portrait providers |
| Active scene name | `GameInterop/Scenes/ActiveSceneNameCache.cs` | Collection dock button, replay recording button |

`RemoteEmbeddedCatalog<T>` owns cache → embedded → remote loading with feature-supplied freshness: single-flight warm and refresh, per-caller cancellation over shared flights, typed outcomes, atomic cache writes, retry re-arming, monotonic publication, and generation-guarded disposal. A queued cold-start refresh takes ownership atomically when its refresh flight begins, so cancelling the originating warm flight before that handoff prevents the remote operation. Feature observers are serialized against disposal on a separate gate, so `TryGet` never waits for feature-side logging or index rebuilds.

Operational logging is governed: features emit through closed `BppLogFeatureScope`s with dotted-snake event ids and typed privacy/cardinality/correlation fields, declared in per-feature `[BppLogEventSource]` `*LogEvents` classes that `BppLogEventCatalog` discovers and validates. Debug events compile out of Release. Ratcheted by `tests/Architecture.Tests/LoggingGovernanceTests.cs`.

## Data And File Locations

Runtime data that BazaarPlusPlus owns is rooted at `<GameRoot>/BazaarPlusPlusV5/`. `BepInExPathProvider` exposes that single root, and storage and feature modules derive the SQLite database, bundle outbox, replay, ghost payload, screenshot, video, voice, LiveBuild, supporter, and Encounter Preview paths from it. No runtime cache uses the process temporary directory.

V5 uses a fresh, non-migrating SQLite schema. Beyond run facts, events, battles, snapshots, screenshots, and replay-video metadata it holds `bundle_seal_jobs` and a non-FK `bundle_outbox`; ghost rows carry remote bundle identity, presigned URL and expiry, and replay state. V4 sync cursors, dirty/checkpoint upload state, and independent screenshot upload state do not exist (`src/BazaarPlusPlus.Storage/RunLog/RunLogSchema.cs`).

## Event Flow

An in-memory event bus decouples runtime coordination between feature modules registered at startup (`src/BazaarPlusPlus/BppComposition.cs` — the `Register` call order is the authoritative roster).

`RunLoggingModule` is the feature-owned event intake. `Start` creates its replicated/queued store chain and session manager only after composition construction succeeds, subscribes once to run lifecycle, run initialization, PvP recording, and replay-drained events, then maps a matching `PVPCombat` manifest directly to persistence. Battles attach to the stable active or deferred run id before the event append and checkpoint. Interrupted runs stay resumable; normal completion owns an active, cancellable two-second replay-persistence deadline; `Stop` is a quiescence barrier that forces any deferred completion before unsubscribing and disposing the store. No Run Logging `MonoBehaviour` is mounted.

PvP battle evidence is a shared `Game/PvpBattles` module rather than a `GameInterop` adapter; it captures local and opponent identities and board snapshots from live game state and net messages.

## Per-feature detail

| To work on | Read |
|---|---|
| Collection filtering, sorting, the source catalog, grid virtualization | [architecture/collection.md](architecture/collection.md) |
| Opening or closing an overlay panel, HistoryPanel, LiveBuildPanel, item boards | [architecture/panels.md](architecture/panels.md) |
| Encounter Preview, native card previews, tooltip presentation | [architecture/previews.md](architecture/previews.md) |
| Combat replay, video recording, screenshots, bundle sealing, uploads, ghost battles | [architecture/capture.md](architecture/capture.md) |
| Desktop native build freshness, local promotion, and signing | [architecture/native-artifacts.md](architecture/native-artifacts.md) |
| Hotkey bindings and settings dock rows | [architecture/input-and-settings.md](architecture/input-and-settings.md) |
| Fonts, localization, voice subtitles, supporter attribution | [architecture/text.md](architecture/text.md) |
| The V5 bundle wire format | [contracts/run-payload-v5.md](contracts/run-payload-v5.md) |

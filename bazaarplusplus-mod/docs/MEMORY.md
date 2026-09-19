# BazaarPlusPlus — Durable Memory

What an agent cannot recover by reading the code in front of it: domain invariants, the decisions already settled, and the traps that fail silently. Structure is in [ARCHITECTURE.md](ARCHITECTURE.md), rationale in [adr/](adr/), process in [../AGENTS.md](../AGENTS.md) — linked, never restated.

## Rules

Domain constraints that must stay true in the system.

- MessagePack-serialized DTOs in the Unity/Mono runtime must keep their whole serialized graph `public`.
- Key entities by template GUID; package cards are `EHiddenTag.Package` via `PackageIdentity.IsPackage` — never display name or `ArtKey`. [`src/BazaarPlusPlus/GameInterop/Cards/PackageIdentity.cs`]
- Bump both `RunLogSchema` version constants together for a column or data change carried by the versioned migration block; there is no separate upload-payload version. Index and trigger DDL needs no bump — it lands in `BootstrapSql`, which `EnsureInitialized` re-executes on every open. [`src/BazaarPlusPlus.Storage/RunLog/RunLogSchema.cs` | ADR-0006]
- CJK text that renders as tofu is routed through `NativeGameTypography`, which applies the game's native serif/sans and extends BPP-owned text with a CJK fallback chain. Fix the font route, not the copy. [`src/BazaarPlusPlus/GameInterop/Fonts/NativeGameTypography.cs`]
- Mod-authored user-facing strings use `LocalizedTextSet` (en + zh-Hans, optional zh-Hant + de/pt/ko/it; anything else falls back to English). [`src/BazaarPlusPlus.Localization/LocalizedTextSet.cs`]
- A categorized degradation event includes the category field in its `BppLogStormPolicy` key — a shared key lets one category's failure suppress every later category during the storm window. [`src/BazaarPlusPlus/Infrastructure/Logging/Core/BppLogSchema.cs`]
- Adding or removing a `BppLogEventSource` event means updating its locked manifest test in the same change; find them with `rg -l the_locked tests/`. Field `Order` must be strictly increasing within an event, not contiguous. [`src/BazaarPlusPlus/Infrastructure/Logging/Core/BppLogEventCatalog.cs` | `tests/Architecture.Tests/PluginLoggingTests.cs`]
- Anchor mod file-write paths on `BepInEx.Paths.GameRootPath` or `<GameRoot>/BazaarPlusPlusV5/`, which BepInEx special-cases on macOS to the directory containing the `.app`. A path built from `Application.dataPath` writes unsealed files inside the `.app` bundle, breaking `codesign` re-signing and the trampoline repair — and therefore `./run.sh build` after every game update. [`src/BazaarPlusPlus/Core/Paths/BepInExPathProvider.cs`]

## Architecture decisions

One line each, full record in [adr/](adr/). A line here exists to stop a settled question from being reopened; the ADR says why.

- ADR-0001: Expose run/encounter state via on-demand `IEncounterStateProbe`, not an event-sourced timeline tracker.
- ADR-0002: Replay exit is explicit and single-owner — `CombatReplayRuntime.TryContinueReplay` is the only programmatic `ReplayState` exit; ghost payloads are stored in recorder perspective, stamped by `PerspectiveVersion`.
- ADR-0003: Keep behavior-specific seams and reject cosmetic unifications — the three Core seams, HistoryPanel async/state ownership, distinct tooltip normalizers, catalog-local facet snapshots, evidence-free registration-order rules.
- ADR-0004: One Collection `Destroy` chip covers the whole destroy-mechanic cluster on base templates; `TTriggerOnCardRepaired` is deliberately excluded.
- ADR-0005: Timing invariants live in pure decision cores, not MonoBehaviour glue. Staged start commits, the two-phase exit decision, the single-owner suppression latch, the null-outcome no-op, and the two-point dispose contract are load-bearing.
- ADR-0006: Outbound Mod API rules have protocol and persistence owners — one Run Bundle contract, one response parser, session-owned transport, a pure seal-convergence core, a Storage-owned bundle queue.
- ADR-0007: Remote data separates runtime catalogs, the release manifest, and build-time seed fetch into three lifecycles.
- ADR-0008: Combat Impact numbers are ledger entries — dimension/basis/coverage/provenance on every value, per-view conservation only, typed residuals never dropped, activation batches are observations (not trigger counts), attribution graph-driven (never card-GUID constants).
- ADR-0009: Tests assert behavior or compiled artifacts, never source text; RS0030 bans file-to-text reads.

## Durable knowledge

Facts that take more than one file to derive, and that ARCHITECTURE does not state.

- CollectionPanel source filtering runs off the embedded `collection-sources.json`, and the catalog size is pinned by the source/merchant/trainer count assertions in `tests/CollectionSourceFiltering.Tests/Program.cs` — adding a source means updating those expectations too. [`src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceCatalog.cs` | `tests/CollectionSourceFiltering.Tests/Program.cs`]
- The cloud backend (uploads, ghost battles, BazaarDB snapshots) lives in sibling `bazaarplusplus-server/` behind `mod-api-v5.bazaarplusplus.com` (`ModApiUploadDefaults.ApiBaseUrl`). Check claims there rather than inferring them from this tree.

## Patterns

Reuse these patterns.

- Reuse the game's native UI components (`CardPreviewBase.SetUp` and the like) and existing prior art instead of hand-rolling a render or upload chain.
- Mod-appended tooltip text goes through `BppTooltipSections`, which clones the tooltip's own passive-text block at 0.75 font scale, keyed per controller and purpose. [`src/BazaarPlusPlus/Patches/Tooltips/BppTooltipSections.cs`]
- Keep Unity-adjacent logic free of Unity types and Compile-Include it into a test project — no InternalsVisibleTo needed. `OverlayLifecycleCore`, `HotkeyBindingPathCore`, `AsyncLoadCache`, `CollectionCardFitMath`, and `SavedReplayLifecycle` reach zero-ManagedPath. `CollectionViewState` does not: it uses `BazaarGameShared` types, so its test project still references the game assemblies. [ADR-0005]

## Gotchas

- Main-menu scene identity is `SceneID.HeroSelectScene`, but the loaded Unity scene is named `MainMenuScene` in current builds. Use `SceneLoader.ActiveScene` and `IsSceneLoaded` for replay return gates; comparing the scene name to `HeroSelectSceneName` silently prevents reopening history. [`src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanel.cs`]

- Owned `MonsterBoardTooltip` clones must inherit the host canvas sorting: the donor has `overrideSorting=true` at order 0, hiding its opaque, correctly loaded cards behind history at order 26. Settle all rentals, restore prefab layout, and detach before returning to the native pool; pooling does not reparent. Fit the carpet from its own four corners: recursive bounds include card/gem overhang and shift opposing boards differently. [`src/BazaarPlusPlus/GameInterop/MonsterBoardPreview/OwnedMonsterBoardPreview.cs`]

Each of these failed silently, or reported something misleading, at least once.

- Runtime `Card` tags do not carry static template tags — hidden *and* public (`DTOUtils.CreateCard` never copies them and snapshot updates overwrite) — derive identity from template tags and static data, merging runtime+template+enchantment as `CombatImpactEntityTags` does. [`decompiled/TheBazaarRuntime/TheBazaar/DTOUtils.cs` | `src/BazaarPlusPlus/Game/PostCombatImpact/Data/CombatImpactEntityTags.cs`]
- Static-data lookup is fallible on degraded and test paths: catch and return a safe default, as the current resolvers do. [`src/BazaarPlusPlus/GameInterop/GameBuildInfoResolver.cs`]
- Rendering the reused uGUI card prefab through an offscreen camera into a `RenderTexture` silently yields an empty texture under URP — native board previews stay on the `ScreenSpaceOverlay` canvas; do not re-propose the RT path.
- A Harmony postfix on an `async Task` game method runs at the first await suspension, not at completion — bind pre-state in a **prefix**.
- A programmatic native `Button.onClick` invoke can return silently through interaction gates such as `AllowInteraction` without throwing — verify the expected game-state transition before treating the action as successful.
- `PublicizeAll` makes `ConfigEntry<T>.SettingChanged` ambiguous (CS0229), so no BPP code subscribes to it; invalidate config-derived caches by raw-value compare. [`src/BazaarPlusPlus/Game/Input/BppHotkeyService.cs`]
- The BPP hotkey conflict check compares BPP actions only against other BPP actions, never native `Gameplay/*` bindings. [`src/BazaarPlusPlus/Game/Input/BppHotkeyService.cs`]
- Panel toggle hotkeys are filtered per registration by `HotkeyGuard`, where returning false swallows the press. HistoryPanel deliberately will not close while a text field is focused. [`src/BazaarPlusPlus/Game/OverlayPanels/OverlayPanelHost.cs` | `Game/HistoryPanel/HistoryPanel.cs`]
- BazaarDB link: `OnPanelShown` must reset `AccountLinkInProgress`. `_session.Begin()` cancels in-flight redeems whose continuations bail on `!IsCurrent` without resetting state, and a re-entrant open skips `OnPanelHidden` — otherwise the link row goes permanently inert. [`src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelCoordinator.cs`]
- VoiceSubtitles labels clone the donor label's font and material and must keep `TextWrappingModes.Normal` + `TextOverflowModes.Overflow`. NoWrap+Ellipsis at a scaled font hits TMP's `m_characterCount == 0` branch and the whole subtitle block disappears. [`src/BazaarPlusPlus/Game/VoiceSubtitles/VoiceLineDisplay.cs`]
- Pooled native tooltip clones share one root-canvas `sortingOrder`, and `overrideSorting` cannot be set on a root canvas. Layering a BPP preview above the other (locked) clone means raising this clone's root-canvas order while visible, refcounted per owner; sibling order inside the prefab is irrelevant. This bug class recurred three times. [`src/BazaarPlusPlus/Patches/Tooltips/TooltipLayerOverride.cs`]
- The only working concealment seam for the native auxiliary tooltip is the `auxParent` CanvasGroup gate: `Tooltip_Aux_P`'s `auxParent` owns the complete visual tree, and a CanvasGroup on the controller root sits above the prefab's nested Canvas — silently inert. Teardown keeps the gate closed; owned gates `DestroyImmediate` on restore, since a deferred destroy leaves an end-of-frame corpse that `GetComponent` adopts and silently ungates. [`src/BazaarPlusPlus/GameInterop/Tooltips/NativePairedTooltipHost.cs` | `tests/NativePairedTooltipHost.Tests/`]
- Native `ShowAuxiliaryTooltipController` only `SetText`s — it never reactivates header/body, so a pooled controller handed back with inactive text nodes collapses to a tiny empty frame over the board. The show handoff force-reactivates requested nodes; a frame audit conceals `visible_without_text` leftovers. [`src/BazaarPlusPlus/GameInterop/Tooltips/NativePairedTooltipHost.cs`]
- Locking a native card tooltip (`SetLockedFlag(true)`) re-enables `blocksRaycasts` via `ToggleInteractabilityOnCanvas`; a tooltip overlapping the pointer then steals hover, and the synthetic `PointerExit` yields a show/hide flicker loop. Keep the lock but force raycasts off while BPP owns the controller. [`src/BazaarPlusPlus/Game/PostCombatImpact/PostCombatImpactController.cs` | `src/BazaarPlusPlus/Patches/PostCombatImpact/PostCombatImpactRecapPatch.cs`]
- UI Toolkit `Button` inherits `TextElement`, so `Q<TextElement>()` returns the button itself when no separate label exists — hiding that "label" hides the whole control (guard with `!ReferenceEquals`), and a refresh writing `Button.text` resurrects the native label beside a custom one. [`src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.Filters.cs`]
- The hardcoded `DayTierSchedule` was removed and its absence is pinned by architecture tests — do not restore it as a fallback. Day tiers resolve through `GameDataDayTierResolver`, whose success cache is keyed on `JsonGameDataManager` reference identity because the game swaps that reference after a GameData download. [`src/BazaarPlusPlus/GameInterop/DayTiers/GameDataDayTierResolver.cs` | `tests/Architecture.Tests/CoreLayeringTests.cs`]
- Source-shadow scenario capsules pin production files through explicit Compile-Include and may define mutually incompatible runtime shims. Keep them in the closed `BppScenarioRunnerProjects` list and execute them only through the process-isolated `ScenarioRunner.Tests` host. [`Directory.Build.props` | `tests/ScenarioRunner.Tests/`]
- Architecture-test file sweeps (absence assertions, reference scans) must enumerate from the `src/` and `tests/` roots, never recurse from the repo root — embedded worktrees such as `.claude/worktrees/` hold stale checkouts that still contain removed code and turn the sweep red. [`tests/Architecture.Tests/CoreLayeringTests.cs`]
- `CombatReplayPayloadStore`/`GhostBattlePayloadStore` stay thin named facades over `FileBackedPayloadStore<T>`. Capsules construct `CombatReplayPayloadStore` directly, so renaming it breaks their build; `GhostBattlePayloadStore` and the `.mpack.gz` suffixes are not test-pinned. [`tests/CombatReplayRecording.Tests/Program.cs`]
- Portrait providers negative-cache exceptions past their service-readiness gates, so one transient asset-load exception caches a null portrait until restart. Preserved by design through the `AsyncLoadCache` migration — changing it is a behavior change. [`src/BazaarPlusPlus/GameInterop/HeroPortraits/`]
- `AssetLoader` signatures vary by game build; route them through `NativeAssetLoaderInvocation`. Process-static caches must load `Global` because scene-scope handles die across lobby↔run; smoke native cards and hero chips across that transition. [`src/BazaarPlusPlus/GameInterop/AssetLoading/`]
- Synthetic or unknown GUIDs must not repeatedly reach native-preview template lookup; the grid negative-caches `TemplateUnavailable` per catalog generation. [`src/BazaarPlusPlus/GameInterop/CardPreview/NativeCardPreviewFactory.cs`]
- Dock-button lifecycle controllers must live on the always-active native button, never a `SetActive(false)` clone — inactive clones get no `LateUpdate` and miss later state transitions. The replay dock button's tooltip is the native cue activator's `defaultValue`, not a BPP-positioned auxiliary tooltip. [`src/BazaarPlusPlus/Game/CombatReplay/CurrentReplayRecordingButtonController.cs`]
- Clone the controller-free native donor first, then attach the BPP MonoBehaviour to the stable owner — cloning a GameObject that already carries a BPP controller duplicates the controller. [`src/BazaarPlusPlus/Game/CombatReplay/CurrentReplayRecordingButtonController.cs`]
- BPP settings dock buttons are clones of native dock buttons that must avoid native-button bounds and re-sync geometry for several frames after a screen-size change. [`src/BazaarPlusPlus/Game/Settings/BppDockButtonVisuals.cs`]
- Aspect-fallback collection cards (no measurable frame or raw-image bounds) hold size via a one-shot `SetSizeWithCurrentAnchors` plus the per-cell bounds cache. This path shipped without in-game smoke; on card-size drift, apply the fallback documented in the fitter header. [`src/BazaarPlusPlus/Game/CollectionPanel/Grid/NativeCardCellFitter.cs`]
- The native end-of-run reveal uses `CreateRawGraph`, whose delay roots have no `ScriptPlayableOutput` and therefore never complete — cards stay FaceDown and screenshot readiness never fires. `EndOfRunRawRevealCompletionPatch` injects a port-1 output per root; keep it. [`src/BazaarPlusPlus/Patches/EndOfRun/EndOfRunRawRevealCompletionPatch.cs`]
- `BackgroundUploadPump.OnDestroy` is a two-point dispose: release arm subscriptions first, dispose the session only after the drain callback. Merging them lets an in-flight `RunAttemptAsync` hit disposed resources. [`src/BazaarPlusPlus/Game/Upload/BackgroundUploadPump.cs` | ADR-0005]
- Scenario capsules construct HistoryPanel types positionally, so their ctor shape is pinned behavior: `HistoryPanelDependencies`' single ctor stays guard-free direct assignment (null guards break it at construction) and `HistoryPanelReplayService` keeps its discarded `pluginsDirectoryPath` parameter to hold arity. [`src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelDependencies.cs` | `src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelReplayService.cs` | ADR-0003]
- `rg 'new TypeName('` misses target-typed `new(...)` call sites, so a "zero call sites" grep proves nothing; prove a deletion by deleting (AGENTS.md, Build & Test). [`tests/PureBehavior.Tests/PureBehavior.Tests.csproj`]
- `src/` and `tests/` both set `BppEnableWarningGate`, so `TreatWarningsAsErrors` turns an unused using (IDE0005) or an unread private field (CS0414) into a build error. [`Directory.Build.props` | `src/Directory.Build.props`]
- Some `*.Tests` directories own no `.csproj`; directory wildcards in three host projects absorb their sources. Deleting or renaming one leaves its wildcard matching zero Compile items instead of failing, so those tests silently stop running. [`tests/PureBehavior.Tests/PureBehavior.Tests.csproj` | `tests/RuntimeIntegration.Tests/RuntimeIntegration.Tests.csproj` | `tests/FeatureLogging.Tests/FeatureLogging.Tests.csproj`]

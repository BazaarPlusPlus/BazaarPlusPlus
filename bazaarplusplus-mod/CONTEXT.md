# BazaarPlusPlus Mod

In-game mod for *The Bazaar*. This glossary captures the project-specific vocabulary that recurs across features. General programming concepts are excluded. Definitions pin down what a term means and where its boundary sits; responsibilities and mechanics live in code and `docs/ARCHITECTURE.md`.

## Run / encounters

**Encounter**:
A single stop on a run — a combat, PvP combat, event, shop, pedestal, loot, or level-up — that the player reaches and enters.
_Avoid_: node, map node

**Pedestal**:
An encounter that upgrades or enchants one of the player's existing items rather than granting a new one. Which pedestal kind the current Choice screen offers drives the upgrade/enchant preview's "smart" mode.

**Encounter Status Probe**:
The on-demand, pull-based read of the player's *current* run/encounter state (`IEncounterStateProbe`). The project's chosen way to expose "where is the player in the run right now" — status queries, not a recorded timeline (see [ADR-0001](docs/adr/0001-encounter-status-probe-not-timeline-tracker.md)).
_Avoid_: encounter tracker, run timeline

**Run Snapshot Probe**:
The on-demand, pull-based read of the current run's recordable facts — day/hour, win/loss, hero, mode, player stats, rank, leaderboard placement (`IRunSnapshotProbe`). Consumers map the snapshot into their own records instead of reading game globals directly.
_Avoid_: run tracker

**Game Build Channel**:
The classification of the running client as `Online`, `Ptr`, or `Unknown`, resolved once at startup by `GameBuildInfoResolver` (conflicting signals resolve to `Ptr`). It gates uploads and is stamped on recorded runs, isolating PTR data from the production dataset.
_Avoid_: environment, server flag

**Run Logging Intake**:
The pure `IBppFeature` that owns run-log subscriptions, session transitions, and persistence/checkpoint ordering. It consumes event-bus inputs; it is not a mounted Unity controller and does not own a run timeline.
_Avoid_: RunLoggingController, run logger MonoBehaviour

**Encounter Preview Module**:
The feature-owned query boundary for event cards, encounter-step rewards, and hero level rewards. Callers supply only a stable template id, current level, and native text they already hold; the module owns everything from plan generation to final presentation.

**End-of-Run Capture Workflow**:
The single run-scoped state machine that owns the end-of-run screenshot flow — readiness, bounded attempts, clean-frame preparation, artifact validation, fail-open terminal outcomes. Harmony patches express only Continue and reveal-start intents through `IEndOfRunCaptureWorkflow` and never drive capture directly.
_Avoid_: screenshot gate

**Ghost Battle**:
A PvP battle fetched from the mod backend in which the local player's uploaded build fought inside another player's run (the game's PvP is asynchronous — opponents are ghosts). Stored payloads keep the recorder's perspective, stamped as `PerspectiveVersion` (see [ADR-0003](docs/adr/0003-bazaaragent-external-replay-video-recording.md)); only the HistoryPanel list row is projected to local perspective, by `GhostBattleLocalProjector`.
_Avoid_: remote battle, opponent battle

## Combat replay

**Saved Replay Lifecycle**:
The single pure owner (`SavedReplayLifecycle`) of a saved-replay playback session's state algebra; the runtime feeds observations and executes the returned decisions. Replay exit itself still flows only through `CombatReplayRuntime.TryContinueReplay` per ADR-0003.

## Overlay panels

**Main Overlay Panel**:
A full-screen mod overlay — Collection Panel, History Panel, or Live Build Panel. At most one is open at a time; the Overlay Panel Host enforces the exclusivity.
_Avoid_: popup, window

**Overlay Panel Host**:
The single module that owns main-overlay-panel lifecycle: mutual exclusion, scene-change policy, combat gating, hotkey and escape routing, and the per-frame tick. Panels register content callbacks with the host instead of re-implementing the lifecycle.
_Avoid_: panel mutex

**Native Card Preview Host**:
The sole owning module (`GameInterop/CardPreview`) for the game's native card prefabs: setup, full visibility, hover, tooltip replacement, pooling, and destruction. Consumers open their own scopes through the host instead of holding runtime, reflection, or pool internals.
_Avoid_: global preview pool

**Native Card Preview Scope / Session**:
A scope is one real UI owner's resource boundary, with its own pool; closing it settles pending acquisitions and destroys its objects. A session is an opaque lease of one set-up card, exposing only the layout root/rect and show/hide/hover intents — callers never hold native components or setup tasks.
_Avoid_: preview handle

## Fonts

**Native Game Typography**:
The single adapter (`GameInterop/Fonts/NativeGameTypography`) through which every BPP surface gets text rendering. It reuses the game's own font assets and zh-CN fallback chains and never exposes a raw `Font`/`TMP_FontAsset`; BPP embeds no fonts of its own.
_Avoid_: custom font, font selector

## Settings dock

**Cycling Settings Dock Entry**:
The unified settings-dock concept for entries that cycle through an ordered value ladder on click, highlight when off-default, and render localized state. Features contribute data only, not behavior classes; a bool toggle is the two-value special case. Action buttons and lock toggles are not this concept.

## Uploads

**Run Bundle Contract**:
The shared V5 reader/writer boundary that opens a Bundle, verifies manifest-to-payload Run identity,
and decides whether a Battle has the exact snapshot and replay inputs required for import. Bundle
composition and Ghost Battle import use the same contract.

**Mod API Response**:
The bounded HTTP response boundary for mod-backend JSON: nested and legacy error-envelope shape,
request ID precedence, `Retry-After`, and the separation between a closed user code and diagnostic
exception. Endpoint-specific disposition and retry policy stay with each client.

**Mod API Session**:
One owned transport lifetime for one mod-backend consumer. It combines normalized routes, standard
headers and timeout with typed Bundle, Ghost Battle, and health operations, without exposing its
`HttpClient` or endpoint adapters. Plugin History, each Upload Feed activation, and tools own
separate sessions.
_Avoid_: shared global HttpClient, public endpoint-client chain

**Bundle Seal Convergence**:
The pure decision core for one Bundle seal pass. It receives the remaining relative deadline and
observed input facts, then decides whether to continue, wait, degrade the screenshot, or terminate;
it owns no clock, SQLite connection, file, codec, or Unity object.

**Bundle Queue Store**:
The concrete Storage owner of `bundle_seal_jobs` and `bundle_outbox` rows, SQL, and multi-row
transactions. Game workflows translate its storage records into composition and upload behavior;
there is deliberately no same-shape single-implementation repository interface.
_Avoid_: outbox repository interface

**Bundle Outbox Files**:
The Game-owned file port for sealed Bundle artifacts. It exposes root-confined open, existence,
length, enumeration, and deletion so upload ordering and retention can be tested without moving
filesystem policy into Storage.

**Upload Feed Session**:
The behavior object (`IUploadFeedSession`) a feed returns from activation: feature enablement, one upload attempt, feed-private arm signals, and resource disposal. The background pump owns only the Unity cadence, the shutdown drain, and the shared gates (PTR channel precondition, run-lifecycle and `UploadArmRequested` arm signals); it never rewires feed internals.
_Avoid_: per-feed upload controller

## Collection panel

**Collection View State**:
The single owner of the Collection Panel's presentable state (`CollectionViewState`) — filter selections, search, catalog acceptance, and the derived render model. Commands and lifecycle events go in; a complete render outcome comes out; the panel's Unity surface only forwards commands and applies outcomes. Nothing outside the module reads or writes the filter.
_Avoid_: ApplyFilters/RefreshView pairing

## Collection sources

**Collection Source Catalog**:
The versioned in-mod catalog that defines merchant and trainer source filters for the Collection Panel. It is built from `collection-sources.json`, validated against `CollectionSourceCatalog.ExpectedSchemaVersion`, and keyed by stable source keys plus source template ids.

**Offer Pool**:
The set of card templates a source can offer, expressed through structured rules (`CollectionSourceEntry.OfferSegments`) rather than runtime encounter fallback heuristics. Resolution keeps both the union of offered card ids and per-card source matches.

**Collection Source Kind**:
The source category used by CollectionPanel source chips: `Merchant` maps to Item sources, `Trainer` maps to Skill sources.

## Day tiers

**Day Tier Resolver**:
The shared GameInterop adapter (`GameInterop/DayTiers/GameDataDayTierResolver`) that resolves the current run day's item/skill tier distribution from live GameData into a normalized weight table plus `MaximumTier` — the highest usable Bronze-to-Diamond tier, not the largest probability. Consumers (Collection's Day gate, Event Preview) fail open when the table is unavailable.
_Avoid_: DayTierSchedule, hardcoded tier table

## Remote embedded data

**Remote Embedded Catalog**:
The shared runtime lifecycle for data shipped as an embedded seed, cached under `<GameRoot>/BazaarPlusPlusV5/`, and refreshed from a remote source (`IRemoteEmbeddedCatalog<T>`). The interface exposes only current-snapshot lookup, warm-up, explicit refresh, and disposal; feature modules keep their own parser, logging, and user-facing refresh policy.
_Avoid_: feature repository loader

**Supporter Catalog Module**:
The composition-owned Remote Embedded Catalog consumer for supporter data: fixed-list bypass,
session-stable snapshots through the supporter facade, retry/disposal ownership.

**Release Manifest**:
The installer-published `{ "version": ... }` document used only for the main-menu update check. Its
HTTP/JSON adapter and request-generation lifecycle are separate from Mod API health and installer
runtime state.
_Avoid_: Mod API health

**Build Seed Fetch**:
The build-time transport operation that stages remote embedded seeds, checks coarse transfer
integrity, runs feature-owned semantic gates, and promotes the complete seed set transactionally.

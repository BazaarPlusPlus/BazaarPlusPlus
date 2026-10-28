# BazaarPlusPlus Mod

Definitions pin down what a term means and where its boundary sits; mechanics live in code and `docs/ARCHITECTURE.md`.

## Run / encounters

**Encounter**:
A single stop on a run — a combat, PvP combat, event, shop, pedestal, loot, or level-up — that the player reaches and enters.
_Avoid_: node, map node

**Pedestal**:
An encounter that upgrades or enchants one of the player's existing items rather than granting a new one.

**Encounter Status Probe**:
The on-demand, pull-based read of the player's *current* run/encounter state (`IEncounterStateProbe`). Status queries, not a recorded timeline (ADR-0001).
_Avoid_: encounter tracker, run timeline

**Run Snapshot Probe**:
The on-demand, pull-based read of the current run's recordable facts — day/hour, win/loss, hero, mode, player stats, rank, leaderboard placement (`IRunSnapshotProbe`). Consumers map it into their own records, never reading game globals.
_Avoid_: run tracker

**Game Build Channel**:
The client's `Online`, `Ptr` (PTR and staging), or `Unknown` classification (`GameBuildInfoResolver`). It gates uploads and is recorded on runs to keep test data out of production.
_Avoid_: environment, server flag

**Run Logging Intake**:
The pure `IBppFeature` that turns event-bus inputs into run-log sessions and persistence; it owns no run timeline.
_Avoid_: RunLoggingController, run logger MonoBehaviour

**Detailed Preview**:
The shared switch for encounter and quest tooltip information supported by each host. Excludes upgrade and enchant previews.

**Encounter Preview Module**:
The event and hero reward query boundary: template id, level, and native text in; rendered preview out.

**End-of-Run Capture Workflow**:
The single run-scoped state machine owning the end-of-run screenshot, from readiness to a fail-open outcome. Patches only send Continue and reveal-start intents (`IEndOfRunCaptureWorkflow`).
_Avoid_: screenshot gate

**Ghost Battle**:
A PvP battle fetched from the mod backend in which the local player's uploaded build fought inside another player's run (the game's PvP is asynchronous — opponents are ghosts). Stored in the recorder's perspective (ADR-0002).
_Avoid_: remote battle, opponent battle

## Combat replay

**Replay Payload**:
The local serialized combat input used to replay a recorded PvP battle: a recoverable cache (`ReplayPayloadRetentionPolicy`) whose battle fact outlives its eviction.

**Replay Availability**:
Whether History can replay a battle: Remote (a Ghost Battle not yet downloaded), Saved (a local Replay Payload exists), Expired, or Unavailable. Local battles are only Saved or Unavailable.
_Avoid_: replay state, downloaded flag

**Replay Video Artifact**:
A user-requested MP4 plus its metadata. Attachment to a battle and file health are separate facts: deleting a run detaches the metadata without deleting a completed MP4.
_Avoid_: replay cache, orphan video

**Native Frame Submission**:
One recording tick's admission of the current rendered frame to a native encoder, including its output timing and capture/drop accounting. Platform capture resources live for the recording, not for the tick.

**Saved Replay Playback**:
The Core module (`SavedReplayPlayback`) owning one saved replay from admission to menu return on both hosts (ADR-0020).

**Combat Impact Corpus**:
Combat Impact's acceptance input in BPP types, exported host-side from game payloads (ADR-0019).

**PvP Battle Capture Window**:
Core's `PvpBattleCaptureWindow`: frames one PvP battle from a host's opening, combat and closing sims; hosts read and persist (ADR-0021).
_Avoid_: matcher, collector, sequence candidate

## Overlay panels

**Live Build Refresh**:
The manual update of the Live Build Panel's recommendation corpus; one successful pull per panel lifetime.

**Main Overlay Panel**:
A full-screen mod overlay on either Host: Collection, History, or Live Build Panel; at most one is open. Not the touch menu.
_Avoid_: popup, window

**Overlay Panel Host**:
Core's `OverlayPanelHost` plus a thin adapter per Host, owning Main Overlay Panel lifecycle (ADR-0022).
_Avoid_: panel mutex

**Panel Switch-Off**:
A panel's setting turned off: the host closes and notifies it, then releases Host resources.

**Cursor Page / Counted Page**:
One read of a History list. A cursor page holds rows plus time/ID anchors and whether newer or older rows exist; the battle timeline and Ghost recovery use it. A counted page adds the one-based position of its first row and the filtered total, from which the neighbours follow; the Runs and Ghost archives use it.
_Avoid_: page with optional counts

**Native Card Preview Host**:
The sole owning module (`GameInterop/CardPreview`) for the game's native card prefabs; consumers reach them only through their own scopes.
_Avoid_: global preview pool

**Native Card Preview Scope / Session**:
A scope is one real UI owner's resource boundary, with its own pool. A session is an opaque lease of one set-up card, exposing only layout and show/hide/hover intents.
_Avoid_: preview handle

## Fonts

**Native Game Typography**:
The single adapter (`GameInterop/Fonts/NativeGameTypography`) through which every BPP surface renders text with the game's own fonts; BPP embeds none.
_Avoid_: custom font, font selector

## Settings dock

**Cycling Settings Dock Entry**:
A settings-dock entry that cycles through an ordered value ladder on click; a bool toggle is the two-value case. Features contribute data only. Action buttons and lock toggles are not this concept.

## Uploads

**Run Bundle Contract**:
The shared V6 reader/writer boundary (`RunBundleV6Contract`) that opens a Bundle, verifies its Run
identity, and decides whether a Battle is importable; Bundle composition and Ghost Battle import share it.

**Mod API Response**:
The bounded HTTP response boundary for mod-backend JSON: error envelope, request ID, `Retry-After`,
closed user code. Endpoint disposition and retry policy stay with each client.

**Mod API Session**:
One owned transport lifetime (`ModApiSession`) for one mod-backend consumer, exposing typed Bundle,
Ghost Battle, and health operations but never its `HttpClient`.
_Avoid_: shared global HttpClient, public endpoint-client chain

**Bundle Seal Convergence**:
The pure decision core for one Bundle seal pass (`BundleSealConvergence`, with
`BundleSealFailurePolicy` and `BundleSealWakeSchedule`): continue, wait, degrade the screenshot, or
terminate. It owns no clock, connection, file, codec, or Unity object.

**Environment-Blocked Seal**:
A waiting seal job whose failure came from the loaded runtime (a missing member, type load, or
assembly binding failure) rather than its data; it is retried once per launch.
_Avoid_: JSON failure

**Seal-Eligible Run**:
A completed, non-PTR Ranked Run, not History-Only, with neither a seal job nor any outbox row;
job discovery gives it a waiting job. `BundleQueueStore.SealEligibleRunCondition` is the one SQL rule
(root `AGENTS.md` Contracts).
_Avoid_: sealable run

**Bundle Queue Store**:
The concrete Storage owner of `bundle_seal_jobs` and `bundle_outbox` rows, SQL, and transactions,
deliberately behind no same-shape repository interface.
_Avoid_: outbox repository interface

**Bundle Outbox Files**:
The Game-owned, root-confined file port (`IBundleOutboxFiles`) for sealed Bundle artifacts.

**Upload Feed Session**:
`BundleUploadFeed.Session`, returned by feed activation: upload attempts, queue backoff, and their resources. The pump owns only cadence, shutdown drain, and shared gates.
_Avoid_: per-feed upload controller

## Cards

**Card Facet**:
A Core enum for one card or combat property (`CardSize`, `CardSocket`), named member for member after the game enum it replaces (ADR-0015, ADR-0019).
_Avoid_: game enums in Core

**Live Card Walk**:
A Host's raw run read in Card Facets (`LiveCardWalk`); Core's `LiveCardSnapshotRead` fits, numbers and reports it.

## Collection panel

**Collection View State**:
The single owner of the Collection Panel's presentable state (`CollectionViewState`, Core). Open takes **Collection Open Facts** (`CollectionOpenFacts`), the host's run and catalog reads.
_Avoid_: ApplyFilters/RefreshView pairing; open selection inputs

## Collection sources

**Collection Source Catalog**:
The Collection Panel's versioned merchant and trainer source catalog (`collection-sources.json`), keyed by stable source keys and source template ids.

**Offer Pool**:
The card templates a source can offer, as structured rules (`CollectionSourceEntry.OfferSegments`), not runtime encounter heuristics.

**Collection Source Kind**:
The source chip category: `Merchant` sources offer Items, `Trainer` sources offer Skills.

## Day tiers

**Day Tier Resolver**:
The shared GameInterop adapter (`DayTiers/GameDataDayTierResolver`) turning live GameData into the run day's tier weights and `MaximumTier`, the highest usable tier (not the most likely).
_Avoid_: DayTierSchedule, hardcoded tier table

## Remote embedded data

**Remote Embedded Catalog**:
The shared runtime lifecycle (`IRemoteEmbeddedCatalog<T>`) for data shipped as an embedded seed, cached under `<GameRoot>/BazaarPlusPlusV6/`, and refreshed remotely; features keep their parser and refresh policy (ADR-0007).
_Avoid_: feature repository loader

**Supporter Catalog Module**:
The composition-owned Remote Embedded Catalog consumer for supporter data.

**Platform Release Manifest**:
Defined in `../GLOSSARY.md`; the mod reads only its own platform's `version` for the update check
(`ReleaseManifestEndpoints`), separately from Mod API health.
_Avoid_: Mod API health

**Build Seed Fetch**:
The build-time operation that stages, gates, and transactionally promotes remote embedded seeds.

## Build

**Game-supplied library**:
A third-party library the game's `Managed/` directory loads for the mod (Newtonsoft.Json, MessagePack). The mod's Mono target compiles against the game's file, never a package, and never publicizes or ships it (`build/GameLibraries.props`, ADR-0010).
_Avoid_: game JSON runtime, compile-only package

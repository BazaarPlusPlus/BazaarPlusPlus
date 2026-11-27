# BazaarPlusPlus Mod

Domain vocabulary and boundaries for the mod. Cross-project terms (`Host`, `Payload`, `Data Root`, `Bundle`, `Run`, `Run Message Stream`, `Ghost Battle`, `Platform Release Manifest`, `Managed Payload`, `Updater`, `Snapshot Lock`) live in [the root glossary](../GLOSSARY.md); internal module seams and mechanics live in code and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Run and encounters

**Encounter**:
A single stop on a run — a combat, PvP combat, event, shop, pedestal, loot, or level-up — that the player reaches and enters.
_Avoid_: node, map node

**Pedestal**:
An encounter that upgrades or enchants one of the player's existing items rather than granting a new one.

**Game Build Channel**:
The client's `Online`, `Ptr` (PTR and staging), or `Unknown` classification (`GameBuildInfoResolver`). It gates uploads and is recorded on runs to keep test data out of production.
_Avoid_: environment, server flag

**Detailed Preview**:
The shared switch for encounter and quest tooltip information supported by each host. Excludes upgrade and enchant previews.

**Ghost Battle** (mod scope):
Defined in [the root glossary](../GLOSSARY.md). Stored locally in the recorder's perspective (ADR-0002).
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

**Combat Impact Corpus**:
Combat Impact's acceptance input in BPP types, exported host-side from game payloads (ADR-0019).

## Overlay panels

**Live Build Refresh**:
The manual update of the Live Build Panel's recommendation corpus; one successful pull per panel lifetime.

**Main Overlay Panel**:
A full-screen mod overlay on either Host: Collection, History, or Live Build Panel; at most one is open. Not the Mobile UI's HUD, which registers with the Overlay Panel Host but never opens.
_Avoid_: popup, window

**Overlay Panel Host**:
Core's `OverlayPanelHost` plus a thin adapter per Host, owning Main Overlay Panel lifecycle (ADR-0022). It also ticks the Mobile UI's HUD, registered as `hud`, which never opens: nothing calls `RequestOpen` on it.
_Avoid_: panel mutex

**Walkthrough Control**:
The Overlay Panel Host's file trigger for the device walkthrough: `open <panel id>` in `walkthrough-command.txt` in the Data Root opens that Main Overlay Panel as a hotkey toggle would, on every Host; `shot <name>` captures the desktop window to `ui-shots/` (ADR-0027).
_Avoid_: debug menu, test hook

**Panel Switch-Off**:
A panel's setting turned off: the host closes and notifies it, then releases Host resources.

**Cursor Page / Counted Page**:
One read of a History list. A cursor page holds rows plus time/ID anchors and whether newer or older rows exist (battle timeline, Ghost recovery). A counted page adds the one-based position of its first row and the filtered total (Runs and Ghost archives).
_Avoid_: page with optional counts

## Settings and loadout

**Cycling Settings Dock Entry**:
A settings-dock entry that cycles through an ordered value ladder on click; a bool toggle is the two-value case. Features contribute data only. Action buttons and lock toggles are not this concept.

**Random Cosmetic Pool**:
The cosmetics of one category that the game's Randomize may equip for one hero at run start. The player leaves cosmetics out while Randomize is on (Pool Mode); a default cosmetic is never in it. With nothing left out, the game's own pick stands.

## Uploads

**Environment-Blocked Seal**:
A waiting seal job whose failure came from the loaded runtime (a missing member, type load, or assembly binding failure) rather than its data; it is retried once per launch.
_Avoid_: JSON failure

**Seal-Eligible Run**:
A completed, non-PTR Ranked Run, not History-Only and not an Empty Run, with neither a seal job nor any outbox row; job discovery gives it a waiting job. `BundleQueueStore.SealEligibleRunCondition` is the one SQL rule (root `AGENTS.md` Contracts).
_Avoid_: sealable run

## Cards

**Effective Card Tags**:
The union of a card's runtime, template, and active-enchantment public and hidden tags; repeated tags count once per card.

**Card Facet**:
A Core enum for one card or combat property (`CardSize`, `CardSocket`), named member for member after the game enum it replaces (ADR-0015, ADR-0019).
_Avoid_: game enums in Core

**Saved Card Values**:
A card's attribute values as captured (battle snapshot, build, combat), keyed by the game's member name (ADR-0017); a card BPP sets up shows them over its template's (`NativeCardSetUp`, `AndroidNativeCardSetUp`).
_Avoid_: instance values, template values

**Tiny**:
A Medium or Large item the game shrank one size for the rest of the Run (`TActionCardTiny`): the live card's `Size` drops and its `Tiny` attribute is set, while keeping its template. A captured board card keeps that smaller size, so anything drawing it takes the size from the card, not the template (`CardRentalSubject.ShownSize`). `EEnchantmentType.Tiny` is not how an item gets it; only a shader keyword uses that name.

## Mobile UI

**Mobile UI**:
BPP's touch surfaces, one source tree (`src/BazaarPlusPlus.Mobile/`) compiled by the Android host (ADR-0025). The Desktop UI is the Desktop Host's mouse-and-keyboard surfaces.
_Avoid_: Android UI, touch port

**Mobile Platform Port**:
What the Android host gives the Mobile UI to reach its runtime (`IMobilePlatformPort`): main-thread work, control actions, borrowed game art and font, card rental, game reads, and overlay registration. Panel and HUD frames come from overlay registration ticks (ADR-0025).
_Avoid_: shim, `#if` branch

**Native Adjustment**:
A change BPP makes to the game's own UI on a mobile platform, such as screen fill, the day clock's place, or a HUD shortcut. Core decides it, the Mobile UI runs it, and the host reads and writes the game objects.
_Avoid_: native patch, HUD hack

**Screen Fill**:
The Native Adjustment that lets the game fill a landscape screen narrower than 16:10 instead of letterboxing it: a lower aspect floor, wider field of view, day clock in the band above the board, day-change overlay kept over the whole screen, and canvases kept at least 16:10 wide (`ScreenFill` in Core, `TouchScreenFill` in Mobile UI).
_Avoid_: letterbox fix, fullscreen mode

## Collection sources

**Collection Source Catalog**:
The Collection Panel's versioned merchant and trainer source catalog (`collection-sources.json`), keyed by stable source keys and source template ids.

**Offer Pool**:
The card templates a source can offer, as structured rules (`CollectionSourceEntry.OfferSegments`), not runtime encounter heuristics.

**Collection Source Kind**:
The source chip category: `Merchant` sources offer Items, `Trainer` sources offer Skills.

## Remote data and build

**Remote Embedded Catalog**:
The shared runtime lifecycle (`IRemoteEmbeddedCatalog<T>`) for data shipped as an embedded seed, cached under `<GameRoot>/BazaarPlusPlusV6/`, and refreshed remotely; features keep their parser and refresh policy (ADR-0007).
_Avoid_: feature repository loader

**Platform Release Manifest** (mod scope):
Defined in [the root glossary](../GLOSSARY.md); the mod reads its platform's version, Managed Payload, and Android APK metadata through the dedicated mod endpoint (`ReleaseManifestEndpoints`), separately from the installer's updater and Mod API health.
_Avoid_: Mod API health

**Build Seed Fetch**:
The build-time operation that stages, gates, and transactionally promotes remote embedded seeds (ADR-0007).

**Game-supplied library**:
A third-party library the game's `Managed/` directory loads for the mod (Newtonsoft.Json, MessagePack). The mod's Mono target compiles against the game's file, never a package, and never publicizes or ships it (`build/GameLibraries.props`, ADR-0010).
_Avoid_: game JSON runtime, compile-only package

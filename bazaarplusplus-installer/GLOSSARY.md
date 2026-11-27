# BazaarPlusPlus Installer

Installer-specific domain vocabulary. Cross-project terms (`Payload`, `Payload Inventory`, `Release Manifest`, `Platform Release Manifest`, `Mainland Mirror`, `Data Root`, `Legacy Root`, `History-Only Run`, `Decision Path`, `Card Catalog`) are defined in [the root glossary](../GLOSSARY.md).

## Installation and maintenance

**Selected Game Installation**:
The session-scoped *The Bazaar* installation shared by Install, History, and Stream.

**InstallState**:
The native contract supplying detected paths, readiness, warnings, and action gates to the Install workflow.

**Legacy Root** (installer scope):
Defined in [the root glossary](../GLOSSARY.md). The Install page measures and deletes Legacy Roots one at a time ([Install and reset](docs/install-reset.md#legacy-roots)).

**Reset Local Data**:
Explicit deletion of the current Data Root, distinct from uninstall, which preserves every Data Root.

**Running-Game Refusal**:
The fail-closed check that blocks any operation touching game-directory files while *The Bazaar* is running or cannot be confirmed closed; each operation has its own problem code.

**Data Maintenance Lock**:
The installer-wide lock that serializes History cleanup, Reset, BepInEx reset, and Legacy Root deletion; acquired before the stream lifecycle gate.

**Semantic Problem**:
A stable error code plus typed parameters and an optional diagnostic; frontend copy derives from the code rather than native error text.

**Generated Bindings**:
The TypeScript command client exported from Rust command signatures; generated files are replaceable artifacts.

## History and stream

**History List**:
The paginated view of local runs selected by filter (hero, period, result, Key Card), with totals across every matching run (or the complete local history when unfiltered). Its selected page and filter are distinct from Run Detail and from thumbnail availability.

**Final Board**:
The items on a run's board in its last local battle whose board the mod captured; socket effects are not items. The analyzer's Build Identity is computed from the same board in Bundles.

**Key Card**:
One item a History List filter requires the Final Board to hold, matched by template id and shown under the game's own title for it.

**History Thumbnail**:
The cropped strip of a run's screenshot on a History List card, served under a process-local source bound to the installation the page was read from. Distinct from the OBS overlay's strip, which follows the captured stream installation.

**Run Detail**:
The day-first view of one history run: its battles, recorded Decision Path, screenshot, and videos.

**Stored Path**:
A path the mod stored relative to one directory of the Data Root (such as a screenshot or video path); History reads every one through one literal rule ([History and storage](docs/history-storage.md#stored-paths)).

**Stream Runtime**:
The serialized owner of the local OBS overlay service lifecycle and captured installation state.

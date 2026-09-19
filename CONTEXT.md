# BazaarPlusPlus

Terms used by more than one project. A project glossary may narrow one of these terms for its own use; it links here for the definition.

## Product release

**Product Release**:
The mod, the installer, and the published release record for one product version. The product version says nothing about a database schema, a native ABI, or the version a user has installed.

**Payload**:
The files the installer carries and installs into the game directory. Each file's ownership decides which paths install, repair, and uninstall may change.

**Payload Inventory**:
The shared facts about Payload files: current files, platform scope, ownership, and retired files. User data and third-party plugins are outside it.

**Release Manifest**:
The published Product Release's version, per-platform updater packages, and actual installer download URLs (`latest.json`). It describes a promoted release, never the state of one platform's upload.

**Release Promotion**:
Setting a Product Release as latest once every declared platform artifact is in place. Repeating the promotion of the same release leaves the existing artifacts unchanged.

## Data pipeline

**Bundle**:
The immutable unit of upload, storage, and delivery: exactly one Run and zero or one Screenshot. The mod writes it, the server stores and delivers it, the analyzer reads it.

**Run**:
The immutable facts, battles, card state, and replay inputs of one completed game run, carried inside a Bundle. It excludes the Screenshot.

**Ghost Battle**:
A PvP battle in which a player's uploaded build fought inside another player's run. The server serves it as a query projection of Bundle manifests; the mod imports it into local history.

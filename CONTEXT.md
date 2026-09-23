# BazaarPlusPlus

Terms used by more than one project. A project glossary may narrow one of these terms for its own use; it links here for the definition.

## Product release

**Product Release**:
The mod, the installer, and the published release record for one product version. One product version names one git commit; each platform ships it at most once and may skip it. The product version says nothing about a database schema, a native ABI, or the version a user has installed.

**Payload**:
The files the installer carries and installs into the game directory. Each file's ownership decides which paths install, repair, and uninstall may change.

**Payload Inventory**:
The shared facts about Payload files: current files, platform scope, ownership, and retired files. User data and third-party plugins are outside it.

**Platform Release Manifest**:
One platform's newest promoted release: its version, updater package, actual installer download URL, and recorded Mainland Mirror address (`latest/<platformKey>.json`). Platforms may be at different versions.

**Release Manifest**:
The newest Product Release that every declared platform promoted at the same version and commit, in the same shape for all platforms (`latest.json`). Clients built before the per-platform endpoint read only this file, so it never runs ahead of any platform.

**Release Promotion**:
Publishing one platform's Platform Release Manifest once its artifacts are in place and its Mainland Mirror is recorded or explicitly waived; the Release Manifest advances when every platform names the same release. Repeating the promotion of the same release leaves the existing artifacts unchanged.

**Mainland Mirror**:
A manually uploaded share page of one platform's installer for mainland-China networks. Its address is an explicit operator input, checked against the uploaded installer and recorded per platform before that platform's Release Promotion, then published in its Platform Release Manifest; consumers read it and never derive it. It is a manual download fallback, never a second release source or an updater endpoint.

## Data pipeline

**Bundle**:
The immutable unit of upload, storage, and delivery: exactly one Run and zero or one Screenshot. The mod writes it, the server stores and delivers it, the analyzer reads it.

**Run**:
The immutable facts, battles, card state, and replay inputs of one completed game run, carried inside a Bundle. It excludes the Screenshot.

**Ghost Battle**:
A PvP battle in which a player's uploaded build fought inside another player's run. The server serves it as a query projection of Bundle manifests; the mod imports it into local history.

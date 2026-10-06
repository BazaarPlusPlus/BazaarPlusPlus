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

## Game assemblies

**Game Assembly Snapshot** (游戏程序集快照):
One captured copy of the game's `Managed/` directory, identified by the game version string Unity writes in `globalgamemanagers` beside it (`1.0.12575-staging-macos-arm64-fecb8f8e`: build number, channel token, platform, game commit) and verified by the sha256 of its DLL records. It is captured from a mounted Steam install for one platform and channel (`macos` or `windows` × `online`, `staging`, `ptr`, the Steam branches `public`, `staging`, `public_test_realm`), stored content-addressed in the private `bazaarplusplus-game-libs` bucket, and unpacked under `bazaarplusplus-mod/game-libs/`. It is a build and test input, never part of the Payload; the Steam buildid and branch are its source record, not its identity. The snapshot channel keeps staging and ptr apart, which the mod's runtime Game Build Channel folds into `Ptr`.
_Avoid_: Steam branch copy, game-libs archive

**Snapshot Lock** (快照锁):
`bazaarplusplus-mod/build/game-libs.lock.json`: the committed binding of the mod source to one Game Assembly Snapshot per platform and channel, six keys that stay `null` until captured. After an explicit override, `ManagedPath` resolves only from it: the fetched snapshot, or the local Steam install whose game version and sha256 match the entry. Each platform's product compiles against its `online` entry; `staging` and `ptr` entries feed compatibility builds and tests. It changes only through a pull request; `release/game-libs.mjs` owns its schema and the mod recipes capture, publish, fetch, and check it.
_Avoid_: game-libs manifest, Steam buildid pin

## Credentials

**Operator Token** (操作员 token):
The one Cloudflare API token a maintainer holds for every R2 bucket (`bppinstaller`, `bazaarplusplus-game-libs`, the metrics bucket) and for Wrangler: `config.ini` `[cloudflare] CLOUDFLARE_API_TOKEN` locally, the `release` environment secret `CLOUDFLARE_API_TOKEN` on GitHub. Credentials are kept one per trust domain, not one per bucket; the other two domains are the read-only CI token (`BPP_GAME_LIBS_TOKEN`, the game-libs bucket only) and the site deploy token. R2's S3 pair is a view of a token, Access Key ID its id and Secret Access Key the SHA-256 of its value, so `release/r2-store.mjs` and the analyzer's `object_store.py` derive it and nothing stores it. `release/github-secrets.json` maps every GitHub name to its scope and local source.
_Avoid_: release R2 keys, per-bucket key pair

## Data pipeline

**Bundle**:
The immutable unit of upload, storage, and delivery: exactly one Run and zero or one Screenshot. The mod writes it, the server stores and delivers it, the analyzer reads it.

**Run**:
The immutable facts, battles, card state, and replay inputs of one completed game run, carried inside a Bundle. It excludes the Screenshot.

**Ghost Battle**:
A PvP battle in which a player's uploaded build fought inside another player's run. The server serves it as a query projection of Bundle manifests; the mod imports it into local history.

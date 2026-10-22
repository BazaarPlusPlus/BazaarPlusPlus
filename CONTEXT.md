# BazaarPlusPlus

Terms used by more than one project. A project glossary may narrow one of these terms for its own use; it links here for the definition.

## Product release

**Product Release**:
The mod, the installer, and the published release record for one product version. One product version names one git commit; each platform ships it at most once and may skip it. The product version says nothing about a database schema, a native ABI, or the version a user has installed.

**Payload**:
The files the mod loader host loads: on desktop, what the installer carries and installs into the game directory; on Android, what sits under the app's `MelonLoader/` files directory and inside the Android Release APK. Each file's ownership decides which paths install, repair, and uninstall may change.

**Payload Inventory**:
The shared facts about Payload files (`release/payload.json`): current files, their host (`bepinex` or `melonloader`) and platform scope, ownership, producer, and retired files. Each release platform installs only its own host's entries. User data and third-party plugins are outside it.

**Platform Release Manifest**:
One platform's newest promoted release (`latest/<platformKey>.json`). A desktop one carries its version, updater package, actual installer download URL, and recorded Mainland Mirror address; the Android one carries the Android Release APK, the Android Mod Payload zip, and the game they were built for. Platforms may be at different versions.

**Lockstep Platform** (lockstep 平台):
A platform the Release Manifest waits for: Windows and macOS (`LOCKSTEP_PLATFORM_KEYS` in `release/release-platforms.mjs`). Android is a release platform but not a Lockstep Platform, so its game-driven cadence never holds back a desktop release.

**Release Manifest**:
The newest Product Release that every Lockstep Platform promoted at the same version and commit, in the same Tauri shape for each (`latest.json`). Installers whose own Platform Release Manifest fails to load fall back to it, so it never runs ahead of any Lockstep Platform.

**Release Promotion**:
Publishing one platform's Platform Release Manifest once its artifacts are in place and, on desktop, its Mainland Mirror is recorded or explicitly waived; the Release Manifest advances when every Lockstep Platform names the same release. Repeating the promotion of the same release leaves the existing artifacts unchanged.

**Android Release APK** (Android 发布 APK):
The official game APK with the Loader Release, the Interop Snapshot and an Android Mod Payload injected, signed with BPP's Android release key: what an Android user installs. Its identity is the game version, the loader and the interop, not the product version: it is stored once under `android/apk/` and named by those three, `builtForProductVersion` records which Mods it carries, and later product versions point at the same APK until the game, loader or interop changes.
_Avoid_: patched APK, Android installer

**Managed Payload** (托管 Payload):
One platform's `managed` and `history-contract` Payload entries for one product version, published as a zip signed with the Tauri updater key at `<version>/<platformKey>/managed/` and recorded as `managedPayload` in that platform's fragment: what the mod's self-update installs. Android publishes it from the start; desktop fragments define the field but no build produces it yet.

**Android Mod Payload** (Android Mods 包):
The Payload files the Android host loads from `MelonLoader/Mods/` and `MelonLoader/UserLibs/`: baked into the Android Release APK for the first start, and published for every product version as Android's Managed Payload.
_Avoid_: Mods zip

**Mainland Mirror**:
A manually uploaded share page of one platform's installer for mainland-China networks. Its address is an explicit operator input, checked against the uploaded installer and recorded per platform before that platform's Release Promotion, then published in its Platform Release Manifest; consumers read it and never derive it. It is a manual download fallback, never a second release source or an updater endpoint.

## Game assemblies

**Game Assembly Snapshot** (游戏程序集快照):
One captured copy of the game's `Managed/` directory, identified by the game version string Unity writes in `globalgamemanagers` beside it (`1.0.12575-staging-macos-arm64-fecb8f8e`: build number, channel token, platform, game commit) and verified by the sha256 of its DLL records. It is captured from a mounted Steam install for one platform and channel (`macos` or `windows` × `online`, `staging`, `ptr`, the Steam branches `public`, `staging`, `public_test_realm`), stored content-addressed in the private `bazaarplusplus-game-libs` bucket, and unpacked under `bazaarplusplus-mod/game-libs/`. It is a build and test input, never part of the Payload; the Steam buildid and branch are its source record, not its identity. The snapshot channel keeps staging and ptr apart, which the mod's runtime Game Build Channel folds into `Ptr`.
_Avoid_: Steam branch copy, game-libs archive

**Interop Snapshot** (互操作快照):
The Il2CppInterop proxy assemblies generated from one official Android APK, with the generator's databases and `interop-manifest.json` beside them: the Android counterpart of a Game Assembly Snapshot, identified by the APK's `versionName` (`1.0.12825-production-android-d9601ded`: no architecture token, `production` is online) and verified by the sha256 of every file record. It is captured with `just mod::snapshot-android` from an APK and a pinned toolchain (Patcher, Cpp2IL, Il2CppInterop), stored content-addressed in the private bucket beside the official APK and that toolchain, and unpacked under `bazaarplusplus-mod/game-libs/android-online-<gameVersion>/interop/`. Like a Game Assembly Snapshot it is a build input, never part of the Payload.
_Avoid_: interop dump, IL2CPP managed folder

**Loader Release** (加载器发行包):
One tagged LemonLoader Android release zip (`LemonLoader-Android-arm64.zip`): the native bootstrap, the CoreCLR runtime and the managed loader assemblies (`MelonLoader.dll`, `0Harmony.dll`, `Il2CppInterop.Runtime.dll`) an Android host compiles against and an injected APK carries. The Snapshot Lock pins it by tag and sha256 inside the `android-online` entry; a copy lives in the private bucket so a deleted upstream tag does not break a rebuild, and `just mod::fetch android online` unpacks it under `bazaarplusplus-mod/game-libs/loader-<tag>/`.
_Avoid_: MelonLoader install, loader runtime

**Snapshot Lock** (快照锁):
`bazaarplusplus-mod/build/game-libs.lock.json`: the committed binding of the mod source to one snapshot per platform and channel, keys that stay `null` until captured: a Game Assembly Snapshot for each of `macos` and `windows` × `online`, `staging`, `ptr`, and an Interop Snapshot with its Loader Release and official APK for `android-online` alone. After an explicit override, `ManagedPath` resolves only from it: the fetched snapshot, or the local Steam install whose game version and sha256 match the entry; `InteropPath` and `LemonLoaderPath` resolve only from the fetched `android-online` entry. Each platform's product compiles against its `online` entry; `staging` and `ptr` entries feed compatibility builds and tests. It changes only through a pull request; `release/game-libs.mjs` owns its schema and the mod recipes capture, publish, fetch, and check it.
_Avoid_: game-libs manifest, Steam buildid pin

## Mod hosts

**Host** (宿主):
One loader and runtime combination the mod runs under, with the assembly and composition root that adapt the shared core to it: the Desktop Host (BepInEx on Mono, `BazaarPlusPlus.dll`, `DesktopComposition`) and the Android Host (LemonLoader on CoreCLR beside an IL2CPP game). Every host references `BazaarPlusPlus.Core`, which references no loader, engine, or game assembly; game adapters and Harmony patches stay per host. `bazaarplusplus-mod/docs/adr/0012-shared-core-and-per-host-adapters.md` records the split.
_Avoid_: platform (a release target such as `macos`), loader (one part of a host)

## Credentials

**Operator Token** (操作员 token):
The one Cloudflare API token a maintainer holds for every R2 bucket (`bazaarplusplus-public`, `bazaarplusplus-bundle-v6`, `bazaarplusplus-game-libs`) and for Wrangler: `config.ini` `[cloudflare] CLOUDFLARE_API_TOKEN` locally, the repository secret `CLOUDFLARE_API_TOKEN` on GitHub, shared by the release, analyzer and site workflows. The only other Cloudflare token is the read-only CI token (`BPP_GAME_LIBS_TOKEN`, the game-libs bucket only), kept apart because Dependabot PRs run updated dependency code in CI. R2's S3 pair is a view of a token, Access Key ID its id and Secret Access Key the SHA-256 of its value, so `release/r2-store.mjs` and the analyzer's `object_store.py` derive it and nothing stores it. `release/github-secrets.json` maps every GitHub name to its scope and local source.
_Avoid_: release R2 keys, per-bucket key pair

## Local data

**Data Root** (数据根目录):
The folder in the game directory where the mod keeps its local database, screenshots, and replays. The current name is `BazaarPlusPlusV6`; the mod's `PathConstants.DataRootDirectoryName` and the installer's `BAZAAR_DATA_DIRECTORY` must agree. Unrelated to the analyzer's `BPP_DATA_ROOT`.

**Legacy Root** (旧数据根目录):
A Data Root an earlier major version wrote (`BazaarPlusPlus/`, `BazaarPlusPlusV4/`, `BazaarPlusPlusV5/`), owned by the user. The mod never reads it; the installer measures it, imports V5 once, and deletes one only on the user's confirmation. The installer's own `BazaarPlusPlusV4` settings folder is not one.

**History-Only Run** (仅历史对局):
A run in the current Data Root that appears only in local history: never sealed into a Bundle, never uploaded, and never protected from cleanup as a seal candidate. Every run imported from a Legacy Root is one. Unlike a **Run**, it is never carried inside a Bundle.

## Data pipeline

**Bundle**:
The immutable unit of upload, storage, and delivery: exactly one Run and zero or one Screenshot. The mod writes it, the server stores and delivers it, the analyzer reads it.

**Run**:
The immutable facts, battles, card state, and replay inputs of one completed game run, carried inside a Bundle. It excludes the Screenshot.

**Ghost Battle**:
A PvP battle in which a player's uploaded build fought inside another player's run. The server serves it as a query projection of Bundle manifests; the mod imports it into local history.

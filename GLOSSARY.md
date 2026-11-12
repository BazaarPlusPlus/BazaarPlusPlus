# BazaarPlusPlus

Terms used by more than one project. A project glossary may narrow one of these terms for its own use; it links here for the definition.

## Product release

**Product Release**:
The mod, the installer, and the published release record for one product version. One product version names one git commit; each platform ships it at most once and may skip it. The product version says nothing about a database schema, a native ABI, or the version a user has installed.

**Payload**:
The files the mod loader host loads: on desktop, what the installer carries and installs into the game directory; on Android, what sits under the Android App's `MelonLoader/` files directory and inside the Android App APK. Each file's ownership decides which paths install, repair, and uninstall may change.

**Payload Inventory**:
The shared facts about Payload files (`release/payload.json`): current files, their host (`bepinex` or `melonloader`) and platform scope, ownership, producer, and retired files. Each release platform installs only its own host's entries. User data and third-party plugins are outside it.

**Platform Release Manifest**:
One platform's newest promoted release. The installer and website read `latest/<platformKey>.json`; the mod reads the same record at its separate endpoint `latest/mod/<platformKey>.json`. Both are published by the same Release Promotion. A desktop one carries its version, updater package, actual installer download URL, and recorded Mainland Mirror address; the Android one carries the Android App APK, the Interop Package and the game it was adapted for. Each carries its Managed Payload with the history database schema and the `nonManagedPayloadDigest` it needs. Platforms may be at different versions.

**Lockstep Platform** (lockstep 平台):
A platform the Release Manifest waits for: Windows and macOS (`LOCKSTEP_PLATFORM_KEYS` in `release/release-platforms.ts`). Android is a release platform but not a Lockstep Platform, so its game-driven cadence never holds back a desktop release.

**Release Manifest**:
The newest Product Release that every Lockstep Platform promoted at the same version and commit, in the same Tauri shape for each (`latest.json`). Installers whose own Platform Release Manifest fails to load fall back to it, so it never runs ahead of any Lockstep Platform.

**Release Promotion**:
Publishing both update endpoints of one platform's Platform Release Manifest once its complete artifacts are in place and, on desktop, its Mainland Mirror is recorded or explicitly waived; the Release Manifest advances when every Lockstep Platform names the same release. Repeating the promotion of the same release leaves the existing artifacts unchanged.

**Android App** (BPP Android App):
BPP's own Android application, package `com.bazaarplusplus.android` (development builds add `.dev`), in `bazaarplusplus-android/`. It loads the user's installed official game in its own process, gives LemonLoader the Loader Release it carries and the Interop Package it downloads, stages the Managed Payload, writes the Launch Record, and also opens the official App unchanged ("native launch"). It has no account of its own. It carries the Loader Release, `libe_sqlite3.so` and the Updater, never the game, interop or Mods. It is not the mod's Android Host, which is the mod assembly it ends up loading. `docs/adr/0012-android-app.md` records the decision. The issues call it 宿主 App.
_Avoid_: host app (clashes with Host), launcher, BPP APK

**Android Release APK** (Android 发布 APK, retired):
The official game APK with the loader, interop and Mods injected and signed by BPP, which Android users installed until ADR 0012 replaced it with the Android App. No release builds one; old copies under `installer/android/apk/` stay but are never updated.

**Game Code Identity** (游戏代码身份):
What decides whether an installed game is the adapted one, on every platform (ADR 0016): on Windows and macOS the digest of the `Managed/` directory (`managedDirectoryDigest`, a Snapshot Lock entry's `sha256`); on Android the Game Code Hash. A content-only game update leaves it unchanged, so the game version and build number play no part. Adapted means the installed and the adapted game have the same identity; not adapted means they differ.

**Game Code Hash** (游戏代码哈希):
Android's Game Code Identity: the sha256 of the installed official game's `libil2cpp.so` and `global-metadata.dat`, taken from whichever of its APKs (base or split) holds each. The Android App compares the pair with the Platform Release Manifest's `game`; `versionName` and the APK's own sha256 play no part, so a content-only game update needs no new Game Adaptation.

**Game Adaptation** (适配):
The binding of one product version to one game's code. On Windows and macOS it is the Snapshot Lock entry the build compiled against, which the build record names and the desktop Platform Release Manifest records as `game`. On Android it is the Snapshot Lock's `android-publisher` entry, the Interop Snapshot generated from it, and the Mods compiled against it, published together as that version's Android release. Only the latest game is adapted.

**Interop Package** (interop 包):
One Interop Snapshot as a deterministic zip, published content-addressed in the public bucket with an RSA signature that binds it to the Game Code Hash it was generated for. The Android App verifies it and adds it to the game's resources so LemonLoader extracts it; releases of one snapshot share one object. ADR 0012 records its layout and keys.

**Launch Record** (启动记录):
The file the Android App writes into LemonLoader's `UserData/` before every BPP launch, telling the mod which App, release, game and interop this launch uses. The mod reads it at start; no file means the mod is not running in the Android App, and an unreadable or stale one is logged while the mod runs normally. ADR 0012 records the decision; the Contracts table in `AGENTS.md` names its owner.

**Managed Payload** (托管 Payload):
One platform's `managed` and `history-contract` Payload entries for one product version, published as a zip signed with the Tauri updater key at `<version>/<platformKey>/managed/` and recorded as `managedPayload` in that platform's fragment: what the mod's self-update installs. A copy takes it only when its history database schema and `nonManagedPayloadDigest` (every other Payload file, the Updater included) match the release; otherwise the installer or a new Android App updates it.

**Managed Payload Manifest** (托管 Payload 清单):
The signed line-format list of one Managed Payload's files with their sha256 and size, its version, platform, history database schema and `nonManagedPayloadDigest` (`release/managed-payload.ts`). It travels in the zip as `manifest.txt` and is installed beside the mod as `BazaarPlusPlus.managed-manifest.txt`, the record of what the running copy is.

**Updater** (更新器):
`BazaarPlusPlus.Updater.dll`, the BPP component that runs at the loader stage before the mod (a BepInEx preloader patcher on desktop, a MelonLoader plugin on Android) and swaps a staged Managed Payload into place. It is not part of the Managed Payload; the installer or the Android App ships it. Not the installer's Tauri updater.
_Avoid_: patcher, self-updater

**Android Mod Payload** (Android Mods 包):
The Payload files the Android Host loads from `MelonLoader/Mods/` and `MelonLoader/UserLibs/`, published for every product version as Android's Managed Payload. The Android App stages it, and the Updater in `MelonLoader/Plugins/` swaps it in, on the first start as on every later one.
_Avoid_: Mods zip

**Mainland Mirror**:
A manually uploaded share page of one platform's installer for mainland-China networks. Its address is an explicit operator input, checked against the uploaded installer and recorded per platform before that platform's Release Promotion, then published in its Platform Release Manifest; consumers read it and never derive it. It is a manual download fallback, never a second release source or an updater endpoint.

## Game assemblies

**Game Assembly Snapshot** (游戏程序集快照):
One captured copy of the game's `Managed/` directory, identified by the game version string Unity writes in `globalgamemanagers` beside it (`1.0.12575-staging-macos-arm64-fecb8f8e`: build number, channel token, platform, game commit) and verified by the sha256 of its DLL records. It is captured from a Steam depot (`just mod::steam-capture`, ADR 0015) or a mounted Steam install for one platform and channel (`macos` or `windows` × `online`, `staging`, `ptr`, the Steam branches `public`, `staging`, `public_test_realm`), stored content-addressed in the private `bazaarplusplus-game-libs` bucket, and unpacked under `bazaarplusplus-mod/game-libs/`. It is a build and test input, never part of the Payload; the Steam buildid and branch are its source record, not its identity. The snapshot channel keeps staging and ptr apart, which the mod's runtime Game Build Channel folds into `Ptr`.
_Avoid_: Steam branch copy, game-libs archive

**Interop Snapshot** (互操作快照):
The Il2CppInterop proxy assemblies generated from one official Android build, with the generator's databases and `interop-manifest.json` beside them: the Android counterpart of a Game Assembly Snapshot, recorded under its Android Source's lock key and verified by the sha256 of every file record. The build's `versionName` names the source: the publisher's form (`1.0.12825-production-android-d9601ded`: no architecture token, `production` is online) or Google Play's bare `x.y.z` (`1.0.6`). It is captured with `just mod::snapshot-android` from the publisher's APK, or a Play install's base APK and splits, and a pinned toolchain (Patcher, Cpp2IL, Il2CppInterop), stored content-addressed in the private bucket beside those APKs and that toolchain, and unpacked under `bazaarplusplus-mod/game-libs/android-<source>-<versionCode>/interop/`; the `versionCode` names it because one Play `versionName` may name more than one build. Like a Game Assembly Snapshot it is a build input, never part of the Payload.
_Avoid_: interop dump, IL2CPP managed folder

**Loader Release** (加载器发行包):
One tagged LemonLoader Android release zip (`LemonLoader-Android-arm64.zip`): the native bootstrap, the CoreCLR runtime and the managed loader assemblies (`MelonLoader.dll`, `0Harmony.dll`, `Il2CppInterop.Runtime.dll`) an Android Host compiles against and the Android App carries. The Snapshot Lock pins it by tag and sha256 inside each Android entry; a copy lives in the private bucket so a deleted upstream tag does not break a rebuild, and `just mod::fetch android <source>` unpacks it under `bazaarplusplus-mod/game-libs/loader-<tag>/`.
_Avoid_: MelonLoader install, loader runtime

**Android Source** (Android 来源):
Where an Android player installs the official game from: `publisher`, the APK the publisher serves at `data.playthebazaar.com`, or `play`, Google Play's base APK and ABI split. The two ship different builds, signed by different keys, so a player switches only by uninstalling; both connect to the online servers. Each has one Snapshot Lock entry (ADR 0019). It is not a channel.
_Avoid_: Android channel, store

**Game Status** (游戏状态):
The online game of each platform and its Game Code Identity, as two objects in the public bucket that the site's download page compares with each Platform Release Manifest's `game`. `game-status/latest.json` holds both desktop platforms of Steam's public branch, written by the Steam snapshot watch when the Snapshot Lock or an open lock pull request records that build; only `online` is public, because staging and ptr are password-protected branches and stay in the watch's run summary. `game-status/android.json` holds the publisher APK's game, written by the Android APK watch when the lock or an open lock pull request records that APK; no Game Status covers Google Play. They are separate because each watch replaces its own object whole. Shared goldens: `release/fixtures/game-status.json` and `release/fixtures/android-game-status.json`.

**Snapshot Lock** (快照锁):
`bazaarplusplus-mod/build/game-libs.lock.json`: the committed binding of the mod source to one snapshot per platform and channel, keys that stay `null` until captured: a Game Assembly Snapshot for each of `macos` and `windows` × `online`, `staging`, `ptr`, and an Interop Snapshot with its Loader Release and official APKs for each Android Source, `android-publisher` and `android-play`, each the newest production build that source ships. After an explicit override, `ManagedPath` resolves only from it: the fetched snapshot, or the local Steam install whose game version and sha256 match the entry; `InteropPath` and `LemonLoaderPath` resolve only from the fetched `android-<source>` entry that `BppAndroidSource` names (default `publisher`). Each desktop product compiles against its `online` entry; `staging` and `ptr` entries feed compatibility builds and tests. It changes only through a pull request; `release/snapshot-lock.ts` owns its schema and the mod recipes capture, publish, fetch, and check it.
_Avoid_: game-libs manifest, Steam buildid pin

## Mod hosts

**Host** (宿主):
One loader and runtime combination the mod runs under, with the assembly and composition root that adapt the shared core to it: the Desktop Host (BepInEx on Mono, `BazaarPlusPlus.dll` in `BepInEx/plugins/`, `DesktopComposition`) and the Android Host (LemonLoader on CoreCLR beside an IL2CPP game, `BazaarPlusPlus.Android.dll` in `MelonLoader/Mods/`, compiled against an Interop Snapshot and a Loader Release). Every host references `BazaarPlusPlus.Core`, which references no loader, engine, or game assembly; game adapters, Harmony patches and the host ports' implementations (settings, log exit, Data Root) stay per host. A host is not a release platform, and the Android Host is not the Android App that loads it: `macos` and `windows` share the Desktop Host. `bazaarplusplus-mod/docs/adr/0012-shared-core-and-per-host-adapters.md` records the split.
_Avoid_: platform (a release target such as `macos`), loader (one part of a host)

## Credentials

**Operator Token** (操作员 token):
The one Cloudflare API token a maintainer holds for every R2 bucket (`bazaarplusplus-public`, `bazaarplusplus-bundle-v6`, `bazaarplusplus-game-libs`) and for Wrangler: `config.toml` `[cloudflare] CLOUDFLARE_API_TOKEN` locally, the repository secret `CLOUDFLARE_API_TOKEN` on GitHub, shared by the release, checks, analyzer and site workflows and also registered for Dependabot. It is an Account API Token and the only Cloudflare token; Dependabot PRs run updated dependency code in CI with it, a risk accepted to keep one token. R2's S3 pair is a view of a token, Access Key ID its id and Secret Access Key the SHA-256 of its value, so `release/r2-store.ts` and the analyzer's `object_store.py` derive it and nothing stores it. `release/github-secrets.json` maps every GitHub name to its scope and local source.
_Avoid_: release R2 keys, per-bucket key pair

## Local data

**Data Root** (数据根目录):
The folder in the game directory where the mod keeps its local database, screenshots, and replays; on the Android Host, the sibling of the MelonLoader base directory under the game's external `files/`. The current name is `BazaarPlusPlusV6`; the mod's `PathConstants.DataRootDirectoryName` and the installer's `BAZAAR_DATA_DIRECTORY` must agree. Unrelated to the analyzer's `BPP_DATA_ROOT`.

**Legacy Root** (旧数据根目录):
A Data Root an earlier major version wrote (`BazaarPlusPlus/`, `BazaarPlusPlusV4/`, `BazaarPlusPlusV5/`), owned by the user. The mod never reads it; the installer measures it and deletes one only on the user's confirmation.

**History-Only Run** (仅历史对局):
A run in the current Data Root that the mod never seals: it appears only in local history, is never uploaded, and is never protected from cleanup as a seal candidate. The installer's V5 import wrote them until 6.2.0 removed it, so every run imported from a Legacy Root is one and existing databases keep them. The development-only History Sampler (`just history-sample`) writes them too: a cloud Bundle's run under the local player's account, told apart by its `sample-` run id and `sample:` mod version. Unlike a **Run**, it is never carried inside a Bundle.

## Data pipeline

**Bundle**:
The immutable unit of upload, storage, and delivery: exactly one Run and zero or one Screenshot. The mod writes it, the server stores and delivers it, the analyzer reads it.

**Run**:
The immutable facts, battles, card state, and replay inputs of one completed game run, carried inside a Bundle. It excludes the Screenshot.

**Empty Run**:
A completed Run with zero wins and zero losses: the player left it before the first fight. The mod does not seal it, and the server rejects a Bundle with no Battle projection (`run_without_battles`).
_Avoid_: bot run

**Ghost Battle**:
A PvP battle in which a player's uploaded build fought inside another player's run. The server serves it as a query projection of Bundle manifests; the mod imports it into local history.

## Design

**Design System** (设计系统):
The one source for what the installer and bazaarplusplus.com look and read like: design tokens, the shared `bpp-*` component styles, the brand mark, the brand book and the writing standard, kept in `design/` and published as a Design System artifact. The products consume it; they do not restate its values (`docs/design.md`).
_Avoid_: style guide, theme

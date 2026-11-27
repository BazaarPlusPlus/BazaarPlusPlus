# BazaarPlusPlus

Terms used by more than one project. A project glossary may narrow one of these terms for its own use; it links here for the definition. User-facing UI copy vocabulary lives separately in `design/writing.md`.

## Product release and hosts

**Product Release**:
The mod, the installer, the Android App, and the published release record for one product version. One product version names one git commit; each platform ships it at most once and may skip it. The product version says nothing about a database schema, a native ABI, or the version a user has installed.

**Host** (宿主):
One loader and runtime combination the mod runs under, with the assembly and composition root that adapt `BazaarPlusPlus.Core` to it: the Desktop Host (BepInEx on Mono, `BazaarPlusPlus.dll`, shared by `macos` and `windows`) and the Android Host (LemonLoader on CoreCLR beside an IL2CPP game, `BazaarPlusPlus.Android.dll`). Core references no loader, engine, or game assembly; the Android Host is distinct from the Android App that loads it (`bazaarplusplus-mod/docs/adr/0012-shared-core-and-per-host-adapters.md`).
_Avoid_: platform (a release target such as `macos`), loader (one part of a host)

**Payload**:
The files the mod loader host loads: on desktop, what the installer carries and installs into the game directory; on Android, what sits under the Android App's `MelonLoader/` files directory and inside the Android App APK. Each file's ownership decides which paths install, repair, and uninstall may change.

**Payload Inventory**:
The shared manifest of Payload files (`release/payload.json`): current files, their host (`bepinex` or `melonloader`) and platform scope, ownership, producer, and retired files. Each release platform installs only its own host's entries. User data and third-party plugins are outside it.

**Platform Release Manifest**:
One platform's newest promoted release. The installer and website read `latest/<platformKey>.json`; the mod reads the same record at `latest/mod/<platformKey>.json`. A desktop manifest carries its version, updater package, installer download URL, Managed Payload, and recorded Mainland Mirror address; the Android manifest carries the Android App APK, recorded Mainland Mirror address, and per-source `adaptations` (each with its adapted game, Interop Package, and Managed Payload). Platforms may be at different versions.

**Lockstep Platform** (lockstep 平台):
A platform the Release Manifest waits for: Windows and macOS (`LOCKSTEP_PLATFORM_KEYS` in `release/release-platforms.ts`). Android is a release platform but not a Lockstep Platform, so its game-driven cadence never holds back a desktop release.

**Release Manifest**:
The newest Product Release that every Lockstep Platform promoted at the same version and commit, in the same Tauri shape for each (`latest.json`). Installers whose own Platform Release Manifest fails to load fall back to it, so it never runs ahead of any Lockstep Platform.

**Release Promotion**:
Publishing both update endpoints of one platform's Platform Release Manifest once its complete artifacts are in place and its Mainland Mirror is recorded or explicitly waived; the Release Manifest advances when every Lockstep Platform names the same release. Repeating the promotion of the same release leaves the existing artifacts unchanged.

**Android App** (BPP Android App):
BPP's own Android application (`com.bazaarplusplus.android`; `.dev` in development builds), in `bazaarplusplus-android/`. It loads the user's installed official game in its own process, provides LemonLoader its carried Loader Release and downloaded Interop Package, stages the Managed Payload, writes the Launch Record, and also opens the official App unchanged ("native launch"). It carries the Loader Release, `libe_sqlite3.so`, and the Updater—never the game, interop, or Mods—and has no account of its own (`docs/adr/0012-android-app.md`). It replaced the retired **Android Release APK** (injected official game APKs signed by BPP; old copies under `installer/android/apk/` are never updated).
_Avoid_: host app (clashes with Host), launcher, BPP APK

**Game Code Identity** (游戏代码身份):
What decides whether an installed game matches the adapted build on every platform (ADR 0016): on Windows and macOS, the sha256 digest of `Managed/` (`managedDirectoryDigest`); on Android, the Game Code Hash. A content-only game update leaves it unchanged, so game version strings and build numbers play no part.

**Game Code Hash** (游戏代码哈希):
Android's Game Code Identity: the sha256 pair of the installed official game's `libil2cpp.so` and `global-metadata.dat` across its base and split APKs. The Android App compares it against each adaptation's `game` in the Platform Release Manifest's `adaptations`; `versionName` and APK sha256 play no part.

**Game Adaptation** (适配):
The binding of one product version to one game's code. On desktop it is the Snapshot Lock entry compiled against (recorded as `game` in the desktop Platform Release Manifest). On Android there is one per Android Source—that source's Snapshot Lock entry, Interop Snapshot, and compiled Mods—published together with its signing certificate in `adaptations` (ADR 0019).

**Interop Package** (interop 包):
One Interop Snapshot packed as a deterministic zip, published content-addressed in the public bucket with an RSA signature binding it to its Game Code Hash (ADR 0012). The Android App verifies it and adds it to the game's resources so LemonLoader extracts it; releases sharing a snapshot share one object.

**Launch Record** (启动记录):
The file the Android App writes into LemonLoader's `UserData/` before every BPP launch, naming the App, release, game, and interop for that launch (ADR 0012; contract owner in `AGENTS.md`). Its absence means the mod was not launched from the App's manifest flow; unreadable or stale records are logged while the mod runs normally.

**Managed Payload** (托管 Payload):
One platform's `managed` and `history-contract` Payload entries (`MelonLoader/Mods/` and `MelonLoader/UserLibs/` on Android), published as a zip signed with the Tauri updater key: what desktop self-update or the Android App stages for the Updater to swap in. Applied only when the history database schema and `nonManagedPayloadDigest` (every other Payload file, the Updater included) match the release; otherwise the installer or a new Android App updates first.
_Avoid_: Android Mod Payload, Mods zip

**Managed Payload Manifest** (托管 Payload 清单):
The signed line-format list of one Managed Payload's files (sha256 and size), version, platform, history database schema, and `nonManagedPayloadDigest` (`release/managed-payload.ts`). Shipped inside the zip as `manifest.txt` and installed beside the mod as `BazaarPlusPlus.managed-manifest.txt`.

**Updater** (更新器):
`BazaarPlusPlus.Updater.dll`, the loader-stage component (BepInEx preloader patcher on desktop, MelonLoader plugin on Android) that swaps a staged Managed Payload into place before the mod loads. Shipped by the installer or Android App, never part of the Managed Payload, and distinct from the installer's Tauri updater.
_Avoid_: patcher, self-updater

**Mainland Mirror**:
A verified copy of one platform's download (desktop installer or Android App APK) hosted on a self-hosted server reachable from mainland-China networks, recorded per platform in its Platform Release Manifest before Release Promotion. Consumers read its URL directly and never derive it; it is a manual download fallback, never a second release source or an updater endpoint.

## Game snapshots

**Game Assembly Snapshot** (游戏程序集快照):
One captured copy of the desktop game's `Managed/` directory, identified by Unity's `globalgamemanagers` version string and verified by DLL sha256s (ADR 0015). Captured per platform (`macos`, `windows`) and Steam channel (`online`, `staging`, `ptr`), stored content-addressed in the private `bazaarplusplus-game-libs` bucket as a build and test input, never part of the Payload. Keeps `staging` and `ptr` apart, which the mod's runtime Game Build Channel folds into `Ptr`.
_Avoid_: Steam branch copy, game-libs archive

**Interop Snapshot** (互操作快照):
The Android counterpart of a Game Assembly Snapshot: Il2CppInterop proxy assemblies, generator databases, and `interop-manifest.json` generated from one official Android build and a pinned toolchain, keyed by Android Source and `versionCode` and verified by file sha256s (ADR 0008). Stored content-addressed in the private bucket as a build input, never part of the Payload.
_Avoid_: interop dump, IL2CPP managed folder

**Loader Release** (加载器发行包):
One tagged LemonLoader Android release zip (`LemonLoader-Android-arm64.zip`): the native bootstrap, CoreCLR runtime, and managed loader assemblies that the Android Host compiles against and the Android App carries. Pinned by tag and sha256 in the Snapshot Lock and mirrored in the private bucket.
_Avoid_: MelonLoader install, loader runtime

**Android Source** (Android 来源):
Where an Android player installs the official game from: `publisher` (the APK at `data.playthebazaar.com`) or `play` (Google Play's base APK and ABI split). Both connect to online servers, but ship different builds signed by different keys, so each has its own Snapshot Lock entry and Game Adaptation (ADR 0019). Not a channel.
_Avoid_: Android channel, store

**Game Status** (游戏状态):
The current online game version and Game Code Identity per platform, published as two public-bucket objects (`game-status/latest.json` for desktop Steam `online`, `game-status/android.json` for Android `publisher`; none covers Google Play) that the site compares against each Platform Release Manifest's `game`. Shared goldens live in `release/fixtures/`.

**Snapshot Lock** (快照锁):
`bazaarplusplus-mod/build/game-libs.lock.json` (`release/snapshot-lock.ts`): the committed binding of mod source to one Game Assembly Snapshot per desktop platform × channel (`online`, `staging`, `ptr`) and one Interop Snapshot, Loader Release, and official APK set per Android Source (`android-publisher`, `android-play`). Desktop products compile against `online` while `staging` and `ptr` feed compatibility checks; `ManagedPath`, `InteropPath`, and `LemonLoaderPath` resolve only from it unless explicitly overridden. Changed only through a pull request.
_Avoid_: game-libs manifest, Steam buildid pin

## Card catalog

**Card Catalog** (卡牌目录):
One `index.json` with the Card Art, Card Text, and Enchantment FX files it names, together with the UI kit component (`CardCatalog` in `packages/ui/src/game/`) that renders them. The site reads the published CDN set; the installer reads the set it exports from the installed game (ADR 0024, ADR 0025, ADR 0026).

**Card Art** (卡图):
Item and skill artwork extracted as content-addressed WebP files from the Steam online Addressables bundles and keyed by TemplateId in `index.json` (`cards` for art files, `names` for English titles across all card types including encounters; ADR 0020). Used by the Card Catalog and the QQ bot.

**Card Text** (卡牌文本):
Localized item and skill definitions keyed by TemplateId in the Card Catalog's locale files, carrying tier attributes, tooltip lines, and deduplicated enchantment definitions from GameData (ADR 0021). A captured Run instance's values can differ from Card Text.

**Enchantment FX** (附魔特效):
The persistent material effect on an enchanted item, optionally mapped by TemplateId in the Card Catalog `index.json` to a material descriptor with renderer profile, keyword sets, values, and texture bindings (ADR 0022).

## Local data

**Data Root** (数据根目录):
The folder where the mod keeps its local SQLite database, screenshots, and replays (`BazaarPlusPlusV6`): inside the game directory on Steam, beside the game build on Tempo Launcher, and beside MelonLoader under the game's external `files/` on Android. Mod (`PathConstants.DataRootDirectoryName`), installer (`BAZAAR_DATA_DIRECTORY`), and Android App (`AppFiles.DATA_ROOT`) must agree; unrelated to the analyzer's `BPP_DATA_ROOT`.

**Legacy Root** (旧数据根目录):
A Data Root an earlier major version wrote (`BazaarPlusPlus/`, `BazaarPlusPlusV4/`, `BazaarPlusPlusV5/`), owned by the user. The mod never reads it; the installer measures it and deletes one only on explicit user confirmation.

**History-Only Run** (仅历史对局):
A run in the current Data Root that the mod never seals, uploads, or protects from cleanup as a seal candidate: V5-imported runs (kept in databases created before 6.2.0 removed V5 import) and dev `just history-sample` runs (`sample-` run id, `sample:` mod version). Unlike a **Run**, it is never carried inside a Bundle.

## Data pipeline

**Bundle**:
The immutable unit of upload, storage, and delivery (`bazaarplusplus-server/contracts/v6/`): exactly one Run and zero or one Screenshot. The mod writes it, the server stores and delivers it, and the analyzer and bot read it.

**Run**:
The immutable facts, battles, card state, and replay inputs of one completed game run, carried inside a Bundle. Excludes the Screenshot.

**Screenshot**:
The optional end-of-run image stored inside a Bundle. Its absence never invalidates the Bundle.

**Run Message Stream**:
The messages the game received and the commands it sent during one run, recorded as the game's own bytes in order by both mod Hosts and carried inside the Run (`bazaarplusplus-mod/docs/adr/0028-record-the-run-message-stream.md`).
_Avoid_: event stream, replay (a replay is one battle)

**Game Message Schema** (游戏消息结构):
The cumulative MessagePack layout of the game's network messages (member keys, union tags, enum values by qualified type name), merged across Game Assembly Snapshots (`packages/runs/`). Used to decode a Run Message Stream.
_Avoid_: message map, proto

**Decision Path** (决策路径):
The per-day, per-hour visit and action sequence projected from a Run Message Stream (`packages/runs/`, ADR 0023): what was offered, what the player did with it, and the Battle that followed.
_Avoid_: replay, timeline (the Battle list)

**Empty Run**:
A completed Run with zero wins and zero losses: the player left it before the first fight. The mod does not seal it, and the server rejects any Bundle with no Battle projection (`run_without_battles`).
_Avoid_: bot run

**Ghost Battle**:
A directional query projection derived from a Bundle manifest for a PvP battle in which one player's uploaded build fought inside another player's run. Served by the server as a query projection (not authoritative Battle data) and imported by the mod into local history.

**BazaarDB Feed**:
The Screenshot-bearing subset of Bundle collection served to BazaarDB at `GET /bazaardb/bundles` (`bazaarplusplus-server/docs/adr/0004-bazaardb-cursor-feed.md`). The server keeps no delivery state; BazaarDB owns its cursor and retries (no claim, lease, or settle).

## Accounts and design

**BPP Account** (BPP 账号):
A site account (one normalized email address and password) created on bazaarplusplus.com and stored in the server's D1 (`bpp_accounts`, server ADR 0007). Optionally linked to one game account ID via a Bind Code verified against the official game account summary API; unlinking keeps the account. Distinct from the QQ bot's Binding.

**Bind Code** (绑定码):
An 8-character single-use code (`23456789ABCDEFGHJKLMNPQRSTUVWXYZ`) generated by an unlinked BPP Account on the site, valid 10 minutes, one active per account (stored in D1 as HMAC). Redeemed in the mod's History panel via `POST /account/bind` to link a game account.

**Record** (云端战绩):
A Bundle shown to a linked BPP Account on bazaarplusplus.com as one of its Runs: matching `run.player_account_id`, still in R2 within the 8-day retention window, with terminal `projection.run` facts in D1 (server ADR 0008). The site decodes its Run segment by byte range; a Record ends when its Bundle expires, while unlinking only stops showing it. In UI copy (`design/writing.md`), "战绩" also names the local `History` page.
_Avoid_: Run Summary, Run detail copy

**Design System** (设计系统):
The single cross-platform visual and writing source in `design/` (design tokens, brand mark, brand book, and user-facing copy glossary in `design/writing.md`), paired with the shared Web UI kit (`@bazaarplusplus/ui` in `packages/ui/`) and live documentation at `bazaarplusplus-ui-docs/` (`docs/design.md`, ADR 0026).
_Avoid_: style guide, theme

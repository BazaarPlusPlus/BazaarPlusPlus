# Android App 只做启动器，每个维护的游戏各有一个适配

状态：提议（2026-11-12）。

## 背景

2026-11-12 的实测（AVD `bpp_emu_20_9` 与 `bpp_emu_3_2`）：

- 游戏同时有两个来源，版本不同：
  - Google Play：`versionName` 1.0.6，`versionCode` 13168，11-11 更新。
  - 发行商 APK（`data.playthebazaar.com/game/android/TheBazaar.apk`）：1.0.13113，11-06 更新。
- 两个来源的游戏代码哈希不同，签名证书也不同（Play App Signing 的证书与 Tempo 的证书）。用户换来源前，必须先卸载游戏。
- 客户端没有强制更新。13113 在 BPP 里能登录，能进主菜单，mod 能加载。
- 没有 Google Play 的设备只能装发行商 APK。CI 拿不到 Play 的 APK。

现在的规则是一个产品版本只适配一个游戏（[ADR 0012](0012-android-app.md) 的"只支持最新游戏版本"，[ADR 0016](0016-one-game-compatibility-rule.md) 的兼容规则）。快照锁只有 `android-online` 一个 Android 条目。所有 Android App 都读最新的平台清单。所以只要发布一个适配新游戏的版本，装着其他游戏的用户就都只能原版启动：

- 适配 Play 13168 后，发行商 APK 的用户进"游戏较旧"档，可是发行商的链接仍是 13113。
- 继续适配 13113，Play 用户就一直停在"等待适配"档。

另外，Android App 跟着每个产品版本重新发布。启动器本身很少变，用户却每次都收到 App 更新提醒。

## 决定

### 三层版本

| 层 | 是什么 | 怎么标识 | 什么时候变 |
|---|---|---|---|
| 游戏 | 用户已装的官方游戏 | 游戏代码哈希与 `versionCode` | 发行商或 Play 发布新版 |
| 适配（Game Adaptation） | 一个游戏的 Interop Package，加上这个产品版本的 Managed Payload | `<产品版本>+<游戏 versionCode>`，例如 `6.3.0+13168` | 每次产品发布，每个维护的游戏一个 |
| Android App | 启动器：门禁、下载、暂存，以及 LemonLoader 运行时和 Updater | App 的 `versionName` 与 `versionCode` | 只在 App 的代码或运行时变化时 |

适配的版本串只用于显示和日志，由平台清单的 `version` 与适配的 `game.versionCode` 组成，不另存字段。

### 维护哪些游戏：快照锁每个来源一个条目

- `android-online` 换成两个键：`android-publisher`（发行商 APK）与 `android-play`（Google Play）。每个键记录这个来源的最新游戏。**这组键就是维护列表。** 当前是 publisher 13113 与 play 13168。
- 来源占锁键里渠道的位置，但它不是游戏服务器的渠道。两个来源都连正式服，mod 都判为 Online。
- 发行商来源仍由 `android-apk-watch.yml` 自动采集，规则不变：APK 变了就重新采集，替换 `android-publisher` 条目，然后开 PR。
- Play 来源手动采集。操作人从装了 Play 版的设备上用 `adb pull` 取下 base 与全部 split，然后运行 `just mod::snapshot-android --source play <apk>...`、`publish`，再开 PR。
- 条目记录每个 APK 的 sha256：发行商来源只有一个 APK，Play 来源是 base 加 split，split 按 sha256 排序。
  - 读 APK 时，`libil2cpp.so`、`global-metadata.dat` 与 `data.unity3d` 都只能出现在一个 APK 里。
  - `AndroidManifest.xml` 只读 base，因为每个 split 也有自己的 manifest。
- 版本串的规则按来源区分：
  - 发行商来源沿用 `GAME_VERSION_PATTERN`。
  - Play 来源的 `versionName` 就是游戏的 `Application.version`，形式是 `x.y.z`（实测为 `1.0.6`）。
  - 快照清单的键改由锁键与版本串组成，不再从版本串解析平台和渠道。
- 两个来源的游戏代码哈希可以相同，例如发行商追上了 Play。这时发布只产生一个适配。
- 所有 Android 条目必须钉同一个 Loader Release 和同一组 `nativeLibs`，因为 App 只带一份运行时。采集时不一致就报错。
- 停止维护一个来源，就删除它的键。这由用户决定。
- 本记录的规则是"每个来源维护最新的那个游戏"。发行商更新到新版本后，13113 不再维护。要固定维护某个旧 build，需要另外决定。

### 一个产品版本只有一份 Mods，配多个 interop

- Android mod 只有一份源码。
- CI 对每个维护中的 interop 各编译一次 `BazaarPlusPlus.Android`，并运行 `mod::test-core`。任何一次失败，发布都停止。
- 每个产品版本只发布一份 Managed Payload，它对 `versionCode` 最大的 interop 编译。
- 同一份 Mods 可以配不同游戏的 interop，原因如下：
  - Mods 通过 .NET 元数据按名字调用 interop 里的类型和方法。
  - interop 自己按元数据 token 绑定游戏里的方法。
  - 所以只要 Mods 用到的类型和方法在每个 interop 里都存在，它就能运行。上一条的逐个编译保证了这一点。
- **这个前提必须先验证**（实施第 2 步）：对 13168 的 interop 编译出的 Mods，配上 13113 的 interop，在模拟器上要能进主菜单，hook 要能命中。
  - 如果验证失败，本记录改为每个游戏一份 Managed Payload。
  - 那时还要修改 Updater 的换入规则：`ManagedPayloadSwap.Run` 现在拒绝换入版本不高于已装版本的 Mods。
- 选择一份 Mods 的理由：
  - 本地历史数据库和 Data Root 在所有游戏之间共用，一份 Mods 保证数据库 schema 不会倒退。
  - Updater 的版本规则不用改。
- 游戏的 API 变了，同一份代码不能对每个 interop 都编译通过时，先在 mod 里做兼容。做不到时，停止维护旧的那个来源。

### 平台清单：`game` 与 `interop` 换成 `adaptations`

```
downloads["android-aarch64"] = {
  app, managedPayload, loader, selfUpdatable, historyDatabaseUserVersion, nonManagedPayloadDigest,
  adaptations: [
    { source: "play" | "publisher",
      game: { gameVersion, versionCode, il2cppSha256, metadataSha256 },
      interop: { url, size, sha256, snapshotSha256, signature } }
  ]
}
```

- `adaptations` 按 `versionCode` 从大到小排列。游戏代码哈希不能重复。
- 每个 interop 有自己的 interop 声明，格式不变：`bpp-interop-v1`、zip 的 sha256、游戏代码哈希、`release <产品版本>`。
- 下列消费方要和这次契约改动放进同一个 PR：
  - Android App：`PlatformManifest`、`CarriedRelease`、`LaunchGate`、`LauncherSession`。
  - mod：`ReleaseManifestClient`、`AndroidGameUpdate`。
  - 下载页：`decodeAndroidDownload` 与 Android 卡片。
  - release：`manifest.ts`、`android-release.ts` 与它们的 fixtures。

### 启动门禁

| 已装的游戏 | 档位 | BPP 启动 |
|---|---|---|
| 哈希等于某个适配的游戏 | 已适配，用这个适配的 interop | 可用 |
| 哈希都不同，`versionCode` 比每个适配都大 | 等待适配 | 不可用 |
| 哈希都不同，`versionCode` 不比某个适配大 | 请先更新游戏。Play 安装的指向 Play，其余指向发行商链接 | 不可用 |
| 未安装、A 方案副本、离线且无缓存 | 不变 | 不变 |

- 启动记录不变。它已经记录这次启动用的 `game` 与 `interop`。
- mod 主菜单用启动记录里的游戏哈希比较清单的 `adaptations`。

### Android App 的版本与发布

- App 的 `versionName` 与 `versionCode` 仍由产品版本得出（`androidAppVersionCode`）。
- 但 App 只在它的输入变化时才发布：`bazaarplusplus-android/` 有改动，或 `nonManagedPayloadDigest` 变了。
- 输入没有变化时，平台清单的 `app` 仍指向上次发布的 App。这样 App 的版本可以比适配旧，例如 App 6.3.0 配适配 6.3.4。用户只在 App 真正变化时收到更新提醒。
- App 怎样判断"输入变了"，在实现时决定。候选做法：平台清单的 `app` 记录构建它的 `gitCommit`。
- App 不用重装，就能下载新的适配。
- App 携带的版本（`assets/bpp-release/`）包含构建时所有维护中的适配：每个 interop 约 16 MB，再加一份 Managed Payload。
- App 最低 API 改为 32，与 Play 版游戏相同。这部分在分支 `build/android-api-32` 上。

### 下载页与游戏状态

- Android 卡片列出每个已适配的游戏，例如"Play 1.0.6 · 发行商 1.0.13113"。
- `game-status/android.json` 仍只写发行商 APK 的游戏，由 watch 写入。它的哈希在 `adaptations` 里时，卡片显示"已适配"。

### Play 版的事实（2026-11-12 实测）

- 包名与发行商版相同，`com.tempogames.thebazaar`。
- 安装由两个 APK 组成：
  - `base.apk`：`global-metadata.dat`、`AndroidManifest.xml`、`data.unity3d`。
  - `split_config.arm64_v8a.apk`：`libil2cpp.so` 与其他原生库。
- `extractNativeLibs=true`，`minSdk` 32，Unity 6000.3.27f2（与锁相同）。
- 游戏内容从 `data.playthebazaar.com/releases/mobile-v<版本>/android/<build>/` 的 Addressables 下载，不用 Play Asset Delivery。所以第一次用 BPP 启动 Play 版时，游戏要在 App 的沙盒里再下载一遍内容。
- 现在的 App 能加载 split 安装：实测能运行到 LemonLoader 与 Mods 加载完成。之后的崩溃来自旧的 interop，和 split 无关。
- `versionCode` 在两个来源之间可以比较大小：13113 与 13168 都是 build 号。

## 否决的方案

- **一个产品版本只适配一个游戏（现状）。** 不管选哪个来源，另一个来源的用户都只能原版启动。
- **每个游戏一份 Managed Payload。** 要求 Updater 允许同一版本按游戏换入；数据库 schema 可能倒退；发布产物翻倍。只在上面的前提验证失败时采用。
- **按 build 号维护任意多个游戏（锁键用 `versionCode`）。** 没有人要求。每个来源维护最新的一个，已经覆盖现在的两个来源。
- **App 用一条独立的版本号。** 需要另一个版本文件、另一套 `versionCode` 规则和另一套发布命令。沿用产品版本、只在 App 变化时发布，已经能让 App 很少更新。
- **只适配 Play，或只适配发行商。** 没有 Play 的设备只能装发行商 APK；而 Play 用户最多。

## 代价

- 每个维护中的游戏，都要多一次 CI 编译和一次模拟器验证；每次发布要签多份 interop 声明。
- App 每多携带一个 interop，APK 约大 16 MB。
- Play 每次更新，都要有人从设备上取 APK 并采集。
- 同一份 mod 代码必须同时兼容所有维护中的游戏。
- Play 版第一次用 BPP 启动时，要在 App 的沙盒里重新下载游戏内容。

## 修订的记录

- [ADR 0012](0012-android-app.md)：
  - 三档启动规则改为对适配列表判断。
  - "只支持最新游戏版本"改为"每个来源维护最新的游戏"。
  - App 最低 API 改为 32。
  - Play 的未验证项按上面的实测结果更新。
- [ADR 0016](0016-one-game-compatibility-rule.md)：Android 的"游戏较旧"按适配列表判断；游戏状态只覆盖发行商来源。
- [ADR 0008](0008-interop-snapshots-in-snapshot-lock.md)：
  - 锁键 `android-online` 换成 `android-publisher` 与 `android-play`。
  - 条目记录多个 APK。
  - Play 来源的版本串是 `x.y.z`。
- `GLOSSARY.md`：
  - 修改 Game Adaptation（Android 上一个产品版本可以有多个适配）、Snapshot Lock、Interop Snapshot、Game Status。
  - 新增 Android 来源（Android source）。
- 根 `AGENTS.md` 的 Contracts 表：Release Manifest 一行写明 Android 的 `adaptations`。

## 实施顺序

1. **快照锁按来源分键，支持 Play 的 split 采集。**
   - 把 Play 13168 采进 `android-play`；`android-publisher` 保持 13113。
   - 锁的所有读者在同一个 PR 里修改：mod 的 `InteropPath.props`、`checks.yml`、`setup-device`、`release/`。
   - 在第 3 步之前，发布仍只用 `android-publisher` 构建一个适配。
2. **验证一份 Mods 配两个 interop。**
   - 用两台模拟器：一台装 Play 13168，一台装发行商 13113。
   - 把结果写进本记录。验证失败时，先修订本记录，再做第 3 步。
3. **平台清单换成 `adaptations`。** 一个契约 PR，包含：App 的门禁与携带版本、mod 的读取方、下载页、release 的构建与校验。
4. **App 只在输入变化时发布。**
5. **发布。** 合并 `build/android-api-32`，提升 `VERSION`，发布 Android，再推广。

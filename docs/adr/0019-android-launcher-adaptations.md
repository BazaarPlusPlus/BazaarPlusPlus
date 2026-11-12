# Android App 只做启动器，每个维护的游戏来源各有一个适配

状态：提议（2026-11-12）。接受之前，先完成"实施顺序"第 0 步的验证。

## 背景

2026-11-12 在 AVD `bpp_emu_20_9` 与 `bpp_emu_3_2` 上实测：

- 游戏有两个来源，现在的版本不同。
  - Google Play：`versionName` 1.0.6，`versionCode` 13168，11-11 更新。
  - 发行商 APK（`data.playthebazaar.com/game/android/TheBazaar.apk`）：1.0.13113，11-06 更新。
- 两个来源的游戏代码哈希不同，签名证书也不同。
  - Play 是 Play App Signing 的证书（`f093884e…`）。
  - 发行商是 Tempo 的证书（`f8f508cf…`）。
  - 所以用户换来源之前，必须先卸载游戏。
- 客户端没有强制更新。13113 在 BPP 里能登录、能进主菜单，mod 能加载。
- 没有 Google Play 的设备只能装发行商 APK。CI 拿不到 Play 的 APK。

现在一个产品版本只适配一个游戏：[ADR 0012](0012-android-app.md) 规定"只支持最新游戏版本"，[ADR 0016](0016-one-game-compatibility-rule.md) 规定了对应的兼容规则。快照锁只有 `android-online` 一个 Android 条目，而所有 Android App 都读最新的平台清单。所以只要发布一个适配新游戏的版本，装着另一个游戏的用户就只能原版启动：

- 适配 Play 13168 后，发行商用户落进"游戏较旧"档，但发行商链接下载到的仍是 13113。
- 继续适配 13113，Play 用户就一直"等待适配"。

## 决定

### 三层版本

| 层 | 是什么 | 怎么标识 | 什么时候变 |
|---|---|---|---|
| 游戏 | 用户已装的官方游戏 | 来源（签名证书）、游戏代码哈希、`versionCode` | 发行商或 Play 发布新版 |
| 适配（Game Adaptation） | 一个游戏的 Interop Package，加上这个产品版本唯一的 Managed Payload | `<产品版本>+<游戏 versionCode>`，例如 `6.3.0+13168` | 每次产品发布，每个维护的来源一个 |
| Android App | 启动器：门禁、下载、暂存，以及 LemonLoader 运行时和 Updater | 产品版本 | 每次产品发布照常构建；只在运行时变化时提醒用户更新 |

- 适配的版本串只用于显示和日志。它由平台清单的 `version` 加上适配的 `game.versionCode` 组成，不另存字段。

### 维护哪些游戏：快照锁每个来源一个条目

- `android-online` 换成两个键：`android-publisher`（发行商 APK）与 `android-play`（Google Play）。
  - 每个键记录这个来源的最新游戏。**这组键就是维护列表。**
  - 当前：publisher 13113，play 13168。
- 来源占用锁键里渠道的位置，但它不是游戏服务器的渠道：两个来源都连正式服。
- 本记录的规则是"每个来源维护最新的游戏"。发行商更新后，13113 不再维护。如果要固定维护某个旧 build，需要另作决定。
- 停止维护一个来源，就删除它的键。这由用户决定。
- 条目记录的内容：
  - 每个 APK 的 sha256。发行商只有一个 APK；Play 是 base 加 split，split 按 sha256 排序。
  - 游戏签名证书的 sha256，App 用它判断来源（见"启动门禁"）。
- 版本串规则按来源区分：
  - 发行商沿用 `GAME_VERSION_PATTERN`。
  - Play 的 `versionName` 就是游戏的 `Application.version`，形式是 `x.y.z`（实测为 `1.0.6`）。它不含 build 号，同一个 `1.0.6` 可以对应多个 build。
- 所以条目的身份是锁键加 `versionCode`。本地快照目录（`InteropPath.props` 读取）和私有桶里快照清单的键，都改用这个身份，不再用版本串，也不再从版本串解析平台和渠道。
- 所有 Android 条目必须钉同一个 Loader Release 和同一组 `nativeLibs`，因为 App 只带一份运行时。采集时如果不一致，就报错。
- 发行商来源仍由 `android-apk-watch.yml` 采集，规则不变，只是目标键改为 `android-publisher`。
- Play 来源手动采集：
  1. 在装了 Play 版的设备上，用 `adb pull` 取下 base 和全部 split。
  2. 在 Linux x64 上生成 interop；在 macOS 上要传 `--interop`，因为 Patcher 只支持 Linux x64。
  3. 运行 `just mod::snapshot-android --source play <apk>...`，然后运行 `publish`，再开 PR。
- 读 APK 的规则：
  - `libil2cpp.so`、`global-metadata.dat` 与 `data.unity3d` 都只能出现在一个 APK 里。
  - `AndroidManifest.xml` 只读 base，因为每个 split 也带自己的 manifest。

### 一份 Mods 配多个 interop

- 每个产品版本只发布一份 Managed Payload。
- 理由：本地历史数据库和 Data Root 在所有游戏之间共用，一份 Mods 保证 schema 不会倒退；Updater 的版本规则也不用改（`ManagedPayloadSwap.Run`）。
- **安全条件：对每个维护中的 interop 各编译一次 `BazaarPlusPlus.Android`，产物 DLL 必须逐字节相同**（项目已开 `Deterministic`）。
  - 字节相同，说明二进制里的绑定也相同：成员签名、所选的重载、内联进 DLL 的 enum 和 const 值、程序集引用的版本都一样。
  - 只是"两边都能编译"不够：签名变了、选了别的重载、enum 中间插了新值，都能编译通过，但发布的 DLL 在另一个 interop 上会在 JIT 时失败，或者静默读错数据。
  - 字节不同时，发布停止。这时有两条路：在 mod 里改写，让各次编译的字节一致；或者停止维护旧的那个来源。
- 按名字挂原生 detour 的补丁（例如 `CombatPlaybackPatches`）不受 DLL 字节约束。所以每个维护中的游戏都要做模拟器验收，验收项见"实施顺序"第 0 步。
- 如果这条路行不通，就改为每个游戏一份 Managed Payload。那需要修订本记录，并修改 Updater 的换入规则。

### 平台清单：加 `adaptations`，按来源分键

```
downloads["android-aarch64"] = {
  app, managedPayload, loader, selfUpdatable, historyDatabaseUserVersion, nonManagedPayloadDigest,
  game, interop,                       // 发行商来源的适配，给已安装的 App 与 mod 读
  adaptations: {
    publisher: { signerSha256, game: {...}, interop: {...} },
    play:      { signerSha256, game: {...}, interop: {...} }
  }
}
```

- 两个来源的游戏代码哈希相同时，两个条目指向同一个按内容寻址的 interop zip。
- 每个 interop 都有自己的 interop 声明，声明格式不变：`bpp-interop-v1`、zip 的 sha256、游戏代码哈希、`release <产品版本>`。每次发布都要为每个 interop 重新签名。
- **保留 `game` 与 `interop`，填入发行商来源的适配。**
  - 原因：已安装的 App（6.2.5 起，`PlatformManifest` 要求这两个字段）、已安装的 mod（`ReleaseManifestClient`）和下载页都只认这个形状。
  - 读不了新清单的 App 会退回缓存，也看不到 App 更新提醒。
  - 只有在 App 与 mod 都改读 `adaptations` 之后，才另作决定删除这两个字段。
- 下载页单独部署，所以它要先上线能解析两种形状的版本，然后才能推广新清单。
- 下列消费方要和这次契约改动放进同一个 PR：
  - Android App：`PlatformManifest`、`CarriedRelease`、`LaunchGate`、`LaunchCheck`、`BppLaunch`（在这里选择 interop）、`LauncherSession`、`InstalledGame`（读取签名证书）。
  - mod：`ReleaseManifestClient`、`AndroidGameUpdate`、`AndroidVersionCheck`。
  - release：`manifest.ts`、`android-release.ts`、`publish.ts`、`downloads.ts`，以及它们的 fixtures。
  - 下载页：`installer.ts` 与 Android 卡片。

### 启动门禁：只和同一来源比较

- App 读取已装游戏的签名证书，按 `signerSha256` 确定它的来源。
- 不用安装器包名判断来源：用 adb 装的 Play split，安装器不是 Play。

| 已装的游戏 | 档位 | BPP 启动 |
|---|---|---|
| 哈希等于任一来源的适配 | 已适配，用这个适配的 interop | 可用 |
| 哈希不同，`versionCode` ≥ 同一来源适配的 `versionCode` | 等待适配 | 不可用 |
| 哈希不同，`versionCode` < 同一来源适配的 `versionCode` | 请先更新游戏，链接指向这个来源 | 不可用 |
| 签名证书不属于任何维护中的来源 | 等待适配 | 不可用 |
| 未安装、A 方案副本、离线且无缓存 | 不变 | 不变 |

- `versionCode` 相同而哈希不同，仍按"较新"处理，与 ADR 0012 相同。
- 同一来源里，更新之后通常就是已适配的版本：发行商链接和 Play 都只提供这个来源的最新版本，而适配跟着最新版本走。例外是 Play 分阶段推送：还没轮到的用户在 Play 里看不到更新（见"代价"）。
- 启动记录不变：它记的是已装的游戏和这次用的 interop。
- mod 主菜单用启动记录里的游戏哈希，与清单的 `adaptations` 比较。

### App 更新提醒

- App 仍然每个产品版本构建和发布，流水线不变。携带版本（`assets/bpp-release/`）包含构建时所有维护中的适配：每个 interop 约 16 MB，再加一份 Managed Payload。
- 启动器只在 App 自己的 `nonManagedPayloadDigest` 与清单的不同时提醒更新 App。这条规则与 mod 的换入规则一致。
- 结果：用户只在运行时变化时被要求更新 App。只修改启动器代码的版本，不会提醒用户。

### 下载页与游戏状态

- Android 卡片列出每个来源已适配的游戏，例如"Play 1.0.6 · 发行商 1.0.13113"。
- `game-status/android.json` 仍只写发行商 APK 的游戏，由 watch 写入。它和 `adaptations.publisher` 比较。

### 最低 API 32（用户决定）

- App 最低 API 改为 32，与 Play 版游戏相同。改动在分支 `build/android-api-32` 上。
- 代价：发行商 APK 的 `minSdk` 是 25，所以 Android 11 和 12 上装发行商 APK 的用户用不了 BPP。

### Play 版的事实（2026-11-12 实测）

- 包名与发行商版相同：`com.tempogames.thebazaar`。
- 安装由两个 APK 组成：
  - `base.apk`：`global-metadata.dat`、`AndroidManifest.xml`、`data.unity3d`。
  - `split_config.arm64_v8a.apk`：`libil2cpp.so` 与其他原生库。
- `extractNativeLibs=true`，`minSdk` 32，Unity 6000.3.27f2（与锁相同）。
- 游戏内容从 `data.playthebazaar.com/releases/mobile-v<版本>/android/<build>/` 的 Addressables 下载，不用 Play Asset Delivery。所以第一次用 BPP 启动 Play 版时，游戏要在 App 的沙盒里再下载一遍内容。
- 现在的 App 能加载 split 安装：实测到 LemonLoader 与 Mods 加载完成。之后的崩溃来自旧的 interop。
- `versionCode` 是 build 号。
- 没有验证：Play 版是否也没有 `Config.ServerOption`。它决定 mod 是否把 Play 版判为 Online（`GameBuildInfoResolver`）。

## 否决的方案

- **一个产品版本只适配一个游戏（现状）。** 不管选哪个来源，另一个来源的用户都只能原版启动。
- **每个游戏一份 Managed Payload。** Updater 要允许同一版本按游戏换入，数据库 schema 可能倒退，发布产物也翻倍。只有当"逐字节相同"做不到时才采用。
- **只要求"每个 interop 都能编译"。** 编译能通过，不能证明发布的那个 DLL 在别的 interop 上运行正确（见"一份 Mods 配多个 interop"）。
- **`adaptations` 用数组，并按哈希去重。** 去重后，会有一个来源没有自己的条目，门禁就无法和同一来源比较。
- **跨来源比较 `versionCode`。** 两个来源的 build 不同步，用户会卡在"请更新游戏"，而更新链接给不出更新的版本。
- **按安装器包名判断来源。** 用 adb 装的 Play split 会被判为发行商。
- **App 只在输入变化时才发布。** 这与清单、下载页和推广的 4 处校验冲突（`release/manifest.ts`、`PlatformManifest`、`release/downloads.ts`、`release/publish.ts`），携带的 interop 也会过期。按摘要提醒，就能得到同样的效果。
- **App 用一条独立的版本号。** 需要另一个版本文件、另一套 `versionCode` 规则和另一套发布命令。
- **只适配 Play，或者只适配发行商。** 没有 Play 的设备只能装发行商 APK；Play 用户又跟不上发行商的版本。

## 代价

- 每个维护中的来源，都要多一次 CI 编译、一次字节比较、一次模拟器验收；每次发布要签多份 interop 声明。
- App 每多携带一个 interop，APK 约大 16 MB。
- Play 每次更新，都要有人从设备上取 APK 并采集。
- Play 分阶段推送时，适配了新 build 之后，还没轮到推送的 Play 用户会看到"请先更新游戏"，但 Play 暂时不给他们更新。
- 同一份 mod 代码在所有维护中的游戏上，必须编译出相同的字节。
- 平台清单要长期保留 `game` 与 `interop`，直到另作决定删除。
- 只修改启动器代码的 App 版本，不会提醒用户更新。
- 第一次用 BPP 启动 Play 版时，要在 App 的沙盒里重新下载游戏内容。

## 修订的记录

- [ADR 0012](0012-android-app.md)：
  - 三档启动规则改为按来源比较。"`versionCode` 相同按较新处理"不变。
  - "只支持最新游戏版本"改为"每个来源维护最新的游戏"。
  - App 最低 API 改为 32。
  - Play 的未验证项按上面的实测结果更新。
- [ADR 0016](0016-one-game-compatibility-rule.md)：Android 的"游戏较旧"按同一来源判断；游戏状态只覆盖发行商来源。
- [ADR 0008](0008-interop-snapshots-in-snapshot-lock.md)：
  - 锁键改为 `android-publisher` 与 `android-play`。
  - 条目记录多个 APK 和签名证书。
  - 条目身份是锁键加 `versionCode`。
- `docs/release.md` 的"Android 发布顺序"，`docs/development.md` 里 setup-device 一段。
- `GLOSSARY.md`：
  - 修改 Game Adaptation、Snapshot Lock、Interop Snapshot、Game Status。
  - 新增 Android 来源（Android source）。
- 根 `AGENTS.md` 的 Contracts 表：Release Manifest 一行写明 Android 的 `adaptations`。

## 实施顺序

0. **先验证，再接受本记录。**
   1. 采集 Play 13168 的 interop。
   2. 用 `-p:InteropPath` 分别对 13113 与 13168 的 interop 编译 `BazaarPlusPlus.Android`，比较两个 DLL 的字节。
   3. 用两台模拟器（Play 13168、发行商 13113）和 Development interop 运行同一份 Mods，确认这些都正常：主菜单、hook 统计、一场战斗的采集、回放播放、收藏面板。
   4. 列出两个 interop 之间程序集版本的差异。
   5. 把结果写进本记录。
1. **下载页先能解析两种清单形状**，并单独部署。
2. **快照锁按来源分键，支持 Play 的 split 采集。** 条目身份是锁键加 `versionCode`，条目记录签名证书。下列锁的读者在同一个 PR 里修改：
   - `InteropPath.props`（包括 Updater 的 csproj）、`mod.just` 的参数模式、`game.sh`、`scripts/mod-validation.ts`；
   - `checks.yml`、`android-apk-watch.yml`、`setup-device`；
   - `release/` 下的 `official-apk.ts`、`android-adaptation.ts`、`game-status.ts`。

   这一步完成后，发布仍然只出 `android-publisher` 的适配。
3. **平台清单加 `adaptations`。** 一个契约 PR，包含：App 的门禁、interop 选择与携带版本，mod 的读取方，release 的构建、逐字节比较与校验。
4. **App 更新提醒改为比较摘要。**
5. **发布。** 合并 `build/android-api-32`，提升 `VERSION`，发布 Android，再推广。

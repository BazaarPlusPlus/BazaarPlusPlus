# Android App 只做启动器，每个维护的游戏各有一个适配包

状态：已接受（2026-11-12）。"实施顺序"的第 0 步已经通过，结果见"第 0 步的验证结果"。

## 背景

2026-11-12 在 AVD `bpp_emu_20_9` 与 `bpp_emu_3_2` 上实测：

- 游戏有两个来源，现在的版本不同：
  - Google Play：`versionName` 1.0.6，`versionCode` 13168，11-11 更新。
  - 发行商 APK（`data.playthebazaar.com/game/android/TheBazaar.apk`）：1.0.13113，11-06 更新。
- 两个来源的游戏代码哈希不同，签名证书也不同：
  - Play：Play App Signing 的证书（`f093884e…`）。
  - 发行商：Tempo 的证书（`f8f508cf…`）。
  - 所以用户换来源之前，必须先卸载游戏。
- 客户端没有强制更新。13113 在 BPP 里能登录、能进主菜单，mod 能加载。
- 没有 Google Play 的设备只能装发行商 APK。CI 拿不到 Play 的 APK。
- 同一份 mod 源码不一定能对每个游戏的 interop 编译：master 对 13113 能编译，对 12825 不能（`AndroidDevLogin` 用到的 `LoginState._submitting` 在 12825 里不存在）。

现在一个产品版本只适配一个游戏：[ADR 0012](0012-android-app.md) 规定"只支持最新游戏版本"，[ADR 0016](0016-one-game-compatibility-rule.md) 规定兼容规则。快照锁只有 `android-online` 一个 Android 条目，所有 Android App 都读最新的平台清单。所以只要发布一个适配新游戏的版本，装着其他游戏的用户就只能原版启动。游戏版本分散、又没有强制更新时，总有一部分用户用不了 BPP。

## 决定

### 三层版本

| 层 | 是什么 | 怎么标识 | 什么时候变 |
|---|---|---|---|
| 游戏 | 用户已装的官方游戏 | 签名证书（来源）、游戏代码哈希、`versionCode` | 发行商或 Play 发布新版 |
| 适配包 | 一个游戏的 Interop Package，加上对这个 interop 编译的 Managed Payload | `<mod 版本>+<游戏 versionCode>`，例如 `6.3.0+13168`、`6.2.6+13113` | 这个游戏的 mod 有新构建时 |
| Android App | 启动器：门禁、下载、暂存，以及 LemonLoader 运行时和 Updater | 产品版本 | 每次产品发布照常构建；只在运行时变化时提醒用户更新 |

- 每个适配包是一个 Game Adaptation 的发布形式。
- 不同游戏的适配包，mod 版本可以不同。新游戏跟着 master 走；旧游戏的适配包可以停在构建它的那个 mod 版本上。

### 维护列表：快照锁每个游戏一个条目

- `android-online` 换成每个游戏一个键：`android-<versionCode>`。**这组键就是维护列表。** 当前是 `android-13113`（发行商）与 `android-13168`（Play）。
- 条目记录的内容：
  - 来源：`publisher` 或 `play`。
  - 游戏签名证书的 sha256。
  - 每个 APK 的 sha256。发行商只有一个 APK；Play 是 base 加 split，split 按 sha256 排序。
  - 现有字段：游戏代码哈希、Unity 版本、interop 摘要、工具链。
- 加入一个游戏或删除一个游戏，都由用户决定。
  - watch 采集到发行商的新版本时，开 PR **加**一个新键，不替换旧键。
  - Play 的新版本由人手动采集，同样是加键。
  - 停止维护一个游戏，就删除它的键。
- 版本串的规则按来源区分：
  - 发行商沿用 `GAME_VERSION_PATTERN`。
  - Play 的 `versionName` 就是游戏的 `Application.version`，形式是 `x.y.z`（实测为 `1.0.6`），不含 build 号。
  - 本地快照目录与私有桶里快照清单的键都改用锁键，不再用版本串，也不再从版本串解析平台和渠道。
- 所有 Android 条目钉同一个 Loader Release 和同一组 `nativeLibs`，因为 App 只带一份运行时。采集时如果不一致，就报错。
- 发行商来源仍由 `android-apk-watch.yml` 自动采集。它和发行商来源里 `versionCode` 最大的条目比较。
- Play 来源手动采集：
  1. 在装了 Play 版的设备上，用 `adb pull` 取下 base 和全部 split。
  2. 生成 interop：在 Linux x64 上直接生成；在 macOS 上要传 `--interop`，因为 Patcher 只支持 Linux x64。
  3. 运行 `just mod::snapshot-android --source play <apk>...`，然后运行 `publish`，再开 PR。
- 读 APK 的规则：
  - `libil2cpp.so`、`global-metadata.dat` 与 `data.unity3d` 都只能出现在一个 APK 里。
  - `AndroidManifest.xml` 只读 base，因为每个 split 也带自己的 manifest。

### 适配包的构建

- 产品发布时，对 master 能编译的每个维护中的游戏，各构建一个适配包：对它的 interop 编译 `BazaarPlusPlus.Android`，打 Managed Payload，签 interop 声明。
- master 对某个游戏编译失败时，这个游戏不在本次发布里构建：
  - 平台清单沿用它上一次发布的适配包。
  - 需要修复时，从构建它的那个版本切维护分支，在分支上构建新的适配包。
- interop 声明的格式不变，用途改为 `release <这个适配包的 mod 版本>`。
- Managed Payload Manifest 加上 `game`，也就是它对之编译的游戏代码哈希。
- 每个适配包都要做模拟器验收，验收项见"实施顺序"第 0 步。

### Updater：按游戏换入

- 现在的规则：暂存的版本不高于已装版本时，不换入（`ManagedPayloadSwap.Run`）。
- 新规则：
  - 已装 Mods 的 `game` 与这次启动的游戏（启动记录里的）不同时，按游戏换入，不比较版本。否则 Mods 配上了别的游戏的 interop，启动就会崩溃。
  - 数据库 schema 的检查不变：适配包的 mod 不支持已有的历史数据库时，拒绝换入，并移走已装的 Mods。这时游戏不带 Mods 运行，不会崩溃。
  - 游戏相同时，规则不变。
- App 暂存时，只暂存与已装游戏匹配的适配包。

### 平台清单：加 `adaptations` 列表

```
downloads["android-aarch64"] = {
  app, loader, selfUpdatable, nonManagedPayloadDigest,
  game, interop, managedPayload, historyDatabaseUserVersion,   // 一个发行商适配包，给已安装的 App 与 mod 读
  adaptations: [
    { source, signerSha256, version,              // version 是 "<mod 版本>+<游戏 versionCode>"
      game: { gameVersion, versionCode, il2cppSha256, metadataSha256 },
      interop: { url, size, sha256, snapshotSha256, signature },
      managedPayload: { url, size, sha256, signature },
      historyDatabaseUserVersion }
  ]
}
```

- `adaptations` 按 `versionCode` 从大到小排列，每个维护中的游戏一项。游戏代码哈希不能重复。
- 推广时，没有在本次构建的游戏，从上一份平台清单里沿用它的适配包。
- 所有适配包的 Managed Payload 都必须和 App 的 `nonManagedPayloadDigest` 一致，因为 App 只带一份运行时。所以运行时变化时（Loader Release、Updater、`nativeLibs`），所有维护中的适配包都要重新构建。
- **保留顶层的 `game`、`interop`、`managedPayload`、`historyDatabaseUserVersion`，填入一个发行商适配包。**
  - 原因：已安装的 App（6.2.5 起，`PlatformManifest` 要求这些字段）、已安装的 mod（`ReleaseManifestClient`）和下载页都只认这个形状。
  - 读不了新清单的 App 会退回缓存，也看不到 App 更新提醒。
  - 只有在 App 与 mod 都改读 `adaptations` 之后，才另作决定删除这些字段。
- 下载页单独部署，所以它要先上线能解析两种形状的版本，然后才能推广新清单。
- 下列消费方要和这次契约改动放进同一个 PR：
  - Android App：`PlatformManifest`、`CarriedRelease`、`LaunchGate`、`LaunchCheck`、`BppLaunch`（在这里选适配包）、`LauncherSession`、`InstalledGame`（读签名证书）、`InteropStatement`（用途）、`ManagedPayloadStaging`。
  - mod：`ReleaseManifestClient`、`AndroidGameUpdate`、`AndroidVersionCheck`、Updater（`ManagedPayloadSwap`、`ManagedPayloadManifest`）。
  - release：`manifest.ts`、`android-release.ts`、`android-interop.ts`、`publish.ts`、`downloads.ts`、`managed-payload.ts`，以及它们的 fixtures 与共享 golden。
  - 下载页：`installer.ts` 与 Android 卡片。

### 启动门禁：只和同一来源比较

- App 读取已装游戏的签名证书，按 `signerSha256` 确定它的来源。
- 不按安装器包名判断：用 adb 装的 Play split，安装器不是 Play。

| 已装的游戏 | 档位 | BPP 启动 |
|---|---|---|
| 哈希等于某个适配包的游戏 | 已适配，用这个适配包 | 可用 |
| 哈希不同，`versionCode` ≥ 同一来源最新适配包的 `versionCode` | 等待适配 | 不可用 |
| 哈希不同，`versionCode` < 同一来源最新适配包的 `versionCode` | 请先更新游戏，链接指向这个来源 | 不可用 |
| 签名证书不属于任何维护中的来源 | 等待适配 | 不可用 |
| 未安装、A 方案副本、离线且无缓存 | 不变 | 不变 |

- `versionCode` 相同而哈希不同，仍按"较新"处理，与 ADR 0012 相同。
- 维护多个游戏之后，"请先更新游戏"只出现在比所有维护版本都旧的游戏上。
- 启动记录的格式不变：它已经记录这次启动用的 `game` 与 `interop`。
- mod 主菜单用启动记录里的游戏哈希，与清单的 `adaptations` 比较。

### App 的版本与更新提醒

- App 仍然每个产品版本构建和发布，流水线不变。
- 携带版本（`assets/bpp-release/`）包含构建时所有维护中的适配包：每个约 20 MB（interop 约 16 MB，Managed Payload 约 3 MB）。
- 启动器只在 App 自己的 `nonManagedPayloadDigest` 与清单的不同时，才提醒用户更新 App。这与 Updater 的规则一致。
- 结果：用户只在运行时变化时被要求更新 App。只修改启动器代码的版本，不会提醒用户。

### 下载页与游戏状态

- Android 卡片列出每个已适配的游戏，例如"Play 1.0.6 · 发行商 1.0.13113"。
- `game-status/android.json` 仍只写发行商 APK 的游戏，由 watch 写入。它和 `adaptations` 比较。

### 最低 API 32（用户决定）

- App 最低 API 改为 32，与 Play 版游戏相同。
- 代价：发行商 APK 的 `minSdk` 是 25，所以 Android 11 和 12 上装发行商 APK 的用户用不了 BPP。

### Play 版的事实（2026-11-12 实测）

- 包名与发行商版相同：`com.tempogames.thebazaar`。
- 安装由两个 APK 组成：
  - `base.apk`：`global-metadata.dat`、`AndroidManifest.xml`、`data.unity3d`。
  - `split_config.arm64_v8a.apk`：`libil2cpp.so` 与其他原生库。
- `extractNativeLibs=true`，`minSdk` 32，Unity 6000.3.27f2（与锁相同）。
- 游戏内容从 `data.playthebazaar.com/releases/mobile-v<版本>/android/<build>/` 的 Addressables 下载，不用 Play Asset Delivery。所以第一次用 BPP 启动 Play 版时，游戏要在 App 的沙盒里再下载一遍内容。
- 现在的 App 能加载 split 安装：实测到 LemonLoader 与 Mods 加载完成。之后的崩溃来自旧的 interop。
- `versionCode` 是 build 号，两个来源用同一套编号。
- Play 版和发行商版的 interop 都没有 `Config.ServerOption`，所以 mod 把两者都判为 Online（`GameBuildInfoResolver`）。
- Play 版启动时，`PlayGamesSdk.initialize` 抛出 `ClassCastException`：`GameContext` 不能转成 `android.app.Application`。游戏照常进入主菜单。所以在 App 里，Play 游戏服务不可用。

### 第 0 步的验证结果（2026-11-12）

- **生成 interop：** 在 Linux x64 上生成 Play 13168 的 interop，177 个文件，摘要 `3548ef0b…`。这一步在 GitHub 托管的 runner 上完成。本机 Docker 用 qemu 模拟 amd64，Cpp2IL 在里面崩溃（`AccessViolationException`），所以本机不能生成。
- **编译：** master 的 `BazaarPlusPlus.Android` 能对 13168 的 interop 编译。对 13113 编译出的 DLL 和它大小相同，但字节不同，所以每个游戏要有自己的 Managed Payload。
- **AVD `bpp_emu_20_9`（Play 13168，Development interop）：**
  - 冒烟测试三级都通过：注入、hook 命中、版本标签。
  - History 面板能显示样例对局。
  - 回放能完整播放：从 0:00.00 播到 0:07.25，显示"Victory!"。另一局回放里缺一个特效资源（Addressables 找不到 `VFX_Bite_Tier3.prefab`），回放没有中断。
  - Collection 面板能显示 141 个物品。
- **对照组（AVD `bpp_emu_3_2`，发行商 13113）：** 回放同样能播放到战斗结束。
- **没有验证：** 在 13168 上实际打一场对局，确认战斗被采集。

## 否决的方案

- **一个产品版本只适配一个游戏（现状）。** 不管选哪个来源、哪个版本，装着其他游戏的用户都只能原版启动。
- **一份 Managed Payload 配所有 interop。** 同一份源码不一定能对每个 interop 编译（12825 就不能）。即使能编译，签名、重载或 enum 值变了，发布的 DLL 在别的 interop 上也可能运行出错。
- **每个来源只维护一个游戏（锁键 `android-publisher` / `android-play`）。** 发行商一更新，旧版本的用户就又落进"请先更新游戏"。这正是要解决的问题。
- **跨来源比较 `versionCode`。** 两个来源的 build 不同步，用户会卡在"请更新游戏"，而更新链接给不出更新的版本。
- **按安装器包名判断来源。** 用 adb 装的 Play split 会被判为发行商。
- **App 只在输入变化时才发布。** 这与清单、下载页和推广的 4 处校验冲突（`release/manifest.ts`、`PlatformManifest`、`release/downloads.ts`、`release/publish.ts`），携带的适配包也会过期。按运行时摘要提醒，就能得到同样的效果。
- **App 用一条独立的版本号。** 需要另一个版本文件、另一套 `versionCode` 规则和另一套发布命令。

## 代价

- 每个维护中的游戏，都要多一次编译、一次模拟器验收；每次发布要签多份 interop 声明。
- 运行时变化时，所有维护中的适配包都要重新构建；master 编译不了的游戏，要在维护分支上构建。
- 旧游戏的适配包可能停在旧的 mod 版本，用不到新功能。
- 用户从新游戏换回旧游戏（要先卸载、换来源）后，旧适配包的 mod 可能读不了新 schema 的历史数据库，这时不带 Mods 运行。
- App 每多携带一个适配包，APK 约大 20 MB。
- Play 每次更新，都要有人从设备上取 APK 并采集。
- 平台清单要长期保留顶层的旧字段，直到另作决定删除。
- 只修改启动器代码的 App 版本，不会提醒用户更新。
- 第一次用 BPP 启动 Play 版时，要在 App 的沙盒里重新下载游戏内容。

## 修订的记录

- [ADR 0012](0012-android-app.md)：
  - "只支持最新游戏版本"改为"支持维护列表里的每个游戏"。
  - 三档启动规则改为按来源比较。"`versionCode` 相同按较新处理"不变。
  - App 最低 API 改为 32。
  - Play 的未验证项按上面的实测结果更新。
- [ADR 0016](0016-one-game-compatibility-rule.md)：Android 的"游戏较旧"按同一来源判断；游戏状态只覆盖发行商来源。
- [ADR 0008](0008-interop-snapshots-in-snapshot-lock.md)：
  - 锁键改为每个游戏一个（`android-<versionCode>`）。
  - 条目记录来源、签名证书和多个 APK。
- mod [ADR-0014](../bazaarplusplus-mod/docs/adr/0014-managed-payload-self-update.md)：Updater 按游戏换入。
- `docs/release.md` 的"Android 发布顺序"，`docs/development.md` 里 setup-device 一段。
- `GLOSSARY.md`：
  - 修改 Game Adaptation、Snapshot Lock、Interop Snapshot、Game Status、Managed Payload Manifest。
  - 新增适配包（Adaptation Package）与 Android 来源（Android source）。
- 根 `AGENTS.md` 的 Contracts 表：Release Manifest 与 Managed Payload Manifest 两行。

## 实施顺序

0. **先验证，再接受本记录。**
   1. 采集 Play 13168 的 interop。
   2. 用 `-p:InteropPath` 对 13168 编译 master 的 `BazaarPlusPlus.Android`。
   3. 在模拟器上用 Development interop 运行它，确认这些都正常：主菜单、hook 统计、一场战斗的采集、回放播放、收藏面板。
   4. 在 Play 版上检查 `Config.ServerOption`。
   5. 把结果写进本记录。
1. **下载页先能解析两种清单形状**，并单独部署。
2. **快照锁改为每个游戏一个键，支持 Play 的 split 采集。** 下列锁的读者在同一个 PR 里修改：
   - `InteropPath.props`（包括 Updater 的 csproj）、`mod.just` 的参数模式、`game.sh`、`scripts/mod-validation.ts`；
   - `checks.yml`、`android-apk-watch.yml`、`setup-device`；
   - `release/` 下的 `official-apk.ts`、`android-adaptation.ts`、`game-status.ts`。

   这一步完成后，发布仍然只构建一个发行商适配包。
3. **平台清单加 `adaptations`，Updater 按游戏换入。** 一个契约 PR，包含：App 的门禁、适配包选择、暂存与携带版本，mod 与 Updater，release 的构建、沿用与校验。
4. **App 更新提醒改为比较运行时摘要。**
5. **发布。** 合并最低 API 32 的改动，提升 `VERSION`，构建 13113 与 13168 两个适配包，发布 Android，再推广。

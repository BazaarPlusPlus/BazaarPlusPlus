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
| 适配包 | 一个游戏的 Interop Package，加上对这个 interop 编译的 Managed Payload | `<产品版本>+<游戏 versionCode>`，例如 `6.3.0+13168`、`6.3.0+13113` | 每次产品发布 |
| Android App | 启动器：门禁、下载、暂存，以及 LemonLoader 运行时和 Updater | 产品版本 | 每次产品发布照常构建；只在运行时变化时提醒用户更新 |

- 每个适配包是一个 Game Adaptation 的发布形式。
- 同一次发布的适配包都来自同一个 mod 提交，所以产品版本相同，历史数据库的 schema 也相同。

### 维护列表：快照锁每个来源一个条目

- `android-online` 换成两个固定的键：`android-publisher`（发行商 APK）与 `android-play`（Google Play）。**这两个键就是维护列表。** 当前是发行商 13113 与 Play 13168。
- 每个键记录这个来源最新的游戏。来源出了新版本后，就替换这个键。
  - 这个来源的旧版本会进"请先更新游戏"档。发行商链接和 Play 都提供这个来源的最新版本，所以用户可以自己更新。
  - 不按游戏开键（例如 `android-<versionCode>`）。原因：锁的校验、写入、告警、快照目录和 `InteropPath.props` 都假设键是固定的，开动态的键要改写整个锁模块。现在没有需要维护的旧 build。
- 来源占锁键里渠道的位置，但它不是游戏服务器的渠道：两个来源都连正式服，mod 都判为 Online。
- 条目新增 `splitApkSha256s`：Play 的 split APK 的 sha256，按 sha256 排序；发行商来源为空数组。`apkSha256` 是 base APK（发行商就是唯一的那个 APK）。
- 每个来源的游戏签名证书 sha256 钉在 `release/` 里，与 `ANDROID_RELEASE_SIGNER_SHA256` 同处，由平台清单发给 App。App 用它判断已装游戏的来源。证书只在来源换签名密钥时才变，所以不进锁条目，采集也不用解析 APK 签名块。
- 版本串的规则按来源区分：
  - 发行商沿用 `GAME_VERSION_PATTERN`。
  - Play 的 `versionName` 就是游戏的 `Application.version`，形式是 `x.y.z`（实测为 `1.0.6`），不含 build 号。
  - Android 的本地快照目录与私有桶里快照清单的键，都改用锁键加 `versionCode`（例如 `android-play-13168`），不再从版本串解析渠道。
- 两个条目必须钉同一个 Loader Release 和同一组 `nativeLibs`，因为 App 只带一份运行时。为空的条目采集时沿用另一个条目的工具链和 `nativeLibs`；发布时核对所有适配包的运行时摘要相同，不同就报错。
- 发行商来源仍由 `android-apk-watch.yml` 自动采集，规则不变，只是目标键改为 `android-publisher`。
- Play 来源手动采集：
  1. 在装了 Play 版的设备上，用 `adb pull` 取下 base 和全部 split，再上传到私有桶。
  2. 在 Linux x64 上生成 interop：手动触发 `android-apk-watch.yml`，传入这些 APK 的 sha256（`play_apks`）。本机 Docker 的 qemu 模拟跑不了 Cpp2IL。
  3. 工作流采集 `android-play`、运行 `publish`、编译检查 Android Mod，然后开 PR。
- 读 APK 的规则：
  - `libil2cpp.so`、`global-metadata.dat` 与 `data.unity3d` 都只能出现在一个 APK 里。
  - `AndroidManifest.xml` 只读 base，因为每个 split 也带自己的 manifest。

### 适配包的构建

- 每次产品发布都从 master 为两个来源各构建一个适配包：对它的 interop 编译 `BazaarPlusPlus.Android`，打 Managed Payload，签 interop 声明。
- master 对某个来源编译失败时，发布停止：修改 mod，或者删除这个来源的键。本记录不做"沿用旧适配包"和维护分支；需要时另作决定。
- interop 声明的格式与用途都不变：用途仍是 `release <产品版本>`。
- Managed Payload Manifest 的格式不变。每个来源的 Managed Payload 有自己的对象键：`<产品版本>/android-aarch64/managed/<来源>/`（`androidManagedPayloadKey`），仍在 App 6.2.5 检查的 `managed/` 前缀之下。
- 每个适配包都要做模拟器验收，验收项见"实施顺序"第 0 步。

### 换游戏时由 App 重新暂存，Updater 不变

- Updater 的规则不变：暂存的版本不高于已装版本时，不换入（`ManagedPayloadSwap.Run`）。
- App 记录它暂存的 Mods 对应哪个游戏（游戏代码哈希），与暂存的版本放在一起。
- 门禁选出的适配包的游戏与记录的不同，或者没有记录时：
  1. App 删除已装 Managed Payload Manifest 列出的每个文件，再删除这个清单。
  2. App 暂存这个适配包的 Managed Payload，不比较版本。
  3. 暂存成功后，App 记录新的游戏。
- 原因：
  - 只删清单不够。清单不在时，Updater 改读 `BazaarPlusPlus.version`，把同版本的暂存当作过期删除，Play 就会加载发行商的 Mods。
  - 清单列出的文件都删了，Updater 就把已装版本看作 0.0.0，换入暂存的适配包。
  - Updater 拒绝换入时（例如运行时摘要不同），游戏不带 Mods 运行，不会因为别的游戏的 Mods 崩溃。
- 游戏相同时，暂存规则不变。

### 平台清单：加 `adaptations` 列表

```
downloads["android-aarch64"] = {
  app, loader, selfUpdatable, nonManagedPayloadDigest,
  game, interop, managedPayload, historyDatabaseUserVersion,   // 一个发行商适配包，给已安装的 App 与 mod 读
  adaptations: {
    publisher: { signerSha256, game: {...}, interop: {...}, managedPayload: {...} },
    play:      { signerSha256, game: {...}, interop: {...}, managedPayload: {...} }
  }
}
```

- `adaptations` 按来源分键，每个维护中的来源一项，键的顺序与 `ANDROID_SOURCES` 相同。
- 所有适配包的 Managed Payload 都必须和 App 的 `nonManagedPayloadDigest` 一致，因为 App 只带一份运行时。所以运行时变化时（Loader Release、Updater、`nativeLibs`），所有维护中的适配包都要重新构建。
- **保留顶层的 `game`、`interop`、`managedPayload`，它们等于 `adaptations.publisher` 的（发布校验这一点）。**
  - 原因：已安装的 App（6.2.5 起，`PlatformManifest` 要求这些字段）、已安装的 mod（`ReleaseManifestClient`）和下载页都只认这个形状。
  - 读不了新清单的 App 会退回缓存，也看不到 App 更新提醒。
  - 只有在 App 与 mod 都改读 `adaptations` 之后，才另作决定删除这些字段。
- 下载页单独部署，所以它要先上线能解析两种形状的版本，然后才能推广新清单。
- 下列消费方要和这次契约改动放进同一个 PR：
  - Android App：`PlatformManifest`、`CarriedRelease`、`LaunchGate`、`LaunchCheck`、`BppLaunch`（在这里选适配包）、`LauncherSession`、`InstalledGame`（读签名证书）、`ManagedPayloadStaging`。
  - mod：`ReleaseManifestClient`、`AndroidGameUpdate`、`AndroidVersionCheck`。
  - release：`manifest.ts`、`android-release.ts`、`publish.ts`、`downloads.ts`，以及它们的 fixtures 与共享 golden。
  - 下载页：`installer.ts` 与 Android 卡片。

### 启动门禁：只和同一来源比较

- App 读取已装游戏的签名证书，按 `signerSha256` 确定它的来源。
- 不按安装器包名判断：用 adb 装的 Play split，安装器不是 Play。

先比较哈希，再看签名证书。所以读不到签名证书时，已适配的游戏仍可用。

| 已装的游戏 | 档位 | BPP 启动 |
|---|---|---|
| 哈希等于任一适配包的游戏 | 已适配，用这个适配包 | 可用 |
| 哈希不同，`versionCode` ≥ 同一来源最新适配包的 `versionCode` | 等待适配 | 不可用 |
| 哈希不同，`versionCode` < 同一来源最新适配包的 `versionCode` | 请先更新游戏，链接指向这个来源 | 不可用 |
| 签名证书不属于任何维护中的来源 | 等待适配 | 不可用 |
| 未安装、A 方案副本、离线且无缓存 | 不变 | 不变 |

- `versionCode` 相同而哈希不同，仍按"较新"处理，与 ADR 0012 相同。
- 每个来源维护它最新的游戏，所以"请先更新游戏"只出现在这个来源的旧版本上，而这个来源的更新链接能给出新版本。
- 启动记录的格式不变：它已经记录这次启动用的 `game` 与 `interop`。它的读者在 Updater 里，拒绝新字段，所以不加来源。
- mod 主菜单用运行中的游戏哈希与清单的 `adaptations` 比较：等于任一适配包的游戏，就是已适配；否则任一适配包的 `versionCode` 更高，就是"游戏较旧"；否则是"未适配"。运行中的游戏启动时已经匹配过一个适配包，每个来源的条目又只会向前走，所以 mod 不需要知道来源。

### App 的版本与更新提醒

- App 仍然每个产品版本构建和发布，流水线不变。
- 携带版本（`assets/bpp-release/`）包含构建时所有维护中的适配包：每个约 20 MB（interop 约 16 MB，Managed Payload 约 3 MB）。
- 启动器只在 App 自己的 `nonManagedPayloadDigest` 与清单的不同时，才提醒用户更新 App。这与 Updater 的规则一致。
- 结果：用户只在运行时变化时被要求更新 App。只修改启动器代码的版本，不会提醒用户。

### 下载页与游戏状态

- Android 卡片的"适配游戏"每个来源占一行：游戏版本，后面写来源。
- `game-status/android.json` 仍只写发行商 APK 的游戏，由 watch 写入。下载页拿它和发行商适配包的游戏比较。
- 下载页也能解析没有 `adaptations` 的清单（本记录之前推广的），这时只列发行商的游戏。

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
- **每个游戏一个锁键（`android-<versionCode>`），维护任意多个 build。** 锁的校验、写入、告警、快照目录和 `InteropPath.props` 都假设键是固定的，要改写整个锁模块。现在每个来源只需要最新的 build：旧 build 的用户可以从同一来源更新。
- **旧游戏沿用旧适配包、开维护分支。** 现在 master 对两个来源都能编译，用不上。
- **跨来源比较 `versionCode`。** 两个来源的 build 不同步，用户会卡在"请更新游戏"，而更新链接给不出更新的版本。
- **按安装器包名判断来源。** 用 adb 装的 Play split 会被判为发行商。
- **Managed Payload Manifest 加 `game` 行，Updater 按游戏换入。** Updater 也编译成桌面的 patcher，又属于非托管载荷（`NON_MANAGED_PRODUCERS`）。它的字节一变，桌面与 Android 的 `nonManagedPayloadDigest` 都会变，每个已安装的客户端都要手动更新。旧 Updater 还要求清单第 6 行起都是 `file` 行，会拒绝带 `game` 行的清单。
- **App 只在输入变化时才发布。** 这与清单、下载页和推广的 4 处校验冲突（`release/manifest.ts`、`PlatformManifest`、`release/downloads.ts`、`release/publish.ts`），携带的适配包也会过期。按运行时摘要提醒，就能得到同样的效果。
- **App 用一条独立的版本号。** 需要另一个版本文件、另一套 `versionCode` 规则和另一套发布命令。

## 代价

- 每个维护中的游戏，都要多一次编译、一次模拟器验收；每次发布要签多份 interop 声明。
- 一个来源的旧版本用不了 BPP，用户要先把游戏更新到这个来源的最新版本。
- master 对某个来源编译不过时，发布停止，直到修好 mod 或删除这个来源。
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
  - 锁键 `android-online` 换成 `android-publisher` 与 `android-play`。
  - 条目记录 split APK。
  - Android 快照目录和快照清单的键改用锁键加 `versionCode`。
- `docs/release.md` 的"Android 发布顺序"，`docs/development.md` 里 setup-device 一段。
- `GLOSSARY.md`：
  - 修改 Game Adaptation、Snapshot Lock、Interop Snapshot、Game Status。
  - 新增适配包（Adaptation Package）与 Android 来源（Android source）。
- 根 `AGENTS.md` 的 Contracts 表：Release Manifest 一行。

## 实施顺序

0. **先验证，再接受本记录。**
   1. 采集 Play 13168 的 interop。
   2. 用 `-p:InteropPath` 对 13168 编译 master 的 `BazaarPlusPlus.Android`。
   3. 在模拟器上用 Development interop 运行它，确认这些都正常：主菜单、hook 统计、一场战斗的采集、回放播放、收藏面板。
   4. 在 Play 版上检查 `Config.ServerOption`。
   5. 把结果写进本记录。
1. **快照锁改为每个来源一个键，支持 Play 的 split 采集。** 下列锁的读者在同一个 PR 里修改：
   - `InteropPath.props`（包括 Updater 的 csproj）、`mod.just` 的参数模式、`game.sh`、`scripts/mod-validation.ts`；
   - `checks.yml`、`android-apk-watch.yml`、`setup-device`；
   - `release/` 下的 `official-apk.ts`、`android-adaptation.ts`、`game-status.ts`。

   这一步完成后，发布仍然只构建一个发行商适配包。
2. **平台清单加 `adaptations`，换游戏时 App 重新暂存。** 一个契约 PR，包含：App 的门禁、适配包选择、暂存与携带版本，mod 的版本检查，release 的构建与校验，以及下载页。这个 PR 在推广新清单之前合并，所以下载页先上线。
3. **App 更新提醒改为比较运行时摘要。**
4. **发布。** 合并最低 API 32 的改动，提升 `VERSION`，构建 13113 与 13168 两个适配包，发布 Android，再推广。

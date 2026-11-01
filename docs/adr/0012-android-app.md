# Android 改由 BPP 的 Android App 在本进程里加载已装的官方游戏

[ADR 0010](0010-android-release-platform.md) 让 Android 用户安装一个注入后由 BPP 签名的官方 APK（下称 A 方案）。它有三个固定代价：用户必须先卸载官方版；游戏每更新一次，用户都要等 BPP 重新注入，再下载约 2.8 GB；每个版本在 R2 多存约 2.9 GB，注入只能在 Linux x64 上跑。2026-11-01 的可行性试验（#747，模拟器 `bpp_host_spike`）证明：一个只用公开 API 的 App（`AppComponentFactory`、`PathClassLoader`、`ContextWrapper`、`ResourcesLoader`）能在自己的进程里加载用户已装、未修改的官方 APK，复用 LemonLoader 的运行时，跑通 master 的全部 Android Mods，并能和官方 App 共存。

本记录取代 ADR 0010。下文用 **Android App**（BPP Android App）指包名为 `com.bazaarplusplus.android` 的 BPP 应用，以便和 mod 的 Android Host（`BazaarPlusPlus.Android`，LemonLoader 上的 mod 程序集）以及官方游戏的 App 区分开。父 issue 写作"宿主 App"的就是它。

## 前提

- **合作授权。** 合作方同意两件事：在 Android App 里运行官方 App；在公开桶里分发由游戏生成的 interop。授权不包括分发游戏本体：官方 APK、`libil2cpp.so`、`global-metadata.dat` 都不进公开桶，也不进 Android App。
- **greenfield。** 不为 A 方案的已安装用户做数据迁移，不设并行期。A 版副本读不了新的平台清单，以后不会再更新，这一点已接受。
- **只支持最新游戏版本。** 每个产品版本只适配一个游戏版本，Snapshot Lock 仍只有 `android-online` 一个条目（[ADR 0008](0008-interop-snapshots-in-snapshot-lock.md)）。

## 决定

### Android App

- 包名 `com.bazaarplusplus.android`，一经发布永不更改，与 iOS 的 `com.bazaarplusplus.ios` 对应。开发构建用 `applicationIdSuffix ".dev"`，可以和正式版并存。
- 最低 Android 11（API 30）：`ResourcesLoader` 从 API 30 起才有。
- Android App 本身没有账号，不需要登录。游戏在 Android App 的沙盒里运行，登录状态和官方 App 分开保存，所以第一次用 BPP 启动时，用户要在游戏自己的登录界面再登录一次。
- 它携带的运行时是 Loader Release（Snapshot Lock 的 `loader` 条目）、`libe_sqlite3.so`（`nativeLibs` 条目）和 `BazaarPlusPlus.Updater.dll`。它不携带 interop，也不携带 Mods。
- 加载方式：用游戏的 `sourceDir` 加 `splitSourceDirs` 建 `PathClassLoader`；在 `instantiateActivity` 里从这个 loader 创建继承游戏 `UnityPlayerActivity` 的 Activity；它的 Context 对外报告游戏的包名、`ApplicationInfo` 和资源，所有可写目录留在 Android App 的沙盒里。Unity 按绝对路径从 `nativeLibraryDir` 加载 `libmain.so`，所以 Android App 构造一个符号链接 lib 目录：`libmain.so` 与 `libe_sqlite3.so` 指向自己的副本，其余指向游戏的库。LemonLoader 通过游戏 `Resources` 的 `AssetManager` 读 `LemonLoader/**`，Android App 用 `ResourcesLoader` 把运行时和 interop 加进去（见下文"interop 包"）。
- 身份冲突：`getPackageName` 返回游戏的包名，UID 却是 Android App 的。试验里由此出现 `MANAGE_GAME_MODE` 与 `DUMP` 两个 SecurityException，都不致命。Play 结算与 Play 游戏服务在 Android App 里的行为尚未验证（#749 第 9 项）；在验证之前，购买只在官方 App 里完成。

### 三档启动规则

Android App 启动时找到官方游戏（`com.tempogames.thebazaar`），在它的所有 APK（base 与 split）里计算 `lib/arm64-v8a/libil2cpp.so` 与 `assets/bin/Data/Managed/Metadata/global-metadata.dat` 的 sha256，合称**游戏代码哈希**，按 `lastUpdateTime` 加 APK 路径缓存。然后和平台清单 `game` 里的两个值比较：

| 已装的官方游戏 | BPP 启动 | 原版启动 |
|---|---|---|
| 两个哈希都与清单一致（包括只改内容、`versionName` 变了的更新） | 正常模式 | 可用 |
| 哈希不同，且 `versionCode` ≥ 清单的 `versionCode` | 兼容模式：用户主动选择，选择按游戏代码哈希记住 | 可用 |
| 哈希不同，且 `versionCode` < 清单的 `versionCode` | 提示"请先更新官方游戏"，附 Play 与发行商下载链接 | 可用 |
| 未安装 | 引导安装官方游戏 | 不可用 |

- 判断只看代码哈希，不看 `versionName`：发行商只改内容的更新不需要新的适配。
- `versionCode` 相同而哈希不同（例如 Play 构建和发行商 APK 的代码不一致，#749 第 2 项待验证），按"比清单新"处理，进入兼容模式的路径。
- "原版启动"直接打开官方 App，不经过 Android App 的任何代码。
- 读不到平台清单（离线）时，用上一次校验过的清单与已下载的产物；没有缓存时只能原版启动。

### 兼容模式

- 兼容模式**绝不**使用旧 interop。interop 用 `GetIl2CppMethodByToken` 按元数据 token 绑定方法，游戏代码一变，token 就会移位，方法会被悄悄绑错。
- interop 只有一个来源：CI 发布的**预适配 interop 包**（见下文）。设备端生成在当前 Loader Release（`v0.7.4-android.4`）里不可用：它的 `runtime/loader/Dependencies/` 只有 `SupportModules/`，没有 `Il2CppAssemblyGenerator/`，也没有 Cpp2IL；试验设备上部署出的 `MelonLoader/Dependencies/` 也一样。所以 #747 设想的"LemonLoader 在设备上生成"这条备用路径不存在。没有预适配包时，Android App 显示"等待适配"，只能原版启动。这个空窗是从游戏发布到 `android-apk-watch.yml` 发布预适配包的时间，它每小时检查一次，加上生成与上传约需一到两小时；可以接受。以后换成带生成器的 Loader Release 时，再修订本节。
- 兼容模式加载当前产品版本的 Mods。mod 读到启动记录的 `mode: "compatibility"` 后关闭所有出站上传（Bundle 与 Ghost），本地记录照常进行，主菜单的版本标签与设置里显示"兼容模式"。

### 发布产物

每个产品版本的 Android 发布有三样产物，都在公开桶 `bazaarplusplus-public` 的 `installer/` 下：

| 产物 | 对象键 | 说明 |
|---|---|---|
| Android App APK | `<版本>/android-aarch64/app/BazaarPlusPlus_<版本>_android-aarch64.apk` | 每个产品版本构建一份，约 65 MB；版本目录不可变（[ADR 0001](0001-product-release-promotion.md)） |
| interop 包 | `android/interop/<sha256>.zip` | 按 zip 文件的 sha256 内容寻址；同一份 Interop Snapshot 只上传一次 |
| Managed Payload | `<版本>/android-aarch64/managed/BazaarPlusPlus_<版本>_android-aarch64-managed.zip` | 不变 |

- **interop 包**是一个确定性 zip：Interop Snapshot 目录的每个顶层文件放在 `assets/LemonLoader/runtime/interop/<文件名>`，没有别的条目。它不需要 `AndroidManifest.xml`、`resources.arsc` 或签名：Android App 用 `ResourcesProvider.loadFromApk` 把它作为第二个 `ResourcesLoader` 加进游戏的资源，LemonLoader 从 `LemonLoader/runtime/interop/` 列出并解压它，与运行时所在的第一个 loader 合并（2026-11-01 在 `bpp_host_spike` 上验证：一个只有 `assets/` 的普通 zip 被接受，174 个文件被解压，30 个补丁全部安装）。zip 的写法与 Managed Payload zip 相同（`release/payload-zip.mjs`），所以同一份快照总得到同一份字节。
- Android App APK 的 `versionCode` 由产品版本算出（`major × 1000000 + minor × 1000 + patch`），由 `release/manifest.mjs` 定义，Gradle 构建接收它。
- Payload Inventory（`release/payload.json`）的 `melonloader` 条目仍相对应用的外部文件目录。A 方案的生产者 `apk-injection` 退役，改为生产者 `android-app`：它的条目是 Android App APK 内部的路径（`lib/arm64-v8a/libmain.so`、`lib/arm64-v8a/libe_sqlite3.so` 与 LemonLoader 运行时），由 Android App 的构建产生，不进 mod 构建。
- 产品版本可以跳过 Android；Android 不在 `latest.json` 的 lockstep 集合里（`LOCKSTEP_PLATFORM_KEYS`）；"一个版本号只对应一个提交"对 Android 同样成立；Android 没有大陆镜像，`promote --platform android` 不读镜像记录。这几条沿用 ADR 0010，不变。

### fragment 与平台清单

平台键 `android-aarch64` 不变。fragment 换成新的 kind `android-app`（`schemaVersion: 1`），A 方案的 `kind: 'android'` 形状不再被接受，旧形状的 fragment 按 kind 名被拒绝，而不是因为缺字段：

```
{ schemaVersion: 1, kind: 'android-app', version, platform: 'android-aarch64', gitCommit,
  app{url, size, sha256, versionCode, signerSha256},
  game{gameVersion, versionCode, il2cppSha256, metadataSha256},
  interop{url, size, sha256, snapshotSha256},
  managedPayload{url, size, sha256, signature},
  loader{tag, sha256},
  selfUpdatable, historyDatabaseUserVersion, nonManagedPayloadDigest }
```

- `game` 是这个版本适配的游戏：Snapshot Lock `android-online` 条目的 `gameVersion`、`versionCode`、`il2cppSha256` 与 `metadataSha256`。官方 APK 的 sha256 不再发布，因为门禁不看它。
- `interop.snapshotSha256` 是锁条目的 `interopSha256`（目录摘要），`interop.sha256` 是 zip 文件本身的 sha256。
- `nonManagedPayloadDigest` 是 Android App 携带的运行时的摘要：锁条目的 loader（tag 与 zip 的 sha256）、`nativeLibs` 各库的 sha256 与 Updater 的 sha256。interop 改为单独下载，不再计入。它只有一种算法，由 `manifest.mjs` 的 `androidNonManagedPayloadDigest` 定义；`release::build` 把算出的值交给 Gradle 写进 Android App，构建后再从 APK 里读回核对，所以 fragment 与 APK 里的值不会各算各的。
- Platform Release Manifest `latest/android-aarch64.json` 与 `latest/mod/android-aarch64.json` 保留共同的顶层字段（`version`、`gitCommit`、`notes`、`pub_date`），`downloads["android-aarch64"]` 放 `app`、`managedPayload`、`interop`、`game`、`loader`、`selfUpdatable`、`historyDatabaseUserVersion`、`nonManagedPayloadDigest`。浏览器、mod 与 Android App 都按这个形状读；浏览器与 mod 用 `release/downloads.ts` 的 `decodeAndroidDownload` 与它的 C# 对应。
- `upload` 与 `promote --platform android` 校验三样产物在 R2 上存在，大小与 sha256 和 fragment 一致，以及 Android App APK 的签名证书（见 [ADR 0009](0009-android-release-key.md) 的 2026-11-01 修订）。

### 预适配 interop 包

新游戏发布、代码哈希变了时，`android-apk-watch.yml` 生成新游戏的 interop，上传 interop 包（同一个内容寻址键），再写一份预适配记录：

- 键：`installer/android/adaptations/<il2cppSha256>_<metadataSha256>.json`，两个哈希都在键里，所以一份记录只对应一份游戏代码。对象只写一次（`If-None-Match: *`），不覆盖。
- 形状：`{ schemaVersion: 1, kind: 'android-adaptation', game{gameVersion, versionCode, il2cppSha256, metadataSha256}, interop{url, size, sha256, snapshotSha256}, loader{tag, sha256}, il2cppInteropVersion, publishedAt }`。
- Android App 只在记录的 `game` 两个哈希与已装游戏一致、`loader.tag` 与自己携带的 Loader Release 相同时使用它；否则视为没有预适配包。
- 它不提升 `VERSION`，不是 Product Release，也不进 Platform Release Manifest。正式适配是之后的一个产品版本：锁条目推进到新游戏，平台清单的 `game` 与已装游戏一致，Android App 回到正常模式。两者用同一份 Interop Snapshot 时，interop 包的键相同，不会重复上传；预适配记录留在原处，此后不再被读取。

### 启动记录（Android App 与 mod 的契约）

Android App 在每次 BPP 启动之前写 `files/MelonLoader/UserData/BazaarPlusPlus.launch.json`（先写临时文件再改名）。写不成功就不启动。

```json
{
  "schemaVersion": 1,
  "app": { "packageName": "com.bazaarplusplus.android", "versionName": "6.1.0", "versionCode": 6001000, "nonManagedPayloadDigest": "<64 位十六进制，开发构建为 null>" },
  "mode": "normal",
  "game": { "packageName": "com.tempogames.thebazaar", "versionName": "…", "versionCode": 12825, "il2cppSha256": "…", "metadataSha256": "…" },
  "interop": { "sha256": "…", "snapshotSha256": "…", "source": "release" },
  "launchedAt": "2026-11-01T00:00:00Z"
}
```

- `mode` 是 `normal` 或 `compatibility`；`interop.source` 是 `release` 或 `pre-adaptation`。
- mod 读它的规则：文件不存在，按"不在 Android App 里"处理（`just mod::android-deploy` 的开发部署），在日志里记录，正常运行；文件存在但读不了、`schemaVersion` 不认识或 `mode` 不认识，按兼容模式处理，关闭上传。宁可少传，不可在错的 interop 上传数据。
- 它不是 Payload，不进 Payload Inventory：它由 Android App 在运行时写，不随任何产物发布。
- 字段的增加要升 `schemaVersion`，并与 mod 的读取在同一个 PR 里改。

### Android App 管理 LemonLoader 的部署状态

LemonLoader 用**游戏**的 `lastUpdateTime` 作为部署 revision 和 interop 标记（试验设备上 `.lemonloader-interop-hash` 的内容是 `apk-<lastUpdateTime>`），因为在 Android App 里 `getPackageName` 返回游戏的包名。所以 Android App 更新后，或者它换了 interop 而游戏没变时，LemonLoader 不会重新部署。Android App 自己记录上一次提供的 Android App `versionCode` 与 interop 的 sha256，启动前比较：

- `versionCode` 变了：删除 `MelonLoader/.lemonloader-deployment-state*`、各 `.lemonloader-*-hash` 和 `MelonLoader/Plugins/BazaarPlusPlus.Updater.dll`，让 LemonLoader 重新解压运行时、重新部署 Updater。
- interop 变了：删除 `MelonLoader/.lemonloader-interop-hash`。2026-11-01 在 `bpp_host_spike` 上验证，删除它之后 LemonLoader 从 `ResourcesLoader` 重新解压 interop。

Android App 打包的部署只有 `Plugins/BazaarPlusPlus.Updater.dll`。Mods 与 UserLibs 都来自 Managed Payload。

### Managed Payload 的安装

Android App 按平台清单下载 Managed Payload zip，校验大小与 sha256，按 Core 的 `ManagedPayloadStager` 定下的布局暂存到 Data Root 的 `Updates/<版本>/`（先解压到 `Updates/.download-<版本>-<随机>/`，再一次改名发布）。Updater 在下一次 BPP 启动的装载阶段验证 minisign 签名和每个文件的哈希，然后换入（mod [ADR-0014](../bazaarplusplus-mod/docs/adr/0014-managed-payload-self-update.md)）。签名只由 Updater 验证：API 30 没有 Ed25519，而 Updater 已经带着验证实现和钉住的公钥。全新安装时 Updater 读不到已安装清单，按"无"处理，可以换入；刚换入的 UserLibs 当次启动就能加载（2026-11-01 在 `bpp_host_spike` 上验证：`updater.swap.applied from=none to=6.0.0`，Data Root 的数据库被创建）。

Updater 拒绝一份暂存时会删除它，游戏照常启动但没有新的 Mods。Android App 记住自己暂存的版本；下次启动时，如果已安装的 Managed Payload 版本仍低于它、暂存目录也不在了，就告诉用户"上次安装 Mods 失败"，并提供诊断导出，然后再暂存一次。

平台清单的 `nonManagedPayloadDigest` 和 Android App 自己的不同时，这个版本的 Managed Payload 不能在这个 Android App 上运行：Android App 提示安装新的 Android App（`app.url`），BPP 启动要等更新完成，原版启动不受影响。mod 内的 `SelfUpdatePolicy.ManualUpdate` 同样指向新的 Android App。

## 否决的方案

- **A：继续注入官方 APK（ADR 0010）。** 用户要卸载官方版，每次游戏更新都要重新下载约 2.8 GB 并等 BPP 重新注入；发布要在 Linux x64 上跑 Patcher，每个版本多存约 2.9 GB；签名密钥是全体用户的单点。
- **B2：直接用 FusionCore 与 BepInEx Fusion。** FusionCore 是 GPL-3.0，复制它的代码会让 Android App 也必须用 GPL；它的机制可以参考，代码不能拿来用。它用自己 fork 的 BepInEx，`BazaarPlusPlus.Android` 要从 MelonLoader API 迁过去，并跟着一个贡献者很少的上游走。
- **B3：TAP-APIA 的路线。** 它在 NextBep 预编译的基线 APK 上改二进制 Manifest、追加 dex，不从源码构建；NextBep 的许可与源码状态不清楚；MelonLoader 兼容层套在 BepInEx 上，两层加载器每次升级都可能断；为防止系统杀进程，它用前台服务、心跳和 wake lock，耗电略高。
- **C：自带引擎运行 Mac 版（iOS 的做法）。** Mono 解释器执行游戏逻辑，CPU 负载与耗电明显高于 IL2CPP；官方已经有 Android 版，这条路在 Android 上不值得。
- **interop 在设备上生成。** 当前 Loader Release 不带生成器（见"兼容模式"）；就算带，首次启动也要灰屏几分钟。
- **interop 包按产品版本放进版本目录。** 每个版本都要重传约 15 MB，预适配包与正式发布也不能共用同一个对象。
- **interop 包做成 aapt2 生成的资源 APK。** 试验证明普通 zip 就被接受；aapt2 的输出不保证跨版本逐字节相同，内容寻址就靠不住。
- **Android App 自己验证 Managed Payload 的签名。** API 30 没有 Ed25519，要么引入 BouncyCastle，要么再写一份 Updater 已有的实现。

## 代价

- 游戏一更新，正式适配之前 BPP 用户只能用兼容模式（不上传），没有预适配包时只能原版启动。
- 游戏的登录状态与下载的资源在 Android App 里另存一份，用户第一次要再登录一次。
- Android App 依赖 LemonLoader 的部署细节（`lastUpdateTime` revision、`.lemonloader-*` 标记文件）。换 Loader Release 时要重新验证"Android App 管理部署状态"一节。
- Play 构建是 split APK（#749 第 1 项待验证），Android App 从第一天起就要处理 `splitSourceDirs`。
- A 版已安装的副本不再更新；它们的数据不迁移。

## 修订的记录

- [ADR 0001](0001-product-release-promotion.md)：Android 发布产物不再含游戏二进制；interop 包是游戏派生物，在合作授权下公开分发。
- [ADR 0002](0002-mainland-mirror-check.md)、[ADR 0003](0003-per-platform-release-promotion.md)：Android 的平台规则（没有大陆镜像、不在 lockstep 集合里）改由本记录承载，内容不变。
- [ADR 0007](0007-one-public-bucket-and-domain.md)：公开桶的 `installer/android/apk/` 不再写入；interop 包与预适配记录进公开桶的 `installer/android/`。
- [ADR 0008](0008-interop-snapshots-in-snapshot-lock.md)：Payload Inventory 不再有 `apk-injection` 条目；Patcher 只作为锁定的 interop 生成工具（`generate-interop`）留在快照采集里，发布不再运行它。
- [ADR 0009](0009-android-release-key.md)：签名对象改为 Android App，换成 2026-11-01 生成的新密钥。
- [ADR 0010](0010-android-release-platform.md)：被本记录取代。
- [ADR 0011](0011-separate-mod-update-endpoint.md)：Android 的 mod 入口与官网入口不变，Android App APK 的规则由本记录定义。

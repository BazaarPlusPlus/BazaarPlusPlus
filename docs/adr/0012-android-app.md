# Android 改由 BPP 的 Android App 在本进程里加载已装的官方游戏

[ADR 0010](0010-android-release-platform.md) 让 Android 用户安装一个注入后由 BPP 签名的官方 APK（下称 A 方案）。它有三个固定代价：用户必须先卸载官方版；游戏每更新一次，用户都要等 BPP 重新注入，再下载约 2.8 GB；每个版本在 R2 多存约 2.9 GB，注入只能在 Linux x64 上跑。2026-11-01 的可行性试验（#747，模拟器 `bpp_host_spike`）证明：一个只用公开 API 的 App（`AppComponentFactory`、`PathClassLoader`、`ContextWrapper`、`ResourcesLoader`）能在自己的进程里加载用户已装、未修改的官方 APK，复用 LemonLoader 的运行时，跑通 master 的全部 Android Mods，并能和官方 App 共存。

本记录取代 ADR 0010。下文用 **Android App**（BPP Android App）指包名为 `com.bazaarplusplus.android` 的 BPP 应用，以便和 mod 的 Android Host（`BazaarPlusPlus.Android`，LemonLoader 上的 mod 程序集）以及官方游戏的 App 区分开。父 issue 写作"宿主 App"的就是它。

## 前提

- **合作授权。** 合作方同意两件事：在 Android App 里运行官方 App；在公开桶里分发由游戏生成的 interop。授权不包括分发游戏本体：官方 APK、`libil2cpp.so`、`global-metadata.dat` 都不进公开桶，也不进 Android App。
- **greenfield。** 不为 A 方案的已安装用户做数据迁移，不设并行期。A 版副本读不了新的平台清单：它的 mod 只会得到"没有可用的 APK"，没有任何提示，以后也不再更新。这一点已接受。
- **只支持最新游戏版本。** 每个产品版本只适配一个游戏版本，Snapshot Lock 仍只有 `android-online` 一个条目（[ADR 0008](0008-interop-snapshots-in-snapshot-lock.md)）。

## 决定

### Android App

- 包名 `com.bazaarplusplus.android`，一经发布永不更改，与 iOS 的 `com.bazaarplusplus.ios` 对应。开发构建用 `applicationIdSuffix ".dev"`，可以和正式版并存；两者的沙盒和外部文件目录各自独立。
- 最低 Android 11（API 30）：`ResourcesLoader` 从 API 30 起才有。
- Android App 本身没有账号，不需要登录。游戏在 Android App 的沙盒里运行，登录状态和官方 App 分开保存，所以第一次用 BPP 启动时，用户要在游戏自己的登录界面再登录一次。
- 它携带的运行时是 Loader Release（Snapshot Lock 的 `loader` 条目）、`libe_sqlite3.so`（`nativeLibs` 条目）和 `BazaarPlusPlus.Updater.dll`（在运行时 zip 的 `assets/LemonLoader/deployment/Plugins/` 下，是 LemonLoader 唯一的打包部署）。它不携带 interop，也不携带 Mods。APK 约 65 MB。
- 加载方式：用游戏的 `sourceDir` 加 `splitSourceDirs` 建 `PathClassLoader`；在 `instantiateActivity` 里从这个 loader 创建继承游戏 `UnityPlayerActivity` 的 Activity；它的 Context 对外报告游戏的包名、`ApplicationInfo` 和资源，所有可写目录留在 Android App 的沙盒里。Unity 按绝对路径从 `nativeLibraryDir` 加载 `libmain.so`，所以 Android App 构造一个符号链接 lib 目录：`libmain.so` 与 `libe_sqlite3.so` 指向自己的副本（Android App 设 `extractNativeLibs="true"`，它们在磁盘上），游戏已解压的库指向游戏的副本；游戏没有解压的库（Play 的 split 安装通常如此）不建链接，类加载器的库搜索路径里加上每个游戏 APK 的 `!/lib/arm64-v8a`，从 APK 内部加载。LemonLoader 通过游戏 `Resources` 的 `AssetManager` 读 `LemonLoader/**`，Android App 用 `ResourcesLoader` 把运行时 zip 和 interop 包加进去。
- 进程被系统杀掉后，系统可能直接恢复游戏的 Activity，绕过启动器。这时游戏还没有准备好，Android App 改为打开启动器，重新走一遍门禁，不在未准备的状态下启动游戏。
- 身份冲突：`getPackageName` 返回游戏的包名，UID 却是 Android App 的。试验里由此出现 `MANAGE_GAME_MODE` 与 `DUMP` 两个 SecurityException，都不致命。Play 结算与 Play 游戏服务在 Android App 里的行为尚未验证（#749 第 9 项）；在验证之前，购买只在官方 App 里完成。

### 三档启动规则

Android App 启动时找到官方游戏（`com.tempogames.thebazaar`），在它的所有 APK（base 与 split）里计算 `lib/arm64-v8a/libil2cpp.so` 与 `assets/bin/Data/Managed/Metadata/global-metadata.dat` 的 sha256，合称**游戏代码哈希**，按 `lastUpdateTime` 加 APK 路径缓存。然后和平台清单 `game` 里的两个值比较：

| 已装的官方游戏 | BPP 启动 | 原版启动 |
|---|---|---|
| 两个哈希都与清单一致（包括只改内容、`versionName` 变了的更新） | 正常模式 | 可用 |
| 哈希不同，且 `versionCode` ≥ 清单的 `versionCode` | 有可用的预适配包时，用户可以主动选择兼容模式，选择按游戏代码哈希记住；没有时显示"等待适配" | 可用 |
| 哈希不同，且 `versionCode` < 清单的 `versionCode` | 提示"请先更新官方游戏"，附 Play 与发行商下载链接 | 可用 |
| 未安装 | 引导安装官方游戏 | 不可用 |

- 判断只看代码哈希，不看 `versionName`：发行商只改内容的更新不需要新的适配。
- `versionCode` 相同而哈希不同，按"比清单新"处理。
- **Play 构建与发行商 APK 的代码是否相同，尚未验证（#749 第 2 项）。** `android-apk-watch.yml` 只采集发行商的 APK。如果两者不同，Play 用户永远进不了正常模式，也拿不到预适配包，只能原版启动；这时要先让采集覆盖 Play 构建，才能把 Android App 发给 Play 用户。因此 #749 第 2 项是首次发布的阻塞条件。两个渠道的 `versionCode` 是否可以直接比大小，同样由这一项验证。
- "原版启动"直接打开官方 App，不经过 Android App 的任何代码。
- 读不到平台清单（离线）时，用上一次校验过的清单与已下载的产物；没有缓存时只能原版启动。

### 兼容模式

- 兼容模式**绝不**使用旧 interop。interop 用 `GetIl2CppMethodByToken` 按元数据 token 绑定方法，游戏代码一变，token 就会移位，方法会被悄悄绑错。
- interop 只有一个来源：CI 发布的**预适配 interop 包**（见下文）。设备端生成在当前 Loader Release（`v0.7.4-android.4`）里不可用：它的 `runtime/loader/Dependencies/` 只有 `SupportModules/`，没有 `Il2CppAssemblyGenerator/`，也没有 Cpp2IL；试验设备上部署出的 `MelonLoader/Dependencies/` 也一样。所以 #747 设想的"LemonLoader 在设备上生成"这条备用路径不存在。空窗是从游戏发布到 `android-apk-watch.yml` 发布预适配包的时间：它每小时检查一次，加上生成与上传约需一到两小时；可以接受。以后换成带生成器的 Loader Release 时，再修订本节。
- 兼容模式加载当前产品版本的 Mods。每个 Harmony 补丁单独安装，一个失败只关闭它自己的功能（`AndroidPatchHost` 现有的行为，`android.patch.failed`），其余照常。mod 读到启动记录的 `mode: "compatibility"` 后关闭所有出站上传（Bundle 与 Ghost），本地记录照常进行，主菜单的版本标签与设置里显示"兼容模式"。上传已经关闭，所以绑错的补丁不会把错的数据传出去。

### 发布产物

每个产品版本的 Android 发布有三样产物，都在公开桶 `bazaarplusplus-public` 的 `installer/` 下：

| 产物 | 对象键 | 完整性 |
|---|---|---|
| Android App APK | `<版本>/android-aarch64/app/BazaarPlusPlus_<版本>_android-aarch64.apk` | Android 的 APK 签名（[ADR 0009](0009-android-release-key.md)） |
| interop 包 | `android/interop/<sha256>.zip`，按 zip 文件的 sha256 内容寻址 | interop 声明的 RSA 签名（见下文） |
| Managed Payload | `<版本>/android-aarch64/managed/BazaarPlusPlus_<版本>_android-aarch64-managed.zip`（不变） | minisign 签名，由 Updater 验证 |

- **interop 包**是一个确定性 zip：Interop Snapshot 目录的每个顶层文件放在 `assets/LemonLoader/runtime/interop/<文件名>`，没有别的条目。它不需要 `AndroidManifest.xml`、`resources.arsc` 或 APK 签名：Android App 用 `ResourcesProvider.loadFromApk` 把它作为第二个 `ResourcesLoader` 加进游戏的资源，LemonLoader 从 `LemonLoader/runtime/interop/` 列出并解压它，与运行时所在的第一个 loader 合并（2026-11-01 在 `bpp_host_spike` 上验证：一个只有 `assets/` 的普通 zip 被接受，174 个文件被解压，30 个补丁全部安装）。zip 的写法与 Managed Payload zip 相同（`release/payload-zip.mjs`），所以同一份快照总得到同一份字节。同一份快照只上传一次。
- **interop 的签名。** interop 是 LemonLoader 在 mod 和 Updater 之前加载的托管代码，所以它必须签名，而不能只靠平台清单里的 sha256（平台清单本身没有签名）。签名的对象是一行**interop 声明** `bpp-interop-v1\n<zip 的 sha256>\n<il2cppSha256>\n<metadataSha256>\n`，算法是 SHA256withRSA，密钥是 Android 发布密钥（ADR 0009）。声明把 interop 和它适配的游戏代码绑在一起，所以被篡改的清单也不能把一份真的 interop 配给另一个游戏。Android App 内置发布证书（公开的 DER），用它验证；Android 11 原生支持这个算法，不需要新的库。开发构建内置同一份证书，所以也能用正式发布的 interop。Android App 在加载前还要检查 zip 里没有 `assets/LemonLoader/runtime/interop/` 以外的条目，因为后加的 `ResourcesLoader` 会遮盖运行时里的同名 asset。
- Android App APK 的 `versionCode` 由产品版本算出（`major × 1000000 + minor × 1000 + patch`），由 `release/manifest.mjs` 定义，Gradle 构建接收它。
- Payload Inventory（`release/payload.json`）的 `melonloader` 条目仍相对应用的外部文件目录。A 方案的生产者 `apk-injection` 退役，改为生产者 `android-app`：它的条目是 Android App APK 内部的路径（`lib/arm64-v8a/libmain.so`、`lib/arm64-v8a/libe_sqlite3.so` 与 LemonLoader 运行时），以及启动记录（见下文），都由 Android App 产生，不进 mod 构建。
- 产品版本可以跳过 Android；Android 不在 `latest.json` 的 lockstep 集合里（`LOCKSTEP_PLATFORM_KEYS`）；"一个版本号只对应一个提交"对 Android 同样成立；Android 没有大陆镜像，`promote --platform android` 不读镜像记录。这几条沿用 ADR 0010，不变。

### fragment 与平台清单

平台键 `android-aarch64` 不变。fragment 换成新的 kind `android-app`（`schemaVersion: 1`）；A 方案的 `kind: 'android'` fragment 按 kind 名被拒绝，而不是因为缺字段：

```
{ schemaVersion: 1, kind: 'android-app', version, platform: 'android-aarch64', gitCommit,
  app{url, size, sha256, versionCode, signerSha256},
  game{gameVersion, versionCode, il2cppSha256, metadataSha256},
  interop{url, size, sha256, snapshotSha256, signature},
  managedPayload{url, size, sha256, signature},
  loader{tag, sha256},
  selfUpdatable, historyDatabaseUserVersion, nonManagedPayloadDigest }
```

- `game` 是这个版本适配的游戏：Snapshot Lock `android-online` 条目的 `gameVersion`、`versionCode`、`il2cppSha256` 与 `metadataSha256`。官方 APK 的 sha256 不再发布，因为门禁不看它。
- `interop.snapshotSha256` 是锁条目的 `interopSha256`（目录摘要），`interop.sha256` 是 zip 文件本身的 sha256，`interop.signature` 是 interop 声明的签名（base64）。
- `nonManagedPayloadDigest` 是 Android App 携带的运行时的摘要：锁条目的 loader（tag 与 zip 的 sha256）、`nativeLibs` 各库的 sha256 与 Updater 的 sha256。interop 改为单独下载，不再计入。它只有一种算法，由 `manifest.mjs` 的 `androidNonManagedPayloadDigest` 定义；`release::build` 把算出的值交给 Gradle 写进 Android App，构建后再从 APK 里读回核对，所以 fragment 与 APK 里的值不会各算各的。
- Platform Release Manifest `latest/android-aarch64.json` 与 `latest/mod/android-aarch64.json` 保留共同的顶层字段（`version`、`gitCommit`、`notes`、`pub_date`），`downloads["android-aarch64"]` 放 `app`、`managedPayload`、`interop`、`game`、`loader`、`selfUpdatable`、`historyDatabaseUserVersion`、`nonManagedPayloadDigest`。平台清单没有 kind：有 `app` 的是本记录的形状，有 `apk` 没有 `app` 的是 A 方案的形状。浏览器、mod 与 Android App 都按本记录的形状读；浏览器与 mod 用 `release/downloads.ts` 的 `decodeAndroidDownload` 与它的 C# 对应。
- `upload` 与 `promote --platform android` 校验三样产物在 R2 上存在，大小与 sha256 和 fragment 一致，以及 Android App APK 的签名证书（见 ADR 0009 的 2026-11-01 修订）。**读已提升的 Android 平台清单时，A 方案的形状按"还没有 Android App 发布"处理**：它的 `apk` 和旧证书不参与任何比较，版本不回退、ETag 条件写的规则照常。这是一条按形状写死的规则，不是可以打开的跳过开关；A 版清单只会在第一次提升时遇到一次。

### 预适配 interop 包

新游戏发布、代码哈希变了时，`android-apk-watch.yml` 生成新游戏的 interop，上传 interop 包（同一个内容寻址键），再写一份预适配记录：

- 键：`installer/android/adaptations/<il2cppSha256>_<metadataSha256>/<loader tag>.json`。游戏代码的两个哈希和 Loader Release 的 tag 都在键里：一份记录只对应一份游戏代码和一个 Loader Release；换了 Loader Release，就为同一份游戏代码再写一份。
- 形状：`{ schemaVersion: 1, kind: 'android-adaptation', game{gameVersion, versionCode, il2cppSha256, metadataSha256}, interop{url, size, sha256, snapshotSha256, signature}, loader{tag, sha256}, il2cppInteropVersion, publishedAt }`。`interop.signature` 与 fragment 里的是同一种 interop 声明的签名，所以记录本身不需要签名：改了记录的人拿不到与已装游戏代码匹配的有效签名。
- 只有 `android-apk-watch.yml` 写它。正常运行时只创建（`If-None-Match: *`）；手动触发并选了 `force` 时可以覆盖，用来纠正一份错的记录。
- Android App 只在记录的 `game` 两个哈希与已装游戏一致、`loader.tag` 与自己携带的 Loader Release 相同、签名验证通过时使用它；否则视为没有预适配包。
- 它不提升 `VERSION`，不是 Product Release，也不进 Platform Release Manifest。正式适配是之后的一个产品版本：锁条目推进到新游戏，平台清单的 `game` 与已装游戏一致，Android App 回到正常模式。两者用同一份 Interop Snapshot 时，interop 包的键相同，不会重复上传；预适配记录留在原处，此后不再被读取。

### 启动记录（Android App 与 mod 的契约）

Android App 在每次 BPP 启动之前，在自己的**外部文件目录**（`getExternalFilesDir(null)`，即 `/storage/emulated/0/Android/data/com.bazaarplusplus.android/files/`，LemonLoader 的 `MelonLoader/` 也在这里）写 `MelonLoader/UserData/BazaarPlusPlus.launch.json`，先写临时文件再改名。写不成功就不启动。字段：

- `schemaVersion`：1。
- `processId`：写它的进程号。游戏就在这个进程里运行，所以 mod 可以用自己的进程号判断记录是不是这次启动写的。
- `app`：`packageName`、`versionName`、`versionCode`、`nonManagedPayloadDigest`（开发构建为 null）。
- `release`：这次启动对应的产品版本，也就是这次暂存或已安装的 Managed Payload 的版本。
- `mode`：`normal` 或 `compatibility`。
- `game`：`packageName`、`versionName`、`versionCode`、`il2cppSha256`、`metadataSha256`。
- `interop`：`sha256`、`snapshotSha256`、`source`（`release` 或 `pre-adaptation`）。

mod 读它的规则：

- 文件不存在：mod 不在 Android App 里运行（A 版副本，或 `just mod::android-deploy` 的开发部署），这些情况的 interop 由 APK 自带，与 Mods 匹配；在日志里记录，正常运行。
- 文件存在，但读不了、`schemaVersion` 或 `mode` 不认识、`processId` 不是当前进程，或 `release` 与正在运行的 Mods 版本不同：按兼容模式处理，关闭上传。宁可少传，不可在错的 interop 上传数据。
- 在 Android App 里运行时，mod 不再自己下载和暂存 Managed Payload（Core 的 `SelfUpdateStaging`）。暂存只由 Android App 做，这样换入的 Mods 总是 Android App 这次启动所用 interop 对应的版本。mod 只显示"有新版本，请从 BPP App 重新启动"。

格式的 owner 是 Android App 里写它的代码和一份共享 golden，mod 的读取用同一份 golden 测试（#751、#752 新建，并登记进根 `AGENTS.md` 的 Contracts 表）。字段的增加要升 `schemaVersion`，并与 mod 的读取在同一个 PR 里改。

### Android App 管理 LemonLoader 的部署状态

LemonLoader 用**游戏**的 `lastUpdateTime` 作为部署 revision 和 interop 标记（试验设备上 `.lemonloader-interop-hash` 的内容是 `apk-<lastUpdateTime>`），因为在 Android App 里 `getPackageName` 返回游戏的包名。所以 Android App 更新后，或者它换了 interop 而游戏没变时，LemonLoader 不会重新部署。Android App 自己记录上一次启动时的 Android App `versionCode` 与 interop 的 sha256，启动前比较。以下路径都相对外部文件目录：

- `versionCode` 变了（包括第一次启动）：删除 `MelonLoader/` 下的 `.lemonloader-deployment-state*`、`.lemonloader-backups`、各 `.lemonloader-*-hash`、`MelonLoader/Il2CppAssemblies/`、`Plugins/BazaarPlusPlus.Updater.dll`，让 LemonLoader 重新解压运行时和 interop、重新部署 Updater。同时删除已安装的 Managed Payload（`MelonLoader/Mods/` 与 `MelonLoader/UserLibs/`）和 Data Root 的 `Updates/`，然后暂存当前版本的 Managed Payload。原因：Updater 拿暂存清单的 `nonManagedPayloadDigest` 和**已安装清单**的比，而新 Android App 的运行时摘要可能已经变了；删除已安装的 Managed Payload 以后，Updater 按全新安装处理（没有已安装清单就不比较），换入与新运行时匹配的版本。Data Root 里的用户数据不动。
- interop 变了：删除 `MelonLoader/.lemonloader-interop-hash` 与 `MelonLoader/MelonLoader/Il2CppAssemblies/`，让 LemonLoader 只解压新的 interop，不留下旧文件。2026-11-01 在 `bpp_host_spike` 上验证，删除标记之后 LemonLoader 从 `ResourcesLoader` 重新解压 interop。

`versionCode` 变化时的这组删除，要在 #750 的 AVD 验收里逐项确认：删除后 LemonLoader 重新部署，Updater 换入暂存的 Managed Payload。换 Loader Release 时要重新确认，因为这些是 LemonLoader 的内部文件。

### 下载与 Managed Payload 的安装

- 所有下载（Android App APK、interop 包、Managed Payload zip）先写到临时文件，校验大小与 sha256 后再改名；interop 包还要验证签名。校验不通过的文件删除，不留下。
- Managed Payload：Android App 按 Core 的 `ManagedPayloadStager` 定下的布局暂存到 Data Root 的 `Updates/<版本>/`（先解压到 `Updates/.download-<版本>-<随机>/`，再一次改名发布）。Android App 只解压 `manifest.txt`、`manifest.txt.minisig` 和 `files/MelonLoader/Mods/<文件名>`、`files/MelonLoader/UserLibs/<文件名>` 这几种条目，名字里不能有路径分隔符或 `..`；有任何别的条目就拒绝整个 zip。Updater 在 BPP 启动的装载阶段验证 minisign 签名和每个文件的哈希，然后换入（mod [ADR-0014](../bazaarplusplus-mod/docs/adr/0014-managed-payload-self-update.md)）。minisign 只由 Updater 验证：API 30 没有 Ed25519，而 Updater 已经带着验证实现和钉住的公钥。全新安装时 Updater 读不到已安装清单，按"无"处理，可以换入；刚换入的 UserLibs 当次启动就能加载（2026-11-01 在 `bpp_host_spike` 上验证：`updater.swap.applied from=none to=6.0.0`，Data Root 的数据库被创建）。
- 启动前，Android App 删除 `Updates/` 下自己这次没有暂存的版本，这样 Updater 只会换入与这次 interop 对应的 Mods。
- Updater 拒绝一份暂存时会删除它，游戏照常启动但没有新的 Mods。Android App 记住自己暂存的版本；下次启动时，如果已安装的 Managed Payload 版本仍低于它、暂存目录也不在了，就告诉用户"上次安装 Mods 失败"，并提供诊断导出，然后再暂存一次。
- 平台清单的 `nonManagedPayloadDigest` 和 Android App 自己的不同时，这个版本的 Managed Payload 不能在这个 Android App 上运行：Android App 提示安装新的 Android App（`app.url`），BPP 启动要等更新完成，原版启动不受影响。

## 否决的方案

- **A：继续注入官方 APK（ADR 0010）。** 用户要卸载官方版，每次游戏更新都要重新下载约 2.8 GB 并等 BPP 重新注入；发布要在 Linux x64 上跑 Patcher，每个版本多存约 2.9 GB；签名密钥是全体用户的单点。
- **B2：直接用 FusionCore 与 BepInEx Fusion。** FusionCore 是 GPL-3.0，复制它的代码会让 Android App 也必须用 GPL；它的机制可以参考，代码不能拿来用。它用自己 fork 的 BepInEx，`BazaarPlusPlus.Android` 要从 MelonLoader API 迁过去，并跟着一个贡献者很少的上游走。
- **B3：TAP-APIA 的路线。** 它在 NextBep 预编译的基线 APK 上改二进制 Manifest、追加 dex，不从源码构建；NextBep 的许可与源码状态不清楚；MelonLoader 兼容层套在 BepInEx 上，两层加载器每次升级都可能断；为防止系统杀进程，它用前台服务、心跳和 wake lock，耗电略高。
- **C：自带引擎运行 Mac 版（iOS 的做法）。** Mono 解释器执行游戏逻辑，CPU 负载与耗电明显高于 IL2CPP；官方已经有 Android 版，这条路在 Android 上不值得。
- **interop 在设备上生成。** 当前 Loader Release 不带生成器（见"兼容模式"）；就算带，首次启动也要灰屏几分钟。
- **interop 包按产品版本放进版本目录。** 每个版本都要重传约 15 MB，预适配包与正式发布也不能共用同一个对象。
- **interop 包做成 aapt2 生成的资源 APK，用 APK 签名保护。** 试验证明普通 zip 就被接受；aapt2 的输出不保证跨版本逐字节相同，内容寻址就靠不住。
- **interop 用 minisign（Tauri updater 密钥）签名。** Android App 要在 API 30 上验证 Ed25519，就得引入 BouncyCastle 或再写一份 Updater 已有的实现；Android 发布密钥的 RSA 签名由系统直接验证。
- **interop 只靠平台清单里的 sha256。** 平台清单没有签名，能写 CDN 或桶的人就能让每个用户加载任意托管代码。
- **Android App 自己验证 Managed Payload 的签名。** 同上，API 30 没有 Ed25519；Updater 已经验证，Android App 只需要拦住 zip 里不该有的路径。

## 代价

- 游戏一更新，正式适配之前 BPP 用户只能用兼容模式（不上传），没有预适配包时只能原版启动。
- 游戏的登录状态与下载的资源在 Android App 里另存一份，用户第一次要再登录一次。
- Android App 依赖 LemonLoader 的部署细节（`lastUpdateTime` revision、`.lemonloader-*` 标记文件）。换 Loader Release 时要重新验证"Android App 管理部署状态"一节。
- Android 发布密钥除了签 Android App，还签 interop 声明；`android-apk-watch.yml` 发布预适配包时也要用它，所以这个工作流的发布步骤要进入 `release` 环境。
- Play 构建是 split APK（#749 第 1 项待验证），Android App 从第一天起就要处理 `splitSourceDirs`；Play 与发行商代码哈希的一致性（#749 第 2 项）决定 Play 用户能不能用正常模式。
- A 版已安装的副本不再更新，也不会得到提示；它们的数据不迁移。

## 修订的记录

- [ADR 0001](0001-product-release-promotion.md)：Android 发布产物不再含游戏二进制；interop 包是游戏派生物，在合作授权下公开分发。
- [ADR 0002](0002-mainland-mirror-check.md)、[ADR 0003](0003-per-platform-release-promotion.md)：Android 的平台规则（没有大陆镜像、不在 lockstep 集合里）改由本记录承载，内容不变。
- [ADR 0007](0007-one-public-bucket-and-domain.md)：公开桶的 `installer/android/apk/` 不再写入；interop 包与预适配记录进公开桶的 `installer/android/`。
- [ADR 0008](0008-interop-snapshots-in-snapshot-lock.md)：Payload Inventory 不再有 `apk-injection` 条目；Patcher 只作为锁定的 interop 生成工具（`generate-interop`）留在快照采集里，发布不再运行它。
- [ADR 0009](0009-android-release-key.md)：签名对象改为 Android App 和 interop 声明，换成 2026-11-01 生成的新密钥。
- [ADR 0010](0010-android-release-platform.md)：被本记录取代。
- [ADR 0011](0011-separate-mod-update-endpoint.md)：Android 的 mod 入口与官网入口不变，Android App APK 的规则由本记录定义。
- mod [ADR-0014](../bazaarplusplus-mod/docs/adr/0014-managed-payload-self-update.md)：Android 上 Managed Payload 由 Android App 暂存，不再由 APK 携带，mod 在 Android App 里不自己暂存。

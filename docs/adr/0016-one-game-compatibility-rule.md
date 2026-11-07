# 三个平台用同一条规则判断游戏兼容

## 背景

到 2026-11-07，三个平台用不同的方法判断"已装的游戏"与"BPP 适配的游戏"是否一致：

- Android App 比较游戏代码哈希（[ADR 0012](0012-android-app.md)、[ADR 0013](0013-remove-compatibility-mode.md)）。只有哈希相同才能用 BPP 启动。
- Android 的 mod 不看哈希。`AndroidGameUpdate.Needed` 比较 versionName 里的 build 号。一个产品版本带上只改内容的新锁条目发布后，还停在旧内容版本的用户被 App 正常放行，mod 却提示"去商店更新游戏"。两条规则互相矛盾。
- 桌面不记录适配的游戏。桌面 fragment 没有 `game`（`release/manifest.ts` 的 `DesktopPlatformFragment`），mod 与 installer 都不比较游戏。游戏更新后，用户看不到 BPP 是否已经适配。[ADR 0015](0015-steam-snapshot-capture.md) 末段因此决定下载页不显示桌面的"已适配版本"。
- 下载页的桌面卡片显示 Steam 的在线游戏（Game Status），Android 卡片显示已适配的游戏。两张卡片字样相近，事实不同，都没有兼容结论。

用户在 2026-11-07 决定统一三个平台的规则，watch 工作流不再升 `VERSION`。Android 在未适配或游戏较旧时仍不能用 BPP 启动（见"修订的记录"）。

## 决定

### 一个概念：游戏代码身份

Game Code Hash 推广为**游戏代码身份**：

| 平台 | 游戏代码身份 | 已有的使用者 |
|---|---|---|
| Windows、macOS | `Managed/` 目录摘要（`managedDirectoryDigest`，锁条目的 `sha256`） | `steamChangeKind` 的 `code`/`content` 分类 |
| Android | `libil2cpp.so` 与 `global-metadata.dat` 的 sha256 | `captureKind`、Android App 的 `LaunchGate` |

只改内容的游戏更新不改变它。所以三个平台都按它判断，不按 versionName 或 build 号判断。

### 一条规则：结果与提示

已装游戏与已适配游戏的代码身份：

| 结果 | 条件 | Android App 启动页 | Android mod 主菜单 | 下载页卡片 |
|---|---|---|---|---|
| 已适配 | 相同 | 不提示 | 不加后缀 | 适配游戏、在线游戏、"已适配" |
| 未适配 | 不同（Android 另需游戏不比已适配的旧） | "BPP 尚未适配游戏 X，适配版本发布前只能原版启动"，BPP 启动不可用 | `未适配此游戏版本` | 适配游戏、在线游戏、"未适配，适配中" |
| 游戏较旧 | 仅 Android：不同，且已装游戏的 `versionCode` 小于已适配的 | "请先把游戏更新到 X"，附 Play 与发行商链接，BPP 启动不可用 | `请更新游戏` | 不出现 |
| 未知 | 读不到任一方 | 不提示 | 不加后缀 | 只显示适配游戏 |

桌面 mod 不比较，主菜单也不显示适配状态；桌面是否已适配只在下载页显示。

主菜单上，自更新的提示（如 `restart to update`）先于适配状态显示。提示文案的中英繁三语在实现时定。

桌面没有"游戏较旧"：Steam 会自动更新游戏，而且桌面的 build 号在渠道之间不能比较（同一提交的 staging 是 13077，online 是 13081）。

### 每个平台在哪里比较

- **桌面**：只有下载页比较，用平台清单的 `game` 与 Game Status 的 `managedSha256`。mod 不比较。
- **Android App**：`LaunchGate` 照旧算三档，只有已适配一档能用 BPP 启动（[ADR 0013](0013-remove-compatibility-mode.md) 的门禁）。未适配与游戏较旧两档只显示上表的提示，可以原版启动。
- **Android mod**：读启动记录里 App 算好的游戏哈希与 `versionCode`，与平台清单的 `game` 比较。启动记录缺失或过期时是"未知"。删除 `AndroidGameUpdate.ParseVersionCode` 与 `Newer`。
- **上传**：不变。桌面照常上传；Android 只在已适配时运行 mod。

### 下载页需要的数据

- 桌面 fragment 加可选的 `game`：`{ gameVersion, managedSha256 }`，取自构建记录的 `lockEntry`，所以用 `-p:ManagedPath` 对着 staging 构建时，它如实记下 staging 条目。现有消费方都忽略多余的键，所以这一步只改 `release/manifest.ts` 与它的 fixtures。
- Game Status 的每个桌面平台加 `managedSha256`。`decodeGameStatus` 与下载页在同一个 PR 里改。
- Android 的在线游戏写进单独的公开对象 `game-status/android.json`，由 `android-apk-watch.yml` 在锁或打开的锁 PR 记录了这个 APK 时写入，字段是 `gameVersion`、`versionCode`、`il2cppSha256`、`metadataSha256`。它不并入桌面的 `game-status/latest.json`：那份文档按 Steam buildid 整体替换，两个工作流写同一个对象会互相覆盖。两个工作流已经用同一个仓库级 R2 token。
- 游戏更新后、采集完成前，Game Status 保持旧值，下载页这段时间仍显示"已适配"。空窗约为一次采集的时间。

## 修订的记录

- **Android 保留启动门禁。** ADR 0013 规定"等待适配"时绝不用旧 interop 启动，因为 interop 用 `GetIl2CppMethodByToken` 按元数据 token 绑定方法，游戏代码一变就会绑错。本记录起初决定未适配时也用最新发布的 interop 启动，接受闪退或读错数据的风险。2026-11-07 在模拟器上实测（#891）：已装游戏 1.0.13113、发布版本 6.2.3（适配 1.0.13084）时，LemonLoader 与 Mods 正常加载，然后 mod 第一次调用游戏方法（`AndroidRunLifecycleSource.Subscribe` 里的 `Event.AddListener`）时 `SIGSEGV`，2 次运行 2 次都在同一处；已装游戏 1.0.12825 时，LemonLoader 自己初始化（`SceneHandler.Init` 的 Harmony 补丁）就 `SIGSEGV`，还没运行 mod 代码。所以不是"可能闪退"，而是每次启动都闪退，用户决定保留门禁。ADR 0012 的三档启动规则与 ADR 0013 都不变；本记录只把提示改为按游戏代码哈希判断。
- ADR 0015 末段"下载页不显示桌面版的已适配版本"被本记录取代。那时桌面清单没有这个字段；本记录加上了它。
- `GLOSSARY.md`：Game Code Hash 改为"游戏代码身份"并加桌面一行；Game Adaptation 不再只指 Android；Game Status 写明桌面与 Android 两个对象。

## 否决的方案

- **Android 未适配时也用最新发布的 interop 启动。** 模拟器上每次都在启动时闪退，见"修订的记录"。
- **只有 Android 的 watch 升 `VERSION`（原做法）。** 它的理由是 Android 在发布前不能用 BPP 启动。门禁仍在，但机器人升版只省下一个小 PR，见"`VERSION` 只在发版时升"。
- **两个 watch 都升 `VERSION`。** 桌面每次游戏代码更新都会多一个不一定发布的版本号。

- **按 `gameVersion` 或 build 号比较。** 只改内容的更新会被判为"未适配"，也就是要修的 Android mod 问题；桌面的 build 号在渠道之间不能比较。
- **未适配时关闭上传。** 用户决定照常上传，与桌面相同。
- **桌面也在未适配时阻止加载。** 没有人要求，它推翻 ADR 0004 的 Harmony 降级。
- **桌面 mod 在主菜单显示"未适配此游戏版本"（本记录的初稿）。** 构建时把锁条目嵌入程序集，启动后在后台线程算已装游戏的 `Managed/` 摘要并比较（#886）。用户在 2026-11-07 实测后决定不显示：桌面 mod 未适配时照常运行，靠 Harmony 降级（[ADR 0004](0004-pinned-game-assembly-snapshots.md)），这个后缀只让人担心，换短的说法（如"兼容模式"）也一样；用户想知道时看下载页。不显示后缀，嵌入、后台哈希与 C# 版摘要算法都没有用处，一起不做。
- **桌面 mod 读平台清单的 `game`。** mod 只关心自己编译时用的游戏；"新版本适配了新游戏"已经由自更新提示。清单的 `game` 只给下载页用。
- **Android 并入 `game-status/latest.json`。** 见上文，两个写入方会互相覆盖。

## 代价

- 桌面用户在游戏里看不到 BPP 是否已适配当前游戏，要看下载页。
- 公开 bucket 多一个对象 `game-status/android.json`。

## 实施顺序

每一步各一个 PR，可以独立合并：

1. Android：`LaunchGate` 的门禁不变，未适配与游戏较旧两档按游戏代码哈希显示提示；mod 改为比较哈希。`ReleaseManifestClient` 的 `AndroidAppRelease` 读 `versionCode`、`il2cppSha256`、`metadataSha256`；`AndroidMod` 把启动记录的 `game` 传给版本检查。
2. 桌面 fragment 加 `game`；`android-apk-watch.yml` 不再升 `VERSION`；Game Status 加 `managedSha256`；`android-apk-watch.yml` 写 `game-status/android.json`；下载页按结果显示。

## `VERSION` 只在发版时升

`steam-snapshot-channel.yml` 与 `android-apk-watch.yml` 都只开改锁的 PR，不升 `VERSION`。发版时由人另开一个升 `VERSION` 的 PR（如 #880），再运行 `release.yml`。现在 Android 的 `code` PR 会升补丁号，这一步删去。

原因是发版本来就要人来做：合并改锁的 PR、运行 `release.yml`、推广。机器人升版只省下一个很小的 PR。两个 watch 用同一条规则更简单，人在决定发版时升 `VERSION`。一个版本号只对应一个提交，平台可以跳过版本，这两条不变。

代价：Android 的游戏代码变了以后，发 Android 版要多开一个升 `VERSION` 的 PR，而在这个版本发布之前，Android 用户只能原版启动。

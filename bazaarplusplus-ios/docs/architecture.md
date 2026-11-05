# 架构

BazaarPlusPlus for iOS 在 iPhone 和 iPad 上运行 The Bazaar 的 Mac 版本，并加载 BazaarPlusPlus 模组。官方还没有 iOS 版本，所以这是过渡方案：App 自带 iOS Unity 引擎和静态 Mono 解释器，从 Steam 下载 Mac depot 来运行。

一个进程里有四部分：原生启动器、在 Mono 里运行的 Steam 服务、作为依赖嵌入的游戏运行时，以及游戏启动后加载的模组。启动器不是 Unity 场景；UnityFramework 只在玩家点"开始游戏"后经 `runEmbeddedWithArgc` 接管窗口。

| 模块 | 目录 | 形态 |
|---|---|---|
| 启动器 | `app/` | Swift/SwiftUI 与 ObjC++ 宿主，自有 Xcode 工程 |
| Steam 服务 | `service/` | C#（SteamKit）运行在 UnityFramework 的 Mono 里，经 C ABI 暴露 |
| 游戏运行时 | `runtime/`、`middleware/` | `UnityFramework.framework`：Unity 引擎、静态 Mono/AOT、IL2CPP 兼容层、中间件适配 |
| 游戏定义 | `game/game.json` | 版本、depot、插件、平台准入、托管适配 |
| 模组 | `bazaarplusplus-mod/`（iOS host 在 `src/BazaarPlusPlus/Host/Ios/`） | 桌面端同一套程序集，`tools/stage-mod.py` 编译并放进 App 的 `Mods/` |

## 接缝

- **启动器 ↔ 服务**：`service/Native/bridge.m` 导出 `bpp_steam_start`、`bpp_steam_command`、`bpp_steam_snapshot`、`bpp_steam_free`。启动器发 JSON 命令，每 0.5 秒读一次 JSON 快照（`service/Managed/SteamService.cs::Snapshot`）；Swift 端的镜像是 `app/Launcher/LauncherModel.swift::ServiceSnapshot`。服务是状态的唯一拥有者。
- **启动器 ↔ 系统后台**：`app/Launcher/BackgroundDownload.swift` 按快照申请后台运行时间，见下面的"登录与下载的恢复"。`app/Host/main.mm::sceneWillEnterForeground` 在启动时和每次回到前台时发 `foreground` 命令。
- **服务 ↔ 原生能力**：`service/Managed/Native.cs` 以 `__Internal` P/Invoke 调用 Keychain、AES、zstd、SHA-1 和符号链接，实现在 `service/Native/`。
- **游戏 ↔ Steam**：游戏调用的 Steamworks C API 由 `middleware/steamworks/` 实现。身份和 Web API 票据只来自进程内提供方：服务登录后调用 `bpp_steam_identity`，并通过 `bpp_steam_set_provider` 接收票据请求。
- **启动器 ↔ 引擎**：`app/Host/main.mm::playTapped` 在服务确认 `launched` 后设置 `BPP_GAME_ROOT`，交出窗口，并释放启动器界面。`tools/build.py` 给导出的 `Filesystem.mm` 打补丁，让引擎从这个目录读取游戏数据。
- **游戏 ↔ 模组**：准备阶段（`service/Managed/Installer.cs::Prepare`）把 `Mods/` 复制到游戏的 `Managed/`，并把 `runtime/helper/entrypoints.json` 里的入口写进游戏的 `RuntimeInitializeOnLoads.json`。模组入口 `BazaarPlusPlus.Host.Ios.IosEntry.Start` 在第一个场景加载前运行，它创建一个常驻 GameObject，再启动和桌面端共用的 `BppMonoHost`。
- **模组 ↔ 运行时**：模组的 Harmony 补丁不能写原生代码，所以 iOS host 换掉 HarmonyX 的 patcher（`InterpreterMethodPatcher`）。HarmonyX 自己生成补丁后的方法，再调用 Mono 补丁 0008 导出的 `mono_interp_redirect_method`，让解释器用新方法的代码代替原方法。被重定向的方法不再被内联；内联过它的根方法会重新转换。模组的 SQLite 调用（`e_sqlite3`）由 `runtime/bridge/il2cpp_on_mono.c` 映射到 App 里静态链接的 SQLite。

## 规则

- 运行时不按游戏名或 Steam App ID 分支。引擎补丁只对锁定的 Unity 构建生效，补丁脚本先检查原始指令，版本不符就失败。
- 中间件按插件 ABI 适配。
- 修补作用于派生副本，下载的原件不改。游戏重新构建时原件的哈希会变，但代码不一定变，所以哈希不一致时 App 记录日志，继续使用派生副本。
- 模组功能只写在 `bazaarplusplus-mod/`。iOS 不用的桌面能力（自更新、Mac 原生回放采集）由 host 选项或运行平台关闭，不在本项目里分支。
- 构建脚本放在它构建的模块旁边；`tools/build.py` 是唯一入口，按顺序调用它们。

## 进程模型

一个进程只有一个 Unity 引擎和一个 Mono domain。服务、游戏和模组共用这个 domain：服务先运行，游戏启动后服务冻结。游戏启动后不能回到启动器；"返回"只能靠重启 App 实现。

## 登录与下载的恢复

玩家只登录一次，只点一次下载。之后的断线、断网、切后台和被系统杀掉都由服务自己恢复（`service/Managed/SteamService.cs`），不需要玩家操作。

- **登录**：Keychain 里有保存的登录时，服务在启动、回到前台、连接断开和操作结束时都会自动恢复（`Recover`）。连接问题按退避时间一直重试，回到前台时立刻重试。只有 Steam 明确拒绝这个登录时才删除它（`Failures.IsRejectedLogin`）；其他失败保留它，点一次"连接 Steam"就能再试。玩家取消登录后，自动登录停止，直到下一次登录。
- **多台设备**：Steam 按登录 ID 区分会话，相同 ID 的新登录会踢掉旧的，所以每个安装有自己的登录 ID（`InstallationIdentity.LoginId`）。服务收到 `LoggedInElsewhere` 时不立刻抢回账号，否则两边可能互相踢；回到 App 或点一次"连接 Steam"才重新登录。
- **Keychain**：下载可能在锁屏时重连，所以登录保存为 `AfterFirstUnlockThisDeviceOnly`。
- **重连期间**：快照的 `reconnecting` 为真，`loggedIn` 保持为真，启动器不提示登录，状态也不变（`ready` 仍是 `ready`）。
- **下载**：内容服务器不需要 Steam 连接，所以连接断开时下载继续，结束后再重连。下载有自己的取消令牌（`downloadRun`）：取消下载不退出登录。网络失败不会结束下载（`RunDownload`），进度也不回退（`DownloadProgress`）。只有重试无用的失败才停止并报错，例如存储空间不足或内容服务器拒绝。
- **续传**：`Games/download-wanted` 表示玩家要的下载还没完成，App 被杀后下次启动自动继续。下载账本（`DownloadLedger`）记下已校验的文件，续传时不再计算哈希；下载完成后删除，所以"校验并准备游戏"仍然是完整校验。
- **后台**：只有 App 进程能下载，所以需要的是运行时间。iOS 26 及以上用 `BGContinuedProcessingTask`，系统显示进度，App 不在屏幕上时也继续运行。它必须在 App 位于前台时申请，可能被系统拒绝或提前结束，而且结束时不说明原因，也分不清是系统结束还是玩家在系统界面上停止，所以结束后下载只是暂停，回到 App 继续；真正的停止是 App 里的"取消"。更早的 iOS 只有几十秒后台时间，启动器会提示下载在后台暂停。模拟器不运行这个任务，这部分只能在真机上验证。

## 游戏更新

`game/game.json::testedGameVersion` 是发布时验证过的游戏版本，启动器显示为"适配版本"。Steam 只提供最新版本，所以 Steam 上的游戏更新后，App 下载新版本并照常启动：模组兼容游戏更新，打不上的补丁由模组自己报告，问题由玩家发现。`Application.version` 和启动器的"游戏版本"都用下载的版本（`GameDefinition.InstalledVersion`）。

只有游戏换了引擎才阻止启动，因为 Unity 引擎和 corlib AOT 编译进了 App。`Installer.Prepare` 比对 Unity 版本和 `mscorlib.dll`，不一致时抛出 `GameEngineChangedException`，服务进入 `outdated` 状态（`errorCode` 为 `GameEngineChanged`），启动器显示"等待适配"。这不是错误，下载会保留。按 `docs/engine-upgrade.md` 升级并发布新版 App 后才能启动。

App Store Connect 只接受数字版本：App 的 `CFBundleShortVersionString` 是产品版本（根目录 `VERSION`，和其中的模组相同），`CFBundleVersion` 默认是游戏 build 号；同一游戏版本重新上传时，用 `just ios::testflight <build>` 指定更大的 build 号（`tools/testflight.py` 把它作为 `BPP_BUILD_NUMBER` 交给 `tools/package-app.py`）。Unity 的 `Application.version` 读 `CFBundleShortVersionString`，而游戏和模组靠完整的游戏版本字符串识别游戏，所以 `app/Host/main.mm::exposeGameVersion` 在引擎启动前把下载版本的完整字符串写回主 bundle 的 info 字典。启动器打开时读 `https://cdn.bazaarplusplus.com/installer/latest/mod/ios-aarch64.json`，版本更新时在首页显示一条提醒，有 `downloads.ios-aarch64.url` 时可以点开下载。提醒不阻止游戏；清单不存在或读不到时什么也不显示。发布流水线还没有发布这个清单。

## 将来：官方 iOS 版

官方发布 iOS 版后，游戏来源换成官方 IPA，本项目的"Steam 下载 + Mac 移植运行时"可以整块删除：`service/`、`runtime/mono/`、`runtime/engine/`、`middleware/` 和 `game/` 都只服务于现在的路线。

- 官方 iOS 版一定是 IL2CPP。模组改用 IL2CPP host，复用 Android host 的 Interop 层（`bazaarplusplus-mod/src/BazaarPlusPlus.Android/`），而不是这里的 Mono host。Core、Storage、ModApi 不变。
- App Store 的 IPA 主二进制有 FairPlay 加密。重新签名注入前必须先拿到解密的二进制，这需要越狱设备一类的工具，还有绕过 DRM 的法律风险。所以只要 Mac 版还在，现在这条路线就保留为备选。
- 普通 iOS 设备不能改代码页。没有 JIT 授权时，原生 inline hook 做不了，只能改 `MethodInfo` 方法指针和 vtable，拦不住直接调用。先做一次和本项目第 1 步一样的 hook 试验，再定方案。

## 分发约束

下载并解释执行游戏代码违反 App Store 审核指南 2.5.2，原生启动器改变不了这一点。可行的分发方式是开发签名、企业签名或侧载。FMOD EULA 对作为引擎或工具集分发有限制，公开分发前需要和供应商确认授权。App 不用 The Bazaar 的名字和图标，只用 BazaarPlusPlus 自己的品牌。

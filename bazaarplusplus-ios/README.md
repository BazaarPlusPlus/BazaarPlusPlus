# BazaarPlusPlus for iOS

在 iPhone 和 iPad 上运行 The Bazaar，并加载 BazaarPlusPlus 模组。官方还没有 iOS 版本，所以 App 运行 Mac 版：从 Steam 下载你已拥有的 Mac depot，游戏脚本和模组交给 Unity Mono 解释执行，渲染和平台集成由 iOS Unity 引擎承担，两者通过 IL2CPP API 兼容层连接。启动器是原生 SwiftUI App。

当前状态：iOS Simulator 上已跑通 App 内 Steam 登录（密码 + 手机批准或验证码、扫码）、Keychain 恢复、完整下载与校验、游戏票据和游戏服务器认证，The Bazaar 的 19 个启动任务全部通过，模组的 Harmony 补丁通过解释器方法重定向生效。设备版能编译，原生自检和 Steam 登录已在真机通过；`game/game.json` 的设备启动门禁已打开，用于首次真机验证；游戏和模组在真机上的运行还没有验证。

## 目录

| 目录 | 模块 |
|---|---|
| `app/` | 启动器：游戏页、账户（`Launcher/`、`Account/`、`Artwork/`），宿主与游戏交接（`Host/main.mm`），资源（`Resources/`）。`BazaarPlusPlus.xcodeproj` 由 `tools/generate-xcodeproj.py` 生成 |
| `service/` | Steam 服务：C# 的登录、下载、安装和票据（`Managed/`），它的原生桥与 Keychain（`Native/`），测试（`Tests/`） |
| `runtime/` | 游戏运行时 UnityFramework：Mono 与补丁（`mono/`），IL2CPP 兼容层（`bridge/`），Unity 引擎外壳与设备引擎准备（`engine/`），注入游戏的辅助程序集和入口表（`helper/`） |
| `middleware/` | 按插件 ABI 做的适配：`steamworks/`、`fmod/`、`sqlite/`、`alembic/`、`mac-plugins/`（仅 Simulator） |
| `game/` | 游戏定义 `game.json`：版本、展示信息、Steam App ID、depot、平台准入和托管适配 |
| `tools/` | 构建 App、编译并放入模组（`stage-mod.py`）、打包、在 Simulator 运行、测试通道、回归、设备审计 |

模组的代码在 `../bazaarplusplus-mod/`，iOS host 在它的 `src/BazaarPlusPlus/Host/Ios/`。App 图标用安装器的 `AppIcon.icon`。

各模块的边界和接缝见 [架构](docs/architecture.md)，启动器行为见 [启动器](docs/launcher.md)，运行时见 [静态运行时](docs/static-runtime.md)，真机进度见 [设备准备情况](docs/device-readiness.md)。

## 环境

macOS Apple Silicon、Xcode（含 iOS SDK 和 Simulator runtime）、Unity Editor（版本由 `toolchain.lock.json` 固定，需 iOS 与 Mac Mono Build Support）、Python 3、.NET SDK、CMake，以及 Mono 构建所需的 autoconf、automake、libtool、pkg-config。

## 一次性准备

```sh
python3 runtime/mono/bootstrap.py
bash runtime/mono/build.sh host-aot
bash runtime/mono/build.sh simulator-aot
bash runtime/mono/build.sh device
python3 runtime/engine/export.py simulator
python3 runtime/engine/export.py device
python3 runtime/engine/export.py mac
```

`bootstrap.py` 获取固定版本的 Mono，重放 `runtime/mono/patches/`，并从本机 Unity 恢复 Mono 配置；`--check` 只做校验。`export.py` 从 `runtime/engine/shell/` 导出 Unity 外壳工程，App 的 UnityFramework 从这份导出构建。

构建还需要两样不进 Git 的输入：

- **游戏的 Mac 版本**，放在 `build/steam/download/TheBazaar.app`，版本必须等于 `game/game.json` 的 `testedGameVersion`。可以从本机 Steam 安装复制。构建用它编译辅助程序集、生成 FMOD 适配，并在 Simulator 上重定向 Mac 原生插件。
- **FMOD for Unity 官方包**，放在 `build/vendor/`。包名和 SHA-256 由 `toolchain.lock.json::fmodRequired.unityPackage` 固定。

## 构建与运行

```sh
python3 tools/build.py simulator --test-control
python3 tools/run-app.py
python3 tools/app-control.py snapshot
```

`tools/build.py` 是唯一的构建入口：依次准备中间件、Steam 服务、辅助程序集、模组、corlib AOT 和静态库，构建 UnityFramework，再生成并构建 `app/BazaarPlusPlus.xcodeproj`。产物在 `build/steam-app/<target>/launcher-derived/Build/Products/`。`--test-control` 只用于 Simulator，加入本地测试通道；`app-control.py` 通过它执行登录、恢复、下载、启动等动作，密码只在终端提示输入，不经命令行参数。

设备构建用 `python3 tools/build.py device`，签名团队写在不进 Git 的 `app/Signing.local.xcconfig`。`just ios::ipa` 把设备版打成可侧载的 IPA，文件名带产品版本和游戏 build 号。

## 测试

```sh
just ios::test
just ios::regress
```

单元测试不能代替回归：改动服务、运行时、打包或模组后，用 `--test-control` 构建，再跑 `just ios::regress`。它走一遍恢复登录 → 校验并准备 → 启动游戏，游戏启动任务有失败时退出码非零。

## 分发限制

App 下载游戏代码并解释执行，违反 App Store 审核指南 2.5.2。只能用开发签名、企业签名或侧载分发。游戏文件、凭据和原始运行日志都不进 Git；游戏日志可能含敏感信息，不能直接分享。

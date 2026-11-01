# BazaarPlusPlus for Android

在 Android 上加载 BazaarPlusPlus 模组的 App（`com.bazaarplusplus.android`）。App 不修改、不重新签名官方游戏：它在自己的进程里加载已安装的 The Bazaar（`com.tempogames.thebazaar`），再启动 LemonLoader 和模组。BPP App 本身没有账号；游戏账号由游戏自己登录。决策见 [ADR 0012](../docs/adr/0012-android-app.md)。

当前状态：启动器按 ADR 0012 的三档规则比较已装游戏的游戏代码哈希和平台清单，给出正常模式、兼容模式（需要用户主动启用，上传关闭）、等待适配、请先更新官方游戏、未安装和需要新版 App 等状态。BPP 启动前，App 校验并下载 interop 包（RSA 签名的 interop 声明）和 Managed Payload，暂存到 Data Root 的 `Updates/`，再写启动记录。启动器还能导出去掉账号信息的诊断文件。发布流水线在后续 issue 中接入（#747）。

## 目录

| 目录 | 模块 |
|---|---|
| `app/src/main/java/.../game/` | 找到已安装的游戏、计算游戏代码哈希、建立游戏类加载器、库链接和游戏 Context |
| `app/src/main/java/.../loader/` | App 携带的运行时和 LemonLoader 部署状态 |
| `app/src/main/java/.../launch/` | 启动器界面、启动规则（`LaunchGate`）、启动记录（`LaunchRecord`） |
| `app/src/main/java/.../release/` | 平台清单、预适配记录、interop 声明、下载校验 |
| `app/src/main/java/.../payload/` | Managed Payload 的暂存 |
| `app/src/main/java/.../diag/` | App 日志、诊断导出、账号信息的去除 |
| `app/src/debug/` | 只在 debug 构建里的开发入口（`DevOverrides`）；`app/src/release/` 是它的空实现 |
| `app/src/bridge/` | 桥接 Activity，编译成单独的 `bridge.dex`，由游戏类加载器定义 |
| `app/src/main/res/` | 界面资源；`bpp_tokens.xml` 和 `bpp_mark.xml` 由 `design/build.mjs` 生成 |

## 环境

JDK 21 和 Android SDK，版本由 `toolchain.lock.json` 固定；Gradle 由 wrapper 固定。`ANDROID_HOME` 或 `local.properties` 指向 SDK。准备运行时需要 Node 和 .NET SDK（构建 Updater），以及 game-libs 存储的访问权限。

## 构建与运行

```sh
just android::stage-runtime
just android::install
just android::interop-package
just android::push-interop
```

没有准备运行时的构建只能直接启动游戏。设备上必须先安装官方游戏。`push-interop` 放入的开发 interop 会跳过平台清单。

要在设备上检查各档状态，用本地的发布代替 CDN：

```sh
just android::dev-signing-key
just android::dev-sign-interop build/interop.zip <il2cppSha256> <metadataSha256> "release <version>"
just android::dev-manifest-port 8765
```

本地目录按公开桶 `installer/` 的布局放文件，从 `latest/mod/android-aarch64.json` 开始。`just android::dev-reset` 删除所有开发入口。

## 测试

```sh
just android::check
just android::test
```

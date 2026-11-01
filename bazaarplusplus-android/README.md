# BazaarPlusPlus for Android

在 Android 上加载 BazaarPlusPlus 模组的 App（`com.bazaarplusplus.android`）。App 不修改、不重新签名官方游戏：它在自己的进程里加载已安装的 The Bazaar（`com.tempogames.thebazaar`），再启动 LemonLoader 和模组。BPP App 本身没有账号；游戏账号由游戏自己登录。决策见 [ADR 0012](../docs/adr/0012-android-app.md)。

当前状态：debug App 在 arm64 模拟器上能直接启动官方游戏，也能用 LemonLoader 启动并部署 Updater；App 更新后会重置 LemonLoader 的部署状态。启动规则、兼容模式、下载和发布在后续 issue 中接入（#747）。

## 目录

| 目录 | 模块 |
|---|---|
| `app/src/main/java/.../game/` | 找到已安装的游戏、计算游戏代码哈希、建立游戏类加载器、库链接和游戏 Context |
| `app/src/main/java/.../loader/` | App 携带的运行时和 LemonLoader 部署状态 |
| `app/src/main/java/.../launch/` | 启动器界面 |
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

没有准备运行时的构建只能直接启动游戏。设备上必须先安装官方游戏。

## 测试

```sh
just android::check
just android::test
```

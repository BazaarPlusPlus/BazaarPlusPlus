<div align="center">

<img src="bazaarplusplus-installer/src-tauri/icons/icon.png" alt="BazaarPlusPlus" width="128" height="128" />

# BazaarPlusPlus

**因热爱而生** · 为 [《The Bazaar》](https://www.playthebazaar.com) 打造的游戏内模组：Windows、macOS 与 Android

[English](README_en.md) · [官网](https://bazaarplusplus.com) · [下载](https://bazaarplusplus.com/download) · [使用教程](https://bazaarplusplus.com/tutorial) · [Release Notes](https://github.com/BazaarPlusPlus/BazaarPlusPlus/releases) · [Ko-fi](https://ko-fi.com/cauyxy)

[![Version](https://img.shields.io/badge/version-6.0.0-6dd9a0?style=flat-square)](https://bazaarplusplus.com)
[![License](https://img.shields.io/badge/license-MIT-e8c87a?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Android-c1875a?style=flat-square)](https://bazaarplusplus.com/download)
[![BepInEx](https://img.shields.io/badge/BepInEx-5.x-8a6d3b?style=flat-square)](https://github.com/BepInEx/BepInEx)
[![MelonLoader](https://img.shields.io/badge/MelonLoader-Android-8a6d3b?style=flat-square)](https://github.com/LavaGang/MelonLoader)
[![.NET](https://img.shields.io/badge/.NET-Standard%202.1%20%7C%2010-512bd4?style=flat-square)](https://learn.microsoft.com/dotnet/)
[![Tauri](https://img.shields.io/badge/Tauri-2.x-24c8d8?style=flat-square)](https://tauri.app)
[![React](https://img.shields.io/badge/React-19-61dafb?style=flat-square)](https://react.dev)

</div>

---

BazaarPlusPlus 是一个面向《The Bazaar》的开源项目，不改变游戏平衡，只做信息增强与记录：卡牌图鉴、十胜阵容推荐、对局历史与战斗回放、附魔 / 升级预览、战斗状态栏等。桌面版（Windows、macOS）是 BepInEx 模组加一个桌面安装器；Android 版是一个注入了 BazaarPlusPlus 的游戏 APK，直接安装即可。同仓库里还有上传后端、数据分析器和官网。

普通玩家请直接到 [下载页](https://bazaarplusplus.com/download) 获取安装器或 APK；本仓库面向想了解实现、提交改动或自行构建的开发者。

> [!NOTE]
> 项目代码主要由 [Codex](https://openai.com/codex) 主导，并由 [Claude Code](https://claude.com/product/claude-code) 协作完成。

## 快速开始

### Windows 与 macOS

1. 打开 [bazaarplusplus.com/download](https://bazaarplusplus.com/download)，选择 Windows `.exe` 或 macOS `.dmg`。
2. 关闭游戏后运行安装器；更新时建议先卸载旧版本，再安装新版本。
3. 安装完成后启动《The Bazaar》一次，让 BazaarPlusPlus 完成初始化。
4. 在主菜单确认「卡牌图鉴」按钮出现，且底部版本信息显示 `BPP version` 字样。

从 5.x 升级后，可以在安装器的安装页一键把旧版对局、截图和回放导入 6.0 的新数据目录；导入的对局只在本地显示、不会上传，旧目录保持不变，之后可以在安装器里删除。

### Android

1. 在 [下载页](https://bazaarplusplus.com/download) 的 Android 卡片下载 `.apk`（arm64）。首个 Android 版本发布前，卡片显示为「即将发布」。
2. 先卸载官方版 The Bazaar：Android 不允许用签名不同的安装包覆盖安装。账号与进度保存在服务器上，卸载只会丢失登录状态。
3. 安装下载的 APK，登录后即可使用；BazaarPlusPlus 的设置在游戏自带的「设置」里。
4. 游戏更新后，回到下载页下载新的 APK 直接覆盖安装，不需要再卸载。只有模组更新时，模组会在游戏内自行更新。

详细教程、快捷键和功能说明见 [bazaarplusplus.com/tutorial](https://bazaarplusplus.com/tutorial)。

## 功能矩阵

✅ 支持　◐ 部分支持　— 不支持

### 游戏内

| 功能                   | 说明                                                                                 | Windows | macOS |         Android          |
| ---------------------- | ------------------------------------------------------------------------------------ | :-----: | :---: | :----------------------: |
| **卡牌图鉴**           | 随时查阅物品与技能，按英雄、品质、体型、来源和天数筛选，查看各品质数值与可用附魔     |   ✅    |  ✅   |            ✅            |
| **十胜阵容**           | 对比当前商店、棋盘与仓库，推荐匹配的十胜终局阵容                                     |   ✅    |  ✅   |            ✅            |
| **对局历史**           | 浏览本地对局、PvP 战斗与幽灵对战，查看保存的双方阵容                                 |   ✅    |  ✅   |       ◐ 无英雄立绘       |
| **战斗回放与幽灵对战** | 在历史面板里回放本地保存的战斗，或下载对手上传的幽灵对战                             |   ✅    |  ✅   |            ✅            |
| **收藏与笔记**         | 收藏关键战斗并写笔记，删除对局后收藏仍保留                                           |   ✅    |  ✅   |            ✅            |
| **回放录像**           | 把回放录成视频文件                                                                   |   ✅    |  ✅   |            —             |
| **战斗状态栏**         | 显示战斗时间，支持暂停与 0.5× / 0.67× / 1× 速度                                      |   ✅    |  ✅   |            ✅            |
| **附魔预览**           | 在物品提示中预览附魔后的效果                                                         |   ✅    |  ✅   |            ✅            |
| **升级预览**           | 预览升级后的数值，按住 Shift 时还标出音符落点                                        |   ✅    |  ✅   | ◐ 提示内开关，无音符落点 |
| **详细预览**           | 事件选项与随机结果、任务奖励、聚合物品缺少的类型；桌面另有商人品质分布和英雄升级奖励 |   ✅    |  ✅   |            ◐             |
| **怪物阵容预览**       | 悬停遭遇时查看怪物的棋盘                                                             |   ✅    |  ✅   |            —             |
| **战斗贡献**           | 战斗结束后悬停物品，查看本场的伤害、治疗等贡献                                       |   ✅    |  ✅   |            —             |
| **双语物品名**         | 在提示中同时显示中英文名称                                                           |   ✅    |  ✅   |            —             |
| **外观名称**           | 在外观选择列表的缩略图上显示名称                                                     |   ✅    |  ✅   |            —             |
| **语音字幕**           | 为角色语音显示中英文字幕                                                             |   ✅    |  ✅   |            —             |
| **匿名模式**           | 截图、录制或直播时把本地玩家名显示为 Anonymous                                       |   ✅    |  ✅   |            —             |
| **传奇名次显示**       | 隐藏名次、「战力爆表」或名次与分数双显                                               |   ✅    |  ✅   |            —             |
| **FSR 超分辨率**       | 降低渲染分辨率后用 FSR 1 放大，提升帧率                                              |   ✅    |  ✅   |            —             |
| **中文术语模式**       | BazaarPlusPlus 界面支持简体中文与台湾繁体术语                                        |   ✅    |  ✅   |            ✅            |
| **终局自动截图**       | 结算页动画稳定后自动截图                                                             |   ✅    |  ✅   |            —             |
| **对局上传**           | 后台上传对局与回放数据，用于幽灵对战和英雄统计；只在不处于对局时进行                 |   ✅    |  ✅   |            ✅            |
| **BazaarDB 截图上传**  | 把终局截图提供给 BazaarDB；默认关闭                                                  |   ✅    |  ✅   |            —             |
| **支持者署名**         | 展示支持者名单                                                                       |   ✅    |  ✅   |            ✅            |
| **模组自更新**         | 只有模组变化时在游戏内自行更新；Android 另提示游戏更新后的新 APK                     |   ✅    |  ✅   |            ✅            |

### 安装与周边

| 功能                       | 说明                                                                                        | Windows | macOS  | Android |
| -------------------------- | ------------------------------------------------------------------------------------------- | :-----: | :----: | :-----: |
| **安装方式**               | 桌面安装器自动定位 Steam 版游戏；Android 直接安装 APK                                       | 安装器  | 安装器 |   APK   |
| **修复 / 卸载 / 重置数据** | 处理安装异常，或一键恢复到干净状态                                                          |   ✅    |   ✅   |    —    |
| **5.x 历史导入**           | 升级到 6.0 时导入旧版对局历史，并管理旧数据目录                                             |   ✅    |   ✅   |    —    |
| **对局历史管理**           | 在安装器里查看、定位和清理本地历史与回放视频                                                |   ✅    |   ✅   |    —    |
| **直播叠层**               | 启动本机浏览器源服务，给 OBS 等工具显示对局信息                                             |   ✅    |   ✅   |    —    |
| **安装器自动更新**         | 通过 Tauri Updater 检查并安装新版本                                                         |   ✅    |   ✅   |    —    |
| **英雄统计（官网）**       | [bazaarplusplus.com/heroes](https://bazaarplusplus.com/heroes) 汇总上传数据的英雄胜率与趋势 |   ✅    |   ✅   |   ✅    |

## 仓库结构

| 目录                                 | 内容                                                               |
| ------------------------------------ | ------------------------------------------------------------------ |
| `bazaarplusplus-mod/`                | 游戏内模组：共享 Core、桌面 BepInEx Host、Android MelonLoader Host |
| `bazaarplusplus-installer/`          | 桌面安装器（Tauri 2 + React）                                      |
| `bazaarplusplus-server/`             | Cloudflare Worker：Bundle 上传、Ghost 发现与 BazaarDB 投递         |
| `bazaarplusplus-analyzer/`           | 把 Bundle 汇总成英雄与阵容快照                                     |
| `bazaarplusplus-site/`               | bazaarplusplus.com                                                 |
| `design/`                            | 设计 token、共享样式与品牌规范                                     |
| `VERSION`、`release.mjs`、`release/` | 产品版本与三个平台的发布流程                                       |

每个项目保留自己的工具链，并有各自的 `README.md` 与 `AGENTS.md`；根目录 [`AGENTS.md`](AGENTS.md) 记录跨项目约定和契约归属。

## 从源码构建

按[开发命令](docs/development.md)装好工具链（just、.NET、Node、Rust、Python/uv），然后：

```bash
just setup                  # Shared local config, locked dependencies, Git hooks
just doctor                 # What is still missing on this machine
just mod::fetch macos online  # Game assemblies for the mod (Windows: windows)
just mod::fetch android online  # Interop Snapshot and loader for the Android Host
just                        # Every command, grouped by project
```

mod 对照快照锁 `bazaarplusplus-mod/build/game-libs.lock.json` 指向的游戏程序集编译：`mod::fetch` 接受版本一致的本机 Steam 版《The Bazaar》，否则从私有存储取包，细节见[开发命令](docs/development.md#游戏程序集)。单个项目用 `just <project>::check` 和 `just <project>::test` 验证，`just fmt` 格式化全部项目。`just mod::build` 只编译；把开发版 DLL 部署进游戏要显式运行 `just mod::build --deploy`。Android Host 用 `just mod::android-build` 编译、`just mod::android-deploy` 推到连接的设备，设备冒烟见[开发命令](docs/development.md#android-冒烟)。

正式包（桌面安装器与 Android APK）由 GitHub Actions 的 `release.yml` 在托管 runner 上构建、签名并上传，签名、公证、Android 发布密钥和 R2 凭据只存在于 GitHub secrets 和维护者本机配置，不在公开仓库中；游戏程序集快照、游戏反编译输出、`.env`、`.dev.vars` 同样不在此树中。版本发布流程见[产品发布](docs/release.md)。

## 二次开发须知

> [!IMPORTANT]
> 如果你计划基于本项目或本模组进行二次开发，请务必遵循《The Bazaar》官方 [Mod Policy](https://www.playthebazaar.com/mod-policy)。

## 致谢

|              |                                                                                                                                                                                                                                                                                                                       |
| ------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **灵感来源** | [BazaarHelper](https://github.com/Duangi/BazaarHelper) · [BazaarPlannerMod](https://github.com/oceanseth/BazaarPlannerMod)                                                                                                                                                                                            |
| **数据来源** | [bazaardb.gg](https://bazaardb.gg)                                                                                                                                                                                                                                                                                    |
| **运行依赖** | [BepInEx](https://github.com/BepInEx/BepInEx) · [MelonLoader](https://github.com/LavaGang/MelonLoader) · [Harmony](https://github.com/pardeike/Harmony) · [Tauri](https://tauri.app) · [React](https://react.dev) · [Vite](https://vite.dev) · [Tailwind CSS](https://tailwindcss.com) · [FFmpeg](https://ffmpeg.org) |
| **共创**     | [Codex](https://openai.com/codex) · [Claude Code](https://claude.com/product/claude-code)                                                                                                                                                                                                                             |

## 支持者

感谢所有支持 BazaarPlusPlus 的朋友。完整支持者名单见 [bazaarplusplus.com/support](https://bazaarplusplus.com/support)。

如果你愿意支持项目持续维护，可以前往 [Ko-fi](https://ko-fi.com/cauyxy) 或在安装器内查看赞助方式。

## License

本项目使用 [MIT License](LICENSE)。

---

<div align="center">

<img src="bazaarplusplus-installer/src-tauri/icons/icon.png" alt="" width="48" height="48" />

**BazaarPlusPlus** · 因热爱而生

[官网](https://bazaarplusplus.com) · [下载](https://bazaarplusplus.com/download) · [使用教程](https://bazaarplusplus.com/tutorial) · [Ko-fi](https://ko-fi.com/cauyxy)

<sub>历史归档：[mod](https://github.com/BazaarPlusPlus/bazaarplusplus-mod) · [installer](https://github.com/BazaarPlusPlus/bazaarplusplus-installer) · [server](https://github.com/BazaarPlusPlus/bazaarplusplus-server) · [analyzer](https://github.com/BazaarPlusPlus/bazaarplusplus-analyzer) · [site](https://github.com/BazaarPlusPlus/bazaarplusplus-site)</sub>

</div>

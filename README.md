<div align="center">

<img src="bazaarplusplus-installer/src-tauri/icons/icon.png" alt="BazaarPlusPlus" width="128" height="128" />

# BazaarPlusPlus

**因热爱而生** · 为 [《The Bazaar》](https://www.playthebazaar.com) 打造的 BepInEx 模组与桌面安装器

[English](README_en.md) · [官网](https://bazaarplusplus.com) · [下载](https://bazaarplusplus.com/download) · [使用教程](https://bazaarplusplus.com/tutorial) · [Release Notes](https://github.com/BazaarPlusPlus/BazaarPlusPlus/releases) · [Ko-fi](https://ko-fi.com/cauyxy)

[![Version](https://img.shields.io/badge/version-5.6.0-6dd9a0?style=flat-square)](https://bazaarplusplus.com)
[![License](https://img.shields.io/badge/license-MIT-e8c87a?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS-c1875a?style=flat-square)](https://bazaarplusplus.com/download)
[![BepInEx](https://img.shields.io/badge/BepInEx-5.x-8a6d3b?style=flat-square)](https://github.com/BepInEx/BepInEx)
[![.NET](https://img.shields.io/badge/.NET-Standard%202.1-512bd4?style=flat-square)](https://learn.microsoft.com/dotnet/standard/net-standard)
[![Tauri](https://img.shields.io/badge/Tauri-2.x-24c8d8?style=flat-square)](https://tauri.app)
[![React](https://img.shields.io/badge/React-19-61dafb?style=flat-square)](https://react.dev)

</div>

---

BazaarPlusPlus 是一个面向《The Bazaar》的开源项目：游戏内由 BepInEx 模组提供卡牌图鉴、对局历史与战斗回放，还有 Tooltip 预览、匿名模式和中文术语等功能；桌面安装器负责下载、安装与修复，也管自动更新和直播叠层；同仓库里还有上传后端、指标分析器和官网。

普通玩家建议直接使用 [下载页](https://bazaarplusplus.com/download) 的安装器；本仓库面向想了解实现、提交改动或自行构建的开发者。

> [!NOTE]
> 项目代码主要由 [Codex](https://openai.com/codex) 主导，并由 [Claude Code](https://claude.com/product/claude-code) 协作完成。

## 快速开始

1. 打开 [bazaarplusplus.com/download](https://bazaarplusplus.com/download)，选择 Windows `.exe` 或 macOS `.dmg`。
2. 关闭游戏后运行安装器；更新时建议先卸载旧版本，再安装新版本。
3. 安装完成后启动《The Bazaar》一次，让 BazaarPlusPlus 完成初始化。
4. 在主菜单确认「卡牌图鉴」按钮出现，且底部版本信息显示 `BPP version` 字样。

详细教程、快捷键和功能说明见 [bazaarplusplus.com/tutorial](https://bazaarplusplus.com/tutorial)。

## 功能概览

### 游戏内模组

| 功能                   | 说明                                                                                               |
| ---------------------- | -------------------------------------------------------------------------------------------------- |
| **卡牌图鉴**           | 在游戏中查阅物品和技能，按英雄、品质、体型、商人等维度过滤，还能跟随当前游戏天数查看可获取的内容。 |
| **BazaarDB 自动上传**  | 社区数据共建功能，在结算后于后台上传通关截图与阵容数据；默认关闭，需手动开启。                     |
| **对局历史与战斗回放** | 通过 `F8` 打开历史面板，浏览本地对局与关键战斗，观看战斗回放和幽灵对战。                           |
| **战斗状态栏**         | 显示战斗时间与暂停状态，并提供速度控制，适合复盘、录制和直播。                                     |
| **匿名模式**           | 在截图、录制或直播时隐藏本地玩家名称。                                                             |
| **传奇名次显示**       | 提供「无人知晓」（隐藏名次）、「战力爆表」、名次与分数双显等显示模式。                             |
| **附魔与升级预览**     | 在物品 Tooltip 中直接预览附魔或升级后的效果。                                                      |
| **中文术语模式**       | 支持简体中文、台湾繁体、香港繁体三种术语风格。                                                     |

### 桌面安装器

| 功能                           | 说明                                                    |
| ------------------------------ | ------------------------------------------------------- |
| **跨平台安装**                 | Windows 与 macOS，自动定位 Steam 版《The Bazaar》目录。 |
| **修复 / 卸载 / 重置本地数据** | 处理安装异常、回放数据损坏，或一键恢复到干净状态。      |
| **对局历史管理**               | 查看、定位和清理本地保存的历史记录与回放视频。          |
| **直播模式**                   | 启动本机浏览器源服务，给 OBS 等工具显示对局信息。       |
| **自动更新**                   | 通过 Tauri Updater 检查并提示新版本。                   |

## 仓库结构

| 目录                                 | 内容                                        |
| ------------------------------------ | ------------------------------------------- |
| `bazaarplusplus-mod/`                | BepInEx 模组                                |
| `bazaarplusplus-installer/`          | 桌面安装器（Tauri 2 + React）               |
| `bazaarplusplus-server/`             | Cloudflare Worker：Bundle 上传与 Ghost 发现 |
| `bazaarplusplus-analyzer/`           | 把 Bundle 汇总成英雄与阵容快照              |
| `bazaarplusplus-site/`               | bazaarplusplus.com                          |
| `VERSION`、`release.mjs`、`release/` | 产品版本与发布流程                          |

每个项目保留自己的工具链，并有各自的 `README.md` 与 `AGENTS.md`；根目录 [`AGENTS.md`](AGENTS.md) 记录跨项目约定和契约归属。

## 从源码构建

按[开发命令](docs/development.md)装好工具链（just、.NET、Node、Rust、Python/uv），然后：

```bash
just setup                  # Shared local config, locked dependencies, Git hooks
just doctor                 # What is still missing on this machine
just mod::fetch macos online  # Game assemblies for the mod (Windows: windows)
just                        # Every command, grouped by project
```

mod 对照快照锁 `bazaarplusplus-mod/build/game-libs.lock.json` 指向的游戏程序集编译：`mod::fetch` 接受版本一致的本机 Steam 版《The Bazaar》，否则从私有存储取包，细节见[开发命令](docs/development.md#游戏程序集)。单个项目用 `just <project>::check` 和 `just <project>::test` 验证，`just fmt` 格式化全部项目。`just mod::build` 只编译；把开发版 DLL 部署进游戏要显式运行 `just mod::build --deploy`。

正式包由 GitHub Actions 的 `release.yml` 在托管 runner 上构建、签名并上传，签名、公证和 R2 凭据只存在于 GitHub secrets 和维护者本机配置，不在公开仓库中；游戏程序集快照、游戏反编译输出、`.env`、`.dev.vars` 同样不在此树中。版本发布流程见[产品发布](docs/release.md)。

## 二次开发须知

> [!IMPORTANT]
> 如果你计划基于本项目或本模组进行二次开发，请务必遵循《The Bazaar》官方 [Mod Policy](https://www.playthebazaar.com/mod-policy)。

## 致谢

|              |                                                                                                                                                                                                                                                              |
| ------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **灵感来源** | [BazaarHelper](https://github.com/Duangi/BazaarHelper) · [BazaarPlannerMod](https://github.com/oceanseth/BazaarPlannerMod)                                                                                                                                   |
| **数据来源** | [bazaardb.gg](https://bazaardb.gg)                                                                                                                                                                                                                           |
| **运行依赖** | [BepInEx](https://github.com/BepInEx/BepInEx) · [Harmony](https://github.com/pardeike/Harmony) · [Tauri](https://tauri.app) · [React](https://react.dev) · [Vite](https://vite.dev) · [Tailwind CSS](https://tailwindcss.com) · [FFmpeg](https://ffmpeg.org) |
| **共创**     | [Codex](https://openai.com/codex) · [Claude Code](https://claude.com/product/claude-code)                                                                                                                                                                    |

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

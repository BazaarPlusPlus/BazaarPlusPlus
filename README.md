<div align="center">

# BazaarPlusPlus

**因热爱而生** · 为 [《The Bazaar》](https://www.playthebazaar.com) 打造的 BepInEx 模组与桌面安装器

[English](README_en.md) · [官网](https://bazaarplusplus.com) · [下载](https://bazaarplusplus.com/download) · [使用教程](https://bazaarplusplus.com/tutorial) · [Release Notes](https://github.com/BazaarPlusPlus/BazaarPlusPlus/releases) · [Ko-fi](https://ko-fi.com/cauyxy)

[![Version](https://img.shields.io/badge/version-5.5.0-6dd9a0?style=flat-square)](https://bazaarplusplus.com)
[![License](https://img.shields.io/badge/license-MIT-e8c87a?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS-c1875a?style=flat-square)](https://bazaarplusplus.com/download)
[![BepInEx](https://img.shields.io/badge/BepInEx-5.x-8a6d3b?style=flat-square)](https://github.com/BepInEx/BepInEx)
[![.NET](https://img.shields.io/badge/.NET-Standard%202.1-512bd4?style=flat-square)](https://learn.microsoft.com/dotnet/standard/net-standard)
[![Tauri](https://img.shields.io/badge/Tauri-2.x-24c8d8?style=flat-square)](https://tauri.app)
[![React](https://img.shields.io/badge/React-19-61dafb?style=flat-square)](https://react.dev)

</div>

---

BazaarPlusPlus 是一个面向《The Bazaar》的开源项目：游戏内由 BepInEx 模组提供卡牌图鉴、对局历史、战斗回放、Tooltip 预览、匿名模式、中文术语等功能；桌面安装器负责下载、安装、修复、自动更新和直播叠层；同仓库里还有上传后端、指标分析器和官网。

普通玩家建议直接使用 [下载页](https://bazaarplusplus.com/download) 的安装器；本仓库面向想了解实现、提交改动或自行构建的开发者。

> 项目代码主要由 [Codex](https://openai.com/codex) 主导，并由 [Claude Code](https://claude.com/product/claude-code) 协作完成。

## 快速开始

1. 打开 [bazaarplusplus.com/download](https://bazaarplusplus.com/download)，选择 Windows `.exe` 或 macOS `.dmg`。
2. 关闭游戏后运行安装器；更新时建议先卸载旧版本，再安装新版本。
3. 安装完成后启动《The Bazaar》一次，让 BazaarPlusPlus 完成初始化。
4. 在主菜单确认「卡牌图鉴」按钮出现，且底部版本信息显示 `BPP version` 字样。

详细教程、快捷键和功能说明见 [bazaarplusplus.com/tutorial](https://bazaarplusplus.com/tutorial)。

## 功能概览

### 游戏内模组

- **卡牌图鉴**：在游戏中查阅物品和技能，按英雄、品质、体型、商人等维度过滤，还能跟随当前游戏天数查看可获取的内容。
- **BazaarDB 自动上传**：社区数据共建功能，在结算后于后台上传通关截图与阵容数据；默认关闭，需手动开启。
- **对局历史与战斗回放**：通过 `F8` 打开历史面板，浏览本地对局与关键战斗，观看战斗回放和幽灵对战。
- **战斗状态栏**：显示战斗时间与暂停状态，并提供速度控制，适合复盘、录制和直播。
- **匿名模式**：在截图、录制或直播时隐藏本地玩家名称。
- **传奇名次显示**：提供「无人知晓」（隐藏名次）、「战力爆表」、名次与分数双显等显示模式。
- **附魔与升级预览**：在物品 Tooltip 中直接预览附魔或升级后的效果。
- **中文术语模式**：支持简体中文、台湾繁体、香港繁体三种术语风格。

### 桌面安装器

- **跨平台安装**：Windows 与 macOS，自动定位 Steam 版《The Bazaar》目录。
- **修复 / 卸载 / 重置本地数据**：处理安装异常、回放数据损坏，或一键恢复到干净状态。
- **对局历史管理**：查看、定位和清理本地保存的历史记录与回放视频。
- **直播模式**：启动本机浏览器源服务，给 OBS 等工具显示对局信息。
- **自动更新**：通过 Tauri Updater 检查并提示新版本。

## 仓库结构

```
.
├── JUSTFILE                                 # 统一开发、检查、测试与发布命令
├── VERSION / release.mjs / release/         # 产品版本、发布入口与共享 Payload Inventory
├── bazaarplusplus-mod/                       # BepInEx 模组源码
│   ├── run.sh                                # 常用 build/test/format/decompile 入口
│   └── src/
│       ├── BazaarPlusPlus/                   # 主模组：Game、Patches、Resources、Data
│       ├── BazaarPlusPlus.ModApi/            # 与服务端通信的 API 客户端
│       ├── BazaarPlusPlus.Storage/           # 本地运行日志、截图和 SQLite 存储
│       └── BazaarPlusPlus.Localization/      # 中文术语与本地化引擎
├── bazaarplusplus-installer/                 # 桌面安装器
│   ├── src/                                  # Vite + React 前端
│   ├── src-tauri/                            # Tauri 2 / Rust 后端
│   └── build.sh                              # 本地开发与发布打包入口
├── bazaarplusplus-server/                    # Cloudflare Worker：Bundle 上传与 Ghost 发现
├── bazaarplusplus-analyzer/                  # 把 Bundle 收成 heroes / builds 快照
└── bazaarplusplus-site/                      # bazaarplusplus.com
```

在仓库任意子目录运行 `just` 查看开发、检查、测试和发布命令。底层仍使用各项目的原生工具链，产品发布规则由根目录 `release.mjs` 维护。环境安装与命令范围见 [开发命令](docs/development.md)。每个子目录有自己的 `AGENTS.md`。

## 从源码构建

### 环境要求

- **模组**：.NET SDK 10，以及本机 Steam 版《The Bazaar》（用于解析游戏程序集引用）。
- **统一命令**：[just](https://just.systems/man/en/packages.html)，macOS 可用 `brew install just` 安装。
- **安装器 / 服务端 / 官网**：统一使用 Node `>=24.15.0 <25`，建议 npm `11.17.0`；各目录分别执行 `npm ci`。
- **安装器原生构建**：Rust 工具链、Tauri 系统依赖（见 [Tauri prerequisites](https://tauri.app/start/prerequisites/)）。
- **分析器**：Python 3.14 与 `uv`。
- **Windows**：just 命令在 Git Bash 中执行；原生构建脚本还要求 PowerShell 7.6.0 或更高版本。

### 构建模组

```bash
# Compile without changing the installed game
just mod-build
just mod-test

# Override the game assembly directory
just mod-build "-p:ManagedPath=<Steam>/steamapps/common/The Bazaar/.../Managed"
```

需要把开发 DLL 部署进游戏时，在 `bazaarplusplus-mod` 目录显式运行 `./run.sh build`。

### 构建安装器

```bash
cd bazaarplusplus-installer

npm ci
just installer-dev # Frontend development server
npm run tauri dev  # Full Tauri desktop app

just installer-check
just installer-test
npm run format
```

在根目录执行 `just fmt` 可一次格式化所有项目；`just hooks-install` 安装根目录 `lefthook.yml` 定义的 Git hooks。

```bash
cd bazaarplusplus-server
npm ci
just server-test
# just server-dev requires the project's gitignored .dev.vars
```

```bash
cd bazaarplusplus-analyzer
uv sync --locked
just analyzer-check
just analyzer-test
```

```bash
cd bazaarplusplus-site
npm ci
just site-test
just site-build
```

发布签名、公证（notarization）、R2 上传等流程依赖本地环境变量与 `signing-secrets/`，这些内容不会提交到公开仓库；在缺少本机游戏、签名凭据或平台依赖的环境中，无法完成完整的发布构建。游戏反编译输出、`decompiled/`、`.env` 和 `.dev.vars` 同样不在此树中。

## 产品发布

mod 与 installer 共用根目录 `VERSION`。修改后执行 `just release-sync`；每个平台使用 `just release-build macos`（或 `windows`）准备 Payload 并打包。分别执行 `just release-upload <platform>` 后，只有双平台同版本、同提交的产物齐备，`just release-promote` 才会推进 latest。底层 `node release.mjs …` 保持可用；完整流程、凭据与恢复约定见 [产品发布](docs/release.md)。

## 二次开发须知

如果你计划基于本项目或本模组进行二次开发，请务必遵循《The Bazaar》官方 Mod Policy：

[The Bazaar Mod Policy](https://www.playthebazaar.com/mod-policy)

## 致谢

- **灵感来源**：[BazaarHelper](https://github.com/Duangi/BazaarHelper)、[BazaarPlannerMod](https://github.com/oceanseth/BazaarPlannerMod)
- **数据来源**：[bazaardb.gg](https://bazaardb.gg)
- **运行依赖**：[BepInEx](https://github.com/BepInEx/BepInEx)、[Harmony](https://github.com/pardeike/Harmony)、[Tauri](https://tauri.app)、[React](https://react.dev)、[Vite](https://vite.dev)、[Tailwind CSS](https://tailwindcss.com)、[FFmpeg](https://ffmpeg.org)
- **字体**：[LXGW WenKai](https://github.com/lxgw/LxgwWenKai)（SIL Open Font License 1.1）
- **共创**：[Codex](https://openai.com/codex)、[Claude Code](https://claude.com/product/claude-code)

## 支持者

感谢所有支持 BazaarPlusPlus 的朋友。完整支持者名单见 [bazaarplusplus.com/support](https://bazaarplusplus.com/support)。

如果你愿意支持项目持续维护，可以前往 [Ko-fi](https://ko-fi.com/cauyxy) 或在安装器内查看赞助方式。

## License

本项目使用 [MIT License](LICENSE)。

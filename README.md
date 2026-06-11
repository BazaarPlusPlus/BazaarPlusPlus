<div align="center">

# BazaarPlusPlus

**因热爱而生** · 为 [《The Bazaar》](https://www.playthebazaar.com) 打造的 BepInEx 模组与桌面安装器

[English](README_en.md) · [官网](https://bazaarplusplus.com) · [下载](https://bazaarplusplus.com/download) · [使用教程](https://bazaarplusplus.com/tutorial) · [Release Notes](https://github.com/cauyxy/BazaarPlusPlus/releases) · [Ko-fi](https://ko-fi.com/cauyxy)

[![Version](https://img.shields.io/badge/version-4.2.0-6dd9a0?style=flat-square)](https://bazaarplusplus.com)
[![License](https://img.shields.io/badge/license-MIT-e8c87a?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS-c1875a?style=flat-square)](https://bazaarplusplus.com/download)
[![BepInEx](https://img.shields.io/badge/BepInEx-5.x-8a6d3b?style=flat-square)](https://github.com/BepInEx/BepInEx)
[![.NET](https://img.shields.io/badge/.NET-Standard%202.1-512bd4?style=flat-square)](https://learn.microsoft.com/dotnet/standard/net-standard)
[![Tauri](https://img.shields.io/badge/Tauri-2.x-24c8d8?style=flat-square)](https://tauri.app)
[![React](https://img.shields.io/badge/React-19-61dafb?style=flat-square)](https://react.dev)

</div>

---

BazaarPlusPlus 是一个面向《The Bazaar》的开源项目：游戏内由 BepInEx 模组提供卡牌图鉴、对局历史、战斗回放、Tooltip 预览、匿名模式、中文术语等功能；桌面安装器负责下载、安装、修复、自动更新和直播叠层。

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
├── bazaarplusplus-mod/                       # BepInEx 模组源码
│   ├── run.sh                                # 常用 build/test/format/decompile 入口
│   └── src/
│       ├── BazaarPlusPlus/                   # 主模组：Game、Patches、Resources、Data
│       ├── BazaarPlusPlus.ModApi/            # 与服务端通信的 API 客户端
│       ├── BazaarPlusPlus.Storage/           # 本地运行日志、截图和 SQLite 存储
│       └── BazaarPlusPlus.Localization/      # 中文术语与本地化引擎
└── bazaarplusplus-installer/                 # 桌面安装器
    ├── src/                                  # Vite + React 前端
    │   ├── pages/ features/ layouts/ api/    # 页面、业务状态、壳层和 Tauri 调用
    │   └── types/generated/                  # Rust -> TypeScript 绑定快照
    ├── src-tauri/                            # Tauri 2 / Rust 后端
    │   ├── src/commands/ services/ history/  # 安装、检测、历史、直播服务
    │   └── resources/                        # BepInEx、FFmpeg、直播叠层和安装 payload
    ├── scripts/                              # bindings、manifest、prebuild 脚本
    └── build.sh                              # 本地开发与发布打包入口
```

## 从源码构建

### 环境要求

- **模组**：.NET SDK 8+，以及本机 Steam 版《The Bazaar》（用于解析游戏程序集引用）。
- **安装器**：Node.js 20+、Rust 工具链、Tauri 系统依赖（见 [Tauri prerequisites](https://tauri.app/start/prerequisites/)）。
- **Windows**：构建脚本与开发流程要求 PowerShell 7.6.0 或更高版本。

### 构建模组

```bash
cd bazaarplusplus-mod

# 开发构建：默认会尝试解析本机游戏目录，并把 Debug DLL 拷贝到 BepInEx/plugins
./run.sh build

# 一次性构建 Debug + Release
./run.sh all

# 显式指定游戏程序集目录
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj \
  -c Debug \
  -p:ManagedPath="<Steam>/steamapps/common/The Bazaar/.../Managed"
```

### 构建安装器

```bash
cd bazaarplusplus-installer

npm install
npm run dev        # Vite 前端开发服务
npm run tauri dev  # 启动完整 Tauri 桌面应用

npm run check
npm run test
npm run format

./build.sh --prod  # 本机平台生产打包
```

发布签名、公证（notarization）、R2 上传等流程依赖本地环境变量与 `signing-secrets/`，这些内容不会提交到公开仓库；在缺少本机游戏、签名凭据或平台依赖的环境中，无法完成完整的发布构建。

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

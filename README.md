<div align="center">

# BazaarPlusPlus

**因热爱而生** · 为 [《The Bazaar》](https://www.playthebazaar.com) 打造的 BepInEx 模组与桌面安装器

[English](README_en.md) · [官网](https://bazaarplusplus.com) · [使用教程](https://bazaarplusplus.com/tutorial) · [Ko-fi](https://ko-fi.com/cauyxy)

[![Version](https://img.shields.io/badge/version-3.3.0-6dd9a0?style=flat-square)](https://bazaarplusplus.com)
[![License](https://img.shields.io/badge/license-MIT-e8c87a?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS-c1875a?style=flat-square)](https://bazaarplusplus.com/download)
[![BepInEx](https://img.shields.io/badge/BepInEx-5.x-8a6d3b?style=flat-square)](https://github.com/BepInEx/BepInEx)
[![.NET](https://img.shields.io/badge/.NET-Standard%202.1-512bd4?style=flat-square)](https://learn.microsoft.com/dotnet/standard/net-standard)
[![Tauri](https://img.shields.io/badge/Tauri-2.x-24c8d8?style=flat-square)](https://tauri.app)

</div>

---

BazaarPlusPlus 在《The Bazaar》之上叠加战斗 UI、对手与 tooltip 预览、历史战绩与战斗回放等增强功能；配套的桌面安装器负责自动检测 Steam 安装路径、安装 BepInEx 与模组本体、提供 OBS 直播叠层和自动更新。

> 项目代码主要由 [Codex](https://openai.com/codex) 主导，并由 [Claude Code](https://claude.com/product/claude-code) 协作完成。

## 快速开始

最简单的方式是从 [bazaarplusplus.com/download](https://bazaarplusplus.com/download) 下载桌面安装器：

1. 启动安装器，它会自动检测 Steam 路径下的《The Bazaar》。
2. 点击 **安装**，安装器会自动部署 BepInEx 与 BazaarPlusPlus，并补全 Steam 启动项。
3. 启动游戏即可生效；安装器后续会自动检查更新。

如需手动构建，请参考下方 [从源码构建](#从源码构建)。

## 功能概览

### 模组（`bazaarplusplus-mod`）

- **战斗状态条** —— 在战斗中实时叠加额外的状态信息。
- **Tooltip 增强** —— 合并双 Tooltip，补充附魔文本，并支持升级预览。
- **历史面板与镜像战斗回放（Ghost Battle Replay）** —— 浏览历史对局，并对已保存的战斗进行回放；面板支持中文，并可通过 F8 快捷开关。
- **传奇段位展示自定义** —— 自定义传奇段位下排名信息的显示样式（如隐藏、显示「#名次 | 分数」、或直播友好的占位形式）。
- **快捷键自定义** —— 模组功能可绑定快捷键，支持鼠标按键。
- **随机英雄池过滤** —— 在随机英雄选择中禁用部分英雄。
- **本地化与设置面板** —— 内置简体中文支持以及独立的设置抽屉。

> 各功能的详细使用教程请见 [bazaarplusplus.com/tutorial](https://bazaarplusplus.com/tutorial)。

### 安装器（`bazaarplusplus-installer`）

- **跨平台一键安装** —— Windows 与 macOS 均可自动定位 Steam 游戏目录。
- **修复 / 卸载 / 重置战绩** —— 在出现异常时一键恢复至干净状态。
- **直播模式（OBS 叠层）** —— 在本机启动一个浏览器源服务，便于直播显示对局信息。
- **自动更新** —— 内置 Tauri Updater，启动时检查并提示新版本。
- **中英双语界面** —— 自动跟随系统语言，可手动切换。

## 仓库结构

```
.
├── bazaarplusplus-mod/         # BepInEx 模组本体（C# / .NET Standard 2.1）
│   ├── BazaarPlusPlus.csproj   # 主工程；Debug 直接拷贝到游戏 BepInEx/plugins
│   ├── Plugin.cs               # 模组入口，注册所有 Game 子模块
│   ├── Core/ Infrastructure/   # 配置、日志、运行时基础设施
│   ├── Game/                   # 各功能模块（CombatReplay、HistoryPanel、Tooltips ...）
│   ├── Patches/                # Harmony 补丁
│   └── Models/ Data/           # 数据结构与内嵌资源
└── bazaarplusplus-installer/   # 桌面安装器（Tauri + SvelteKit + TypeScript）
    ├── src/                    # SvelteKit 前端：路由、组件、i18n
    ├── src-tauri/              # Rust 后端：环境检测、文件操作、Tauri 命令
    ├── scripts/                # 构建辅助脚本（生成 bindings、版本同步等）
    └── build.sh                # 一键打包脚本
```

## 从源码构建

### 环境要求

- **模组**：.NET SDK 6+ / 8+，本地拥有 Steam 版《The Bazaar》（用于解析游戏程序集引用）。
- **安装器**：Node.js 20+、Rust 工具链、Tauri 所需的系统依赖（参见 [Tauri prerequisites](https://tauri.app/start/prerequisites/)）。
- **Windows 用户**：构建脚本与开发流程要求 PowerShell 7.6.0 或更高版本。

### 构建模组

```bash
cd bazaarplusplus-mod

# 开发构建：自动拷贝到本机《The Bazaar》的 BepInEx/plugins 目录
dotnet build -c Debug

# 发布构建：打包资源并写入安装器使用的 BepInExSource
dotnet build -c Release

# 一次性构建 Debug + Release
dotnet msbuild -t:BuildAll
```

如果自动检测的游戏路径与本机不符，可显式覆盖 `ManagedPath`：

```bash
dotnet build -c Debug -p:ManagedPath="<Steam>/steamapps/common/The Bazaar/.../Managed"
```

### 构建安装器

```bash
cd bazaarplusplus-installer

npm install
npm run dev        # 在浏览器中启动前端预览（不依赖 Tauri）
npm run tauri dev  # 启动桌面 Tauri 应用

# 检查 / 测试 / 格式化
npm run check
npm run test
npm run format

# 生产打包
./build.sh --prod
```

## 二次开发须知

如果你计划基于本项目或本模组进行二次开发，请务必遵循《The Bazaar》官方 Mod Policy：

[The Bazaar Mod Policy](https://www.playthebazaar.com/mod-policy)

## 致谢

- **灵感来源**：[BazaarHelper](https://github.com/Duangi/BazaarHelper)、[BazaarPlannerMod](https://github.com/oceanseth/BazaarPlannerMod)
- **数据来源**：[bazaardb.gg](https://bazaardb.gg)
- **运行依赖**：[BepInEx](https://github.com/BepInEx/BepInEx)、[Harmony](https://github.com/pardeike/Harmony)、[Tauri](https://tauri.app)、[SvelteKit](https://kit.svelte.dev)
- **共创**：[Codex](https://openai.com/codex)、[Claude Code](https://claude.com/product/claude-code)

## 支持者

感谢所有支持 BazaarPlusPlus 的朋友——这个项目能持续迭代、维护并保持公开发布，离不开你们的帮助与信任。

完整支持者名单：[bazaarplusplus.com/support](https://bazaarplusplus.com/support)

也感谢所有未署名的支持者。如果你愿意支持本项目，可以前往 [Ko-fi](https://ko-fi.com/cauyxy) 或在安装器内查看赞助方式。

## License

本项目使用 [MIT License](LICENSE)。

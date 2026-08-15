# BazaarPlusPlus

面向《The Bazaar》的 BepInEx 5 模组。它不改变游戏平衡，只做信息增强与记录：战斗界面增强、附魔 / 升级预览、run 记录与历史回放、终局自动截图与阵容推荐，以及可选的后台云同步。

## 功能

**对局界面**

| 功能 | 说明 | 操作 |
|---|---|---|
| 战斗状态条 | 底部 HUD 显示逻辑战斗时间、已处理帧数、暂停状态与速度档 | 自动显示 |
| 附魔 / 升级预览 | 预览物品升级 / 附魔后的效果，三档可视性（Off / 智能 / 常显，默认常显） | 按住 Ctrl / Shift 手动覆盖 |
| 卡牌图鉴 | 全屏 Item / Skill 图鉴，支持英雄、品质、体型、来源、天数筛选 | Tab 键或大厅 dock 按钮 |
| 终局阵容面板 | 展示实时 shop / board / stash，并按候选物品推荐匹配的十胜终局 build | 局内 CapsLock 开关 |

**记录与回放**

- 活跃 run 自动写入本地 SQLite；游戏内历史面板可浏览 runs、PVP battles、ghost battles，并预览保存的战斗快照。
- PVP 战斗回放数据本地保存，条件满足时可在历史面板中回放。
- 终局总结页动画稳定后自动截图并保存元数据，之后才放行 `Continue` 按钮。

**云同步（可选，默认按项说明）**

- run / replay 后台上传到 V4 后端，仅在不处于 live run 时执行。
- BazaarDB 截图上传默认关闭；启用后终局截图快照推到 V4 后端，由 BazaarDB 队列拉取。
- Anonymous Mode 可将本地玩家名替换为 `Anonymous`。

**BazaarAgent（外部集成，默认不构建、不安装）**

独立的 host BepInEx 插件，在本地回环 `127.0.0.1:47900` 提供 HTTP 服务：外部工具可读取当前决策上下文并发起动作，浏览器打开 `http://127.0.0.1:47900/` 可实时查看协议活动。mod 本身不做任何策略决策。需要时用 `./run.sh build --with-bazaaragent` 构建；默认构建不产出（并主动清除）host dll。详见 [docs/ARCHITECTURE.md#bazaaragent-optional-host](docs/ARCHITECTURE.md#bazaaragent-optional-host)。

## 安装（玩家）

前提：已安装《The Bazaar》与 [BepInEx 5](https://github.com/BepInEx/BepInEx)。

手动安装：把构建输出中的以下文件复制到游戏目录的 `BepInEx/plugins/`：

- `BazaarPlusPlus.dll`
- `BazaarPlusPlus.ModApi.dll`
- `BazaarPlusPlus.Storage.dll`
- `BazaarPlusPlus.Localization.dll`
- 同目录下的 SQLite 原生运行时依赖

首次运行后，配置文件生成于 `BepInEx/config/BazaarPlusPlus.cfg`。

## 从源码构建（开发者）

一切构建与测试都通过 `./run.sh` 进行（macOS 与 Windows Git Bash 均可用；macOS 下它还负责游戏更新后的 trampoline 修复，不要直接调 `dotnet build`）。运行 `./run.sh` 不带参数可查看全部子命令。

```bash
./run.sh build          # Debug 构建；识别到游戏目录时自动复制到 BepInEx/plugins/
./run.sh test           # 默认离线测试套件（不部署游戏、不下载种子）
./run.sh publish        # 生产发布：刷新远端数据、种子门禁、安装器打包
```

要点：

- 目标框架 `netstandard2.1`（C# 12）。游戏程序集通过 `ManagedPath` 解析，自动识别常见 Steam 安装路径；识别不到时传 `-p:ManagedPath=/path/to/TheBazaar_Data/Managed`。
- 默认本地构建从 `src/BazaarPlusPlus/obj/remote-data/` 嵌入 `voice-lines.json`（缺失时自动获取），并使用仓库内的 `builds.json` 基线。`./run.sh fetch-data` 可手动刷新远端种子；发布流程会在语义门禁通过后才把新种子提升为构建输入。
- 普通 Release 只编译；只有 `./run.sh publish` 会写入相邻 installer 仓库并生成 `BepInEx.zip`。

测试分三条独立的 lane：

| 命令 | 范围 |
|---|---|
| `./run.sh test` | 默认套件：12 个 xUnit 工程，完全离线、无副作用 |
| `./run.sh test-compat` | 兼容性前提测试，需要本机有 Managed / 反编译输入，缺失项会报告跳过 |
| `./run.sh test-corpus <path>` | 可选的 replay 证据语料验收，必须显式提供 corpus |

## 数据与网络行为

- run 记录、战斗回放与终局截图均保存在本地（SQLite、replay payload、截图文件）。
- 云同步不携带任何鉴权凭证，且只在非 live run 状态下执行上传扫描。
- 语音字幕与终局 build 种子由构建管线嵌入，运行时在本地缓存过期后后台刷新。
- 云端后端（上传、ghost battles、replay 链接、BazaarDB 快照投递）在独立仓库 `bazaarplusplus-server`，部署于 `mod-api-v4.bazaarplusplus.com`；mod 侧 HTTP 客户端在 `src/BazaarPlusPlus.ModApi/`。

## 仓库导览

| 路径 | 内容 |
|---|---|
| `src/BazaarPlusPlus/` | 主插件工程。`Plugin.cs` 为 BepInEx 入口，feature wiring 走 `BppComposition.cs` 组合根，其下按 `Core/`、`GameInterop/`、`Game/`、`Patches/`、`Infrastructure/`、`Data/` 分层 |
| `src/BazaarPlusPlus.ModApi/` `…Storage/` `…Localization/` | HTTP 客户端、本地持久化、本地化引擎，三个零 game/Unity/BepInEx 依赖的独立程序集 |
| `src/BazaarPlusPlus.BazaarAgent/` `…BazaarAgentHost/` | 可选的 BazaarAgent 纯核心与 host 插件 |
| `tests/` | 12 个默认 xUnit 测试宿主、兼容性清单、`ScenarioRunner.Tests` 逐子进程执行的场景 capsule、需显式 corpus 的 `CombatImpact.Corpus` 离线验收 |
| `decompiled/` | 游戏 DLL 的 ILSpy 反编译输出，只读参考 |
| `run.sh` | 本地构建、测试、格式化和反编译的统一入口 |

## 文档

文档与代码冲突时，以 `src/BazaarPlusPlus/` 下的实际实现为准。

- [docs/README.md](docs/README.md)：文档索引与生命周期说明
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)：当前实现的 living architecture（按主题组织，带代码证据）
- [CONTEXT.md](CONTEXT.md)：项目术语表
- [docs/adr/](docs/adr/)：设计决策记录
- [GitHub Issues](https://github.com/BazaarPlusPlus/bazaarplusplus-mod/issues)：后续工作、需求与 bug 追踪

## License

MIT License，见 [LICENSE](LICENSE)。

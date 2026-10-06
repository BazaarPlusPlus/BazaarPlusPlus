# BazaarPlusPlus

面向《The Bazaar》的 BepInEx 5 模组。它不改变游戏平衡，只做信息增强与记录：战斗界面增强、附魔 / 升级预览、run 记录与历史回放、终局自动截图与阵容推荐，以及可选的后台云同步。

## 功能

**对局界面**

| 功能 | 说明 | 操作 |
|---|---|---|
| 战斗状态条 | 局内底部显示黄铜铭牌，复用游戏原生贴图与字体，提供逻辑战斗时间、0.5× / 0.67× / 1× 速度和暂停 / 继续 | 自动显示；点击倍速数字循环，点击右侧按钮暂停 / 继续 |
| 附魔 / 升级预览 | 预览物品升级 / 附魔后的效果，三档可视性（Off / 智能 / 常显，默认常显） | 按住 Ctrl / Shift 手动覆盖 |
| 卡牌图鉴 | 全屏 Item / Skill 图鉴，支持英雄、品质、体型、来源、天数筛选 | Tab 键或大厅 dock 按钮 |
| 终局阵容面板 | 展示实时 shop / board / stash，并按候选物品推荐匹配的十胜终局 build | 局内 CapsLock 开关 |

**记录与回放**

- 活跃 run 自动写入本地 SQLite；游戏内历史面板可浏览 runs、PVP battles、ghost battles，并预览保存的战斗快照。
- PVP 战斗回放数据本地保存，条件满足时可在历史面板中回放。
- 终局总结页动画稳定后自动截图并保存元数据，之后才放行 `Continue` 按钮。

**云同步（可选，默认按项说明）**

- run / replay 后台上传到 V5 后端，仅在不处于 live run 时执行。
- BazaarDB 截图上传默认关闭；启用后终局截图快照推到 V5 后端，由 BazaarDB 队列拉取。
- Anonymous Mode 可将本地玩家名替换为 `Anonymous`。

## 安装（玩家）

用[安装器](https://bazaarplusplus.com/download)安装：mod 的 DLL、依赖和原生组件由仓库根目录的 Payload Inventory 定义，手动复制容易漏。首次运行后，配置文件生成于 `BepInEx/config/BazaarPlusPlus.cfg`。

## 从源码构建（开发者）

在仓库根目录用 `just mod::<命令>` 构建和测试，`just --list mod` 列出全部命令；环境要求见[开发命令](../docs/development.md)。`just mod::build` 只编译，部署进游戏用 `just mod::build --deploy`（它会在游戏更新后修复 macOS trampoline，`dotnet build` 不做这一步）。游戏程序集通过 `ManagedPath` 从快照锁 `build/game-libs.lock.json` 解析（词条见根 `CONTEXT.md` 的 Snapshot Lock）：先执行 `just mod::fetch macos online`（Windows 用 `windows`），它接受 `globalgamemanagers` 版本串与锁条目一致的本机 Steam 安装，否则从私有存储取包到 `game-libs/`；两者都不满足时报错并列出两个版本串，切换 Steam 分支或等锁更新。显式 `-p:ManagedPath=/path/to/Managed` 覆盖解析，但 `mod::check` 仍按锁条目核对该目录的 sha256，`release::prepare` 拒绝不对应任何锁条目的目录。

## 数据与网络行为

- run 记录、战斗回放与终局截图均保存在本地（SQLite、replay payload、截图文件）。
- 云同步不携带任何鉴权凭证，且只在非 live run 状态下执行上传扫描。
- 语音字幕与终局 build 种子由构建管线嵌入，运行时在本地缓存过期后后台刷新。
- 云端后端（上传、ghost battles、BazaarDB 快照投递）在 monorepo 的 `bazaarplusplus-server/`；mod 侧 HTTP 客户端在 `src/BazaarPlusPlus.ModApi/`。

## 文档

文档与代码冲突时，以实际实现为准。从 [docs/README.md](docs/README.md) 的文档索引开始；后续工作、需求与 bug 在 [GitHub Issues](https://github.com/BazaarPlusPlus/BazaarPlusPlus/issues)。

## License

MIT License，见 [LICENSE](LICENSE)。

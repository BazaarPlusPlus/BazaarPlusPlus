# 开发命令

根目录 `JUSTFILE` 是开发、验证和产品发布的统一入口，只转发给各项目已有的工具链和脚本。在仓库任意目录执行 `just` 按项目分组列出命令，不会构建或发布。本文只记录命令列表和 `--help` 说不出来的约定。

## 新 clone 与本地配置

先装好下文的语言工具链，再在仓库根目录执行：

```bash
just setup
just doctor
```

配置文件、分节、取值写法和 `setup --from <旧 checkout>` 的导入规则都由 `node scripts/workspace.mjs --help` 说明。它说不出来的约定：

- 配置目录（默认 `~/.config/bazaarplusplus`）由同一台机器的所有 clone 共用，必须放在任何 checkout 之外。新机器要单独恢复这个私有目录，以及游戏、平台工具链和系统证书；Git clone 不带这些东西。
- 只改 `config.ini`。server 的 `.dev.vars` 和 analyzer 的 `.env` 是它的受管理副本；改完配置运行 `just setup --skip-deps`，或者直接用会自动刷新副本的 `just server::dev`、`just analyzer::cli <command>`。直接跑 npm/uv 命令读到的是当前副本，不会刷新。
- 发布凭据按用途分开保存，每个 bucket 各用一套，不用一把密钥覆盖所有权限。
- `doctor` 只是清单：退出成功不代表远端权限、签名密码或服务可用。

### 游戏程序集

mod 对照快照锁 `bazaarplusplus-mod/build/game-libs.lock.json` 指向的 Game Assembly Snapshot 编译（词条见根 `CONTEXT.md`，决定见 [ADR 0004](adr/0004-pinned-game-assembly-snapshots.md)），不再探测本机 Steam 路径。新 clone 在 `just setup` 之后执行一次：

```bash
just mod::fetch macos online      # Windows 用 windows
```

它按本平台的 online 锁条目解析 Managed 目录并打印出来：本机 Steam 安装的 `globalgamemanagers` 版本串与条目一致、Managed 目录 sha256 也一致时直接采用；否则从私有存储取包到 `bazaarplusplus-mod/game-libs/`，这一步需要 `[release]` 凭据，写作 `just with-config release just mod::fetch macos online`。两边都不满足时报错并列出锁和本机的两个版本串：切换 Steam 分支、等锁更新，或显式传 `-p:ManagedPath=...`。没有凭据的外部贡献者只能对着 online 构建；staging 和 ptr 条目只要求云端能取到。

它说不出来的约定：

- 显式 `-p:ManagedPath`（或 `config.ini` `[machine]` 的 `BPP_MANAGED_PATH`）绕过锁解析，但只是换一种取包方式，不是换一套程序集：`mod::check` 里的 `lock-check` 对它解析到的目录核对，版本串被某个锁条目记录而 sha256 不一致即失败，不被任何条目记录只告警；`release::prepare` 则直接拒绝不对应任何锁条目的目录。
- 锁条目为空时 `mod::check` 和 `release::check` 只告警；本平台 online 条目仍为空时，Steam 当前挂载渠道的条目可以满足 online 构建，这条过渡规则写在 `build/ManagedPath.props` 的注释里，online 条目填上后随注释一起删除。
- 锁只通过 PR 推进：在挂了对应 Steam 分支的机器上 `just mod::snapshot`，再 `just mod::publish <platform> <channel>` 上传私有存储，然后提交锁文件。游戏更新后本机 Steam 与锁不一致，锁推进前无法构建，这是接受的代价。

## 环境与依赖

根发布工具和各项目各自保留依赖与锁文件，不用 npm workspaces。

| 范围 | 版本来源 | 安装依赖 |
| --- | --- | --- |
| just | 本仓库用 `just 1.58.0` 验证 | macOS `brew install just`；Windows `winget install --id Casey.Just --exact` |
| 根发布工具 | `.nvmrc`、根 `package.json` 的 `packageManager` | 根目录 `npm ci` |
| installer、site、server | 各自 `package.json` 的 `engines` 与 `packageManager` | 各目录 `npm ci`；site 另需 `npx playwright install chromium`（只有 `site::e2e` 需要） |
| mod | `bazaarplusplus-mod/global.json`；快照锁指向的游戏程序集，见[游戏程序集](#游戏程序集) | .NET restore |
| installer Rust | `bazaarplusplus-installer/rust-toolchain.toml`；[Tauri 系统依赖](https://tauri.app/start/prerequisites/) | locked Cargo |
| analyzer | `bazaarplusplus-analyzer/.python-version`、uv | analyzer 目录 `uv sync --locked` |

Windows 在 Git Bash 中执行 just，`bash`、`just` 和语言工具链都要在 PATH 上。`JUSTFILE` 的 shell 保持 Bash：参数转发依赖 Bash 的位置参数。Windows 原生构建另需 PowerShell 7.6.0 或更高版本；正式包的平台工具链和签名材料见[产品发布](release.md)。

## 验证

命令写作 `just <project>::<recipe>`（也可写 `just <project> <recipe>`）。recipe 在项目目录中执行，所以相对路径参数按项目目录解析。只改一个项目时用该项目的 `check` 和 `test`；`just check`、`just test` 覆盖全仓库，遇到首个失败即停止。

- 全仓库门禁需要所有项目的工具链，mod 还需要快照锁能解析的游戏程序集；缺依赖时的失败不能当作通过。
- `installer::check` 已包含 installer 的测试，先跑 `check` 再跑 `test` 会把它们跑两遍。
- `mod::build` 只编译；部署进游戏要显式执行 `just mod::build --deploy`。含空格的参数整体加引号：

```bash
just mod::build "-p:ManagedPath=/absolute/path/The Bazaar/Managed"
```

## 依赖更新与云端检查

Dependabot 更新配置在 `.github/dependabot.yml`，普通版本更新的分组和节奏由配置维护，安全告警继续保留。自动合并的资格和必需检查由 `.github/scripts/dependabot-auto-merge.mjs` 维护：仅接受可信机器人提交、限定目录中的稳定补丁更新，新增依赖或其他变更留给人工审查。TypeScript 与类型感知 lint 工具的大版本需要配套升级；Tauri 的 npm 与 Cargo 更新也必须一起验证安装器。

Mod 的自动更新只开放测试工具白名单。编译期依赖同样可能改变游戏内行为，不能因为 `PrivateAssets`、补丁版本或 NuGet 版本号相同就认为兼容。游戏自带 DLL、生成器、publicizer 和随包运行库的维护遵循 [ADR-0010](../bazaarplusplus-mod/docs/adr/0010-compile-against-game-supplied-libraries.md)。机器人 PR 的实际差异还会经过 `.github/scripts/check_mod_dependency_update.py`：允许测试工具版本修改，但生产锁文件或其他 Mod 文件变化必须转人工维护。

云端检查的覆盖范围以 `.github/workflows/` 的 job 名称和命令为准。site 的部署触发方式和凭据位置见 `bazaarplusplus-site/README.md` 的 Deploy 一节。Mod 的云端 lane 在 macOS 和 Windows 上按 [ADR 0004](adr/0004-pinned-game-assembly-snapshots.md) 从私有存储取 online 快照跑 `mod::check` 与 `mod::test`，再对 staging 和 ptr 快照做 CompatCheck 编译；它不验证 Unity/Mono 加载，Ghost 响应契约的消费方检查在 `mod::test` 中。installer 在 Windows 和 macOS 运行完整源码门禁，但不替代安装包签名、安装与升级验收。涉及云端未覆盖的范围时，合并前仍须提供相应项目的本地门禁结果。Mod 运行时依赖升级还需对快照锁的每个已采集条目编译（`just mod::matrix`），并验证实际启动与受影响功能；通过编译和 .NET 测试不能替代这一步。

游戏程序集快照只存在于私有存储，不进公开仓库、不进 Actions cache。依赖游戏程序集的 job 按快照锁取包，凭据是仓库级 secret `BPP_GAME_LIBS_R2_*`（仅限 `bazaarplusplus-game-libs` bucket 的只读令牌）；fork PR 拿不到 secrets，这类 job 在 fork 上跳过而不是失败，仓库内分支的 PR 才运行完整矩阵（[ADR 0004](adr/0004-pinned-game-assembly-snapshots.md)）。Dependabot 的 PR 读取的是 Dependabot secrets，`BPP_GAME_LIBS_R2_*` 未同时注册在那里时该 lane 同样跳过。被跳过的 job 不等于通过：GitHub 把被跳过的必需检查算作通过，lane 是否真的执行以它的 job summary 为准；来自 fork 的改动合并前仍要有本地 `mod::check` 与 `mod::test` 结果。

配置静态检查不能证明机器人已经成功更新锁文件；首次启用及工具链升级后需查看 Dependabot 的实际更新日志，尤其是它的包管理器支持范围尚未覆盖仓库所用版本时。新的 CI 检查需在 GitHub 首轮成功后再设为必需检查。

自动合并工作流只执行目标分支的可信脚本，通过 API 读取 PR 数据，不执行 PR 代码。开启仓库 auto-merge 和 Actions 审批权限后，还须把脚本列出的检查设为来自 GitHub Actions 的严格必需检查，并保留至少一人审批及新提交撤销旧审批；缺少任一条件时不会自动批准。启用后仍使用 GitHub 原生 auto-merge 等待检查，不使用管理员绕过。首次配置完分支规则后，可重跑符合条件的机器人工作流以重新判断资格。

组织策略禁止 Actions 审批时，符合条件的 PR 仍可登记 auto-merge，但保留人工 Approve；组织管理员放开该权限后才可自动审批。两种情况均不取消审批或 CI 要求。

## Git hooks

根目录 `lefthook.yml` 是唯一的 hook 配置，按改动路径选择各项目的 recipe。先在根目录执行 `npm ci`，再执行 `just hooks-install`。它把仓库级 `core.hooksPath` 指向 git common dir 下的 `hooks`，让 linked worktree 共用同一套 hooks，并覆盖全局 hooks 路径。

## 产品发布

发布命令、顺序、凭据和恢复见[产品发布](release.md)。发布命令不是 `check`、`test` 或 `mod::build` 的依赖。

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

## 环境与依赖

根发布工具和各项目各自保留依赖与锁文件，不用 npm workspaces。

| 范围 | 版本来源 | 安装依赖 |
| --- | --- | --- |
| just | 本仓库用 `just 1.58.0` 验证 | macOS `brew install just`；Windows `winget install --id Casey.Just --exact` |
| 根发布工具 | `.nvmrc`、根 `package.json` 的 `packageManager` | 根目录 `npm ci` |
| installer、site、server | 各自 `package.json` 的 `engines` 与 `packageManager` | 各目录 `npm ci` |
| mod | `bazaarplusplus-mod/global.json`；本机 Steam 版《The Bazaar》的 Managed 程序集 | .NET restore |
| installer Rust | `bazaarplusplus-installer/rust-toolchain.toml`；[Tauri 系统依赖](https://tauri.app/start/prerequisites/) | locked Cargo |
| analyzer | `bazaarplusplus-analyzer/.python-version`、uv | analyzer 目录 `uv sync --locked` |

Windows 在 Git Bash 中执行 just，`bash`、`just` 和语言工具链都要在 PATH 上。`JUSTFILE` 的 shell 保持 Bash：参数转发依赖 Bash 的位置参数。Windows 原生构建另需 PowerShell 7.6.0 或更高版本；正式包的平台工具链和签名材料见[产品发布](release.md)。

## 验证

命令写作 `just <project>::<recipe>`（也可写 `just <project> <recipe>`）。recipe 在项目目录中执行，所以相对路径参数按项目目录解析。只改一个项目时用该项目的 `check` 和 `test`；`just check`、`just test` 覆盖全仓库，遇到首个失败即停止。

- 全仓库门禁需要所有项目的工具链，mod 还需要游戏程序集；缺依赖时的失败不能当作通过。`server::test` 会先构建 mod 的 `ModApi.Tests`，同样需要游戏程序集。
- `installer::check` 已包含 installer 的测试，先跑 `check` 再跑 `test` 会把它们跑两遍。
- `mod::build` 只编译；部署进游戏要显式执行 `just mod::build --deploy`。含空格的参数整体加引号：

```bash
just mod::build "-p:ManagedPath=/absolute/path/The Bazaar/Managed"
```

## Git hooks

根目录 `lefthook.yml` 是唯一的 hook 配置，按改动路径选择各项目的 recipe。先在根目录执行 `npm ci`，再执行 `just hooks-install`。它把仓库级 `core.hooksPath` 指向 git common dir 下的 `hooks`，让 linked worktree 共用同一套 hooks，并覆盖全局 hooks 路径。

## 产品发布

发布命令、顺序、凭据和恢复见[产品发布](release.md)。发布命令不是 `check`、`test` 或 `mod::build` 的依赖。

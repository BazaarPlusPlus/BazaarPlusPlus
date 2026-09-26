# 开发命令

根目录 `JUSTFILE` 是日常开发、验证和产品发布的统一入口。它调用各项目已有的工具链和脚本；产品版本、Payload 准备、构建锁与远端发布规则由 `release.mjs` 及其发布模块维护。

## 新 clone 与本地配置

安装下文的语言工具链后，在仓库根目录执行：

```bash
just setup
just doctor
```

`setup` 安装各项目锁定的依赖、mod 的本地 .NET 工具及 Git hooks。同一台机器的 clone 共用仓库外的 `~/.config/bazaarplusplus`，也可用绝对路径 `BPP_CONFIG_HOME` 指定另一套配置；不允许把配置目录放进来源或目标 checkout。文件名、作用域和全部选项由 `node scripts/workspace.mjs --help` 维护。首次使用会创建空白模板，已有配置不覆盖。新机器需要另外恢复该私有目录，以及安装游戏、平台工具链和系统证书；Git clone 不携带这些材料。

从旧 clone 接入配置时，首次 setup 使用 `just setup --from /absolute/path/to/old-checkout`。它只导入已知的 analyzer、server 和 installer 签名配置，导入前检查冲突，保留旧文件；相对数据目录保留原位置含义，Apple 私钥引用改指向集中目录。它不复制依赖缓存、构建产物、游戏反编译结果或 analyzer 数据，也不创建空数据目录冒充恢复完成。额外的发布凭据应按用途分别保存，不用一套密钥替代所有 bucket 的权限。

集中目录是本地配置的维护入口。analyzer 和 Wrangler 要求的项目文件是受管理副本，根目录忽略的 `.bpp-local.json` 记录上次同步摘要。编辑集中配置后运行 `just setup --skip-deps`；`just server::dev` 和 `just analyzer::cli <command>` 也会在启动前刷新副本。若项目副本被手工修改，先合并要保留的修改并使两份内容一致，再运行 setup；脚本会拒绝直接覆盖冲突。直接执行原来的 npm/uv 命令读取当前副本，绕过自动刷新。

`just release::build` 按签名作用域接入集中目录；`upload` / `mirror` / `promote` 只加载 R2 发布配置。已有显式环境变量优先。其他命令可用 `just with-config <profile> <command...>`，例如非标准游戏位置的 `just with-config mod just mod::build`，或 `just with-config cloudflare npm --prefix bazaarplusplus-site run deploy`（仅在明确要部署时执行）。配置按数据解析，不执行 shell 语句；普通检查、测试、官网开发不会自动加载发布密钥。

`doctor` 只读盘点工具、依赖、配置完整度、数据路径、游戏程序集和本机签名身份，输出缺项但不打印密钥。它是清单命令，退出成功不表示所有能力可用，也不代表验证过远端权限、签名密码或服务健康。macOS Keychain、GitHub/Wrangler 登录态继续由各自工具管理；setup 不导出证书、不轮换 token、不修改线上 secret，也不启动分析或发布。POSIX 配置目录和文件分别收紧为 700/600；Windows 需通过用户目录 ACL 控制访问。

## 环境与依赖

安装 [just](https://just.systems/man/en/packages.html)，本仓库使用 `just 1.58.0` 验证。macOS 可运行 `brew install just`，Windows 可运行 `winget install --id Casey.Just --exact`。Windows 的命令在 Git Bash 中执行，`bash`、`just` 和对应语言工具链都需要在 PATH 中；不要将 `JUSTFILE` 的 shell 换成 PowerShell，因为参数转发使用 Bash 的位置参数。

根发布工具与各项目分别保留自己的依赖与锁文件，不使用 npm workspaces，也不需要 Turborepo：

| 范围 | 环境 | 安装依赖 |
| --- | --- | --- |
| 根发布工具与命令门禁 | Node 版本见根目录 `.nvmrc`，npm 版本见根 `package.json` 的 `packageManager` | 根目录执行 `npm ci` |
| Node 项目 | 遵循各项目 `package.json` 的 `engines` 与 `packageManager` | 在 installer、site、server 各自目录执行 `npm ci` |
| mod | .NET SDK，版本策略见 `bazaarplusplus-mod/global.json`；本机游戏 Managed 程序集 | 构建或测试时由 .NET restore 恢复 |
| installer Rust | `bazaarplusplus-installer/rust-toolchain.toml` 指定的工具链、Tauri 系统依赖 | 已有验证脚本使用 locked Cargo 依赖 |
| analyzer | Python 版本见 `bazaarplusplus-analyzer/.python-version`、uv | 在 analyzer 目录执行 `uv sync --locked` |

Windows 原生构建还需项目要求的 PowerShell 7.6.0+ 等工具；正式包另需平台工具链和签名材料，见[产品发布](release.md)。setup 不自动安装系统工具链、合并锁文件或加载根目录 `.env`。各项目继续按自己的配置规则读取环境；不要把发布凭据写进 `JUSTFILE`。

## 日常入口

在仓库根目录或任意子目录执行 `just` 都会按项目分组列出可用命令，不会开始构建或发布。每个项目是一个 just module（`bazaarplusplus-mod/mod.just` 等），命令写作 `just <project>::<recipe>`，也可以写成 `just <project> <recipe>`；recipe 在该项目目录中执行，所以相对路径参数按项目目录解析。只改一个项目时，优先使用该项目的命令：

```bash
just
just site::dev
just site::check
just site::test
just site::build
just installer::dev
just installer::check
just server::check
just analyzer::test
just mod::build
```

- `check` / `test`：顺序运行全仓库检查或测试，首个失败立即停止，保留失败退出码。需要所有项目的工具链；mod 编译和测试还需要游戏程序集，不能在缺失依赖时当作通过。
- `commands-check`：检查 `JUSTFILE` 格式及根 npm 配置、`release.mjs`、`release/`、`scripts/` 的 Prettier 格式（使用根目录依赖与 `.prettierrc.json`，只需先在根目录执行 `npm ci`），并在临时目录中用工具替身验证命令路由、工作目录、参数转发和失败传播，不运行真实发布。
- `release::test`：使用根目录 Vitest 运行 `release/` 的发布测试，纳入 `just test`；程序集版本集成测试需要 .NET SDK，不需要游戏 Managed 程序集或 R2 凭据。
- `fmt`：就地格式化所有项目（含 analyzer 的 Ruff 安全修复），最后执行 `release::sync`。
- `mod::fmt-check`：用仓库锁定版本的 CSharpier 检查 C# 格式，属于 `check`。
- `installer::check`：调用现有 `verify -- --source-only` 和文档检查，包含格式检查、oxlint、Clippy、Rust/JavaScript 测试、绑定生成、类型检查、Rust 文档与前端构建。因此先运行 `check` 再运行 `test` 会重复 installer 测试。
- `site::check`：类型、lint、格式检查及生产构建；`server::check`：Worker 的现有检查；`analyzer::check`：Ruff 格式、lint 和 ty 检查。三个项目均有对应的 `test`。
- `installer::dev`：仅启动前端开发服务。完整桌面开发执行 `just installer::app`。`server::dev` 需要 server 自己的 `.dev.vars`。

## Git hooks

验证在本地完成：lefthook hooks 按改动范围执行门禁，`just check` / `just test` 执行全仓库检查与测试。

根目录 `lefthook.yml` 是唯一的 hook 配置，执行 `just hooks-install` 安装（需先在根目录执行 `npm ci`）。两个 hook 都按 `lefthook.yml` 的项目与共享输入 glob 选择门禁：pre-commit 调用各项目 `check`，installer 使用 `check-fast`（格式、lint、类型与文档检查）；pre-push 调用所有受影响项目的 `test`，并对 installer 运行完整 `check`。检查内容只在项目 just 模块及其底层验证脚本维护，hook 不重写工具命令。

`hooks-install` 将 repository-local `core.hooksPath` 设为 `git rev-parse --git-common-dir` 下 `hooks` 的绝对路径，使 linked worktrees 共用 hooks，并覆盖全局 hooks 路径，避免 lefthook 拒绝安装或将仓库 hooks 写入共享目录。脚本先检查目标路径，再核验 Git 的有效路径；解析符号链接后超出该 git 目录或路径被覆盖时拒绝安装，验证通过才调用根依赖中的 `lefthook install --force`。重复执行保持相同配置与 hooks，不修改 global / system Git 配置。

全仓库执行：

```bash
just check
just test
```

这些命令不签名、不上传、不推进 latest、不运行 analyzer 的数据发布，也不修改游戏安装；它们可以恢复依赖、生成绑定、写入本地构建产物和测试缓存，并不承诺完全离线。`mod::build` 只编译。需要在游戏内调试部署时，显式执行 `just mod::build --deploy`。

mod 命令允许转发已有脚本支持的选项。含空格的完整参数必须引用：

```bash
just mod::build --fast
just mod::build "-p:ManagedPath=/absolute/path/The Bazaar/Managed"
just mod::test "-p:ManagedPath=/absolute/path/The Bazaar/Managed"
```

## 产品发布

```bash
just release::sync
just release::check
just release::prepare macos
just release::build macos
just release::upload macos
just release::mirror macos <大陆分享页地址>
just release::verify-mirror
just release::promote
just release::promote --platform macos
```

`prepare`、`build`、`upload` 的平台必须显式指定为 `macos` 或 `windows`，并在对应原生构建机上操作；`mirror`、`verify-mirror` 和 `promote` 可以在任一台机器执行。`release::build` 内部包含 Payload 准备、验证、签名和打包，不会上传；`release::upload` 和 `release::mirror` 不推进任何 manifest；只有 `release::promote` 发布。各命令的参数以 `node release.mjs --help` 为准，顺序和规则见[产品发布](release.md)。它们不是普通 `check`、`test` 或 `mod::build` 的依赖。

`release::prepare` 和 `release::build` 的平台参数后可直接传递 `ManagedPath`，just 会替调用者补上 Node CLI 的 `--` 分隔符：

```bash
just release::build macos "-p:ManagedPath=/absolute/path/The Bazaar/Managed"
```

底层 `node release.mjs …` 保持可用。发布顺序、凭据和恢复要求以[产品发布](release.md)为准；just 不缓存或跳过任何发布检查。

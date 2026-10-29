# 开发命令

根目录 `JUSTFILE` 是开发、验证和产品发布的统一入口，只转发给各项目已有的工具链和脚本。在仓库任意目录执行 `just` 按项目分组列出命令，不会构建或发布。本文只记录命令列表和 `--help` 说不出来的约定。

## 新 clone 与本地配置

先装好下文的语言工具链，再在仓库根目录执行：

```bash
just setup
just doctor
```

配置文件、分节和取值写法都由 `node scripts/workspace.mjs --help` 说明。它说不出来的约定：

- 配置目录（默认 `~/.config/bazaarplusplus`）由同一台机器的所有 clone 共用，必须放在任何 checkout 之外。新机器要单独恢复这个私有目录，以及游戏、平台工具链和系统证书；Git clone 不带这些东西。
- 只改 `config.ini`。server 的 `.dev.vars` 和 analyzer 的 `.env` 是它的受管理副本；改完配置运行 `just setup --skip-deps`，或者直接用会自动刷新副本的 `just server::dev`、`just analyzer::cli <command>`。直接跑 npm/uv 命令读到的是当前副本，不会刷新。
- Cloudflare 只有两把 token。操作员 token（`[cloudflare] CLOUDFLARE_API_TOKEN`，R2 读写所有桶，加 Wrangler 部署需要的权限）是唯一能写的凭据：本机发版、部署都用它，GitHub 上它是一个仓库级 secret，`release.yml`、`analyzer.yml` 和 `deploy-site.yml` 共用。CI 只读 token（仅读 `bazaarplusplus-game-libs`，`[cloudflare] BPP_GAME_LIBS_TOKEN`，GitHub 仓库 secret 和 Dependabot secret `BPP_GAME_LIBS_TOKEN`）单独存在，是因为 Dependabot PR 会在 CI 里执行依赖更新带进来的代码，那里只能拿到只读凭据。R2 的 S3 密钥对不单独保存：Access Key ID 是 token 的 id，Secret Access Key 是 token 值的 SHA-256，`release/r2-store.mjs` 与 analyzer 的 `object_store.py` 自己推导。账号 ID 不是 secret，本地唯一来源是 `[cloudflare] CLOUDFLARE_ACCOUNT_ID`。
- GitHub secrets 和变量按 `release/github-secrets.json` 同步：`just secrets-check` 只列名字，`just secrets-sync` 把本地有值的项通过 `gh` 写上去（值走 stdin，不打印）；人工步骤只剩造 token 和导出证书，见[产品发布](release.md#github-actions-发版)。
- `doctor` 只是清单：退出成功不代表远端权限、签名密码或服务可用。

### 游戏程序集

桌面 mod 对照快照锁 `bazaarplusplus-mod/build/game-libs.lock.json` 指向的 Game Assembly Snapshot 编译（词条见根 `GLOSSARY.md`，决定见 [ADR 0004](adr/0004-pinned-game-assembly-snapshots.md)）。`bazaarplusplus-mod/build/ManagedPath.props` 优先采用已下载的锁定快照，也可复用版本与锁条目一致的本机 Steam 安装。新 clone 在 `just setup` 之后执行一次：

```bash
just mod::fetch macos online      # Windows 用 windows
```

它按本平台的 online 锁条目解析 Managed 目录并打印出来：本机 Steam 安装的 `globalgamemanagers` 版本串与条目一致、Managed 目录 sha256 也一致时直接采用；否则从私有存储取包到 `bazaarplusplus-mod/game-libs/`，这一步需要 `[cloudflare]` 的操作员 token（`release` profile），写作 `just with-config release just mod::fetch macos online`。两边都不满足时报错并列出锁和本机的两个版本串：切换 Steam 分支、等锁更新，或显式传 `-p:ManagedPath=...`。没有凭据的外部贡献者只能对着 online 构建；staging 和 ptr 条目只要求云端能取到。

它说不出来的约定：

- 显式 `-p:ManagedPath`（或 `config.ini` `[machine]` 的 `BPP_MANAGED_PATH`）绕过锁解析，但只是换一种取包方式，不是换一套程序集：`mod::check` 里的 `lock-check` 对它解析到的目录核对，版本串被某个锁条目记录而 sha256 不一致即失败，不被任何条目记录只告警；`release::prepare` 则直接拒绝不对应任何锁条目的目录。
- 锁条目为空时 `mod::lock-check` 和 `release::check` 只告警；构建仍须解析所选渠道的锁条目，不会借用其他渠道的条目。
- 锁只通过 PR 推进：在挂了对应 Steam 分支的机器上 `just mod::snapshot`，再 `just mod::publish <platform> <channel>` 上传私有存储，然后提交锁文件。游戏更新后本机 Steam 与锁不一致时，仍可使用已下载或从私有存储获取的锁定快照构建；采用新版本程序集须先推进锁。
- Android 只有 `android-online` 一个键，快照是 Interop Snapshot（[ADR 0008](adr/0008-interop-snapshots-in-snapshot-lock.md)）。`just with-config release just mod::fetch android online` 打印 interop 目录，并把 Loader Release 解到 `game-libs/loader-<tag>/`；Android 工程导入 `bazaarplusplus-mod/build/InteropPath.props` 取 `InteropPath` 与 `LemonLoaderPath`。推进时 `just mod::snapshot-android <TheBazaar.apk>` 沿用锁里的工具链，只在 Linux x64 上生成 interop；在其他主机上要传 `--interop <dir>`，用对同一 APK 生成好的目录。换工具链的选项见 `node release/game-libs.mjs --help`；Cpp2IL 用发布页的 Linux 二进制，并对照 GitHub 资产摘要核对。之后 `just mod::publish android online` 上传 interop、官方 APK 与工具链（APK 约 2.8 GB，一次流式上传），再提交锁文件。interop 归档以目录摘要为键而非自身摘要，所以 publish 会把已存的归档读回核对，内容不符就替换；macOS tar 若不带 `COPYFILE_DISABLE=1` 会把扩展属性打成 `._` 成员，Linux 上解出来就是多余文件，摘要随之改变。

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

命令写作 `just <project>::<recipe>`（也可写 `just <project> <recipe>`）。recipe 在项目目录中执行，所以相对路径参数按项目目录解析。只改一个项目时用该项目的 `check` 和 `test`；`just check`、`just test` 覆盖除 iOS 外的全部项目，遇到首个失败即停止。iOS 需要本机的 Mono 源码和 Xcode，单独执行 `just ios::check` 和 `just ios::test`。

- 全仓库门禁需要所有项目的工具链，mod 还需要快照锁能解析的游戏程序集；缺依赖时的失败不能当作通过。
- `installer::check` 已包含 installer 的测试，先跑 `check` 再跑 `test` 会把它们跑两遍。
- `mod::build` 只编译；部署进游戏要显式执行 `just mod::build --deploy`。macOS 部署通过 installer 的无界面 CLI 修复跳板，因此还需要 installer 的 Rust 工具链；普通 mod 编译不需要。含空格的参数整体加引号：

```bash
just mod::build "-p:ManagedPath=/absolute/path/The Bazaar/Managed"
```

## 依赖更新与云端检查

Dependabot 更新配置在 `.github/dependabot.yml`，普通版本更新的分组和节奏由配置维护，安全告警继续保留。自动合并由 `.github/scripts/dependabot-auto-merge.mjs` 登记，必需检查由 GitHub 分支规则维护。Dependabot PR 通过现有检查即可自动合并，接受兼容问题在后续使用中发现和修复；TypeScript 与类型感知 lint 工具、Tauri 的 npm 与 Cargo 依赖仍可能需要配套调整。

Mod 的自动更新只开放测试工具白名单。编译期依赖同样可能改变游戏内行为，不能因为 `PrivateAssets`、补丁版本或 NuGet 版本号相同就认为兼容。游戏自带 DLL、生成器、publicizer 和随包运行库的维护遵循 [ADR-0010](../bazaarplusplus-mod/docs/adr/0010-compile-against-game-supplied-libraries.md)。机器人 PR 的实际差异还会经过 `.github/scripts/check_mod_dependency_update.py`：允许测试工具版本修改，但生产锁文件或其他 Mod 文件变化必须转人工维护。

云端 Actions 按用途命名为 `CI /`（代码验证）、`Security /`（安全扫描）、`Delivery /`（发布部署）和 `Operations /`（后台任务与依赖维护）。代码验证由 `.github/scripts/checks-scope.mjs` 的 `classifyChanges` 选择项目，`planRun` 决定是否附加快照兼容性和性能检查；`.github/workflows/checks.yml` 执行计划，汇总消费同一份范围清单并显示本次计划验证的内容。PR 比较 merge-base 到 head，master push 比较 before 到 after，删除与重命名的新旧路径都参与分类。项目自己的命令 recipe 只选择本项目及根工具；已识别的仓库元数据只选根工具。未知共享输入或无法取得可靠 diff 时仍全量验证。跨项目契约扩展到消费者；未登记的源码依赖走全量，并由根工具测试阻止规则漂移。

Site 的 `site::ci` 在同一次验证中复用构建产物：部署预检和浏览器测试检查的是同一份 `dist`，独立的本地 preview 和 e2e 仍先构建。部署工作流同样上传前置检查生成的产物，避免检查后再次构建。

`Checks summary` 要求分类成功，且所有选中的 job 真正成功；未选中的不阻塞，选中后失败、取消或意外跳过会阻塞。普通 Mod 源码验证 macOS Desktop Host 和 Android Host，Mod 文档只在 macOS 执行包含文档约束的测试。Installer 前端只消费已提交绑定，原生输入在 macOS 重新生成绑定并检查 diff，依据见 [Installer ADR 0004](../bazaarplusplus-installer/docs/adr/0004-tauri-specta-command-bindings.md)。Windows 的触发提高到构建、依赖、工具链和显式 Windows 路径等输入变化；具体路径由分类器维护。普通共享源码中仅 Windows 生效的问题，允许推迟到发布验证发现。

staging/ptr 的 CompatCheck 编译只在 Snapshot Lock 更新、手动检查和桌面发布前执行；墙钟性能测试只在手动检查和桌面发布前执行，退出日常云端门禁。手动入口可按 scope/platform 显式选择平台并执行扩展检查，生成 `Manual checks (<scope>/<platform>)`，不能替代 PR 的 `Checks summary`。不可靠 diff 的自动运行保守补齐双平台及兼容检查。安全与依赖检查独立运行，不参与此处范围裁剪。

master push 使用同一分类规则；选中的 Installer 原生 job 保存 Rust 缓存，其他事件只恢复缓存。Windows 因较少运行可能遇到冷构建，接受这一代价以避免每次共享源码修改都运行两个原生工具链。Installer 步骤耗时直接写入各 job 摘要，不再通过独立报告 job 阻塞合并。严格 Rust API 文档编译放到发布验证，普通源码检查保留编译、lint、测试和绑定检查。

源码检查不替代实际运行和发布验收。Mod online 编译与测试不能验证 Unity/Mono 或设备加载；Android 检查仍须取得 Interop Snapshot 与 Loader Release。本地 `mod::check` 与 `mod::test` 不编译 Android Host，共享代码改动仍需另跑 Android 验证。正式发布在所选平台执行完整 Mod 验证，桌面还执行性能与 staging/ptr 检查，Installer 的发布构建执行完整原生验证；`dry_run` 仅准备 Payload。具体流程见 [产品发布](release.md)。运行时依赖升级仍要验证实际启动与受影响功能。

游戏程序集快照只存在于私有存储，不进公开仓库或 Actions cache。Mod job 使用只读 token `BPP_GAME_LIBS_TOKEN` 和账号变量 `CLOUDFLARE_ACCOUNT_ID`；Dependabot 也要注册对应 secrets。被选中的仓库内 job 缺凭据会失败。fork PR 保留明确的跳过例外，汇总说明未验证游戏兼容性；合并前仍需本地验证证据，不能把跳过当作通过。

配置静态检查不能证明机器人已经成功更新锁文件；首次启用及工具链升级后需查看 Dependabot 的实际更新日志，尤其是它的包管理器支持范围尚未覆盖仓库所用版本时。新的 CI 检查需在 GitHub 首轮成功后再设为必需检查。

自动合并工作流只执行目标分支的可信脚本，通过 API 读取 PR 数据，不执行 PR 代码。仓库须开启 auto-merge 和 Actions 审批权限；分支规则继续执行审批和必需检查要求，脚本不再重复维护检查清单。GitHub 原生 auto-merge 等待这些要求满足，不使用管理员绕过。工作流摘要显示跳过、等待合并或已合并的结果；首次配置完仓库权限后，可重跑机器人工作流。

组织策略禁止 Actions 审批时，符合条件的 PR 仍可登记 auto-merge，但保留人工 Approve；组织管理员放开该权限后才可自动审批。两种情况均不取消审批或 CI 要求。

## Android 冒烟

Android 没有云端的设备 lane：托管 runner 跑不了 arm64 游戏的模拟器（macOS arm64 托管 runner 没有嵌套虚拟化，x86 runner 跑不了只有 arm64 的游戏），自托管一台常开、常登录的机器只为夜间冒烟不值。发版前的设备验证在开发机上做：一台跑着 LemonLoader 注入版游戏、已登录测试账号的设备或 AVD，`just mod::android-smoke enabled` 构建、推送、重启、抓取并断言；`just mod::android-smoke disabled` 不装任何 patch，必须在 hook 命中级失败，用来证明断言不是空过。同一时间只驱动一台设备。

断言分三级（注入成功、hook 命中、功能生效），定义在 `scripts/android.sh` 的 `cmd_verify`。三级分开，是因为前一级通过不说明后一级：DLL 加载了，patch 未必装上，patch 命中了标签未必写对。证据写进 `bazaarplusplus-mod/artifacts/android/smoke-<mode>/`（gitignored，含整机 logcat 和已登录账号的截图，不要提交，也不要原样贴进公开的 issue 或 PR）；`just mod::android-verify <目录>` 重算结论（路径按 `bazaarplusplus-mod/` 解析，最好传绝对路径），版本取自证据里的 `version.txt`，不受之后 `VERSION` 变动影响。版本标签在主菜单加载前就写入，所以三级不依赖主菜单；`android-smoke` 另外要求主菜单在超时内出现，登录过期卡在登录页的运行因此仍然失败。主菜单不产生记录，数据库行数只作证据。`observe` 只计数不写标签，必然在功能生效级失败。

## Git hooks

根目录 `lefthook.yml` 是唯一的 hook 配置，按改动路径选择各项目的 recipe。pre-push 读取 Git 传入的推送 refs：已有远端分支比较推送前后的提交，首次推送比较当前提交与远端默认分支的 merge-base，并复用 CI 的项目与共享契约映射。远端默认分支引用或差异不可用时保守运行全部检查。先在根目录执行 `npm ci`，再执行 `just hooks-install`。它把仓库级 `core.hooksPath` 指向 git common dir 下的 `hooks`，让 linked worktree 共用同一套 hooks，并覆盖全局 hooks 路径。

需要保存验证证据时，使用 `just verify <recipe...>`：桌面端可运行 `just verify mod::check mod::test mod::test-performance`，Android 可运行 `just verify mod::android-build mod::test-core`。每次运行在 `artifacts/verification/` 下生成独立目录，记录 SHA、工作区是否有改动、各步骤最终退出码和完整日志。只有进程和输出流都结束、退出成功且 HEAD 未变化，回执才标记通过；中断留下的 `running` 不能作为通过证据。回执不是缓存，未提交改动的结果也不能证明后续提交已验证。

本地钩子保持完整验证，不随云端分层裁剪。Mod 的提交钩子负责 `mod::check`，推送钩子负责 `mod::test` 和 `mod::test-performance`，都经由 `verify` 保存结果。同一版本已由钩子完成的检查无需紧接着手工再跑；修改代码或 rebase 后重新验证。共享构建输出的检查串行执行。

`mod::test` 保留批量数据的数量、SQLite 调用次数和工作量断言；墙钟时间预算由 `mod::test-performance` 单独执行，避免与功能测试并发争抢资源。桌面、Core 和性能测试分别在 `bazaarplusplus-mod/artifacts/tests/` 下创建独立目录，保存 TRX；测试宿主崩溃时收集 blame 信息与小型转储。CI 无论通过或失败都会上传这些诊断和验证回执。失败时先查看本次证据，再决定是否重跑。

## 产品发布

发布命令、顺序、凭据和恢复见[产品发布](release.md)。发布命令不是 `check`、`test` 或 `mod::build` 的依赖。

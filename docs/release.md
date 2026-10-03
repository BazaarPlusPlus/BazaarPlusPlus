# 产品发布

mod 与 installer 是同一个 Product Release 的两个产物。根目录 `release/` 拥有发布模块及其测试，installer 脚本单向消费它并负责正式签名和打包。根目录 `VERSION` 是唯一手工维护的产品版本；`just release::sync` 将它投影到 npm、Tauri、Cargo 和 README，MSBuild 直接读取它。数据库 schema、native ABI、V5 用户数据格式以及用户当前安装版本保持独立。

## 入口

每个平台在自己的原生构建机上准备和打包；just 的安装与 Windows 约定见[开发命令](development.md)。参数以 `node release.mjs --help` 为准。

```bash
just release::sync
just release::check
just release::prepare macos
just release::build macos
just release::upload macos
just release::mirror-all <Windows大陆分享页地址> <macOS大陆分享页地址>
just release::verify-mirror
just release::promote
just release::promote --platform macos
```

Windows 将 `macos` 换成 `windows`。`release::prepare` 和 `release::build` 可在平台后追加 `"-p:ManagedPath=<absolute-path>"` 指定正式服游戏程序集；不接受编译器、版本、目标或输出目录覆盖。`build` 包含 `prepare`，但不会自动上传；`upload` 不修改 latest；`mirror-all` 在双平台上传齐备后统一核对并记录大陆镜像地址；`mirror` 用于单平台发布；`verify-mirror` 只读复核，不需要凭据；`promote` 发布双平台版本，`promote --platform` 只发布一个平台，见[按平台发布](#按平台发布)。installer 的 `npm run prepare:resources -- --platform …` 同样转入产品发布协调器；installer 的 `scripts/bundle.sh` 只在 `release::build` 持有的构建锁内运行。

`release/projections.mjs` 的 `checkProductProjections` 是共享源码对齐入口：根 `check` 与 installer 预检查均调用它，验证版本、Payload 投影、两份 README badge、平台配置、updater endpoint 和快照锁 `bazaarplusplus-mod/build/game-libs.lock.json` 的格式（六个键齐全，条目为空或过期只告警，见 [ADR 0004](adr/0004-pinned-game-assembly-snapshots.md)）。发布 origin 和 updater endpoint 列表由 `release/downloads.ts` 的 `RELEASE_BASE_URL` 与 `UPDATER_ENDPOINTS` 定义；Tauri 配置必须与后者逐项相等。`sync` 更新版本和 badge，不改写发布 origin。

just 只转发，不缓存或跳过任何发布检查。直接调用 `node release.mjs prepare|build` 时，MSBuild 参数要放在 `--` 之后；just 会自动补上。

## 发布顺序

1. 修改 `VERSION`，执行 `sync`，验证源码。
2. 两个平台分别执行 `prepare`。`prepare` 和后续 `build` 都显式传入同一个 `-p:ManagedPath=...`，指向正式服 Managed 或固定快照；mod 直接对照其中的游戏自带库编译，本机默认发现的安装可能与发布默认选择的快照不同。如果 native 输入锁或受版本管理的预构建资源变化，审阅并提交这些变化；把两个平台需要的更新汇入同一个提交。
3. 两台构建机检出这个相同提交，分别执行 `build`。发布相关源码必须干净；不相关的 site/analyzer 工作不会污染产品构建身份。若 `prepare` 又改变了跟踪的 native 输入，先汇入提交，再重新构建。
4. 分别执行 `upload`，保存同一个版本、同一个 Git commit 的平台产物和 fragment。
5. 两个平台上传完成后，单独执行大陆镜像阶段：把两个安装包原样上传到蓝奏云，拿到各自分享页地址后执行 `mirror-all`。它先验证双平台产物齐备、版本和提交一致，再核对两个分享页并记录地址，见[中国大陆镜像](#中国大陆镜像)。
6. 任一发布机执行 `promote`。两个平台未齐、提交不一致、远端产物缺失或校验不符、任一平台没有镜像记录时均拒绝写入。它先写每个平台的 Platform Release Manifest，再写 `latest.json`。

一个平台先发、另一个平台稍后跟上时，第 5 步仍用 `mirror <platform> <分享页地址>` 记录该平台的镜像，第 6 步用 `promote --platform <platform>`，见[按平台发布](#按平台发布)。

第 2 到第 4 步可以整体交给 [GitHub Actions 发版工作流](#github-actions-发版)：两个托管 runner 各跑一遍 `prepare`、`build`、`upload`，输入与本机相同；第 5、6 步仍在本机执行。

## GitHub Actions 发版

`.github/workflows/release.yml` 在 `macos-14` 与 `windows-latest` 各起一个 job，依次执行 `just release::check`、`just mod::fetch <platform> online`、`just release::prepare`、`just release::build`、`just release::upload`。build 与 upload 在同一个 job 里，满足"同机"要求；runner 本身就是原生宿主，原生宿主检查不需要改动。触发方式两种：`workflow_dispatch` 填 `version`（必须等于所选 ref 上的 `VERSION`，否则在装任何工具链之前失败），或推送 `v<VERSION>` 标签。`dry_run` 输入在 `prepare` 之后停止，只需要快照存储凭据，用来在没有签名材料时验证取包与 Payload 准备。

游戏程序集来自快照锁指向的 online 条目（[ADR 0004](adr/0004-pinned-game-assembly-snapshots.md)）：条目为空时 `mod::fetch` 失败并指出要采集哪个快照，不会退回本机 Steam。原生录制插件沿用 `release/native-recorder-input.mjs` 的新鲜度判断：已提交的产物新鲜就直接复用；不新鲜时在 runner 上重建，但重建会改动跟踪文件，工作流随即以具名错误失败并把差异作为 `native-recorder-inputs-<platform>` 产物上传，用 `git apply` 合入提交后重跑，不会上传一个脏构建。

secrets 放在名为 `release` 的 GitHub Environment 里，`BPP_GAME_LIBS_R2_*` 三个只读凭据是仓库级 secret，与 `checks.yml` 的游戏依赖 lane 共用。每个 job 的第一步 `.github/scripts/release-secrets.sh check` 按平台和阶段核对，缺一个就以 `::error` 点名并说明取值来源，是本阶段唯一的 secrets 清单；签名材料由同一脚本 `stage` 成 `bundle.sh` 读取的 `BPP_SIGNING_SECRETS_DIR` 文件布局，与本机 `node scripts/workspace.mjs run signing` 的暂存一致。本地取值位置见 `node scripts/workspace.mjs --help`：

| secret | 本地来源 | 用途 |
|---|---|---|
| `BPP_GAME_LIBS_R2_ACCOUNT_ID` / `_ACCESS_KEY_ID` / `_SECRET_ACCESS_KEY` | 仅限 `bazaarplusplus-game-libs` 的只读 R2 令牌 | `mod::fetch` |
| `BPP_R2_ACCOUNT_ID` / `BPP_R2_ACCESS_KEY_ID` / `BPP_R2_SECRET_ACCESS_KEY` | `config.ini` `[release]` | `release::upload` |
| `TAURI_SIGNING_PRIVATE_KEY` | `keys/tauri-updater.key` 的内容 | updater 签名，两平台 |
| `TAURI_SIGNING_PRIVATE_KEY_PASSWORD` | `config.ini` `[signing]`，可留空 | 同上 |
| `APPLE_SIGNING_IDENTITY` / `APPLE_API_ISSUER` / `APPLE_API_KEY` | `config.ini` `[signing]` | macOS 签名与公证 |
| `APPLE_API_KEY_P8` | `keys/AuthKey_<APPLE_API_KEY>.p8` 的内容 | macOS 公证 |
| `APPLE_CERTIFICATE` / `APPLE_CERTIFICATE_PASSWORD` | 从 Keychain Access 导出的 Developer ID Application 证书（含私钥）.p12 的 base64 与导出密码 | 导入 runner 的临时 keychain；`bundle.sh` 在 Tauri 打包前就要 codesign 内嵌资源，所以不能交给 Tauri 自己导入 |

`promote` 不进工作流：它要求两个平台都有大陆镜像记录（或显式豁免），而蓝奏云上传是手动步骤，分享页地址只有上传后才存在，`mirror-all` 必须在 `promote` 之前拿到它们。所以两平台 job 成功后仍按[发布顺序](#发布顺序)第 5、6 步在本机执行 `mirror-all` 与 `promote`，这两步只需要 `[release]` 凭据。

构建依赖 Node/npm、.NET、Rust、本机正式服 Managed 程序集、平台 native 工具链和 bootstrap 资源。准备阶段会抓取并验证 Build Seed Fetch 数据。macOS 正式打包另需 Developer ID、公证和 Tauri updater 签名材料，具体本机约定见 [installer 发布文档](../bazaarplusplus-installer/docs/release.md)。源代码验证无需这些签名凭据。

## Payload 的共同事实

`release/payload.json` 定义安装路径、平台、生产者、必需文件和归属；生成的 `release/generated/Payload.targets`、Node ZIP 校验以及 Rust 安装清理共同消费它，文件列表只在这里维护。

- `private` 是 BPP 私有文件；`dependency` 可能与其他插件共享；`bootstrap` 是加载器设施。
- `runtime` 只参与运行时清理规则，不进入安装包；`retired` 保留清理归属，但禁止重新打包。
- 目录归属包含其子树，文件归属只覆盖该文件。用户数据目录、BundleOutbox 和第三方插件不属于 Payload Inventory。

新增或删除 Payload 文件后修改清单并执行 `sync`。源码校验会拒绝生成投影漂移、越界路径和所有权重叠。产品程序集还必须通过实际 assembly metadata 的版本校验，不能只依赖版本文本文件。

## 本地准备与恢复

`prepare` 在隔离目录中复用或重建 native 输入，构建 managed 产物、校验 seed、生成一次 unsigned ZIP，并封存源文件摘要、Managed 程序集摘要、所用的快照锁条目（平台、渠道、游戏版本、sha256、buildid；Managed 目录不对应任何锁条目时拒绝准备）、seed 摘要和 Payload 文件 hashes/modes。最终校验全部成功后才切换当前平台的 SourceForBuild、ZIP 与 native 输入锁；另一个平台不变。

准备和整个 installer 构建共享一个进程锁，覆盖校验、签名、bundle 和最终 artifact manifest。打包结束重新检查 unsigned 来源、源码及 Git 身份；失败不会留下上一轮可上传的 artifact manifest。macOS 签名只变换 ZIP 中的副本，最终分发 hash 由 artifact manifest 记录。

切换过程留下恢复 journal。异常退出后预检查拒绝继续打包；重新执行 `prepare` 会在持锁状态下恢复上一组目录，再开始准备。活进程的锁不能抢占；同一主机已退出进程的锁可以回收。极短的锁获取阶段若被中断，残留的 `payload.lock.claim` 会阻止并发抢锁，需要确认没有发布进程后再人工清理该空目录。`node release.mjs assert-build-owner` 是 installer 打包使用的内部只读协议：校验构建 token、主机和活进程，不获取锁、不准备资源。常规调用者使用 `release::build`。来源记录失效或文件被修改时重新准备，不手工补写记录。journal 无法验证时停止并检查备份，不删除它来绕过检查。

## 远端发布

上传使用 R2 S3 条件写，需要仅对安装器 bucket 授权的凭据：

- `BPP_R2_ACCOUNT_ID`
- `BPP_R2_ACCESS_KEY_ID`
- `BPP_R2_SECRET_ACCESS_KEY`

它们只由 `upload` / `mirror` / `mirror-all` / `promote` 读取；不要放入 Git。此流程不使用 Wrangler 登录态，因为该 CLI 没有提供这里需要的 ETag 条件写。

对应的 just 命令通过 `scripts/workspace.mjs` 从集中配置接入这三个变量；`build` 通过临时签名目录接入 `[signing]` 与 `keys/`。配置初始化、已有环境变量的优先级和本机状态检查见[开发命令](development.md#新-clone-与本地配置)。直接执行 `node release.mjs` 仍要求调用者提供环境。

版本目录中的产物和 platform fragment 不可变：相同 bytes 的重试成功，不同 bytes 必须发布新版本。上传先固定所有本地文件内容并复查 hashes，避免并发本机构建污染远端版本路径。读取失败、权限错误和服务错误都不是“文件不存在”。

`promote` 验证完整性，然后以 ETag compare-and-swap 写入各平台的 `latest/<平台键>.json` 和 `latest.json`。有其他发布者抢先写入时重新检查版本，不能用旧版本覆盖新版本。重复发布同版本只能确认已有事实，不能替换它们。回退产品行为需要发布一个更高版本号的修复版本。

`release/downloads.ts` 是浏览器可消费的发布事实入口，统一官网与 installer 的发布 origin、平台键、两种 manifest 的路径、updater endpoint 列表，以及读取大陆镜像地址的 `decodeMainlandDownloadUrl`。共享样例有两组：`release/fixtures/latest.json` 是双平台的 Release Manifest，由发布 writer 测试、mod 更新检查和 Tauri updater 字段校验消费；`release/fixtures/latest/<平台键>.json` 是各平台的 Platform Release Manifest，两个平台版本不同，由官网测试、mod 测试和 writer 测试消费。这三份样例是发布流水线的产物：`release/cli.test.mjs` 的 `publish pipeline reproduces the shared fixtures` 在临时 git 仓库里用真实的 `main()` 跑完双平台 3.1.1 和单平台 3.1.2 的 upload、mirror 与 promote，再把内存 store 里的 manifest 与样例逐字节比对，其中提交哈希换成样例里的合成值。更新样例只有一个办法：`BPP_UPDATE_GOLDENS=1 npx vitest run --config release/vitest.config.mjs release/cli.test.mjs` 重新生成，并在同一个 PR 里跑 `just site::test mod::test`。

每个平台的 Platform Release Manifest（`latest/<平台键>.json`）和双平台的 Release Manifest（`latest.json`）形状相同：保留 Tauri updater 的 `platforms` 字段，并在 `downloads` 中提供该平台真实的安装器地址和大陆镜像地址 `mainlandUrl`。官网不再猜测主下载文件名，也不再拼接镜像地址；部署新版官网前先发布新 manifest，否则官网会使用已有的 GitHub 下载入口。

## 按平台发布

两个平台各有一份 Platform Release Manifest，可以处于不同版本。installer 的 updater endpoint 按 `UPDATER_ENDPOINTS` 的顺序先读平台文件，Tauri 会把占位符替换成本平台的键；平台文件返回非 2xx、网络出错或内容无法解析时才回退到 `latest.json`，此时客户端会把落后的双平台版本当成最新，所以发布后用 `verify-mirror --latest` 确认平台文件可读。mod 的版本检查和官网也按平台读各自的文件，不回退。`latest.json` 只在两个平台版本和提交都相同时推进，它表示"两个平台都发布了的最新版本"。决策记录见 [ADR 0003](adr/0003-per-platform-release-promotion.md)。

`promote --platform <platform>` 只写该平台的 manifest，写完后检查另一个平台：版本相同就顺带推进 `latest.json`，不同就保持不变并在日志里说明。规则：

- 一个版本号只对应一个提交。`upload` 和 `promote --platform` 都会拒绝与另一平台同版本不同提交的产物；后发的平台要么构建同一提交，要么升版本号。
- 每个平台的版本只前进，同版本重复执行只是确认，不能替换已发布的事实。
- 切换前构建的客户端只读 `latest.json`，它们只能看到双平台都发布了的版本。它们仍有向前的路径，是因为第一个带新 endpoint 的版本 5.5.0 是双平台同步发布的，而 `latest.json` 的 rollback guard 让它不会回到 5.5.0 以下：旧客户端先升到 5.5.0 或更新的双平台版本，再按平台文件升到本平台的最新版本。所以 `promote --platform` 不再检查 `latest.json` 的版本（[ADR 0003](adr/0003-per-platform-release-promotion.md) 2026-10-03 修订）。
- 镜像记录在该平台的 manifest 发布后冻结；`verify-mirror --latest` 逐平台复核各自的 manifest。

## 中国大陆镜像

Mainland Mirror 是手工上传到蓝奏云的安装包分享页，只作为大陆网络下手动下载的兜底，不是第二个发布来源，也不能充当 updater endpoint：蓝奏云只提供分享页，不提供可校验签名的直链。

镜像地址是发布者显式提供的输入，不由版本号拼接。`mirror` 读取该平台已上传的 fragment，抓取分享页核对标题里的文件名等于安装包文件名，然后把地址写入 `<版本>/<平台键>/mirror/mainland.json`；`promote` 把记录组装成 `downloads[平台键].mainlandUrl`。官网和 installer 只从 manifest 读取这个地址，manifest 里没有就不显示大陆入口。决策记录见 [ADR 0002](adr/0002-mainland-mirror-check.md)。

双平台发布使用 `mirror-all`。预检包括两个平台 fragment 的版本与提交、R2 产物的大小和 hash、平台是否已发布，以及两个分享页的文件名。任一预检失败都不会写镜像记录；全部通过后分别以条件写入保存两份记录。两次写入不构成跨对象事务，网络中断可能只留下第一份记录，修复后重跑同一命令即可。`mirror-all` 成功后再执行 `promote`；它本身不会上传蓝奏云文件或修改任何 latest 清单。不要并发运行镜像阶段和 `promote`。

上传约定：文件名必须与 R2 上 `installer` 目录里的文件名完全一致，不改名；分享地址随意。核对结果分三类：`verified`、`missing-or-misnamed`（分享不存在、被取消或文件名不符，修正分享后重跑 `mirror` 即可覆盖记录）、`unverifiable`（超时、非 200 或页面格式不认识）；`--allow-unverified-mirror` 把未通过的地址记录为 `verified: false` 并打印警告。核对只比文件名，不比 hash，也只能证明核对那一刻分享存在。

记录在该平台提升前可以覆盖，提升后 `mirror` 拒绝再写，写入前后都会复查，提升与记录撞车时以已发布的地址为准：已发布的镜像地址和其他已发布事实一样，修改需要新版本。`promote` 缺少任一平台的记录时拒绝写入，`--without-mainland-mirror` 显式跳过并打印警告，跳过的平台这个版本不能再补上；确认已发布的版本不需要记录。`verify-mirror` 不需要 R2 凭据：默认复核 VERSION 已记录的镜像，可用 `--platform` 只看一个平台；`--latest` 逐平台复核线上 `latest/<平台键>.json` 里的地址，也可加 `--platform`；显式跳过镜像的平台报告为 `waived`，不算失败。发布后的例行核对用 `--latest`，因为 VERSION 通常已经提前推进。

## 验证

- 根目录：先执行 `npm ci`，再运行 `just release::check` 和 `just release::test`；发布测试覆盖 CLI guard、投影漂移、Payload 事务和 Release Manifest，程序集集成用例需要 .NET SDK；全仓库源码检查与测试分别为 `just check`、`just test`。
- installer：`just installer::check`；真实准备后在 installer 目录使用 `npm run verify -- --release-platform macos`（或 `windows`）。
- mod：`just mod::build`、`just mod::test`。mod 直接对照 `ManagedPath` 下的游戏自带库编译（[mod ADR 0010](../bazaarplusplus-mod/docs/adr/0010-compile-against-game-supplied-libraries.md)），所以 `prepare` / `build` 的 Release 编译就是对该 Managed 的兼容检查；反射调用和运行时语义不在其中。
- site：`just site::test`、`just site::check`。

没有对应平台的本机工具链、Payload 来源记录或签名材料时，不能以源码测试通过代替正式平台包验证。

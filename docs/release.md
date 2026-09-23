# 产品发布

mod 与 installer 是同一个 Product Release 的两个产物。根目录 `release/` 拥有发布模块及其测试，installer 脚本单向消费它并负责正式签名和打包。根目录 `VERSION` 是唯一手工维护的产品版本；`just release::sync` 将它投影到 npm、Tauri、Cargo 和 README，MSBuild 直接读取它。数据库 schema、native ABI、V5 用户数据格式以及用户当前安装版本保持独立。

## 入口

通过根目录 `JUSTFILE` 执行，Windows 使用 Git Bash；每个平台在自己的原生构建机上准备和打包。just 的安装和日常检查命令见[开发命令](development.md)。

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

Windows 将 `macos` 换成 `windows`。`release::prepare` 和 `release::build` 可在平台后追加 `"-p:ManagedPath=<absolute-path>"` 指定正式服游戏程序集；不接受编译器、版本、目标或输出目录覆盖。`build` 包含 `prepare`，但不会自动上传；`upload` 不修改 latest；`mirror` 核对并记录一个平台的大陆镜像地址；`verify-mirror` 只读复核，不需要凭据；`promote` 发布双平台版本，`promote --platform` 只发布一个平台，见[按平台发布](#按平台发布)。installer 的 `npm run prepare:resources -- --platform …` 同样转入产品发布协调器；installer 的 `scripts/bundle.sh` 只在 `release::build` 持有的构建锁内运行。

`release/projections.mjs` 的 `checkProductProjections` 是共享源码对齐入口：根 `check` 与 installer 预检查均调用它，验证版本、Payload 投影、两份 README badge、平台配置和 updater endpoint。发布 origin 和 updater endpoint 列表由 `release/downloads.ts` 的 `RELEASE_BASE_URL` 与 `UPDATER_ENDPOINTS` 定义；Tauri 配置必须与后者逐项相等。`sync` 更新版本和 badge，不改写发布 origin。

just 只转发命令；版本规则、锁、签名流程和远端条件写仍在 Node 发布模块中执行，不使用任务缓存。原有 `node release.mjs sync|check|promote` 以及 `node release.mjs prepare|build|upload --platform <platform>` 保持可用；直接使用 Node 的 `prepare` / `build` 时，MSBuild 参数仍需放在 `--` 后。

日常开发使用各项目的 just 命令或原有子目录脚本。`just mod::build` 只编译，不修复 trampoline，也不修改游戏安装；部署进游戏用 `just mod::build --deploy`。

## 发布顺序

1. 修改 `VERSION`，执行 `sync`，验证源码。首次使用此流程时选择尚未发布的新版本，不能覆盖旧流程已经占用的版本目录。
2. 两个平台分别执行 `prepare`。如果 native 输入锁或受版本管理的预构建资源变化，审阅并提交这些变化；把两个平台需要的更新汇入同一个提交。
3. 两台构建机检出这个相同提交，分别执行 `build`。发布相关源码必须干净；不相关的 site/analyzer 工作不会污染产品构建身份。若 `prepare` 又改变了跟踪的 native 输入，先汇入提交，再重新构建。
4. 分别执行 `upload`，保存同一个版本、同一个 Git commit 的平台产物和 fragment。
5. 把两个平台的安装包原样上传到大陆镜像，拿到分享页地址后分别执行 `mirror`，把地址核对并记录到该平台的版本目录，见[中国大陆镜像](#中国大陆镜像)。
6. 任一发布机执行 `promote`。两个平台未齐、提交不一致、远端产物缺失或校验不符、任一平台没有镜像记录时均拒绝写入。它先写每个平台的 Platform Release Manifest，再写 `latest.json`。

一个平台先发、另一个平台稍后跟上时，把第 6 步换成各自的 `promote --platform <platform>`，见[按平台发布](#按平台发布)。

构建依赖 Node/npm、.NET、Rust、本机正式服 Managed 程序集、平台 native 工具链和 bootstrap 资源。准备阶段会抓取并验证 Build Seed Fetch 数据。macOS 正式打包另需 Developer ID、公证和 Tauri updater 签名材料，具体本机约定见 [installer 发布文档](../bazaarplusplus-installer/docs/release.md)。源代码验证无需这些签名凭据。

## Payload 的共同事实

`release/payload.json` 定义安装路径、平台、生产者、必需文件和归属；生成的 `release/generated/Payload.targets`、Node ZIP 校验以及 Rust 安装清理共同消费它。不要在各语言中再维护文件列表。

- `private` 是 BPP 私有文件；`dependency` 可能与其他插件共享；`bootstrap` 是加载器设施。
- `runtime` 只参与运行时清理规则，不进入安装包；`retired` 保留清理归属，但禁止重新打包。
- 目录归属包含其子树，文件归属只覆盖该文件。用户数据目录、BundleOutbox 和第三方插件不属于 Payload Inventory。

新增或删除 Payload 文件后修改清单并执行 `sync`。源码校验会拒绝生成投影漂移、越界路径和所有权重叠。产品程序集还必须通过实际 assembly metadata 的版本校验，不能只依赖版本文本文件。

## 本地准备与恢复

`prepare` 在隔离目录中复用或重建 native 输入，构建 managed 产物、校验 seed、生成一次 unsigned ZIP，并封存源文件摘要、Managed 程序集摘要、seed 摘要和 Payload 文件 hashes/modes。最终校验全部成功后才切换当前平台的 SourceForBuild、ZIP 与 native 输入锁；另一个平台不变。

准备和整个 installer 构建共享一个进程锁，覆盖校验、签名、bundle 和最终 artifact manifest。打包结束重新检查 unsigned 来源、源码及 Git 身份；失败不会留下上一轮可上传的 artifact manifest。macOS 签名只变换 ZIP 中的副本，最终分发 hash 由 artifact manifest 记录。

切换过程留下恢复 journal。异常退出后预检查拒绝继续打包；重新执行 `prepare` 会在持锁状态下恢复上一组目录，再开始准备。活进程的锁不能抢占；同一主机已退出进程的锁可以回收。极短的锁获取阶段若被中断，残留的 `payload.lock.claim` 会阻止并发抢锁，需要确认没有发布进程后再人工清理该空目录。`node release.mjs assert-build-owner` 是 installer 打包使用的内部只读协议：校验构建 token、主机和活进程，不获取锁、不准备资源。常规调用者使用 `release::build`。来源记录失效或文件被修改时重新准备，不手工补写记录。journal 无法验证时停止并检查备份，不删除它来绕过检查。

## 远端发布

上传使用 R2 S3 条件写，需要仅对安装器 bucket 授权的凭据：

- `BPP_R2_ACCOUNT_ID`
- `BPP_R2_ACCESS_KEY_ID`
- `BPP_R2_SECRET_ACCESS_KEY`

它们只由 `upload` / `mirror` / `promote` 读取；不要放入 Git。此流程不使用 Wrangler 登录态，因为该 CLI 没有提供这里需要的 ETag 条件写。

版本目录中的产物和 platform fragment 不可变：相同 bytes 的重试成功，不同 bytes 必须发布新版本。上传先固定所有本地文件内容并复查 hashes，避免并发本机构建污染远端版本路径。读取失败、权限错误和服务错误都不是“文件不存在”。

`promote` 验证完整性，然后以 ETag compare-and-swap 写入各平台的 `latest/<平台键>.json` 和 `latest.json`。有其他发布者抢先写入时重新检查版本，不能用旧版本覆盖新版本。重复发布同版本只能确认已有事实，不能替换它们。回退产品行为需要发布一个更高版本号的修复版本。

`release/downloads.ts` 是浏览器可消费的发布事实入口，统一官网与 installer 的发布 origin、平台键、两种 manifest 的路径、updater endpoint 列表，以及读取大陆镜像地址的 `decodeMainlandDownloadUrl`。共享样例有两组：`release/fixtures/latest.json` 是双平台的 Release Manifest，由发布 writer 测试、mod 更新检查和 Tauri updater 字段校验消费；`release/fixtures/latest/<平台键>.json` 是各平台的 Platform Release Manifest，两个平台版本不同，由官网测试、mod 测试和 writer 测试消费。

每个平台的 Platform Release Manifest（`latest/<平台键>.json`）和双平台的 Release Manifest（`latest.json`）形状相同：保留 Tauri updater 的 `platforms` 字段，并在 `downloads` 中提供该平台真实的安装器地址和大陆镜像地址 `mainlandUrl`。官网不再猜测主下载文件名，也不再拼接镜像地址；部署新版官网前先发布新 manifest，否则官网会使用已有的 GitHub 下载入口。

## 按平台发布

两个平台各有一份 Platform Release Manifest，可以处于不同版本。installer 的 updater endpoint 按 `UPDATER_ENDPOINTS` 的顺序先读平台文件，Tauri 会把占位符替换成本平台的键；平台文件返回非 2xx、网络出错或内容无法解析时才回退到 `latest.json`，此时客户端会把落后的双平台版本当成最新，所以发布后用 `verify-mirror --latest` 确认平台文件可读。mod 的版本检查和官网也按平台读各自的文件，不回退。`latest.json` 只在两个平台版本和提交都相同时推进，它表示"两个平台都发布了的最新版本"。决策记录见 [ADR 0003](adr/0003-per-platform-release-promotion.md)。

`promote --platform <platform>` 只写该平台的 manifest，写完后检查另一个平台：版本相同就顺带推进 `latest.json`，不同就保持不变并在日志里说明。规则：

- 一个版本号只对应一个提交。`upload` 和 `promote --platform` 都会拒绝与另一平台同版本不同提交的产物；后发的平台要么构建同一提交，要么升版本号。
- 每个平台的版本只前进，同版本重复执行只是确认，不能替换已发布的事实。
- 切换前构建的客户端只读 `latest.json`，它们只能看到双平台都发布了的版本。所以 `promote --platform` 要求线上 `latest.json` 已经不低于 `release/manifest.mjs` 里的 `PLATFORM_MANIFEST_SINCE`，第一个带新 endpoint 的版本必须用不带 `--platform` 的 `promote` 双平台一起发布。旧客户端会先升到那个版本，再升到本平台的最新版本。
- 镜像记录在该平台的 manifest 发布后冻结；`verify-mirror --latest` 逐平台复核各自的 manifest。

## 中国大陆镜像

Mainland Mirror 是手工上传到蓝奏云的安装包分享页，只作为大陆网络下手动下载的兜底，不是第二个发布来源，也不能充当 updater endpoint：蓝奏云只提供分享页，不提供可校验签名的直链。

镜像地址是发布者显式提供的输入，不由版本号拼接。`mirror` 读取该平台已上传的 fragment，抓取分享页核对标题里的文件名等于安装包文件名，然后把地址写入 `<版本>/<平台键>/mirror/mainland.json`；`promote` 把记录组装成 `downloads[平台键].mainlandUrl`。官网和 installer 只从 manifest 读取这个地址，manifest 里没有就不显示大陆入口。决策记录见 [ADR 0002](adr/0002-mainland-mirror-check.md)。

上传约定：文件名必须与 R2 上 `installer` 目录里的文件名完全一致，不改名；分享地址随意。5.4.0 及更早的 installer 自行拼接的旧地址不再维护，它们升级到 5.5.0 后即读取清单。核对结果分三类：`verified`、`missing-or-misnamed`（分享不存在、被取消或文件名不符，修正分享后重跑 `mirror` 即可覆盖记录）、`unverifiable`（超时、非 200 或页面格式不认识）；`--allow-unverified-mirror` 把未通过的地址记录为 `verified: false` 并打印警告。核对只比文件名，不比 hash，也只能证明核对那一刻分享存在。

记录在该平台提升前可以覆盖，提升后 `mirror` 拒绝再写，写入前后都会复查，提升与记录撞车时以已发布的地址为准：已发布的镜像地址和其他已发布事实一样，修改需要新版本。`promote` 缺少任一平台的记录时拒绝写入，`--without-mainland-mirror` 显式跳过并打印警告，跳过的平台这个版本不能再补上；确认已发布的版本不需要记录。`verify-mirror` 不需要 R2 凭据：默认复核 VERSION 已记录的镜像，可用 `--platform` 只看一个平台；`--latest` 逐平台复核线上 `latest/<平台键>.json` 里的地址，也可加 `--platform`；显式跳过镜像的平台报告为 `waived`，不算失败。发布后的例行核对用 `--latest`，因为 VERSION 通常已经提前推进。

## 验证

- 根目录：先执行 `npm ci`，再运行 `just release::check` 和 `just release::test`；发布测试覆盖 CLI guard、投影漂移、Payload 事务和 Release Manifest，程序集集成用例需要 .NET SDK；全仓库源码检查与测试分别为 `just check`、`just test`。
- installer：`just installer::check`；真实准备后在 installer 目录使用 `npm run verify -- --release-platform macos`（或 `windows`）。
- mod：`just mod::build`、`just mod::test`。
- site：`just site::test`、`just site::check`。

没有对应平台的本机工具链、Payload 来源记录或签名材料时，不能以源码测试通过代替正式平台包验证。

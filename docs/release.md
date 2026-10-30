# 产品发布

mod 与 installer 是同一个 Product Release 的两个产物。每个桌面发布版本（包括补丁版本）都产出内置当版完整 Payload 的安装器和独立的 Managed Payload 更新包：游戏内只下载兼容的 mod 更新，无法使用游戏内更新的用户可直接下载完整安装器安装当版 mod。根目录 `release/` 拥有发布模块及其测试，installer 脚本单向消费它并负责正式签名和打包。根目录 `VERSION` 是唯一手工维护的产品版本；`just release::sync` 将它投影到 npm、Tauri、Cargo 和 README，MSBuild 直接读取它。数据库 schema、native ABI、Data Root 目录名以及用户当前安装版本保持独立。

## 入口

`prepare`、`build`、`upload` 默认由 [GitHub Actions 发版工作流](#github-actions-发版)在两个托管 runner 上执行；在本机执行时每个平台仍要在自己的原生宿主上，游戏程序集由快照锁解析而不是本机 Steam 探测，所以装了游戏不再是发布机的条件。just 的安装与 Windows 约定见[开发命令](development.md)。参数以 `node release.mjs --help` 为准。

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

Windows 将 `macos` 换成 `windows`。`release::prepare` 和 `release::build` 默认对照本平台的 online 锁条目编译（`scripts/game.sh managed-path` 只解析这一个条目），可在平台后追加 `"-p:ManagedPath=<absolute-path>"` 换一种取包方式，但该目录仍必须哈希到某个锁条目，否则 `prepare` 在落任何文件之前拒绝；不接受编译器、版本、目标或输出目录覆盖。`build` 包含 `prepare`，但不会自动上传；`upload` 不修改 latest；`mirror-all` 在双平台上传齐备后统一核对并记录大陆镜像地址；`mirror` 用于单平台发布；`verify-mirror` 只读复核，不需要凭据；`promote` 发布双平台版本，`promote --platform` 只发布一个平台，见[按平台发布](#按平台发布)。installer 的 `scripts/bundle.sh` 只在 `release::build` 持有的构建锁内运行。

`release/projections.mjs` 的 `checkProductProjections` 是共享源码对齐入口：根 `check` 与 installer 预检查均调用它，验证版本、Payload 投影、两份 README badge、平台配置、updater endpoint 和快照锁 `bazaarplusplus-mod/build/game-libs.lock.json` 的格式（六个键齐全，条目为空或过期只告警，见 [ADR 0004](adr/0004-pinned-game-assembly-snapshots.md)）。版本达到 6.2.0 而 V5 导入模块 `bazaarplusplus-installer/src-tauri/src/v5_import/` 仍存在时它拒绝通过，所以 `release::check`、`release::prepare` 和 installer 预检查都会失败；该目录必须先删除，见 [installer ADR 0008](../bazaarplusplus-installer/docs/adr/0008-legacy-data-roots-and-v5-import.md) 与 [#264](https://github.com/BazaarPlusPlus/BazaarPlusPlus/issues/264)。发布 origin 和 updater endpoint 列表由 `release/downloads.ts` 的 `RELEASE_BASE_URL` 与 `UPDATER_ENDPOINTS` 定义；Tauri 配置必须与后者逐项相等。`sync` 更新版本和 badge，不改写发布 origin。

同一入口用 `release/history-database.mjs` 的 `assertHistoryDatabaseCompatibility` 比较 mod 的 `bazaarplusplus-mod/src/BazaarPlusPlus.Storage/BazaarPlusPlus.history-database.json` 与 installer 的 `bazaarplusplus-installer/src-tauri/history-database-compatibility.json`：两边必填的 `dataRootDirectoryName` 必须相同，mod 的 `historyDatabaseUserVersion` 必须是 installer `supportedUserVersions` 里最新的一项。两份 JSON 各自由项目测试钉住源码常量：mod 的 `RunLogSchemaReleaseContractTests` 对 `PathConstants.DataRootDirectoryName` 与 `RunLogSchema.LocalDatabaseSchemaVersion`，installer `src-tauri/src/config.rs` 的测试对 `BAZAAR_DATA_DIRECTORY`。所以只改一边的 Data Root 名称，会让该项目的测试或 `release::check` 失败。`release::prepare` 打包前对暂存 Payload 里的 mod 合约再做一次同样的比较（`release/payload-zip.mjs`）。

just 只转发，不缓存或跳过任何发布检查。直接调用 `node release.mjs prepare|build` 时，MSBuild 参数要放在 `--` 之后；just 会自动补上。

## 发布顺序

1. 修改 `VERSION`，执行 `sync`，验证源码。本机发版时，在所发桌面平台取得 online 快照后，执行 `just verify mod::check mod::test mod::test-performance` 和 `just with-config release just mod::matrix staging ptr`；日常 PR 不再提供完整双平台与 staging/ptr 证据，不能代替此步骤。
2. 两个平台分别执行 `prepare`。游戏程序集来自本平台的 online 锁条目，构建记录会写入这个条目；本机执行前先 `just mod::fetch <platform> online`，让条目在本机可解析。online 条目尚未采集时，本机只能显式传 `-p:ManagedPath=...` 指向一个哈希到其他锁条目（如 staging）的 Managed，`prepare` 和后续 `build` 传同一个值，日志会带非 online 告警。如果 native 输入锁或受版本管理的预构建资源变化，审阅并提交这些变化；把两个平台需要的更新汇入同一个提交。
3. 两个平台在这个相同提交上分别执行 `build`，托管 runner 或各自的原生宿主均可。发布相关源码必须干净；不相关的 site/analyzer 工作不会污染产品构建身份。若 `prepare` 又改变了跟踪的 native 输入，先汇入提交，再重新构建。
4. 在执行 `build` 的同一台机器上执行 `upload`，保存同一个版本、同一个 Git commit 的平台产物和 fragment。
5. 两个平台上传完成后，单独执行大陆镜像阶段：把两个安装包原样上传到蓝奏云，拿到各自分享页地址后执行 `mirror-all`。它先验证双平台产物齐备、版本和提交一致，再核对两个分享页并记录地址，见[中国大陆镜像](#中国大陆镜像)。
6. 任一发布机执行 `promote`。两个平台未齐、提交不一致、远端产物缺失或校验不符、任一平台没有镜像记录时均拒绝写入。它先为每个平台依次写安装器入口和 mod 入口的 Platform Release Manifest，再写 `latest.json`。

一个平台先发、另一个平台稍后跟上时，第 5 步仍用 `mirror <platform> <分享页地址>` 记录该平台的镜像，第 6 步用 `promote --platform <platform>`，见[按平台发布](#按平台发布)。

第 2 到第 4 步默认由 [GitHub Actions 发版工作流](#github-actions-发版)执行：两个托管 runner 各跑一遍 `prepare`、`build`、`upload`，输入与本机相同；本机执行是 runner 不可用或要调试打包时的备用路径。第 5、6 步只在本机执行。

## GitHub Actions 发版

`.github/workflows/release.yml` 在原生 macOS 与 Windows runner 上依次执行 `just release::check`、`just mod::fetch <platform> online`、所选平台的 Mod 验证（正式构建）、`just release::prepare`、`just release::build`、`just release::upload`。build 与 upload 在同一个 job 里，满足"同机"要求。正式构建在 Payload 准备、签名与上传之前补齐所选平台的 Mod 验证：桌面执行 online 检查、功能测试、墙钟性能测试及 staging/ptr 兼容编译，Android 执行宿主编译和 Core 测试。任一步失败即停止发布；Installer 的完整原生验证由 `release::build` 调用 `bazaarplusplus-installer/scripts/bundle.sh` 的 `run_release_prechecks` 执行。`build_only` 同样执行这些验证，`dry_run` 保留仅准备 Payload 的行为。macOS runner 必须支持 Icon Composer 源文件；工作流在准备 Payload 前运行 `bazaarplusplus-installer/scripts/release/compile-macos-icon.mjs` 的 `compileMacOSIcon`，提前发现图标工具链不兼容，PR 检查也执行相同的真实图标编译。

触发方式两种：`workflow_dispatch` 填 `version`（必须等于所选 ref 上的 `VERSION`，否则在装任何工具链之前失败），或推送 `v<VERSION>` 标签。手动触发时可选 `platform`，只重建需要验证的平台。`dry_run` 在 `prepare` 之后停止，只验证取包与 Payload 准备；需要验证完整签名和打包时用 `build_only`，它保留 Actions 安装包产物而不向 R2 上传。后者适合验证已暂存版本的修复，避免以新提交覆盖不可变版本目录。两者同时开启时，仍在 `prepare` 后停止。

游戏程序集来自快照锁指向的 online 条目（[ADR 0004](adr/0004-pinned-game-assembly-snapshots.md)）：条目为空时 `mod::fetch` 失败并指出要采集哪个快照，不会退回本机 Steam。原生录制插件沿用 `release/native-recorder-input.mjs` 的新鲜度判断：已提交的产物新鲜就直接复用；不新鲜时在 runner 上重建，但重建会改动跟踪文件，工作流随即以具名错误失败并把差异作为 `native-recorder-inputs-<platform>` 产物上传，用 `git apply` 合入提交后重跑，不会上传一个脏构建。

每个 GitHub secret 和变量的名字、作用域和本地来源只在 `release/github-secrets.json` 里维护：`release` 环境持有签名 secret 与 Apple 三个标识符变量；仓库级变量 `CLOUDFLARE_ACCOUNT_ID`、仓库级 secret `CLOUDFLARE_API_TOKEN`（操作员 token，`checks.yml`、`analyzer.yml`、`deploy-site.yml` 共用）放在仓库级。每个 job 的第一步 `.github/scripts/release-secrets.sh check` 读同一张表按平台和阶段核对，缺一个就以 `::error` 点名并说明取值来源；签名材料由同一脚本 `stage` 成 `bundle.sh` 读取的 `BPP_SIGNING_SECRETS_DIR` 文件布局，与本机 `node scripts/workspace.mjs run signing` 的暂存一致。R2 的 S3 密钥对不是 secret：`release/r2-store.mjs` 从 `CLOUDFLARE_API_TOKEN` 推导（Access Key ID 是 token id，Secret 是 token 值的 SHA-256）。

| 名字 | GitHub 作用域 | 本地来源 | 用途 |
|---|---|---|---|
| `CLOUDFLARE_ACCOUNT_ID` | 仓库变量 | `config.ini` `[cloudflare]` | 所有 R2 桶和 site Workers 的账号 |
| `CLOUDFLARE_API_TOKEN` | 仓库 secret（Dependabot 另注册一份） | `config.ini` `[cloudflare]`，操作员 token | `mod::fetch`、`release::upload`；也给 `checks.yml` 的 mod lane、`analyzer.yml` 和 `deploy-site.yml` |
| `TAURI_SIGNING_PRIVATE_KEY` | `release` 环境 secret | `keys/tauri-updater.key` 的内容 | updater 签名，两平台；三个平台的 Managed Payload zip 及其清单也用它（minisign） |
| `ANDROID_KEYSTORE` / `ANDROID_KEYSTORE_PASSWORD` / `ANDROID_KEY_ALIAS` | `release` 环境 secret | `keys/android-release.p12`（推送时转 base64）与 `config.ini` `[signing]` 的同名两项 | Android Release APK 的签名密钥，永不轮换，见 [ADR 0009](adr/0009-android-release-key.md) |
| `APPLE_SIGNING_IDENTITY` / `APPLE_API_ISSUER` / `APPLE_API_KEY` | `release` 环境变量 | `config.ini` `[signing]` | macOS 签名与公证 |
| `APPLE_API_KEY_P8` | `release` 环境 secret | `keys/AuthKey_<APPLE_API_KEY>.p8` 的内容 | macOS 公证 |
| `APPLE_CERTIFICATE` / `APPLE_CERTIFICATE_PASSWORD` | `release` 环境 secret | `keys/developer-id.p12`（从 Keychain Access 导出的 Developer ID Application 证书含私钥，推送时转 base64）与 `config.ini` `[signing] APPLE_CERTIFICATE_PASSWORD` | 导入 runner 的临时 keychain；`bundle.sh` 在 Tauri 打包前就要 codesign 内嵌资源，所以不能交给 Tauri 自己导入 |

发版前置条件：`just secrets-check` 的清单两边都不缺。它只列名字；`just secrets-sync` 把本地有值的项写到 GitHub（`--dependabot` 同时给 Dependabot 注册操作员 token）。只有三件事要人工做：在 Cloudflare dashboard 造操作员 token（`release/r2-store.mjs` 只接受 Account API Token）填进 `[cloudflare]`，从 Keychain Access 导出证书到 `keys/developer-id.p12` 并把导出密码填进 `[signing]`。环境 secret 会覆盖同名的仓库 secret，所以不要在任何环境里再建仓库 secret 的副本。

`promote` 不进工作流：它要求两个平台都有大陆镜像记录（或显式豁免），而蓝奏云上传是手动步骤，分享页地址只有上传后才存在，`mirror-all` 必须在 `promote` 之前拿到它们。所以两平台 job 成功后仍按[发布顺序](#发布顺序)第 5、6 步在本机执行 `mirror-all` 与 `promote`，这两步只需要 `[cloudflare]` 的操作员 token。

本机执行时，构建依赖 Node/npm、.NET、Rust、快照锁 online 条目解析出的 Managed 程序集（[开发命令](development.md#游戏程序集)）、平台 native 工具链和 bootstrap 资源。准备阶段会抓取并验证 Build Seed Fetch 数据。macOS 正式打包另需 Developer ID、公证和 Tauri updater 签名材料，具体本机约定见 [installer 发布文档](../bazaarplusplus-installer/docs/release.md)。源代码验证无需这些签名凭据。

## Payload 的共同事实

`release/payload.json` 定义安装路径、宿主、平台、生产者、必需文件和归属；生成的 `release/generated/Payload.targets`、Node ZIP 校验以及 Rust 安装清理共同消费它，文件列表只在这里维护。

- 每个条目的 `host` 是 `bepinex`（桌面，路径相对游戏目录）或 `melonloader`（Android，路径相对应用的外部文件目录，根是 `MelonLoader/`）。平台按 `release/release-platforms.mjs` 的 `payloadHost` 只取自己宿主的条目，`platforms` 只能写同一宿主的平台。`Payload.targets` 按宿主出两组条目，工程用 `BppPayloadHost` 选择，缺省是 `bepinex`；installer 只读 `bepinex` 条目。
- 生产者 `apk-injection` 的条目是 Android Release APK 内部的路径（`lib/arm64-v8a/`、`assets/LemonLoader/`），由 APK 注入步骤产生，不进 mod 构建和 `Payload.targets`；APK 内部路径也只能由它产生。
- `private` 是 BPP 私有文件；`dependency` 可能与其他插件共享；`bootstrap` 是加载器设施。
- `runtime` 只参与运行时清理规则，不进入安装包；`retired` 保留清理归属，但禁止重新打包。
- 目录归属包含其子树，文件归属只覆盖该文件。用户数据目录、BundleOutbox 和第三方插件不属于 Payload Inventory。

新增或删除 Payload 文件后修改清单并执行 `sync`。源码校验会拒绝生成投影漂移、越界路径和所有权重叠。产品程序集还必须通过实际 assembly metadata 的版本校验，不能只依赖版本文本文件。

## Managed Payload 与 mod 自更新

生产者 `managed` 与 `history-contract` 的条目组成 Managed Payload，mod 自己更新它；其余条目（加载器、native、`bootstrap`、`sqlite-*` 和生产者 `updater` 的 Updater）只由 installer 或新 APK 更新，它们的 sha256 汇成 fragment 的 `nonManagedPayloadDigest`。决策记录见 [mod ADR 0014](../bazaarplusplus-mod/docs/adr/0014-managed-payload-self-update.md)。

- `prepare` 把 Managed Payload Manifest 写进 Payload（生产者 `managed-manifest`，安装后位于 mod 旁边）；`build` 把清单、清单签名和 `files/` 下的文件打成确定性 zip 并签名，三个平台都放在 `<版本>/<平台键>/managed/`。格式与写法见 `release/managed-payload.mjs`，共享样例 `release/fixtures/managed-payload/manifest.txt`。
- fragment 记录 `managedPayload`、`historyDatabaseUserVersion` 与 `nonManagedPayloadDigest`。`selfUpdatable` 只给下载页用：`upload` 时与已提升的平台清单比较这两项。mod 只与正在运行的副本比较。
- Updater 以固定版本 1.0.0 确定性构建，同一编译器构建不变的源码时文件不变；改动它就改变 `nonManagedPayloadDigest`，所有用户都要走 installer 或新 APK。

## 本地准备与恢复

`prepare` 在隔离目录中复用或重建 native 输入，构建 managed 产物、校验 seed、生成一次 unsigned ZIP，并封存源文件摘要、Managed 程序集摘要、所用的快照锁条目（平台、渠道、游戏版本、sha256、buildid；Managed 目录不对应任何锁条目时拒绝准备）、seed 摘要和 Payload 文件 hashes/modes。最终校验全部成功后才切换当前平台的 SourceForBuild、ZIP 与 native 输入锁；另一个平台不变。

准备和整个 installer 构建共享一个进程锁，覆盖校验、签名、bundle 和最终 artifact manifest。打包结束重新检查 unsigned 来源、源码及 Git 身份；失败不会留下上一轮可上传的 artifact manifest。macOS 签名只变换 ZIP 中的副本，最终分发 hash 由 artifact manifest 记录。

切换过程留下恢复 journal。异常退出后预检查拒绝继续打包；重新执行 `prepare` 会在持锁状态下恢复上一组目录，再开始准备。活进程的锁不能抢占；同一主机已退出进程的锁可以回收。极短的锁获取阶段若被中断，残留的 `payload.lock.claim` 会阻止并发抢锁，需要确认没有发布进程后再人工清理该空目录。`node release.mjs assert-build-owner` 是 installer 打包使用的内部只读协议：校验构建 token、主机和活进程，不获取锁、不准备资源。常规调用者使用 `release::build`。来源记录失效或文件被修改时重新准备，不手工补写记录。journal 无法验证时停止并检查备份，不删除它来绕过检查。

## 远端发布

发布产物放在公开桶 `bazaarplusplus-public` 的 `installer/` 前缀下，对外地址是 `https://cdn.bazaarplusplus.com/installer`（`RELEASE_BASE_URL`）。同一个桶还放 analyzer 的 `metrics/v6/` 和 `just release::mod-data` 上传的 `mod-data/`，见 [ADR 0007](adr/0007-one-public-bucket-and-domain.md)。新桶从 6.0.0 开始，之前的版本不在里面；第一次 `upload` 读不到已提升的平台清单，`selfUpdatable` 记为 false。

### 5.x 客户端桥接

5.x 的安装器和 mod 只读旧桶 `bppinstaller`（`bppinstaller.bazaarplusplus.com`）的清单。6.0.0 两个平台都 `promote` 完、`verify-mirror --latest` 通过之后，手工执行一次：先把旧桶里的 `latest.json`、`latest/windows-x86_64.json`、`latest/darwin-aarch64.json` 下载下来备份，再把新桶 `installer/` 下的同名三份清单原样写进旧桶的根目录（`wrangler r2 object get/put --remote`，`--content-type application/json`）。清单里的安装包地址仍然指向 `cdn.`，5.x 的 updater 用相同的公钥验签。写完后用一台装着 5.7.0 的机器确认它能自动更新到 6.0.0。要回滚，就把备份写回去。

只桥接 6.0.0。之后的版本不再写旧桶，满一个月后旧桶和旧域名一起删除（#664）。

上传使用 R2 S3 条件写，凭据是操作员 token 与账号 ID：

- `CLOUDFLARE_API_TOKEN`：必须是该账号的 Account API Token，User API Token 会被拒绝
- `CLOUDFLARE_ACCOUNT_ID`

S3 密钥对由 `release/r2-store.mjs` 从 token 推导（一次 `GET /accounts/{account_id}/tokens/verify` 取 token id 作 Access Key ID，token 值的 SHA-256 作 Secret），每个进程只验证一次。它们只由 `upload` / `mirror` / `mirror-all` / `promote` 读取；不要放入 Git。此流程不使用 Wrangler 登录态，因为该 CLI 没有提供这里需要的 ETag 条件写。

对应的 just 命令通过 `scripts/workspace.mjs` 的 `release` profile 从 `[cloudflare]` 接入这两个变量；`build` 通过临时签名目录接入 `[signing]` 与 `keys/`。配置初始化、已有环境变量的优先级和本机状态检查见[开发命令](development.md#新-clone-与本地配置)。直接执行 `node release.mjs` 仍要求调用者提供环境。

版本目录中的产物和 platform fragment 不可变：相同 bytes 的重试成功，不同 bytes 必须发布新版本。上传先固定所有本地文件内容并复查 hashes，避免并发本机构建污染远端版本路径。读取失败、权限错误和服务错误都不是“文件不存在”。

`promote` 验证完整安装器及 Managed Payload 的完整性，然后以 ETag compare-and-swap 依次写入各平台的安装器入口 `latest/<平台键>.json`、mod 入口 `latest/mod/<平台键>.json`，最后写入 `latest.json`。两个平台入口使用同一份 Platform Release Manifest，包括版本、提交、发布时间与下载地址；没有单独推进 mod 版本的命令。有其他发布者抢先写入时重新检查版本，不能用旧版本覆盖新版本。重复发布同版本只能确认已有事实，不能替换它们。回退产品行为需要发布一个更高版本号的修复版本。多个清单的写入不是原子事务：中途失败时命令报错，重跑同一次 `promote` 补齐 mod 入口，即使 `latest.json` 已是该版本也会补齐；确认旧 lockstep 版本不会回退已经领先的平台。首次发布的新入口尚未写成时，mod 检查可能暂时失败，补齐后重新启动游戏再检查。

`release/downloads.ts` 是浏览器可消费的发布事实入口，统一发布 origin、平台键、安装器与 mod 清单路径（`platformManifestPath`、`modManifestPath`）、updater endpoint 列表，读取大陆镜像地址的 `decodeMainlandDownloadUrl`，以及读取 Android 平台清单的 `decodeAndroidDownload`。共享样例有两组：`release/fixtures/latest.json` 是双平台的 Release Manifest，由发布 writer 测试、mod 更新检查和 Tauri updater 字段校验消费；`release/fixtures/latest/<平台键>.json` 是各平台的 Platform Release Manifest，三个平台版本不全相同，由官网测试、mod 测试和 writer 测试消费。这四份样例是发布流水线的产物：`release/cli.test.mjs` 的 `publish pipeline reproduces the shared fixtures` 在临时 git 仓库里用真实的 `main()` 跑完双平台 3.1.1、Windows 单平台 3.1.2 的 upload、mirror 与 promote，再把同一提交的 Android 3.1.2 产物与 fragment 放进内存 store 并执行 `promote --platform android`，最后把 manifest 与样例逐字节比对，并检查 mod 入口与对应安装器入口的字节相同，其中提交哈希换成样例里的合成值。更新样例只有一个办法：`BPP_UPDATE_GOLDENS=1 npx vitest run --config release/vitest.config.mjs release/cli.test.mjs` 重新生成，并在同一个 PR 里跑 `just site::test mod::test`。

lockstep 平台（Windows 与 macOS）的 Platform Release Manifest（`latest/<平台键>.json`）和双平台的 Release Manifest（`latest.json`）形状相同：保留 Tauri updater 的 `platforms` 字段，并在 `downloads` 中提供该平台真实的安装器地址和大陆镜像地址 `mainlandUrl`。Android 的平台清单没有 `platforms` 字段，见 [Android 发布](#android-发布)。官网不再猜测主下载文件名，也不再拼接镜像地址；部署新版官网前先发布新 manifest，否则官网会使用已有的 GitHub 下载入口。

## 按平台发布

每个平台各有一份 Platform Release Manifest，可以处于不同版本。本节说的"两个平台"是 lockstep 平台 Windows 与 macOS；Android 见 [Android 发布](#android-发布)。installer 的 updater endpoint 按 `UPDATER_ENDPOINTS` 的顺序先读平台文件，Tauri 会把占位符替换成本平台的键；平台文件返回非 2xx、网络出错或内容无法解析时才回退到 `latest.json`，此时客户端会把落后的双平台版本当成最新，所以发布后用 `verify-mirror --latest` 确认平台文件可读。官网和旧 mod 仍读原平台文件；新 mod 通过 [ReleaseManifestEndpoints.ForPlatformKey](../bazaarplusplus-mod/src/BazaarPlusPlus.Core/Infrastructure/ReleaseManifest/ReleaseManifestEndpoints.cs) 读取独立的 mod 入口，已知平台请求失败时不回退到安装器入口。两个入口共用清单格式，因此旧 mod 仍能更新到带新入口的版本；先用新版发布工具完成两个入口的提升，再验证新客户端。`latest.json` 只在两个平台版本和提交都相同时推进，它表示"两个平台都发布了的最新版本"。决策记录见 [ADR 0003](adr/0003-per-platform-release-promotion.md) 与 [ADR 0011](adr/0011-separate-mod-update-endpoint.md)。

`promote --platform <platform>` 只写该平台清单的两个入口，写完后检查另一个平台：版本相同就顺带推进 `latest.json`，不同就保持不变并在日志里说明。规则：

- 一个版本号只对应一个提交。`upload` 和 `promote --platform` 都会拒绝与另一平台同版本不同提交的产物；后发的平台要么构建同一提交，要么升版本号。
- 每个平台的版本只前进，同版本重复执行只是确认，不能替换已发布的事实。
- 切换前构建的客户端只读 `latest.json`，它们只能看到双平台都发布了的版本。它们仍有向前的路径，是因为第一个带新 endpoint 的版本 5.5.0 是双平台同步发布的，而 `latest.json` 的 rollback guard 让它不会回到 5.5.0 以下：旧客户端先升到 5.5.0 或更新的双平台版本，再按平台文件升到本平台的最新版本。所以 `promote --platform` 不再检查 `latest.json` 的版本（[ADR 0003](adr/0003-per-platform-release-promotion.md) 2026-10-03 修订）。
- 镜像记录在该平台的 manifest 发布后冻结；`verify-mirror --latest` 逐平台复核各自的 manifest。

## Android 发布

Android 是第三个发布平台（平台键 `android-aarch64`），不进 `latest.json` 的 lockstep 集合，决策记录见 [ADR 0010](adr/0010-android-release-platform.md)。每个产品版本发布两样东西：Android Release APK（对象键由 `release/manifest.mjs` 的 `androidApkKey` 根据产品版本和游戏版本生成，放在 `installer/android/apk/`）和 Android Mod Payload zip（`<版本>/android-aarch64/managed/`）。新用户装的 APK 带着当版的 Mods；已安装的用户在 `nonManagedPayloadDigest` 不变时只下载 zip。fragment 与平台清单的字段由 `release/manifest.mjs` 的 `buildAndroidFragment` 定义。

```bash
just release::promote --platform android
```

`promote --platform android` 校验 APK 与 Mods zip 在 R2 上存在且大小、sha256 与 fragment 一致，依次写入 `latest/android-aarch64.json` 与 `latest/mod/android-aarch64.json`，从不推进 `latest.json`。Android 第一版没有大陆镜像：不读镜像记录，`--without-mainland-mirror`、`mirror` 和 `verify-mirror` 对 Android 拒绝执行。"一个版本号只对应一个提交"同样成立：桌面上传与提升会和同版本的 Android fragment 比对提交，Android 提升也会和桌面 fragment 比对；Android 可以跳过版本。

### Android 发布顺序

官方 APK 更新后，`.github/workflows/android-apk-watch.yml` 每小时比较一次发行商 APK 的 Content-Length 和 ETag：大小对照锁定 APK 在私有桶里的大小，ETag 对照锁条目的 `apkEtag`（`node release/game-libs.mjs official-apk`，不下载）。手动采集的条目 `apkEtag` 为 null，只比大小。任一项不一致，或手动触发时选了 `force`，它就在 Linux runner 上下载 APK 并核对 sha256。sha256 与锁相同而 ETag 变了，它只开一个记录新 ETag 的 PR，不升版本号，否则以后每次检查都会重新下载。sha256 变了，它采集新的 `android-online` 条目（记下下载时的 ETag），上传输入，跑 `mod::android-build` 和 `mod::test-core`，再开一个更新锁文件并把 `VERSION` 升一个补丁号的 PR。这个 PR 由 `GITHUB_TOKEN` 创建，不会触发检查，所以检查结果写在 PR 正文里。合并后从下面第 1 步开始发布。仓库设置里要允许 GitHub Actions 创建 PR。

```bash
just with-config release just mod::fetch android online
just release::prepare android
just release::build android
just release::upload android
just release::promote --platform android
```

1. 与桌面同一个提交（`VERSION` 相同）。本机发版在取包后先执行 `just verify mod::android-build mod::test-core`。`mod::fetch android online` 把锁条目 `android-online` 的 Interop Snapshot 与 Loader Release 取到 `bazaarplusplus-mod/game-libs/`。
2. `prepare` 对照锁定的 interop 与 Loader Release 编译 Android 宿主，并核对宿主暂存的 Android Mod Payload 恰好是 Payload Inventory 里由 mod 构建产生的 `melonloader` 条目：清单要求而宿主没有产出的文件会被逐一点名。`-p:InteropPath=`、`-p:LemonLoaderPath=` 可以覆盖目录，但覆盖的仍必须是锁条目钉住的那一份。
3. `build` 重做 `prepare`，把 Payload 打成确定性的 Managed Payload zip，用 Tauri updater 私钥签名；签名走 installer 锁定的 Tauri CLI，所以要先在 `bazaarplusplus-installer` 执行 `npm ci`。然后注入、对齐并签名当版的 APK，步骤与工具要求见 `release/android-release.mjs` 的 `injectAndroidApk` 与 `resolveBuildTools`。注入只能在 Linux x64 上跑，因为 Patcher CLI 只有这一种发行包。原生库必须不压缩地放进 APK，系统才能就地映射。签名材料的来源见上面的 secret 表与 [ADR 0009](adr/0009-android-release-key.md)。所有输出都在工作区根的 `target/android/`。
4. `upload` 在同一台机器上用流式条件写依次上传 APK、Managed Payload zip 与 `.sig`，最后写 fragment。新 APK 的签名证书必须与已提升的 APK 相同，`promote` 也做同样的检查。`selfUpdatable` 比较已提升的平台清单，与桌面相同。上传停在 fragment 之前时，用同一次构建再跑 `upload` 即可续传；重新构建得到的 APK 字节不同（Patcher 与 zip 的输出不可复现），会被已上传的对象拒绝，这时先从 R2 删掉这个 APK 再上传，或者升版本。
5. `promote --platform android`。

`.github/workflows/release.yml` 的 `platform: android` 在 `ubuntu-latest` 上跑第 1 到 4 步，标签推送只构建桌面两平台。输入 APK、中间产物和输出 APK 同时占约 9 GB，所以 job 先删掉用不到的预装工具链，结束后清理中间目录。`dry_run` 只跑到 `prepare`。`build_only` 构建后把 APK 与 Managed Payload zip 留作 Actions 产物、不上传 R2，用于在干净的 AVD 上验收。

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

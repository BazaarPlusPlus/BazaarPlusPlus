# 产品发布

mod 与 installer 是同一个 Product Release 的两个产物。根目录 `VERSION` 是唯一手工维护的产品版本；`just release::sync` 将它投影到 npm、Tauri、Cargo 和 README，MSBuild 直接读取它。数据库 schema、native ABI、V5 用户数据格式以及用户当前安装版本保持独立。

## 入口

通过根目录 `JUSTFILE` 执行，Windows 使用 Git Bash；每个平台在自己的原生构建机上准备和打包。just 的安装和日常检查命令见[开发命令](development.md)。

```bash
just release::sync
just release::check
just release::prepare macos
just release::build macos
just release::upload macos
just release::promote
```

Windows 将 `macos` 换成 `windows`。`release::prepare` 和 `release::build` 可在平台后追加 `"-p:ManagedPath=<absolute-path>"` 指定正式服游戏程序集；不接受编译器、版本、目标或输出目录覆盖。`build` 包含 `prepare`，但不会自动上传；`upload` 不修改 latest；只有 `promote` 发布完整双平台版本。installer 的 `./build.sh --prod` 和 `npm run prepare:resources -- --platform …` 均转入产品发布协调器。

just 只转发命令；版本规则、锁、签名流程和远端条件写仍在 Node 发布模块中执行，不使用任务缓存。原有 `node release.mjs sync|check|promote` 以及 `node release.mjs prepare|build|upload --platform <platform>` 保持可用；直接使用 Node 的 `prepare` / `build` 时，MSBuild 参数仍需放在 `--` 后。

日常开发使用各项目的 just 命令或原有子目录脚本。`just mod::build` 只编译，不修复 trampoline，也不修改游戏安装；部署进游戏用 `just mod::deploy`。

## 发布顺序

1. 修改 `VERSION`，执行 `sync`，验证源码。首次使用此流程时选择尚未发布的新版本，不能覆盖旧流程已经占用的版本目录。
2. 两个平台分别执行 `prepare`。如果 native 输入锁或受版本管理的预构建资源变化，审阅并提交这些变化；把两个平台需要的更新汇入同一个提交。
3. 两台构建机检出这个相同提交，分别执行 `build`。发布相关源码必须干净；不相关的 site/analyzer 工作不会污染产品构建身份。若 `prepare` 又改变了跟踪的 native 输入，先汇入提交，再重新构建。
4. 分别执行 `upload`，保存同一个版本、同一个 Git commit 的平台产物和 fragment。
5. 任一发布机执行 `promote`。两个平台未齐、提交不一致、远端产物缺失或校验不符时均拒绝推进 latest。

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

切换过程留下恢复 journal。异常退出后预检查拒绝继续打包；重新执行 `prepare` 会在持锁状态下恢复上一组目录，再开始准备。活进程的锁不能抢占；同一主机已退出进程的锁可以回收。极短的锁获取阶段若被中断，残留的 `payload.lock.claim` 会阻止并发抢锁，需要确认没有发布进程后再人工清理该空目录。来源记录失效或文件被修改时重新准备，不手工补写记录。journal 无法验证时停止并检查备份，不删除它来绕过检查。

## 远端发布

上传使用 R2 S3 条件写，需要仅对安装器 bucket 授权的凭据：

- `BPP_R2_ACCOUNT_ID`
- `BPP_R2_ACCESS_KEY_ID`
- `BPP_R2_SECRET_ACCESS_KEY`

它们只由 `upload` / `promote` 读取；不要放入 Git。此流程不使用 Wrangler 登录态，因为该 CLI 没有提供这里需要的 ETag 条件写。

版本目录中的产物和 platform fragment 不可变：相同 bytes 的重试成功，不同 bytes 必须发布新版本。上传先固定所有本地文件内容并复查 hashes，避免并发本机构建污染远端版本路径。读取失败、权限错误和服务错误都不是“文件不存在”。

`promote` 验证双平台完整性，然后以 ETag compare-and-swap 写入 `latest.json`。有其他发布者抢先写入时重新检查版本，不能用旧版本覆盖新版本。重复发布同版本只能确认已有事实，不能替换它们。回退产品行为需要发布一个更高版本号的修复版本。

Release Manifest 保留 Tauri updater 的 `platforms` 字段，并提供 `downloads` 中真实的安装器地址。官网不再猜测主下载文件名；部署新版官网前先发布完整的新 manifest，否则官网会使用已有的 GitHub 下载入口。中国大陆镜像仍由其独立上传流程维护。

## 验证

- 根目录：`just release::check`；全仓库源码检查与测试分别为 `just check`、`just test`。
- installer：`just installer::check`；真实准备后在 installer 目录使用 `npm run verify -- --release-platform macos`（或 `windows`）。跨仓库发布测试还需要 .NET SDK。
- mod：`just mod::build`、`just mod::test`。
- site：`just site::test`、`just site::check`。

没有对应平台的本机工具链、Payload 来源记录或签名材料时，不能以源码测试通过代替正式平台包验证。

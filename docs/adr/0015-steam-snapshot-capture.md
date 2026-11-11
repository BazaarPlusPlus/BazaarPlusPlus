# 从 Steam depot 自动采集游戏程序集快照

2026-11-07 注：[ADR 0016](0016-one-game-compatibility-rule.md) 取代了末段"下载页不显示桌面版的已适配版本"：桌面平台清单加上已适配的游戏，下载页按同一规则显示。

[ADR 0004](0004-pinned-game-assembly-snapshots.md) 把采集留在挂了对应分支的机器上手工执行，理由是 Steam Guard 在无人值守环境下不可靠，并写明"自动化另起决定"。手工采集的代价已经出现：同一渠道的两个平台会相差很多个版本，发布也要跳过过期的预览渠道。iOS App 的 Service 证明了这个理由已经不成立：操作员登录一次后，保存的 refresh token 可以不经 Steam Guard 再次登录，SteamKit 也能直接从 depot 下载文件。

决定如下。桌面的六个锁条目都从 Steam depot 采集。`bazaarplusplus-mod/tools/SteamSnapshot` 用 refresh token 登录，读取分支的 buildid 和 depot manifest，只下载 `globalgamemanagers` 和它旁边的 `Managed/`。它不假设平台的安装目录结构，所以 Linux runner 可以采集 macOS 和 Windows 两个平台。下载后的文件走 `captureSnapshot` 的原有路径：游戏版本串、渠道与分支的核对、Managed 摘要都不变。`release/game-assembly-snapshot.ts` 的 `STEAM_DEPOTS` 拥有 depot 的对应关系，`release/snapshot-lock.ts` 拥有渠道与分支的对应关系，工具本身不知道渠道。

`.github/workflows/steam-snapshot-watch.yml` 每小时按渠道分别运行 `steam-snapshot-channel.yml`，一个渠道失败不影响其他渠道，每个渠道有自己的 PR。它比较 Steam 的 buildid 和锁条目。buildid 是应用级的，任何 depot 变化都会移动它，所以它只决定何时采集。采集后按结果分三种：Managed 摘要变了是 `code`，只有游戏版本串变了是 `content`，都没变是 `none`。`none` 不改锁也不开 PR，代价是下一次游戏更新前每小时多下载一次这些文件。另外两种发布到私有存储，再开一个只改锁文件的 PR，锁仍然只通过 PR 更新，由人合并，这一点与 ADR 0004 相同。

`code` 在开 PR 前先在 macOS 和 Windows runner 上运行检查：`online` 是 `just verify mod::check mod::test`，`staging` 和 `ptr` 按 ADR 0004 只做兼容性编译（`just mod::matrix <channel>`）。结果写进 PR 正文，这与 `android-apk-watch.yml` 的做法相同，因为 `GITHUB_TOKEN` 开的 PR 不触发检查。不派发 `checks.yml`：手动运行总是加上 staging、ptr 矩阵和性能测试，过期的预览渠道会让每个 PR 都失败，这也不是这个 PR 要回答的问题。结果是这些 PR 没有 `Checks summary`，合并需要管理员权限。检查失败仍然开 PR，因为这正是提前发现游戏改动的信号；游戏改动需要的适配提交加到这个分支上，更新的 build 不会关闭带这种提交的 PR。

`staging` 和 `ptr` 是要访问代码的私有分支，Steam 不在应用信息里列出它们。工具用代码换取分支的 key，再读取这个分支的 depot 信息，`SteamKit2` 3.5.0 起有这个接口。之后的下载与 `online` 相同，所以 `just mod::snapshot` 不再是推进锁的必要步骤，只保留给没有 Steam 登录的机器。

登录和访问代码放在 `steam` GitHub Environment 的 `STEAM_SNAPSHOT_LOGIN` 和 `STEAM_BETA_PASSWORDS` 里，只有读取 Steam 的两个步骤能拿到。工具的 NuGet 依赖图由提交的 `packages.lock.json` 锁定，因为它运行时环境里有这份登录。Steam 会结束同一账号上 `LoginID` 相同的旧会话，所以工具每次运行都用随机的 `LoginID`，这样同时运行的渠道、桌面客户端和 iOS App 不会互相顶掉。

否决的方案：共用 iOS 的 `Downloader`，它运行在 iOS 的 Mono 里，写死了 `public` 分支并下载全部文件，而且 iOS 这条路线以后可能整块删除。使用 DepotDownloader，它把登录保存在自己的本地存储里，无法直接从环境变量读取，也不能只读分支状态而不下载。用 `api.steamcmd.net` 判断版本变化，它是第三方服务，也看不到私有分支。

代价是多了会失效的凭据：refresh token 或访问代码失效后任务会失败并说明原因，更新它们只能由操作员完成。账号也必须拥有游戏，所以建议使用专用账号，不使用操作员的主账号。

工作流顺带把 `online` 的版本和 Steam 的更新时间写成公开的 Game Status，下载页读取它，这样游戏更新后不用等发布就能显示。Steam 的更新时间来自分支信息，版本串来自采集，所以只有锁或打开的锁 PR 记录了这个 build 时才写入。`staging` 和 `ptr` 只写进运行摘要，因为它们是要访问代码的私有分支。下载页不显示桌面版的“已适配版本”：桌面 Release Manifest 没有这个字段，而且桌面 mod 靠 Harmony 降级运行在更新的游戏上，这个说法也不成立。

# 公开文件收进一个桶、一个域名，私有数据按信任边界分桶

5.x 时代每类公开文件各有一个 R2 桶和一个子域名：安装包在 `bppinstaller`（`bppinstaller.bazaarplusplus.com`），统计快照在 `bazaarplusplus-metrics-v6`（`bpp-metrics-v6.bazaarplusplus.com`），支持者名单和 voice-lines 各在一个仓库外的桶里（`bpp-static.`、`bazaarline-installer.`）。私有的 analyzer 状态也单独占一个桶。每多一个桶，token 的授权、文档和迁移就多一份；V6 时 metrics 还为了换契约版本新建了桶和域名。

决定如下。公开文件只放在 `bazaarplusplus-public` 一个桶，只挂 `cdn.bazaarplusplus.com` 一个域名。第一层目录按写入方划分，每条流水线只写自己的前缀：

- `installer/`：发版流程（`release/r2-store.mjs` 的默认前缀，`RELEASE_BASE_URL` 为 `https://cdn.bazaarplusplus.com/installer`）。目录内部结构不变。
- `metrics/v6/`：analyzer（`BPP_METRICS_R2_PREFIX`）。契约换版本时并排加 `metrics/v7/`，不新建桶或域名。
- `mod-data/`：维护者用 `just release::mod-data` 上传的 `supporters/supporter-list.json` 和 `voice-lines/voice-lines.json`。

私有数据按“谁能读”分桶，而不是按用途分：`bazaarplusplus-bundle-v6` 放玩家 Bundle（`bundles/`，8 天生命周期规则只作用于这个前缀）和 analyzer 状态（`analyzer-state/`）；`bazaarplusplus-game-libs` 单独一个桶，因为 CI 和 Dependabot 用的只读 token 只能按桶授权，它不能读到玩家数据。游戏程序集是游戏本体文件，不能进公开桶。

已发布的 5.x 客户端写死了旧域名，所以 `bppinstaller.`、`bpp-static.`、`bazaarline-installer.` 作为别名挂到 `bazaarplusplus-public`，用 Cloudflare URL Rewrite 规则把旧路径改写到新前缀，等 5.x 基本退出后逐个摘掉。6.0.0 之前推广的版本，其 manifest 里的安装包地址仍是旧域名，官网同时接受 `LEGACY_RELEASE_BASE_URL` 下的地址。`bpp-metrics-v6.` 只有未发布的 6.0.0 开发版用过，直接下线。

否决的方案：沿用 `bppinstaller` 当公开桶，省一次复制，但桶名与内容不符，并且仍要改目录；game-libs 也并进私有数据桶，桶少一个，但只读 token 会读到玩家截图；按子域名区分内容，每类内容仍要一个域名和一条 DNS 记录。

代价是迁移要复制全部历史安装包（保留 `sha256` 元数据），旧域名要维护改写规则直到 5.x 退出，而且 6.0.0 必须在迁移完成后再发布，否则会把旧地址写进新版本。

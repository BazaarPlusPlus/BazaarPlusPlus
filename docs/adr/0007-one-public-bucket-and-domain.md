# 公开文件收进一个桶、一个域名，私有数据按信任边界分桶

5.x 时代每类公开文件各有一个 R2 桶和一个子域名：安装包在 `bppinstaller`（`bppinstaller.bazaarplusplus.com`），统计快照在 `bazaarplusplus-metrics-v6`（`bpp-metrics-v6.bazaarplusplus.com`），支持者名单和 voice-lines 各在一个仓库外的桶里（`bpp-static.`、`bazaarline-installer.`）。私有的 analyzer 状态也单独占一个桶。每多一个桶，token 的授权、文档和迁移就多一份；V6 时 metrics 还为了换契约版本新建了桶和域名。

决定如下。公开文件只放在 `bazaarplusplus-public` 一个桶，只挂 `cdn.bazaarplusplus.com` 一个域名。第一层目录按写入方划分，每条流水线只写自己的前缀：

- `installer/`：发版流程（`release/r2-store.mjs` 的默认前缀，`RELEASE_BASE_URL` 为 `https://cdn.bazaarplusplus.com/installer`）。目录内部结构不变。
- `metrics/v6/`：analyzer（`BPP_METRICS_R2_PREFIX`）。契约换版本时并排加 `metrics/v7/`，不新建桶或域名。
- `mod-data/`：维护者用 `just release::mod-data` 上传的 `supporters/supporter-list.json` 和 `voice-lines/voice-lines.json`。

私有数据按“谁能读”分桶，而不是按用途分：`bazaarplusplus-bundle-v6` 放玩家 Bundle（`bundles/`，8 天生命周期规则只作用于这个前缀）和 analyzer 状态（`analyzer-state/`）；`bazaarplusplus-game-libs` 单独一个桶，因为 CI 和 Dependabot 用的只读 token 只能按桶授权，它不能读到玩家数据。游戏程序集是游戏本体文件，不能进公开桶。

已发布的 5.x 客户端写死了旧域名：安装器 updater 和 mod 的更新检查读 `bppinstaller.` 的清单，mod 还从 `bpp-static.` 读支持者名单，从 `bazaarline-installer.` 读台词。旧桶不迁移，只做一次桥接。6.0.0 在新桶提升之后，把它的 Release Manifest 和两个 Lockstep Platform 的 Platform Release Manifest 原样写进 `bppinstaller` 桶的根目录。清单里的安装包地址指向 `cdn.`，签名公钥也没有变，所以 5.x 安装器会直接更新到 6.0.0，此后改从新地址更新。从那以后三个旧桶不再写入，桥接满一个月后连同域名一起删除（#664）。还停在 5.x 的用户之后只能从官网下载。新桶从 6.0.0 开始，官网和 updater 只认 `RELEASE_BASE_URL` 下的地址。`bpp-metrics-v6.` 只有未发布的 6.0.0 开发版用过，直接下线。

否决的方案：沿用 `bppinstaller` 当公开桶，省一次复制，但桶名与内容不符，并且仍要改目录；game-libs 也并进私有数据桶，桶少一个，但只读 token 会读到玩家截图；按子域名区分内容，每类内容仍要一个域名和一条 DNS 记录；把全部历史安装包复制进 `installer/`，再用 Cloudflare URL Rewrite 把旧域名改写到新桶，这样 5.x 客户端不需要桥接，但要复制全部历史、长期维护改写规则，而且 6.0.0 必须等迁移完成才能发布（2026-10-29 改为桥接）。

代价是 5.x 用户读到的支持者名单和台词会停在桥接时的版本；旧域名删除之后，5.x 不再有自动更新的路径。6.0.0 之前的安装包不进新桶，官网也不再提供下载。

2026-10-22 注：[ADR 0010](0010-android-release-platform.md) 在合作授权下把 Android Release APK（含游戏二进制）放进公开桶的 `installer/android/apk/`，这是"游戏本体文件不能进公开桶"的唯一例外。游戏程序集快照、interop、官方 APK 与 Loader Release 仍只在 `bazaarplusplus-game-libs`。Android 是 6.0.0 新增的平台，它的地址只在 `RELEASE_BASE_URL` 下。

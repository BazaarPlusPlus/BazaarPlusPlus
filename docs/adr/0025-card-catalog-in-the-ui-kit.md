# 卡牌目录、游戏术语和回放接入归 UI kit

状态：已接受（2026-11-27）

## 背景

整局回放（#1214）在网站和安装器里用同一个 `RunReplay`，但安装器的卡牌只显示 8 位 TemplateId，没有卡名、卡牌详情、悬停预览和附魔效果（#1215）。原因是 `design/ui` 只管"怎么画"，"画什么"由各端自己写：

- 读卡图索引、Card Text 和附魔效果材质的代码只在网站里；
- "卡牌引用 → `GameCard`"写了三遍（网站一遍，安装器两遍），安装器写死了 CDN 地址；
- 品阶、附魔、操作名、地点类型的中英文在网站、安装器和 UI 文档站各写一份；
- 回放接入（`SiteReplay`、`LocalReplay`）写了两遍。

[ADR-0018](0018-one-web-component-library.md) 规定读产品数据或 i18n 的组件留在产品里，组件库只依赖 `react`。这两条让上面的代码无法共用。

## 决定

- `design/ui/game/catalog.tsx` 是卡牌目录 `CardCatalog`：
  - 接收素材来源 `CardSource`（目录地址和 `revision`），读 `index.json`、Card Text 和附魔效果材质。`index.json` 以外的文件名都是内容摘要，不会变；`revision` 变了才重读 `index.json`。
  - 把卡牌引用变成 `GameCard`：卡名、卡图、附魔效果、悬停详情和点击弹窗。
  - 卡名的来源依次是：中文用中文 Card Text 的标题，英文用索引的 `names`；然后是这局战斗快照里的名字；PvP 遭遇显示 PvP；最后是 TemplateId 的前 8 位。
  - 数据缓存在模块里，用 `useSyncExternalStore` 读取，不依赖 react-query。读取失败的请求下次会重试。
  - 弹窗由产品通过 `dialogHost` 放进自己的弹窗栈（安装器用 `ModalSource`）。
- `design/ui/game/terms.ts` 持有游戏术语的中英文 `gameTerms`：品阶、附魔、卡牌详情、回放的标签、地点类型和操作名。产品不再自己定义这些词。
- `design/ui/game/Decisions.tsx` 的 `DecisionReplay` 和 `StreamFrames` 是回放接入。产品只传 `buildDecisionPath` 产出的 `replay` 列表、战斗结果和这局自己的卡名。
- 由此修改 ADR-0018 的两条规则：
  - 组件库可以引入 `react` 和根目录 `runs/`（相对路径），不能引入其他运行时依赖。
  - 两端相同的游戏数据读取和游戏术语归组件库。产品自己的数据（这一局、路由、产品文案）仍留在产品里。
- 素材来源：
  - 网站读同源的 `/card-art/`（Worker 转发 CDN）。
  - 安装器读本机导出（[ADR 0024](0024-card-catalog-in-rust.md) 的 crate），不联网：
    - 安装器用自己的可执行文件以子进程运行导出（`bppinstaller card-catalog`）。导出峰值约 1.3 GB 内存，而且发布配置是 `panic = "abort"`，放在子进程里不会影响安装器本身。子进程在 stdin 关闭时退出，所以不会比安装器活得久，也不会挡住安装器的更新。它以较低优先级运行（Windows 为 BELOW_NORMAL，其他平台 nice 10），并读取 `GameData.db` 和 `zh-CN.bytes` 的副本，因为游戏更新时会替换这两个文件。
    - 读资源包只在一个线程上做；解码、缩放和 WebP 编码（卡图和附魔效果贴图）在多个线程上并行，安装器用一半的 CPU 核心，最多 4 个。输出与单线程逐字节一致。在 Apple Silicon 上整套导出约 30 秒。
    - 子进程在 stdout 上报进度，安装页的"游戏资产"区显示状态（与游戏一致、游戏已更新、生成中、生成失败）、进度条、生成时间和占用空间，并可以手动重新生成。
    - 导出到 `<缓存目录>/BazaarPlusPlusInstaller/card-catalog/`，一直用同一个目录，并把上一次的 `index.json` 作为 `previous`，所以游戏删掉的卡仍有卡图和卡名。`index.json` 原子替换，旧的 Card Text 文件删除。
    - `revision` 取自导出格式版本、`StreamingAssets/aa/catalog.hash`、GameData 的 ETag 和 zh-CN 翻译的 ETag。安装器启动时、安装页和历史详情页打开时检查，`revision` 变了才重新导出；导出成功后由子进程写下 `revision`。导出失败的 `revision` 不再自动重试，只在手动重新生成时重试。
    - 页面通过自定义协议 `bppcard://` 读取文件。协议只提供 `index.json` 和内容摘要文件名，并带 `Access-Control-Allow-Origin`。
  - 安装器的浏览器预览没有本机导出，经开发服务器的 `/card-art/` 代理读已发布的目录。它还在 `vp dev` 下提供网站测试里那局固定的真实对局（`src/api/preview-run.json`，由 `bazaarplusplus-installer/tools/export-preview-run.ts` 生成）。
- 对手英雄名：安装器在 Rust 里解析（`history/hero.rs`，它的测试读 `hero-aliases.json`），网站用 `heroName`。`DecisionReplay` 接收已经解析好的名字。

## 否决的方案

- 安装器从 CDN 下载卡图和附魔效果，按需缓存（#1215 原方案）：用户要求断网可用，并选择从本机游戏导出。
- 在安装器主进程里直接调用导出：导出时的 panic 会让安装器直接退出，1 GB 的内存峰值也留在安装器进程里。
- 每次导出用一个新的空目录：游戏删掉的卡会失去卡图，导出期间也没有可用的目录。

## 代价

- 安装器第一次导出完成前（约 30 秒），以及找不到游戏或游戏缓存时，卡牌只有战斗快照里的名字，没有卡图、卡牌详情和附魔效果。页面在导出时显示一行状态。
- 网站中文页面的卡名改为中文，打开对局详情时会读中文 Card Text（约 324 KB，gzip 后）。
- 组件库的改动现在也会影响 `runs/` 的使用方式；改 `runs/` 的展示接口时，要跑 installer、site 和 UI 文档站的 check 与 test。

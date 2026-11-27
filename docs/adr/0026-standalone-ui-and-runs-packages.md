# 独立 `@bazaarplusplus/ui`、`@bpp/runs` 包与前端白名单 Bun Workspaces

状态：已接受（2026-11-27）

## 背景

[ADR-0018](0018-one-web-component-library.md) 和 [ADR-0023](0023-shared-run-decisions.md) 把共享 Web 组件和决策投影放在根目录 `design/ui/` 与 `runs/`，无 `package.json`、无独立构建，由各消费端通过 Vite alias 和 tsconfig paths 源码直编。随着组件库成熟（接入 `CardCatalog`、`DecisionReplay` 和 WebGL2 Enchantment FX，见 [ADR-0025](0025-card-catalog-in-the-ui-kit.md)）以及在线组件文档 `bazaarplusplus-ui-docs/`（`https://ui.bazaarplusplus.com`）上线，旧结构暴露出四个限制：

1. **外部项目无法直接调用**：`design/ui/` 没有标准包边界、类型声明或样式导出，且耦合了 `design/components/bundle.css` 中的产品私有样式及相对路径 `../../../runs/decisions`。
2. **跨端设计真源与 Web 组件混在同一目录**：改动任意 React 组件就会触发 `design/` 的全平台门禁（包括 Android、Server、Bot）。
3. **样式层叠与消费形态单一**：`bundle.css` 处于未分层 CSS，既不利于 Tailwind CSS v4 工具类覆盖，也让仅用基础原语的外部应用不得不拉入游戏组件样式。
4. **Claude.ai Design System Artifact 冗余**：`design/design-system.json` 与 23 份 `preview.html` 原本只用于给外部工具维护设计系统预览，已被真实运行、持续测试的 `bazaarplusplus-ui-docs/` 完整取代。

## 决定

### 1. 拆分跨端设计真源与独立 Web UI Kit（`@bazaarplusplus/ui`）

- `design/` 回归纯跨端设计真源（`tokens.json`、`build.ts`、品牌手册、文案规范、品牌标志、封面图、桌面游戏内 Kit 贴图），继续服务 Web、Mod、Android、Server 邮件与 Bot。
- Web 组件库迁移至 `packages/ui/`，包名 `@bazaarplusplus/ui`（`peerDependencies`: `react@^19.0.0`, `react-dom@^19.0.0`），采用双入口导出：
  - `@bazaarplusplus/ui`：通用 UI 原语与图标（`Button`、`Badge`、`Dialog`、`PageHeader`、`SegmentedControl`、`StatusBanner`、`StatusPanel`、icons）；
  - `@bazaarplusplus/ui/game`：*The Bazaar* 领域组件（`CardTile`、`Board`、`CardDetail`、`DayCard`、`RunReplay`、`DecisionReplay`、`FrameInspector`、`CardCatalog`、`createCardCatalogClient`、`gameTerms` 及结构化 replay 类型）。
- 样式拆分为 `packages/ui/styles/` 并包裹在 `@layer components` 中，由 `design/build.ts` 生成 `tokens.css` 与 Tailwind v4 `@theme inline` 桥接文件 `theme.css`，与手写的 `primitives.css`、`game.css`、`all.css` 一并作为子路径导出（`./tokens.css`、`./theme.css`、`./primitives.css`、`./game.css`、`./styles.css`）。这样 Tailwind CSS v4 工具类可自然覆盖组件样式，非 Tailwind 项目亦可直接引入。
- 原 `design/components/bundle.css` 中仅属单个产品的样式迁回对应产品（installer 壳层样式迁至 `bazaarplusplus-installer/src/styles/shell.css`，site 的 `.panel` 与 `.bpp-hero-badge*` 迁至 `bazaarplusplus-site/src/styles/global.css`）。
- 构建产物使用 `vp pack`（`unbundle: true`, `dts: true`, `sourcemap: true`），保留 17 个 WebGL2 Enchantment FX 动态 shader 模块拆分，确保消费端可按需加载且不破坏 `bazaarplusplus-site` 的 `route-chunks.json`。
- 仓库内零构建开发体验通过自定义条件 `"@bpp/source"` 实现（消费端 Vite `resolve.conditions` 与 `tsconfig.json` `customConditions` 启用）；发布前由 `packages/ui/tools/publish-manifest.ts` 剥离 `"@bpp/source"`。
- 外部可用性增强：
  - `CardCatalog` 新增 `createCardCatalogClient({ fetch })` 与 `client` prop，支持隔离缓存、自定义 fetcher 与手动清理，同时保留无参默认行为和 ADR-0025 失败重试语义；
  - `CardDetail` 新增受控 `showAllEnchantments` / `onShowAllEnchantmentsChange` props，传入时绕过共享 `localStorage` 以适配 SSR 与外部状态管理；
  - `packages/ui/tools/pack-check.ts`（`just ui::pack-check`）每次打包真实 `.tgz` 并在外部 React 19 + Tailwind 4 与纯 CSS 两个隔离消费工程中执行 `tsc --noEmit` + `vp build` 冒烟验证。
- 发布目标首选 GitHub Packages（`https://npm.pkg.github.com`），由 `.github/workflows/publish-ui.yml` 在 `ui-v*` tag 或手动 dispatch 时执行校验并发布（使用 `GITHUB_TOKEN` + `packages: write`，不传 GitHub Packages 不支持的 `--provenance`）。

### 2. 独立内部包 `@bpp/runs`（`packages/runs/`）

- 原根目录 `runs/` 迁移至 `packages/runs/`（`name: "@bpp/runs"`, `private: true`），保留子路径导出（`./msgpack`、`./decisions`、`./decision-path`、`./message-stream/*`），保持懒加载 chunk 边界。
- `@bazaarplusplus/ui/game` 不再依赖 `@bpp/runs`；`packages/ui/src/game/replay-types.ts` 定义结构化类型，`DecisionReplay` 与 `StreamFrames` 仅依赖这些结构化接口，外部项目无需安装内部解析包即可复用回放组件。

### 3. 白名单根 Bun Workspaces

- 根目录 `package.json` 启用白名单 `workspaces`：`["packages/*", "bazaarplusplus-site", "bazaarplusplus-installer", "bazaarplusplus-ui-docs"]`，合并这 5 个成员的 `bun.lock` 与根 `overrides`，消除消费端路径别名和 React 去重 hack。
- `bazaarplusplus-server` 与 `bazaarplusplus-bot` 仍保持独立目录与独立 `bun.lock`，不加入根 workspace，因为两者依赖 Vitest 4 + `@cloudflare/vitest-pool-workers@0.23.0`（见 [ADR-0017](0017-vite-plus.md)）。
- 根目录 `release/version-sync.ts` 同步更新根 `bun.lock` 中 `bazaarplusplus-installer` 的 workspace 版本记录；`scripts/vite-plus.ts` 将 `packages/ui` 纳入 `vite-plus` 版本对齐范围。
- CI（`.github/scripts/checks-scope.ts` 与 `.github/workflows/checks.yml`）新增独立 `ui` scope 与 job：仅改 `packages/ui/` 时只跑 `ui`、`installer-frontend`、`site`、`ui-docs`，不再扩散至 `mod`、`android`、`server`、`bot`。

### 4. 下线 Claude.ai Design System Artifact

- 删除 `design/design-system.json` 与 `design/components/*/preview.html`，设计系统的在线预览与 Token 文档唯一归属 `bazaarplusplus-ui-docs/`（`https://ui.bazaarplusplus.com`）。

## 后果

- 本决定取代 [ADR-0018](0018-one-web-component-library.md)，并修订 [ADR-0017](0017-vite-plus.md)（关于根 Bun workspace 与 `packages/ui`）、[ADR-0023](0023-shared-run-decisions.md)（`runs/` 迁至 `packages/runs/`）和 [ADR-0025](0025-card-catalog-in-the-ui-kit.md)（`CardCatalog` 迁至 `packages/ui/src/game/` 并支持可注入 `client`、`@bazaarplusplus/ui` 不再直接引用 `runs/`）。
- 外部项目可通过 GitHub Packages（需配置 `@bazaarplusplus:registry=https://npm.pkg.github.com` 与 `read:packages` 凭据）直接安装 `@bazaarplusplus/ui`，按需引入原语或游戏组件，并自由搭配 Tailwind CSS v4 或纯 CSS。
- `lefthook.yml` 中的 `ui` 钩子调用 `just ui::check-fast`（不清理 `dist/`），避免与并行运行的消费端 `vp lint --type-aware` 竞争 `packages/ui/dist/`；完整 `vp pack` 与 `pack-check` 在 `just ui::check` 和 CI `ui` job 中执行。

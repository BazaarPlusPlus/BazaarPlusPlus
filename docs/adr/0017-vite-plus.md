# 根工具、installer 和 site 用 Vite+ 承载 JS 工具链

## 背景

到 2026-11-09，根工具、installer、site 和 server 各自安装 oxlint、oxfmt、Vitest，installer 和 site 还安装 Vite。每个项目有自己的 `.oxlintrc.json` 和 `.oxfmtrc.json`，工具版本也各不相同（oxlint 1.90 到 1.92）。

Vite+（`vite-plus`）把 Vite、Vitest、oxlint、oxfmt 合成一个 `vp` 命令，并固定这些工具的版本。site 的 [ADR 0002](../../bazaarplusplus-site/docs/adr/0002-oxc-toolchain.md) 选的 oxc 工具链不变，只是改由 Vite+ 提供。

## 决定

根工具、installer 和 site 依赖 `vite-plus`，版本固定到确切的发布号：

- lint 和格式配置写在各自 `vite.config.ts` 的 `lint` 块和 `fmt` 块，不再有 `.oxlintrc.json` 和 `.oxfmtrc.json`。
- 脚本用 `vp test`、`vp lint`、`vp fmt`、`vp build`、`vp dev`。`vp check` 一次跑格式、lint 和类型检查。
- `vite` 依赖指向 `vite-plus` 的 core（`npm:@voidzero-dev/vite-plus-core`），`overrides` 里同样指过去。
- 根目录的 lint 和格式化覆盖除各 `bazaarplusplus-*` 项目和 Markdown 以外的全部文件。各项目自己检查自己。

server 暂不迁移。它的测试用 `@cloudflare/vitest-pool-workers` 在 workerd 里跑，提供 D1、R2 绑定和每个测试独立的存储。这个包到 0.23.0 只支持 Vitest 4，而 Vite+ 1.x 自带 Vitest 5；在 Vitest 5 下 server 测试报 17 个未处理错误。等这个包支持 Vitest 5 后再迁移 server。

## 后果

- oxlint、oxfmt、`oxlint-tsgolint` 和 Vitest 的版本由 `vite-plus` 决定，不能在项目里单独升。升 TypeScript 主版本时，要换成带对应 `oxlint-tsgolint` 的 `vite-plus`。
- `vite-plus` 和 `vite` 别名必须一起升，Dependabot 把它们放在同一个分组里。
- 仓库没有 Bun workspace。`vp migrate` 要在每个项目目录里分别运行：在根目录运行时，它会改写子项目的导入。
- server 仍直接用 oxlint、oxfmt 和 Vitest 4，格式选项留在 `.oxfmtrc.json`，和另外三份 `fmt` 块保持一致。

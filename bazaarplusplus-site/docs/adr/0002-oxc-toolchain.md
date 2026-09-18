# 0002. 用 oxc 工具链承载 lint 与格式化，并绑定 TypeScript 7

Status: Accepted

## Context

仓库此前没有任何 lint 或格式化工具，验证只有 `npm test` 和 `npm run typecheck`。同时 TypeScript 7 已发布，是 Go 原生移植版，`tsc --noEmit` 明显更快。

两者存在硬冲突。TypeScript 7 的 npm 包不再导出 JS 编译器 API——`exports["."]` 只指向 `./lib/version.cjs`，其余是各平台原生二进制。`typescript-eslint` 通过 `require("typescript")` 取编译器 API，因此在 TS 7 下直接抛错拒绝启动：

```
typescript-eslint does not support TS 7.0.
```

这不是 peer range 滞后。截至决策时 `typescript-eslint` 的 `latest`（8.67.0）与 `canary` peer 均为 `typescript >=4.8.4 <6.1.0`，不存在支持 TS 7 的版本；上游跟踪见 typescript-eslint#10940，目标是 TS >=7.1。用别名安装 TS 6 也绕不过，因为解析入口是硬编码的 `require("typescript")`。

因此在 ESLint 路线下，类型感知 lint 与 TypeScript 7 只能二选一。

## Decision

采用 oxc 工具链：`oxlint` 做 lint，`oxlint-tsgolint` 提供类型感知规则，`oxfmt` 做格式化。TypeScript 保持 7.x。

`oxlint-tsgolint` 基于 tsgolint，不依赖 TS 的 JS 编译器 API，因此类型感知 lint 与 TypeScript 7 可以共存——这是 ESLint 路线拿不到的组合。

`npm run lint` 固定带 `--type-aware`；不带该 flag 只跑语法级规则，会漏掉一整类检查。

`.oxlintrc.json` 启用 `correctness` / `suspicious` / `perf` 三个 category 为 error。`pedantic` 不启用：在本仓库产出 238 条几乎全为噪音的告警（`require-unicode-regexp`、`max-lines-per-function` 等）。以下规则单独关闭，因为它们对本仓库的既有写法系统性误报：

| 规则 | 关闭原因 |
| --- | --- |
| `react/react-in-jsx-scope` | 项目用 `jsx: react-jsx` 自动运行时，该规则产生 404 条误报 |
| `unicorn/no-array-sort`、`unicorn/no-array-reverse` | 全部 16 处调用都已作用在 `[...x]` 或新建的 `.map()` 结果上；规则无法区分，恒为误报 |
| `eslint/no-await-in-loop` | 命中的是 `hero-metrics-dataset.ts` 的重试循环，串行 await 正是其语义，规则建议的 `Promise.all` 是错的 |
| `import/no-unassigned-import` | 命中 `main.tsx` 的 CSS 副作用导入 |
| `jsx-a11y/prefer-tag-over-role`、`jsx-a11y/control-has-associated-label` | 前者要求把 `role="group"` 的 div 换成 `fieldset` 等语义标签，是主观改写；后者看不穿动态 `{label}` 子节点 |
| `typescript/no-unsafe-type-assertion` | 主要命中 CSS 自定义属性的 `as CSSProperties`，是 React 的既定写法 |
| `typescript/consistent-return` | 命中 `DialogShell` 的 useEffect 早返回，是标准 React 清理函数写法 |
| `typescript/unbound-method` | 命中 `spaLocation.replaceScope`，它是闭包捕获 `current`/`adapter`/`notify` 的对象字面量简写方法，不触碰 `this` |

`oxfmt` 只接管代码，`**/*.md` 排除在外：它按字符数而非显示宽度对齐表格，会把仓库里以中文为主的文档表格排歪。

## Consequences

- `typescript` 与 `oxlint-tsgolint` 的主版本必须同步升级。`oxlint-tsgolint` 的版本号对齐 TypeScript 主版本（`7.0.2001` 对应 TS 7），单独升 `typescript` 会让类型感知 lint 失配。
- 放弃 typescript-eslint 的规则广度。若后续需要它独有的规则，需回退到 TypeScript 6，届时应新开 ADR 推翻本决策。
- 首次接入 `oxfmt` 产生了一次性全量重排。此后靠 `npm run format:check` 本地把关，仓库没有 CI 门禁。
- 关闭的规则清单是对本仓库既有写法的判断，不是通用建议；写法变化时应重新评估。

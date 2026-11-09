# installer 和 site 共用一套 Web 组件库

## 背景

到 2026-11-09，`design/` 已经是 token、`bpp-*` 组件样式（`design/components/bundle.css`）和组件规格的唯一来源，但没有组件代码。`design/components/index.d.ts` 只是文档。installer 在 `src/components/ui/` 用 `bpp-*` 类实现 Button、StatusBadge、StatusPanel、Dialog 等组件；site 在 `src/shared/components/` 用 Tailwind 工具类把同一套取值再写一遍。两份实现已经分开：

- StatusPanel 的属性不同。site 有 `headingLevel` 和 `progressLabel`，installer 有 `compact` 和 `icon`，标题级别写死为 h3。
- site 的 Button 能渲染成链接，installer 的不能；installer 的 Button 有 `busyLabel`，site 的没有。
- 弹窗是两套行为：installer 用原生 `<dialog>`，site 用 `div` 自己实现遮罩、Escape 和焦点恢复。

两个产品都用 React 19、Tailwind 4 和 Vite+，没有技术原因要维护两份实现。做法参照 oinkvm 的 oink-ui：组件、样式和测试在一处，应用只写页面布局。

## 决定

- `design/ui/` 是 Web 组件库，入口 `design/ui/index.ts`。它没有 `package.json`、不单独构建、不发布到 npm。installer 和 site 用 Vite 别名和 tsconfig 路径把它当作 `@bpp/ui` 编译进自己。
- 组件只用 `bundle.css` 的类，不用 Tailwind、clsx 或 lucide-react，只依赖 `react`。site 和 installer 一样直接引入 `bundle.css`。
- 两个产品都用、或规格在 `design/components/` 里且不依赖产品的组件放进组件库：Button、Badge、StatusPanel、StatusBanner、Dialog、PageHeader、SegmentedControl，以及它们画的图标。依赖产品数据、路由、i18n 或弹窗栈的组件留在产品里：installer 的 Toast、ConfirmDialog、ProblemBanner、PageShell，site 的 HeroBadge 和页面外框。HeroBadge 改用 `bundle.css` 已有的 `.bpp-hero-badge*` 类。
- 组件库里的文件从引入它的产品解析 React：两边的 Vite 配置设 `resolve.dedupe: ['react', 'react-dom']`，tsconfig 把 `react` 映射到本项目的 `@types/react`。开发服务器的 `server.fs.allow` 加上 `../design`。
- 组件库自己做类型检查和 lint：`design/ui/tsconfig.json` 取两个产品编译选项的并集，`design/ui/vite.config.ts` 用产品的 React 规则；根目录的 `typecheck` 和 `lint` 脚本运行它们。根目录 devDependencies 只为此加 `@types/react`。这扩展了 [ADR-0017](0017-vite-plus.md)：根目录检查非项目文件，但 `design/ui` 用自己的配置。
- 测试放在产品里，因为只有产品装了 jsdom。installer 的 `src/components/ui/library.test.tsx` 测组件行为。两个产品各有一个 `bundle-classes` 测试，它用 `design/ui-classes.ts` 检查组件库和本产品用到的 `bpp-*` 类都定义在样式表里。只改 site 的 PR 也会运行这个检查。
- 依赖只有一个方向：产品引入 `design/`，`design/` 不引入产品。

合并时按规格选行为：

- Button 忙碌时禁用（`Button/README.md`）。传 `href` 时渲染成链接，禁用或忙碌时移出 Tab 顺序并忽略点击。
- Badge 默认带圆点，状态读作"圆点 + 文字"；中性事实（地区、快照标签）传 `dot={false}`。
- StatusPanel 默认 h2，installer 的页面里传 `headingLevel={3}`；h1 用页面级字号。
- Dialog 用原生 `<dialog>`，并保留 site 的两项行为：打开时锁住页面滚动，关闭后把焦点还给打开它的控件。`dismissible` 关掉 Escape 和点遮罩关闭；installer 按弹窗栈的策略传入。

## 后果

- 共享组件改一次，两个产品同时生效。site 的按钮、徽标、状态面板和弹窗改用 `bundle.css` 的取值，site 的 Linux 截图 golden 随之重生成。
- 组件库里写 Tailwind 工具类不会生效：Tailwind 不扫描 `../design`。`bundle-classes` 测试会拦下 `bundle.css` 里没有的类名。
- 组件库不能有 `react` 以外的运行时依赖，因为它没有自己的 `node_modules`。
- `design/components/index.d.ts` 删除，`design/tokens.json` 的 `components` 映射指向 `design/ui/`。组件库不发布到 claude.ai 的 Design System artifact。
- site 的部署工作流监听 `design/components/bundle.css`、`design/ui/**` 和 `design/ui-classes.ts`。

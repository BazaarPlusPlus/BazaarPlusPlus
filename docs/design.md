# 视觉规范

installer、bazaarplusplus.com 与 Android App 共用一套设计系统：跨端设计真源（token、品牌手册、文案规范、品牌标志与游戏内 Kit 贴图）放在仓库根目录的 `design/`，独立可发布的 React 19 + Tailwind 4 Web UI Kit（`@bazaarplusplus/ui`）放在 `packages/ui/`，取值不在本文重复。完成度以 Linear、Steam 与 Discord 客户端为参照。OBS 叠加层的直播画面暂时保持原有样式，不在本规范范围内。

游戏内面板优先借用游戏原生美术（字体、按钮和边框贴图、奖杯、卡牌），BPP 自己画的颜色（底色、遮罩、文字、金线、状态色）统一来自 `design/tokens.json` 的 `ingame-*` token，`just design::build` 生成桌面 UI 和 Mobile UI（Android 宿主编译）的 `InGameTheme.g.cs`。两个平台共用这一套视觉，只有布局分开：桌面按鼠标布局，安卓按 `TouchChrome` 的触屏布局。桌面图鉴是唯一的例外：它用中性炭灰底色和冷蓝选中态（`ingame-neutral-*`、`ingame-select`，mod ADR-0026）。面板不写颜色字面量，比例透明度走 `InGameTheme.WithAlpha`；mod 的 `InGameUiColourTests` 与 `MobileUiColourTests` 检查这一点（mod ADR-0026）。细则见 `design/README.md` 的 In game 一节。

## Android App 的启动器

Android App 是启动器：一页里显示游戏、状态和下一步操作，做成原生手机 App 的样子：封面图满幅铺到屏幕顶部，状态栏压在图上；下方面板盖住封面下沿，放标题与状态、三栏版本信息、一行来源和钉在底部的主操作；右上角只浮一个"更多"按钮，没有顶栏和页脚。它用 Android View 实现规格 `design/components/Launcher/`，加载玩家已安装的官方游戏。布局只看窗口大小：窄于 `pane-breakpoint`（600）是一栏；从 600 起封面在上、下面两栏等宽，所以折叠屏展开后折痕落在两栏的间隙里；只有窗口宽过高且不窄于 `cover-pane-breakpoint`（840）时，才左边封面、右边内容。辅助功能字号保持一栏。新版 App 只是提醒，不拦启动：每个版本弹一次对话框。

[组件文档](https://ui.bazaarplusplus.com)（`bazaarplusplus-ui-docs/`）提供可操作的组件示例、设计变量与源码，是设计系统的在线预览入口。

## 设计系统在哪里

| 内容 | 唯一的编辑源 | 产品里怎么用 |
|---|---|---|
| 颜色、间距、圆角、尺寸、阴影、动效、字体栈、字号 | `design/tokens.json` | `just design::build` 生成 `packages/ui/styles/tokens.css`（导出为 `@bazaarplusplus/ui/tokens.css`）和 Tailwind v4 `@theme inline` 桥 `packages/ui/styles/theme.css`（导出为 `@bazaarplusplus/ui/theme.css`），installer、site 与 ui-docs 直接 `@import`；产品颜色、间距、圆角、尺寸和字号另外生成 Android App 的 `res/values/bpp_tokens.xml` |
| 共享 `bpp-*` 组件样式（`@layer components`） | `packages/ui/styles/primitives.css` 与 `packages/ui/styles/game.css` | 通过 `@bazaarplusplus/ui/styles.css`（或按需 `primitives.css` / `game.css`）在 installer、site、ui-docs 及外部项目中 `@import` |
| Web 组件库（通用原语与 The Bazaar 游戏组件） | `packages/ui/`（`@bazaarplusplus/ui` 与 `@bazaarplusplus/ui/game`） | installer、site、ui-docs 通过 workspace 依赖引入，外部项目通过 GitHub Packages 安装；见 [ADR-0026](adr/0026-standalone-ui-and-runs-packages.md) |
| 视觉、对齐、动效与组件规则 | `design/README.md` 与 `design/components/*/README.md` | 全部产品与 `@bazaarplusplus/ui` |
| 文案：语气、术语表、大小写与标点、句式 | `design/writing.md` | installer 的 `src/i18n/messages.ts` 与 site 的 `src/content/site-copy.ts` |
| 游戏内颜色 | `design/tokens.json` 的 `ingame-*` | `just design::build` 生成 mod 桌面 UI 与 Mobile UI 的 `InGameTheme.g.cs` |
| 品牌标志 | `design/assets/Logos/bpp-mark.svg` | `just design::build` 复制到 installer 的 `static/brand/`；site 页头使用现有的 `public/bazaarplusplus-icon.webp` 应用图标；Android App 用生成的矢量图 `res/drawable/bpp_mark.xml`；server 的验证码邮件用生成的 PNG `bazaarplusplus-server/src/modules/email-mark.g.ts` |

## 改动流程

- 改 token 或标志：改 `design/` 里对应的文件，运行 `just design::build`，把重新生成的 `packages/ui/styles/tokens.css`、`packages/ui/styles/theme.css`、Android 资源和复制出的标志一起提交。`just design::check` 会拦下过期的生成文件、`packages/ui/styles/{primitives,game}.css` 里没有对应 token 的 `var()` 或 px 字号，以及与 `hero-*` token 不一致的 site 英雄颜色。
- 同一个变更里跑 `just ui::check ui::test ui::pack-check`，以及 installer、site、ui-docs（改动 token 时还需 Android App、mod 与 server）的 check 和 test；installer 的外壳快照（`src/__shell_snapshots__/`）用 `BPP_UPDATE_GOLDENS=1` 重新生成并逐个核对。
- 组件里不写颜色、字号或尺寸字面量：共享组件样式写在 `packages/ui/styles/` 的 `@layer components` 内；页面布局用 `var(--token)` 或 `@bazaarplusplus/ui/theme.css` 映射出的 Tailwind v4 工具类（`bg-panel`、`text-text-2`、`border-line`、`rounded-control`、`text-body`）。`text-<样式>` 只设字号，不设行高；Tailwind 自带的 `text-sm`、`text-lg` 仍可用。
- 改了 `packages/ui/` 就跑 `just ui::check ui::test ui::pack-check` 以及 installer、site、ui-docs 的 check 与 test；`packages/ui/test/styles-classes.test.ts` 与各产品的 `bundle-classes` 测试会拦下样式表里未定义的 `bpp-*` 类名。

## 约定

规则全文在 `design/README.md`，这里只列最常被问到的几条：

- **单一深色主题，层级靠填充而不是阴影。** 阴影只给弹窗、菜单、提示这类浮层。
- **琥珀色只做强调；状态是"圆点 + 文字"。** 颜色是第三个信号，不能单独承载含义。
- **一套刻度。** 字号、间距、控件高度、行高、图标尺寸和圆角都只能取 token 里的值，不在刻度上的值就是错的。
- **让布局去解释。** 每个东西只有一个标签，不加重复标题、提示或描述的第二行。
- **每个元素都对齐到一条共享的边、中线或基线**，不靠目测。
- **字体用系统 UI 字体，不从 CDN 加载**，大陆网络也能正常显示。
- **每类组件只有一个实现。** 两个产品都用的组件放在 `packages/ui/`（`@bazaarplusplus/ui`）；只有一个产品用、或要读产品数据的组件（installer 的 Toast、ConfirmDialog，site 的 HeroBadge）留在该产品里，并用组件库拼出来。卡牌目录、游戏术语和整局回放的接入两端相同，放在 `@bazaarplusplus/ui/game` 里（[ADR-0025](adr/0025-card-catalog-in-the-ui-kit.md)、[ADR-0026](adr/0026-standalone-ui-and-runs-packages.md)）。页面不手写按钮样式，不复制类名字符串。

## macOS 标题栏

installer 在 macOS 上使用覆盖式标题栏（`tauri.conf.json` 的 `titleBarStyle`），应用自己的顶栏就是窗口标题栏：左侧为红绿灯留出位置，全屏时收回这段留白；顶栏整体是拖拽区域，按钮、链接和弹出菜单除外。Windows 继续使用无边框窗口和应用内的窗口控制按钮。

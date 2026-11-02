# 视觉规范

installer、bazaarplusplus.com、Android App 与 iOS App 共用一套设计系统，放在仓库根目录的 `design/`：token、共享组件样式、品牌手册、文案规范和品牌标志都只在这里维护，取值不在本文重复。完成度以 Linear、Steam 与 Discord 客户端为参照。OBS 叠加层的直播画面暂时保持原有样式，不在本规范范围内。

游戏内面板优先借用游戏原生美术（字体、按钮和边框贴图、奖杯、卡牌），BPP 自己画的颜色（底色、遮罩、文字、金线、状态色）统一来自 `design/tokens.json` 的 `ingame-*` token，`just design::build` 生成桌面 UI 和 Mobile UI（两个宿主共用）的 `InGameTheme.g.cs`。两个平台共用这一套视觉，只有布局分开：桌面按鼠标布局，安卓按 `TouchChrome` 的触屏布局。细则见 `design/README.md` 的 In game 一节。

## 两个 App 的启动器

iOS App 和 Android App 都是启动器：一页里显示游戏、状态和下一步操作。两端都用原生 UI（SwiftUI、Android View）实现同一份规格 `design/components/Launcher/`，状态用词、按钮和布局一致，只是游戏来源不同：iOS 登录 Steam 下载 Mac 版，Android 加载已安装的官方游戏。布局只看窗口宽度：窄于 `pane-breakpoint`（600）是一栏，宽于它是居中的两栏等宽，所以 iPhone Duo 合上是一栏、展开是两栏，折痕落在两栏的间隙里；辅助功能字号保持一栏。新版 App 只是提醒，不拦启动。

## 设计系统在哪里

| 内容 | 唯一的编辑源 | 产品里怎么用 |
|---|---|---|
| 颜色、间距、圆角、尺寸、阴影、动效、字体栈 | `design/tokens.json` | `just design::build` 生成 `bazaarplusplus-installer/src/styles/tokens.css`；site 在 `dev` 和 `build` 前用 `npm run tokens` 把它复制到被 git 忽略的 `bazaarplusplus-site/src/styles/installer-tokens.css`；产品颜色、间距、圆角、尺寸和字号另外生成 Android App 的 `res/values/bpp_tokens.xml` 与 iOS App 的 `app/Theme/BppTokens.g.swift` |
| 共享 `bpp-*` 组件样式 | `design/components/bundle.css` | installer 的 `src/styles/index.css` 直接 `@import`；site 用 Tailwind 工具类实现同一套规格 |
| 视觉、对齐、动效与组件规则 | `design/README.md` 与 `design/components/*/README.md` | 两个产品 |
| 文案：语气、术语表、大小写与标点、句式 | `design/writing.md` | installer 的 `src/i18n/messages.ts` 与 site 的 `src/content/site-copy.ts` |
| 游戏内颜色 | `design/tokens.json` 的 `ingame-*` | `just design::build` 生成 mod 桌面 UI 与 Mobile UI 的 `InGameTheme.g.cs` |
| 品牌标志 | `design/assets/Logos/bpp-mark.svg` | `just design::build` 复制到 installer 的 `static/brand/`；site 页头使用现有的 `public/bazaarplusplus-icon.webp` 应用图标；Android App 用生成的矢量图 `res/drawable/bpp_mark.xml`，iOS App 用生成的 `app/Theme/BppMark.g.swift` |

`design/` 同时发布为 claude.ai 上的 [Design System artifact](https://claude.ai/artifact/4Xv54mp3y68YPc95PWNGVk)，用来浏览组件预览和品牌手册；仓库是源头，artifact 跟随仓库更新。维护流程见 `design/AGENTS.md`。

## 改动流程

- 改 token、组件样式或标志：改 `design/` 里对应的文件，运行 `just design::build`，把重新生成的 `tokens.css`、Android 资源、iOS 的 Swift 文件和复制出的标志一起提交。`just design::check` 会拦下过期的生成文件、`bundle.css` 里没有对应 token 的 `var()`，以及与 `hero-*` token 不一致的 site 英雄颜色。
- 同一个变更里跑 installer、site 与 Android App 的 check 和 test，iOS App 在 Mac 上重新构建；installer 的外壳快照（`src/__shell_snapshots__/`）用 `BPP_UPDATE_GOLDENS=1` 重新生成并逐个核对。
- 组件里不写颜色、字号或尺寸字面量：installer 用 `var(--token)` 或 `bpp-*` 类，site 用 Tailwind `@theme` 桥映射出的工具类（`bg-panel`、`text-text-2`、`border-line`、`rounded-control`）。

## 约定

规则全文在 `design/README.md`，这里只列最常被问到的几条：

- **单一深色主题，层级靠填充而不是阴影。** 阴影只给弹窗、菜单、提示这类浮层。
- **琥珀色只做强调；状态是"圆点 + 文字"。** 颜色是第三个信号，不能单独承载含义。
- **一套刻度。** 字号、间距、控件高度、行高、图标尺寸和圆角都只能取 token 里的值，不在刻度上的值就是错的。
- **让布局去解释。** 每个东西只有一个标签，不加重复标题、提示或描述的第二行。
- **每个元素都对齐到一条共享的边、中线或基线**，不靠目测。
- **字体用系统 UI 字体，不从 CDN 加载**，大陆网络也能正常显示。
- **每类组件只有一个实现。** installer 的 `src/components/ui/` 与 site 的 `src/shared/components/` 各自按 `design/components/` 的规格实现 Button、Badge、StatusBanner、StatusPanel、弹窗外框等组件；页面不手写按钮样式，不复制类名字符串。

## macOS 标题栏

installer 在 macOS 上使用覆盖式标题栏（`tauri.conf.json` 的 `titleBarStyle`），应用自己的顶栏就是窗口标题栏：左侧为红绿灯留出位置，全屏时收回这段留白；顶栏整体是拖拽区域，按钮、链接和弹出菜单除外。Windows 继续使用无边框窗口和应用内的窗口控制按钮。

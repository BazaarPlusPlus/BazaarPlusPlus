# 视觉规范

installer 与 bazaarplusplus.com 共用一套视觉语言：现代桌面工具的常规做法，完成度以 Linear、Steam 与 Discord 客户端为参照。品牌只保留 BazaarPlusPlus logo；强调色取自 logo 里的琥珀宝石。OBS 叠加层的直播画面暂时保持原有样式，不在本规范范围内；游戏内面板跟随游戏原生 UI。

## Token

颜色、圆角、阴影、动效时长和字体栈的取值只写在 token 文件里，本文不重复：

- installer：`bazaarplusplus-installer/src/styles/tokens.css`，是唯一的编辑源。
- site：`bazaarplusplus-site/src/styles/tokens.css`，是逐字副本。两个项目工具链独立，不能互相 import；`scripts/design-tokens.test.mjs`（由 `just commands-check` 运行）拒绝两份 `:root` 声明出现差异。

改 token 时先改 installer 的文件，再同步到 site，并在同一个变更里跑两个项目的检查。各项目通过 Tailwind `@theme` 把 token 映射成工具类，组件里不写颜色字面量。

## 约定

- **单一深色主题。** 使用场景是游戏前后、常在夜间的桌面，底色为带一点暖调的近黑，不跟随系统切换浅色。
- **层级靠填充而不是阴影。** 画布、侧栏、面板、悬停与选中逐级变亮，面板只加 1px 分隔线。阴影只给浮层：弹窗、菜单、提示。
- **不用装饰效果。** 不用渐变、毛玻璃、发光、文字阴影或暗角；这些是旧版 installer 显得"重"的主要来源。
- **琥珀色只做强调。** 用在主按钮、选中项图标和关键数字上。成功、警告、危险三种状态色只表达状态，不作装饰。
- **状态是"圆点 + 文字"。** 颜色是第三个信号，不能单独承载含义。
- **字体用系统 UI 字体。** 不从 CDN 加载字体，大陆网络也能正常显示。等宽字体只用于路径、地址、端口这类需要逐字核对的内容；表格数字用等宽数字（tabular-nums）。
- **标题上方不加小号大写的 eyebrow 标签。** 标题本身承担层级。
- **动效不阻塞操作。** 页面切换立即完成，新内容只做一次很短的淡入；悬停与按下是快速的颜色过渡；弹窗和菜单进入时做透明度加轻微缩放。尊重系统的"减少动态效果"设置。
- **每类组件只有一个实现。** installer 的 `src/components/ui/` 与 site 的 `src/shared/components/` 分别持有 Button、状态徽标、问题横幅、弹窗外框、空状态等组件；页面不手写按钮样式，不复制类名字符串。

## macOS 标题栏

installer 在 macOS 上使用覆盖式标题栏（`tauri.conf.json` 的 `titleBarStyle`），应用自己的 40px 顶栏就是窗口标题栏：左侧为红绿灯留出位置，全屏时收回这段留白；顶栏整体是拖拽区域，按钮、链接和弹出菜单除外。Windows 继续使用无边框窗口和应用内的窗口控制按钮。

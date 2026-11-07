The start screen of the Android App: one game, its status and the one action that status allows, laid out like a native mobile app rather than a web page. The App builds it in Android Views from this spec and the generated tokens. It loads the official game the player installed.

Source: `bazaarplusplus-android/app/src/main/res/layout/launcher.xml`, `launcher_panes.xml` and `launcher_wide.xml` (with their `launcher_*` parts), `launch/LauncherScreen.java` (what to show) and `launch/LauncherActivity.java` (rendering), over the generated `bpp_tokens.xml`.

## Anatomy

One page, no navigation, no app bar and no footer. In order:

1. **Cover.** The game's art, full bleed from the top edge of the screen: the status bar sits over the art, with no gradient, scrim, logo overlay or shadow. The logo stays clear of the system's areas (see Cover art). It runs 24 under the panel. Until the art decodes, the cover is plain `bg-hover`.
2. **More.** One floating button at the top end, inside the status bar inset, its hit area `touch-target`: ⋮ on a 40 `bg-panel` circle that opens a popup menu. The menu holds 导出诊断信息 / Export diagnostics and 关于 / About.
3. **Panel.** `bg-canvas`, top corners `r-dialog` (12), rising 24 over the cover's bottom edge. Inset 24 at the top and 16 at the sides (24 in panes), the safe area below. Its parts are 16 apart:
   - **Title line.** "The Bazaar" in `display`, with the status badge at the line's end. While a download runs, the badge carries the percentage: "正在下载… 27%".
   - **Stats.** Three equal columns, 游戏版本 / 适配版本 / BazaarPlusPlus: the key in `caption` / `text-3` above the value in `body-strong` with tabular figures. At accessibility text sizes they are key-value rows instead, so long values never squeeze.
   - **Banners.** A failure, as an `error` StatusBanner.
   - **Notes.** `caption` / `text-2`, each one sentence.
   - **Source row.** The one list row: where the game comes from (see Source row).
   - **Progress.** While a download with a known size runs (see Progress).
   - **Primary.** Pinned to the bottom of the panel (see Actions by status).

## Layout

The launcher is portrait only on every device, a foldable's cover and inner screens included; the game it starts is landscape only. The layout follows the window's size, never the device model.

| Window | Layout |
|---|---|
| narrower than `pane-breakpoint` (600) | **One column**: the cover on top, the panel below it. Phones, a foldable's cover screen. |
| `pane-breakpoint` and wider, unless the row below applies | **Cover on top, two panes below.** The panel splits into two equal panes 24 apart. The start pane holds the title line and the stats as key-value rows without dividers; the end pane holds the banners, notes, source row, progress and primary. Unfolded foldables (884 × 1104), tablets. |
| `cover-pane-breakpoint` (840) and wider, in a window wider than tall | **Cover pane and content pane.** The cover fills the start half at full height; the content fills the end half, vertically centred with a 24 inset: the title with the badge on the line below it, the stats as key-value rows with `line` dividers, then the banners, notes, primary and source row. More floats at the content pane's top end, without its circle. A window the system does not hold to portrait. A window as tall as it is wide keeps the cover on top: half of it would be too narrow to show the logo. |

- **The panel takes the height it needs; the cover takes the rest**, at least half its width. When the panel needs more than the window, the page scrolls as one, and the primary follows the content instead of pinning. A page that fits its window does not move under a finger: no rubber band, which would show the canvas above the cover.
- **Accessibility text sizes keep one column** whatever the width: Android font scale 1.3 and above.
- **The fold falls in the gutter.** The two panes are equal, so on a foldable whose inner screen is portrait, the hinge falls in the 24 gutter and nothing straddles it.
- **Nothing is drawn into the system areas** except the cover. Content keeps clear of the system bar and cutout insets; the App draws edge to edge.
- **A fold or a text-size change only re-lays the page out.** The App keeps its state and any running check or download: it keeps its check and launch state in the process, not in the Activity.

## Cover art

Two store images of The Bazaar, bundled in the App from `design/assets/Covers/` (root `docs/adr/0014-bundle-cover-art.md`). Each cover picks the image that suits its shape:

| Cover | Image | Crop |
|---|---|---|
| Wide (width at least 1.2 × height): a short cover, when the panel takes most of the window | The library hero (the hunter) | Around the hunter's face, at 70% across and 30% down the art, as near the cover's centre as the art's edges allow |
| Otherwise: phones, foldables folded and unfolded, tablets, the cover pane | The hero capsule (the store's promotional art, with the logo) | From the top, around the logo's centre at 53% across, so the logo stays in view |

The logo on the hero capsule stays clear of the system's areas, so a camera cutout or a side status bar never covers it: below the top inset and before the side inset. Where that moves the art away from the cover's edge, the art's edge is reflected into the gap, which reads as more of the same art. The Android App does not implement this yet.

The images are decoded no larger than the cover needs, because the game later shares the App's process, and released when the game starts. The focus points belong to the bundled images; a new image may need new ones. The source row's tile for the publisher shows the promotional art.

## Status

The badge is the Badge component: dot + word, one tone.

| Status | English | Tone | When |
|---|---|---|---|
| 可以开始 | Ready | ok | Game Code Hash matches the release, or a Development interop |
| 等待适配 | Waiting for adaptation | warn | Installed game code differs from the adapted one and is not older |
| 需要更新游戏 | Game update required | warn | Installed game older than the adapted one |
| 未安装 | Not installed | neutral | Official game not installed |
| 需要更换 | Replacement required | bad | The package holds a retired Android Release APK |
| 离线 | Offline | neutral | No release information and none cached |
| 未发布 | Not released | neutral | No Android App release yet |
| 正在检查… | Checking… | busy | The launch check runs |
| 正在下载… | Downloading… | busy | Interop or plugin download |
| 正在准备… | Preparing… | busy | Staging before the game starts |

When the check itself fails there is no status: the title line shows no badge and the failure is a banner.

## Source row

The panel's one list row: at least `source-row-height` (56), `bg-panel`, `r-touch` (12), a 32 leading tile, the label in `body-medium`, a trailing glyph in `text-3`.

| When | Row | Tap |
|---|---|---|
| 需要更新游戏 | The cover art, 从 Google Play 更新 / Update from Google Play when Google Play is installed and the game came from it; otherwise 从发行商下载 / Download from the publisher, ↗ | Opens the store or the publisher's APK |
| Otherwise, the official game is installed | The official game's icon at `r-panel`, 打开官方游戏 / Open the official game, ↗ | Opens the official game untouched |
| Game not installed or to be replaced, Google Play installed | The cover art, 从发行商下载 / Download from the publisher, ↗ | Opens the publisher's APK in the browser |

The launcher shows no row when neither applies. A busy launcher keeps the row but ignores taps.

## Actions by status

The primary is the one thing to do next, written as verb + object. It is `touch-target` high, the full width of its column, at `r-pill`. A primary that cannot run now is hidden, not disabled; the status and a note say why.

| Status | Primary |
|---|---|
| 可以开始, 等待适配, 需要更新游戏 | 开始游戏 / Play |
| 未发布 | none |
| 离线 | 重试 / Retry |
| 未安装, 需要更换 | 从 Google Play 安装 / Install from Google Play when Google Play is installed; otherwise 从发行商下载 ↗ |
| busy | none |
| failure (no status) | 重试 / Retry |

- **An unadapted or older game still plays** (root ADR 0016), with a note: 等待适配 says "BPP 尚未适配游戏 {gameVersion}，部分功能可能出错。", 需要更新游戏 says "请先把游戏更新到 {adaptedVersion}。" and its source row offers the update.
- **The install source follows the device.** The Android App reads whether Google Play is installed and, for an update, which store installed the game (`PackageManager.getInstallSourceInfo`). A device without Google Play never sees a Google Play action.
- **A failed launch or service step keeps its status and actions.** The failure shows as an `error` banner above them: "未能启动游戏：{reason}". The primary becomes 重试.

## App update

A newer BazaarPlusPlus App is a reminder, never a gate. It never hides an action of its own accord.

It is a Material dialog, once per version, on `bg-panel` at `r-dialog`: the brand mark at 32, "BazaarPlusPlus v{version} 已发布" in `title`, "适配游戏版本 {gameVersion}。" in `text-2`, then 稍后 / Later (text) and 下载 / Download (`r-pill` primary, only when the release links a download). Either button closes it; it does not come back for that version.

## Progress

A download with a known size is the one progress bar in the system. It is a `progress-height` (6) track on `bg-hover` with an `accent` fill and `r-pill` ends. Under it, one `caption` line in tabular figures says what and how much: "插件 · 12.3 MB / 40.0 MB". The busy badge carries the step and the percentage, so there is no second label. Work without a known size shows only the busy badge.

## Stats

| Key | English | Value |
|---|---|---|
| 游戏版本 | Game version | The installed game's version, or —: the short form of `versionName` ("1.0.12825"). |
| 适配版本 | Adapted version | The game version this release adapts: the manifest's `game.gameVersion`. |
| BazaarPlusPlus | BazaarPlusPlus | The App's version, "v6.2.0". |

## More menu

- **导出诊断信息 / Export diagnostics**: writes the redacted zip to Download, then opens the system share sheet on it; the result shows as a toast.
- **关于 / About**: a dialog with "BazaarPlusPlus v{version}" and a link to bazaarplusplus.com.

## Platform notes

- **Type follows the system text size** in `sp`. Rows, badges and buttons have minimum heights, never fixed ones, so they grow with their text.
- **Hit areas are at least 48.** Every tappable thing has a hit area of at least `touch-target` (48) in both directions. More (`control-md`) and banner actions (`sm`) keep their box and extend their hit area past it.
- **Almost nothing moves.** Busy dots pulse at 1.2s, and a pressed button takes `accent-hover` (primary) or `bg-hover` (the others) over `t-fast`. Reduce Motion stops the pulse.
- **One dark theme.** The App follows neither the system light mode nor Material You dynamic colour.

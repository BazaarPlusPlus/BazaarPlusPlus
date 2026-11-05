The start screen of the iOS App and the Android App: one game, its status and the one action that status allows, laid out like a native mobile app rather than a web page. Each App builds it in its own native UI (SwiftUI on iOS, Android Views on Android) from this spec and the generated tokens. They differ only in where the game comes from. The iOS App signs in to Steam and downloads the Mac build; the Android App loads the official game the player installed.

Source:
- iOS: `bazaarplusplus-ios/app/Launcher/LauncherViews.swift` over `app/Theme/` (the generated `BppTokens.g.swift` and the hand-written `BppViews.swift`).
- Android: `bazaarplusplus-android/app/src/main/res/layout/launcher.xml`, `launcher_panes.xml` and `launcher_wide.xml` (with their `launcher_*` parts), `launch/LauncherScreen.java` (what to show) and `launch/LauncherActivity.java` (rendering), over the generated `bpp_tokens.xml`.

## Anatomy

One page, no navigation, no app bar and no footer. In order:

1. **Cover.** The game's art, full bleed from the top edge of the screen: the status bar sits over the art, with no gradient, scrim, logo overlay or shadow. The logo stays clear of the system's areas (see Cover art). It runs 24 under the panel. Until the art decodes, the cover is plain `bg-hover`.
2. **More.** One floating button at the top end, inside the status bar inset, its hit area `touch-target`. iOS shows ⋯ on a `control-md` circle on `bg-panel` with a `line-strong` border and opens a pull-down menu; Android shows ⋮ on a 40 `bg-panel` circle and opens a popup menu. The menu holds 导出诊断信息 / Export diagnostics (Android only) and 关于 / About.
3. **Panel.** `bg-canvas`, top corners `r-dialog` (12), rising 24 over the cover's bottom edge. Inset 24 at the top and 16 at the sides (24 in panes), the safe area below. Its parts are 16 apart:
   - **Title line.** "The Bazaar" in `display`, with the status badge at the line's end. On Android, while a download runs, the badge carries the percentage: "正在下载… 27%". On iOS the progress carries it.
   - **Stats.** Three equal columns, 游戏版本 / 适配版本 / BazaarPlusPlus: the key in `caption` / `text-3` above the value in `body-strong` with tabular figures. At accessibility text sizes they are key-value rows instead, so long values never squeeze.
   - **Banners.** A failure, as an `error` StatusBanner; on iOS also the App update (see App update).
   - **Notes.** `caption` / `text-2`, each one sentence.
   - **Source row.** The one list row: where the game comes from (see Source row).
   - **Progress.** While a download with a known size runs (see Progress).
   - **Primary.** Pinned to the bottom of the panel (see Actions by status).

## Layout

The launcher is portrait only on every device, the iPhone Duo's outer and inner screens included; the game it starts is landscape only. The layout follows the window's size, never the device model.

| Window | Layout |
|---|---|
| narrower than `pane-breakpoint` (600) | **One column**: the cover on top, the panel below it. Phones, iPhone Duo folded (466 wide), a foldable's cover screen. |
| `pane-breakpoint` and wider, unless the row below applies | **Cover on top, two panes below.** The panel splits into two equal panes 24 apart. The start pane holds the title line and the stats as key-value rows without dividers; the end pane holds the banners, notes, source row, progress and primary. iPhone Duo unfolded (669 × 951), unfolded foldables (884 × 1104), tablets. |
| `cover-pane-breakpoint` (840) and wider, in a window wider than tall | **Cover pane and content pane.** The cover fills the start half at full height; the content fills the end half, vertically centred with a 24 inset: the title with the badge on the line below it, the stats as key-value rows with `line` dividers, then the banners, notes, primary and source row. More floats at the content pane's top end, without its circle. A window the system does not hold to portrait. A window as tall as it is wide keeps the cover on top: half of it would be too narrow to show the logo. |

- **The panel takes the height it needs; the cover takes the rest**, at least half its width. When the panel needs more than the window, the page scrolls as one, and the primary follows the content instead of pinning. A page that fits its window does not move under a finger: no rubber band, which would show the canvas above the cover.
- **Accessibility text sizes keep one column** whatever the width: iOS accessibility Dynamic Type sizes, and Android font scale 1.3 and above.
- **The fold falls in the gutter.** The two panes are equal, so on a foldable whose inner screen is portrait, such as the iPhone Duo, the hinge falls in the 24 gutter and nothing straddles it.
- **Nothing is drawn into the system areas** except the cover. Content keeps clear of the safe areas (iOS) and the system bar and cutout insets (Android draws edge to edge).
- **A fold or a text-size change only re-lays the page out.** The App keeps its state and any running check or download.
  - The iOS App runs full screen (`UIRequiresFullScreen`), because the game shares its window, so it offers no Split View or Slide Over.
  - The Android App keeps its check and launch state in the process, not in the Activity.

## Cover art

Two store images of The Bazaar, bundled in both Apps from `design/assets/Covers/` (root `docs/adr/0014-bundle-cover-art.md`). Each cover picks the image that suits its shape:

| Cover | Image | Crop |
|---|---|---|
| Wide (width at least 1.2 × height): a short cover, when the panel takes most of the window | The library hero (the hunter) | Around the hunter's face, at 70% across and 30% down the art, as near the cover's centre as the art's edges allow |
| Otherwise: phones, the iPhone Duo folded and unfolded, unfolded foldables, tablets, the cover pane | The hero capsule (the store's promotional art, with the logo) | From the top, around the logo's centre at 53% across, so the logo stays in view |

The logo on the hero capsule stays clear of the system's areas, so the Dynamic Island, a camera cutout or a side status bar never covers it: below the top inset and before the side inset. Where that moves the art away from the cover's edge, the art's edge is reflected into the gap, which reads as more of the same art. Implemented on iOS.

The images are decoded no larger than the cover needs, because the game later shares the App's process, and released when the game starts. The focus points belong to the bundled images; a new image may need new ones. The Android source row's tile for the publisher shows the promotional art, and the iOS App's first-run onboarding shows a third bundled image, the library capsule.

## Status

The badge is the Badge component: dot + word, one tone. Both Apps use the same words.

| Status | English | Tone | iOS App | Android App |
|---|---|---|---|---|
| 可以开始 | Ready | ok | Signed in, files verified | Game Code Hash matches the release, or a Development interop |
| 等待适配 | Waiting for adaptation | warn | Steam's build is newer than the adapted one (`outdated`) | Installed game code newer than the adapted one |
| 需要更新游戏 | Game update required | warn | | Installed game older than the adapted one |
| 未安装 | Not installed | neutral | Not downloaded | Official game not installed |
| 已下载 | Downloaded | neutral | Downloaded but not verified, or signed out | |
| 需要更换 | Replacement required | bad | | The package holds a retired Android Release APK |
| 暂不支持 | Not supported yet | neutral | This device cannot launch yet (`installed`) | |
| 离线 | Offline | neutral | | No release information and none cached |
| 未发布 | Not released | neutral | | No Android App release yet |
| 正在检查… | Checking… | busy | The runtime is starting | The launch check runs |
| 正在登录… | Signing in… | busy | Steam sign-in | |
| 正在校验… | Verifying… | busy | Checking the files on disk before downloading the rest | |
| 正在下载… | Downloading… | busy | Game download | Interop or plugin download |
| 正在准备… | Preparing… | busy | Any other service work | Staging before the game starts |

When the check itself fails, or the iOS runtime cannot start, there is no status: the title line shows no badge and the failure is a banner.

## Source row

The panel's one list row, in the same place on both Apps: at least `source-row-height` (56), `bg-panel`, `r-touch` (12), a 32 leading tile, the label in `body-medium`, a trailing glyph in `text-3`.

| App | Row | Tap |
|---|---|---|
| iOS | A person glyph on a `bg-selected` circle, the Steam account name (or 登录 Steam / Sign in to Steam), "Steam" in `caption` and › | Opens the Steam sign-in sheet; signed in, it shows the account and 退出登录 |
| Android | The official game's icon at `r-panel`, 打开官方游戏 / Open the official game, ↗ | Opens the official game untouched |
| Android, game not installed or to be replaced, Google Play installed | The cover art, 从发行商下载 / Download from the publisher, ↗ | Opens the publisher's APK in the browser |

Android shows no row when neither applies. A busy launcher keeps the row but ignores taps on Android.

## Actions by status

The primary is the one thing to do next, written as verb + object. It is `touch-target` high and the full width of its column: `r-touch` (12) on iOS, `r-pill` on Android. A primary that cannot run now is hidden, not disabled; the status and a note say why.

| Status | Primary |
|---|---|
| 可以开始 | 开始游戏 / Play |
| 等待适配, 未发布, 暂不支持 | none |
| 离线 | 重试 / Retry |
| 未安装, 已下载 (iOS, signed out) | 连接 Steam / Connect Steam |
| 未安装 (iOS, signed in) | 下载游戏 / Download the game |
| 已下载 (iOS, signed in) | 校验并准备游戏 / Verify and prepare the game |
| 未安装, 需要更换 (Android) | 从 Google Play 安装 / Install from Google Play when Google Play is installed; otherwise 从发行商下载 ↗ |
| 需要更新游戏 (Android) | 从 Google Play 更新 / Update from Google Play when Google Play is installed and the game came from it; otherwise 从发行商下载 ↗ |
| busy | 取消 / Cancel, as a secondary button (iOS, while signing in or downloading: cancelling a download stays signed in); otherwise none |
| failure (no status) | 重试 (Android) |

- **The install source follows the device.** The Android App reads whether Google Play is installed and, for an update, which store installed the game (`PackageManager.getInstallSourceInfo`). A device without Google Play never sees a Google Play action.
- **A failed launch or service step keeps its status and actions.** The failure shows as an `error` banner above them: "未能启动游戏：{reason}" on Android, the service's message on iOS. Android's primary becomes 重试.

## App update

A newer BazaarPlusPlus App, on either platform, is a reminder, never a gate. It never hides an action of its own accord.

- **Android: a dialog, once per version.** A Material dialog on `bg-panel` at `r-dialog`: the brand mark at 32, "BazaarPlusPlus v{version} 已发布" in `title`, "适配游戏版本 {gameVersion}。" in `text-2`, then 稍后 / Later (text) and 下载 / Download (`r-pill` primary, only when the release links a download). Either button closes it; it does not come back for that version.
- **iOS: a banner.** An `info` StatusBanner in the panel reading "BazaarPlusPlus v{version} 已发布。" / "BazaarPlusPlus v{version} is out.", with a 下载 / Download action when the release links one.

## Progress

A download with a known size is the one progress bar in the system. It is a `progress-height` (6) track on `bg-hover` with an `accent` fill and `r-pill` ends. Under it, one `caption` line in tabular figures says what and how much: "插件 · 12.3 MB / 40.0 MB" on Android, where the busy badge carries the step and the percentage, so there is no second label. Work without a known size shows only the busy badge.

The iOS game download takes minutes, so it gets more room, in `space-8` steps: the percentage in `display` with tabular figures and one decimal, with the speed across from it in `caption` ("12 MB/s", or what the download is doing while no data arrives: "正在校验本地文件", "网络不稳定，正在等待恢复"); the bar, its fill moving steadily between reports; then the amount and the time left in `caption`: "1.2 GB / 4.5 GB", "约 5 分钟". The time left is whole minutes, so it does not tick while the speed moves a little. The numbers never go back: a retry keeps its place. Before the size is known, the bar is empty and the speed's place says what the download is doing. On an iOS that cannot keep the App running, one more `caption` / `text-3` line says the download pauses off screen and continues on return.

## Stats

| Key | English | Value |
|---|---|---|
| 游戏版本 | Game version | The installed game's version, or —: the short form of `versionName` on Android ("1.0.12825"), the downloaded build's short version on iOS. |
| 适配版本 | Adapted version | The game version this release adapts: the manifest's `game.gameVersion` on Android, the short form of `testedGameVersion` on iOS. |
| BazaarPlusPlus | BazaarPlusPlus | The App's version, "v6.2.0". |

## More menu

- **导出诊断信息 / Export diagnostics** (Android): writes the redacted zip to Download, then opens the system share sheet on it; the result shows as a toast. The iOS App has no export yet: its logs need the same redaction first.
- **关于 / About**: a dialog with "BazaarPlusPlus v{version}" and a link to bazaarplusplus.com.

## Platform notes

- **Type follows the system text size.** Android uses `sp`; iOS uses Dynamic Type, scaling each style relative to body. Rows, badges and buttons have minimum heights, never fixed ones, so they grow with their text.
- **Hit areas are at least 48.** Every tappable thing has a hit area of at least `touch-target` (48) in both directions. More (`control-md`) and banner actions (`sm`) keep their box and extend their hit area past it.
- **Almost nothing moves.** Busy dots pulse at 1.2s, and a pressed button takes `accent-hover` (primary) or `bg-hover` (the others) over `t-fast`. Reduce Motion stops the pulse.
- **One dark theme.** Neither App follows the system light mode or, on Android, Material You dynamic colour.
- **iOS-only flows** (the Steam sign-in sheet and first-run onboarding) open over this page and use the same tokens; their buttons are `r-touch`.

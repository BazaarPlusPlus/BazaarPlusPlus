The start screen of the iOS App and the Android App: one game, its status and the actions that status allows. Each App builds it in its own native UI (SwiftUI on iOS, Android Views on Android) from this spec and the generated tokens. They differ only in where the game comes from. The iOS App signs in to Steam and downloads the Mac build; the Android App loads the official game the player installed.

Source:
- iOS: `bazaarplusplus-ios/app/Launcher/LauncherViews.swift` over `app/Theme/` (the generated `BppTokens.g.swift` and the hand-written `BppViews.swift`).
- Android: `bazaarplusplus-android/app/src/main/res/layout/launcher.xml` and `launcher_panes.xml` (with their `launcher_*` parts), `launch/LauncherScreen.java` (what to show) and `launch/LauncherActivity.java` (rendering), over the generated `bpp_tokens.xml`.

## Anatomy

One page, no navigation. In order:

1. **App bar.** At least `appbar-height` (56) on `bg-canvas`, no border. The brand mark at 24 and "BazaarPlusPlus" in `body-strong` sit on the page's start edge. The trailing slot holds the iOS account chip: a `control-md` pill on `bg-panel` with a `line` border, holding a status dot and the Steam account name, or 登录 Steam / Sign in to Steam. The Android App has no account, so its slot is empty.
2. **Banners.** Full width, above everything else: first the App update, then a failure. Each is a StatusBanner whose action is an `sm` secondary button.
3. **Game card.** A Panel at `r-dialog` (12) holding the cover, then a 16 inset with the game name ("The Bazaar", `title`) above the status badge. While a download runs, its percentage sits at the end of the badge's line.
   - **Cover.** 2:1 on `bg-hover`, with no gradient, scrim, logo overlay or shadow. iOS shows the Steam store's library hero, fetched at runtime (never bundled) and centre-cropped. Android shows the installed official game's own icon at 64, centred, which is read from the package manager without network. Until art loads, or when there is none, the cover is plain `bg-hover`.
4. **Actions.** One primary and up to two secondary buttons, each the full width of its column, `touch-target` high, `r-control`, 8 apart: `body-strong` on the primary, `body-medium` on the others. Above them while a download runs is the progress block. Below them are notes in `caption` / `text-2`, each one sentence.
5. **Info.** A Panel of key-value rows at least `row-height` (52) tall: the key in `text-3`, the value in `text-1` with tabular figures, end-aligned.
6. **Footer.** Ghost buttons for support actions: 导出诊断信息 / Export diagnostics on Android, with its result as a `caption` line below.

## Layout

The launcher is portrait only on every device, the iPhone Duo's outer and inner screens included; the game it starts is landscape only. The layout follows the window's width, never the device model.

| Window | Layout |
|---|---|
| narrower than `pane-breakpoint` (600) | **One column**: app bar, banners, game card, actions, info, footer. Page gutter 16. Phones, iPhone Duo folded, a foldable's cover screen. |
| `pane-breakpoint` and wider | **Two equal panes** under the app bar and banners, 24 apart. The start pane holds the game card; the end pane holds the actions, info and footer. Page gutter 24, content at most `launcher-max` (960) and centred. iPhone Duo unfolded, iPad, Android tablets, unfolded foldables. |

- **Accessibility text sizes keep one column** whatever the width: iOS accessibility Dynamic Type sizes, and Android font scale 1.3 and above.
- **The fold falls in the gutter.** The panes are equal and the content is centred, so on a foldable whose inner screen is portrait, such as the iPhone Duo, the hinge falls in the 24 gutter and nothing straddles it.
- **The panes line up.** Both start at the same top edge, so the end pane's first button aligns with the top of the cover.
- **The page scrolls as one.** Nothing is pinned except the system bars. Content keeps clear of the safe areas (iOS) and the system bar and cutout insets (Android draws edge to edge).
- **A fold or a text-size change only re-lays the page out.** The App keeps its state and any running check or download.
  - The iOS App runs full screen (`UIRequiresFullScreen`), because the game shares its window, so it offers no Split View or Slide Over.
  - The Android App keeps its check and launch state in the process, not in the Activity.

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
| 正在下载… | Downloading… | busy | Game download | Interop or plugin download |
| 正在准备… | Preparing… | busy | Any other service work | Staging before the game starts |

When the check itself fails, or the iOS runtime cannot start, there is no status: the card shows no badge and the failure is a banner.

## Actions by status

The primary is the one thing to do next, written as verb + object. A primary that cannot run now is hidden, not disabled; the status and a note say why.

| Status | Primary | Secondary |
|---|---|---|
| 可以开始 | 开始游戏 / Play | 打开官方游戏 / Open the official game (Android) |
| 等待适配, 未发布 | none | 打开官方游戏 (Android) |
| 离线 | 重试 / Retry | 打开官方游戏 |
| 未安装, 已下载 (iOS, signed out) | 连接 Steam / Connect Steam | 继续以 {account} 登录 / Continue as {account}, when a session is remembered |
| 未安装 (iOS, signed in) | 下载游戏 / Download the game | |
| 已下载 (iOS, signed in) | 校验并准备游戏 / Verify and prepare the game | |
| 未安装, 需要更换 (Android) | 从 Google Play 安装 / Install from Google Play | 从发行商下载 / Download from the publisher |
| 需要更新游戏 | 从 Google Play 更新 / Update from Google Play | 从发行商下载, 打开官方游戏 |
| busy | none | 取消 / Cancel (iOS, while signing in or downloading) |
| failure (no status) | 重试 (Android) | 打开官方游戏 when the game is installed (Android) |

- **打开官方游戏 opens the official App untouched.** It becomes the iOS App's secondary as well once an official iOS game exists.
- **A failed launch or service step keeps its status and actions.** The failure shows as an `error` banner above them: "未能启动游戏：{reason}" on Android, the service's message on iOS. Android's primary becomes 重试.

## App update

A newer BazaarPlusPlus App, on either platform, is a reminder, never a gate: an `info` banner reading "BazaarPlusPlus v{version} 已发布。" / "BazaarPlusPlus v{version} is out.", with a 下载 / Download action when the release links one. It never hides an action of its own accord.

## Progress

A download with a known size is the one progress bar in the system. It is a `progress-height` (4) track on `bg-hover` with an `accent` fill and `r-pill` ends. Under it, one `caption` line in tabular figures says what and how much: "1.2 GB / 4.5 GB · 12 MB/s · 5 分钟" on iOS, "插件 · 12.3 MB / 40.0 MB" on Android. The busy badge carries the step and the percentage, so there is no second label. Work without a known size shows only the busy badge.

## Info rows

| Key | English | Value |
|---|---|---|
| 游戏版本 | Game version | The installed official game's `versionName`, or —. Android only: the iOS App always runs the adapted version. |
| 适配版本 | Adapted version | The game version this release adapts: the manifest's `game.gameVersion` on Android, the short form of `testedGameVersion` on iOS. |
| BazaarPlusPlus | BazaarPlusPlus | The App's version, "v6.2.0". |

## Platform notes

- **Type follows the system text size.** Android uses `sp`; iOS uses Dynamic Type, scaling each style relative to body. Bars, badges, rows and buttons have minimum heights, never fixed ones, so they grow with their text.
- **Hit areas are at least 48.** Every tappable thing has a hit area of at least `touch-target` (48) in both directions. The full-width action and footer buttons are 48 high. The account chip (`control-md`) and banner actions (`sm`) keep their box and extend their hit area past it.
- **Almost nothing moves.** Busy dots pulse at 1.2s, and a pressed button takes `accent-hover` (primary) or `bg-hover` (the others) over `t-fast`. Reduce Motion stops the pulse.
- **iOS-only flows** (the Steam sign-in sheet and first-run onboarding) open over this page and use the same tokens.

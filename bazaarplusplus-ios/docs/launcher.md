# BazaarPlusPlus 启动器

启动器是原生 UI：`app/` 下的 Swift 文件（SwiftUI）负责首页、游戏页与 Steam 账户，首页不要求先登录；`app/Host/main.mm::SteamController` 只保留引擎加载、服务桥、0.5 秒快照刷新、游戏交接和测试通道，通过 `BPPLauncherHost` 把快照交给 SwiftUI。启动器有自己的工程 `app/BazaarPlusPlus.xcodeproj`，由 `tools/generate-xcodeproj.py` 生成；Unity 导出只构建 `UnityFramework`，启动器工程把它嵌入 App，并在签名前用 `tools/package-app.py` 打包 `Data/` 等运行时资源。`tools/build.py` 依次构建两者。产品名 BazaarPlusPlus 只来自 `app/Resources/Info.plist` 的 `CFBundleDisplayName`：主屏名称、首页标题、界面文字和 Steam Guard 请求里的设备名（“<产品名> · iPhone”）都读它。工程、scheme 与 `.app` 都叫 BazaarPlusPlus，bundle id 是 `com.bazaarplusplus.ios`，只在 `tools/generate-xcodeproj.py::BUNDLE_ID` 定义：钥匙串条目的 service（`service/Native/keychain.m`）和模拟器访问组（`app/Resources/simulator.entitlements` 里的 `$(PRODUCT_BUNDLE_IDENTIFIER)`）都从它派生。改 bundle id 会换一个 App 沙盒和钥匙串访问组，已保存的登录和已下载的游戏都留在旧的那份里。界面深色优先，游戏页按 `game/game.json` 的 `accent` 着色，支持竖屏、横屏和键盘避让。

## 登录与引导

首次打开且从未登录时显示三页引导（游戏库是什么、需要准备什么、账号安全），可跳过；看过后不再出现，开发时用启动参数 `-launcher.onboarding.force YES` 预览。钥匙串里有记住的账号时，启动器打开即自动恢复登录，不显示引导。

`app/AccountFlow.swift::AccountSheet` 按服务状态分步展示：账号 → 验证 → 完成。验证只有一页，和 Steam 官方客户端一致：在手机上批准，或输入 Steam 令牌的 5 位验证码，先到先用。服务总是向 SteamKit 声明走验证码，等待输入的同时轮询手机批准，所以两种方式都不需要重新发起登录，也不必在内存里保留密码；登录前不再让用户预先选择方式。只开了邮件令牌的账号会显示邮件验证码提示（快照 `verification` 为 `email`）。`service/Managed/SteamService.cs::Snapshot` 还提供 `account`、`savedAccount` 和 `errorCode`，常见 Steam 拒绝原因翻译为可操作的提示；恢复登录被 Steam 拒绝时清除失效凭据。

账号页用分段控件在“账号密码”和“扫码登录”之间切换，iPad 默认扫码，iPhone 默认账号密码：扫码需要另一台已登录 Steam 手机 App 的设备，本机的 Steam App 扫不了本机屏幕，iPhone 上会说明这一点。点“显示二维码”才发起 `BeginAuthSessionViaQRAsync`（服务动作 `qr`），不在打开登录页时自动开始；快照状态为 `qr`，`qrUrl` 是 Steam 的挑战链接，Steam 轮换链接时随之更新，启动器本地生成二维码。等待扫描最长 600 秒，取消或切回账号密码会结束这次会话。Steam 会在一到两分钟后以 `TryAnotherCM` 断开未登录的连接，短于一次扫码可能需要的时间，所以等待扫描期间断线时服务自动重连并换一个新二维码（`SteamService.Command` 的重连循环，日志 `STEAM_QR_RECONNECT`），只受总时长限制；到时提示“二维码已过期”（`errorCode` 为 `QrExpired`）。扫码批准后与密码登录一样拿到刷新令牌并存入钥匙串。

所有会话共用一份 `SteamConfiguration`，CM 服务器列表因此在两次尝试之间保留。WebSocket 在 Steam 应答前就连不上时（例如某台 CM 服务器不响应），这台服务器会被列表标为不可用，服务最多再试 3 次，每次换一台服务器（日志 `STEAM_CONNECT_RETRY`）。

模拟器构建必须保持 ad hoc 签名：一旦设置开发团队，Xcode 会注入自己的模拟 entitlements，覆盖 `app/Resources/simulator.entitlements`，钥匙串访问组随之改变，已保存的登录就读不到了。`tools/generate-xcodeproj.py` 因此只对真机使用 `Signing.local.xcconfig` 里的团队。

## 商店素材

`app/Artwork.swift::ArtworkStore` 在运行时从 Steam 商店公开接口 `IStoreBrowseService/GetItems` 获取封面（library capsule）、横幅（library hero）、logo、截图、简介与开发/发行商，图片缓存在 `Library/Caches/Artwork/<app-id>/`，按显示尺寸降采样解码；开始游戏前清空内存缓存，因为游戏与启动器共用进程。素材不打包进 IPA：Valve 会轮换带哈希的素材路径，也不应随 App 再分发商店美术。离线或接口失败时使用缓存，没有缓存则回退到 `monogram` 与 `accent` 文字封面。App ID 来自打包的 `game.json`。

## 状态

`service/Managed/GameDefinition.cs` 校验游戏 ID、版本和下载配置，拒绝路径穿越；缺少能力声明的平台不允许启动。下载状态来自 depot 回执，启动前仍会重新校验内容和版本。回执不是运行兼容性的证明。

`service/Managed/SteamService.cs::Snapshot` 提供游戏（`game`）、账户、进度和平台能力。准备成功后，原生端和托管端同时检查登录、平台能力、准备状态与互斥条件，再把派生路径交给引擎。引擎启动后服务冻结，避免票据关联到另一个 App ID。

下载原件保存在 `Games/content/<app-id>`；派生运行目录为 `Games/runtimes/<game-id>`。每次准备重建相对 StreamingAssets 链接，因此 App 更新引起沙盒搬迁不会保留旧绝对路径。进程内只有一个 Unity 引擎和 Mono domain，游戏启动后不能回到启动器。

## 设备边界

Steam 登录、下载、独立票据及启动调度都编入静态设备目标，实际设备交互未验证。设备构建不会混入 Mac 插件、Simulator 二进制补丁和测试控制 socket。SQLite、Alembic、BC 解码与完整 icall 注册已接入设备构建，App 启动服务前运行原生自检。The Bazaar 在设备上允许下载和启动；设备启动门禁为首次真机验证而打开，资源、音频和引擎适配尚未经过真机执行验证。测试入口和剩余依赖见 `docs/device-readiness.md`。

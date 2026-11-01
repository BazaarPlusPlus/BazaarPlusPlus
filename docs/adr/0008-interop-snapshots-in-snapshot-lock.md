# Interop Snapshot 与官方 APK 进入快照锁和私有存储

Android 客户端是 IL2CPP 构建，没有 `Managed/` 目录可采。Android 宿主对着 Il2CppInterop 从 `libil2cpp.so` 与 `global-metadata.dat` 生成的代理程序集编译，运行时由 LemonLoader 的 Loader Release 提供 `MelonLoader.dll`、`0Harmony.dll` 与 `Il2CppInterop.Runtime.dll`；Android 发版要把 loader、interop 与 mod 注入官方 APK。这三样都是游戏派生物或游戏本身，[ADR 0004](0004-pinned-game-assembly-snapshots.md) 对游戏程序集定下的规则同样适用：显式锁定、只存在于私有存储、不进公开仓库和 Actions cache、不进 Payload Inventory。

决定如下。快照锁增加 `android-online` 一个键：Android 只有生产 APK，不预留 `staging`、`ptr` 空键，渠道列表按平台给出。条目的身份仍是游戏版本串，即 APK 的 `versionName`（`1.0.12825-production-android-d9601ded`，没有架构段，渠道词 `production` 归 `online`），`release/game-libs.mjs` 的 `GAME_VERSION_PATTERN` 与 `build/ManagedPath.props` 的 `BppGameVersionPattern` 同时放宽，release 测试钉住二者相等。条目记录 APK 的 `versionCode` 与 sha256、APK 内 `libil2cpp.so` 与 `global-metadata.dat` 的 sha256、Unity 版本、interop 目录的内容摘要，以及生成它的固定工具链：Loader Release 的 tag、zip sha256 与它携带的 CoreCLR 版本，Patcher Linux x64 发行包的 tag 与 sha256，Cpp2IL Linux 二进制的版本与 sha256，Il2CppInterop 版本，和宿主随 loader 携带的 native 库（按 NuGet 包与版本钉 sha256）。字段集合与顺序由 `release/game-libs.mjs` 校验；`build/InteropPath.props` 按位置读 `gameVersion` 与 `loader.tag`，所以顺序也是契约。

interop 摘要覆盖目录里每一个顶层文件，不只是 DLL：生成器的 `MethodAddressToToken.db` 和 `interop-manifest.json` 由注入步骤和 loader 一起使用。生成结果不保证逐字节确定（`interop-manifest.json` 带生成时间），锁钉住的是存下来的那一份字节，重新生成只在推进锁时发生。Cpp2IL 版本和 Il2CppInterop 版本取自生成器写的 `interop-manifest.json`，不由人填写。

私有桶 `bazaarplusplus-game-libs` 里每件输入按自己的 sha256 内容寻址：interop 目录的 tarball 在 `game-libs/interop/<interopSha256>.tar.gz`，官方 APK、Loader Release、Patcher 与 Cpp2IL 在 `game-libs/{apk,loader,patcher,cpp2il}/<sha256>[扩展名]`，快照清单按条目身份放在 `game-libs/android-online-<gameVersion>.json`。官方 APK 进桶是因为发行商会在同一个 URL 原地覆盖文件，从 CDN 现拉会让锁里的 `apkSha256` 在与 BPP 无关的时刻失配；Loader Release 与工具链进桶是为了上游删 tag 后仍能复现。mod 编译只取 interop 与 loader（`just mod::fetch android online`），发版 job 才取 APK。多 GB 的对象以流式单次 PUT 上传，签名的 payload 哈希就是文件 sha256，R2 拒收不一致的字节。

采集是 `just mod::snapshot-android <TheBazaar.apk>`：读 APK 身份，用 Patcher 的 `generate-interop` 生成 interop，或用 `--interop` 采用对同一 APK 生成好的目录；第一次采集必须显式给出工具链文件和 tag，之后的采集沿用锁里的工具链并按 sha256 从私有存储取回。Patcher CLI 是 Linux x64 二进制，在其他主机上生成会直接报错并提示 `--interop`。

否决的方案：只存 interop、发版时从发行商 CDN 下载 APK，锁会在发行商覆盖文件后失配；把 interop 摘要限定为 DLL，注入所需的数据库文件就不受锁约束；为 Android 预留 `staging` 和 `ptr` 空键，它们没有任何采集来源，只会永远告警；在锁里只写 Cpp2IL 版本，桶里的二进制就无法按锁取回和核对。

代价：游戏每发一个 Android 版本就要有人下载 APK、重新生成 interop、推进锁，Android 发版节奏被这件事卡住；私有桶每个游戏版本多约 2.8 GB；首次采集的 interop 是在 macOS 上用 Cpp2IL 的 OSX ARM64 版生成的，锁里钉的是同版本的 Linux 二进制，Linux 上的生成路径尚未端到端跑过。

2026-10-22 注：[ADR 0010](0010-android-release-platform.md) 让 Payload Inventory 用 `apk-injection` 条目列出注入后 APK 内的路径（`lib/arm64-v8a/libmain.so`、`assets/LemonLoader/` 等），以定义 Android Release APK 的布局。快照锁里的构建输入（Loader Release、interop、官方 APK）本身仍不进 Payload Inventory，也仍只在私有存储。

2026-11-01 注：[ADR 0012](0012-android-app.md) 取代 ADR 0010。Payload Inventory 不再有 `apk-injection` 条目，改由生产者 `android-app` 的条目列出 Android App APK 内的路径。发布不再运行 Patcher；Patcher 只作为锁定的 interop 生成工具（`generate-interop`）留在快照采集与 `android-apk-watch.yml` 里。Interop Snapshot 仍按本记录存进私有桶，它的 interop 包另在公开桶发布。

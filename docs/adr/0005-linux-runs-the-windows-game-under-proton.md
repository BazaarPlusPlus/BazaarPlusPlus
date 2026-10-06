# Linux 主机上的游戏平台是 Windows

mod 与 installer 目前只面向 Windows 和 macOS 发布，代码里的"平台"只有这两个值：快照锁的键是 `macos|windows`，Payload Inventory 的 `platforms` 是 `macos|windows`，bootstrap 只有 Windows 的 `winhttp.dll` + `doorstop_config.ini` 和 macOS 的 `libdoorstop.dylib` + 跳板。The Bazaar 没有原生 Linux 版，Linux 玩家通过 Proton 运行 Windows 版，于是"跑工具链的主机"和"游戏二进制的平台"在 Linux 上不再是同一个东西。若把主机平台直接当作游戏平台，Linux 上解析不出任何快照锁条目、找不到任何 Payload 文件，macOS 的跳板不变式也会被错误地套用。

决定如下。主机平台（Host Platform）是运行工具链与 installer 的操作系统，`windows`、`macos` 或 `linux`；游戏平台（Game Platform）是 mod 与 Payload 面向的游戏二进制平台，只有 `macos` 和 `windows`。**Linux 主机的游戏平台是 `windows`**，因为游戏就是 Proton 下的 Windows 版。快照锁条目（`<platform>-<channel>`）、Payload Inventory 的 `platforms` 作用域、bootstrap 文件、原生插件、进程映像名都按游戏平台取用；主机平台只决定工具链与打包（Tauri 的 Linux 目标、Linux 的 Steam 库发现）。mod 运行时不需要任何改动：Proton 下游戏报告的仍是 `WindowsPlayer`，BepInEx 日志里是 `System platform: Bits64, Windows`。

Linux 的 bootstrap 因此就是 Windows 的那一套：`winhttp.dll`（Doorstop 代理）加 `doorstop_config.ini`。Proton 默认会加载内置 `winhttp`，所以 installer 必须在 The Bazaar 的 Steam `LaunchOptions` 里写入 `WINEDLLOVERRIDES="winhttp=n,b" %command%`（等价于在 Proton 前缀的注册表里把 `winhttp` 覆盖为 `native,builtin`），并且在改写 `localconfig.vdf` 前关闭 Steam，否则改动会被覆盖。macOS 的"启动项必须为空"不变式在 Linux 上反过来：启动项必须等于那个覆盖，installer 据此把状态判为需要 `Repair`。Steam 库与 Proton 前缀通过 `libraryfolders.vdf` 与 `appmanifest_1617400.acf` 定位，不猜测路径。

否决的方案：做一个原生 Linux Payload——游戏没有 Linux 二进制，无从编译；给 Payload Inventory 增加一套 `linux` 平台条目去复制 Windows 的——同一批文件会有两个 owner，违背每个事实一个 owner；让 Linux 复用 macOS 的跳板——跳板只为绕开 macOS 的应用包签名限制，Proton 不加这层壳；要求 Linux 用户手工改启动项或注册表——installer 的职责正是把这一步自动化。

代价是 installer 必须持续保证启动项正确，否则 mod 会静默不加载；`WINEDLLOVERRIDES` 的写法跟随 Proton 行为，未来 Proton 若改变 `winhttp` 的处理方式需要同步。Linux 的**发布**平台键（用于 Platform Release Manifest、下载页与 installer 自更新）是另一项决定，不在本记录内：本记录只规定构建与运行期的游戏平台归属。

因为 Payload 是 Windows 的那一份，Linux 的 Tauri 目标也复用 Windows 的资源包：`bazaarplusplus-installer/src-tauri/tauri.linux.conf.json` 把 `bundle.targets` 设为 `deb` + `appimage`，并像 Windows、macOS 一样把 `BepInExSource/windows/BepInEx.zip` 映射进 bundle。打包前必须按正常发布流程准备好该资源（`just release::prepare` 产出的 `BepInExSource/`），否则在源码树里能运行、但不能产出可用的安装包。

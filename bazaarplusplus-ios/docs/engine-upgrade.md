# 升级游戏引擎

The Bazaar 升级 Unity 版本时，按这份流程升级 iOS App。App 严格比对 `game/game.json` 和 `toolchain.lock.json` 里的版本与哈希。Steam 上的游戏更新后，App 保留下载并显示“等待适配”，升级完成并发布新版 App 后才能启动。所有值由这两个文件和 `ProjectVersion.txt` 拥有，这里不重复。

只有游戏更新、引擎不变时，只做第 7 步和第 8 步。

## 1. 确认新版本

从新版游戏的 `TheBazaar.app` 读三个值：

```sh
plutil -p TheBazaar.app/Contents/Info.plist | grep -E 'ShortVersion|GetInfo|UnityBuildNumber'
strings -a TheBazaar.app/Contents/Frameworks/libmonobdwgc-2.0.dylib | grep -m1 'explicit/'
```

`explicit/` 后面是 Mono commit 的前缀。用它查完整 commit：

```sh
git ls-remote https://github.com/Unity-Technologies/mono.git | grep '^<前缀>'
```

装好 Editor 后，也要在 Editor 自带的 `Unity.app/Contents/Frameworks/libmonobdwgc-2.0.dylib` 里确认同一个标记。

## 2. 安装 Editor

在 Unity Hub 安装同一版本，勾选 iOS Build Support 和 Mac Mono Build Support。链接格式是 `unityhub://<版本>/<revision>`。

## 3. 更新固定值并重放补丁

1. 改 `toolchain.lock.json` 的 `unity` 和 `mono.commit`。`mono.submodules` 按新 commit 的 gitlink 更新：`git ls-tree <commit> external/bdwgc external/corefx`。
2. 改 `runtime/engine/shell/ProjectSettings/ProjectVersion.txt`。
3. 在新 worktree 运行 `python3 runtime/mono/bootstrap.py`。它在 worktree 里新建 Mono checkout，不碰主 checkout。主 checkout 的 `third_party/mono` 通常有本地改动，`bootstrap.py` 也不会 reset。合并后要先问过改动的所有者，再移开这个目录并重新 bootstrap。
4. 删除旧的 `build/mono-*-obj` 和 `build/mono-etc`（新 worktree 没有这两项）。

补丁打不上时，改补丁文件，不改 Mono checkout。编号不复用。

## 4. 读上游差异

```sh
git diff --stat <旧 commit> <新 commit>
```

补丁能打上，不代表语义没变。重点看这些地方：

- `mono/mini/interp/`：`0008-interp-method-redirect.patch` 依赖解释器内部结构，模组的 `InterpreterMethodPatcher` 又依赖这个补丁。
- 新加入编译的源文件：看 `mono/metadata/Makefile.am`。它们要能在 iOS 上编译。
- 依赖结构布局的代码：上游的 `mono_class_setup_interfaces` 用写死的偏移读 `MonoClass` 位域，偏移取决于 `DISABLE_REMOTING`，我们的构建关闭了 Remoting。`tests/test_class_layout.py` 检查这个偏移。

## 5. 构建 Mono

```sh
bash runtime/mono/build.sh host-aot
bash runtime/mono/build.sh simulator-aot
bash runtime/mono/build.sh device
```

## 6. 导出外壳，更新设备引擎

```sh
python3 runtime/engine/export.py simulator
python3 runtime/engine/export.py device
python3 runtime/engine/export.py mac
```

1. 还原 Unity 对 `ProjectSettings.asset` 中 `iPhoneSdkVersion` 的改写。新 Editor 写入的新字段保留。
2. 从 `runtime/engine/shell/Builds/iOSDevice/Libraries/libiPhone-lib.a` 更新 `toolchain.lock.json::deviceEngine`：
   - `sha256`：`shasum -a 256`。
   - `graphicsObject`、`serializeObject`：`xcrun nm -A` 查 `DecompressNativeTexture` 和 `SerializedFile::ReadMetadata` 所在的成员。
   - `icallRegistrations`：`prepare-device.py` 按 `nm -gU` 统计 `Register_*`。数量变化是正常的，但要有 diff 依据。
3. 运行 `python3 runtime/engine/prepare-device.py <libiPhone-lib.a> <输出目录>`。在 `UnityRuntime` 的副本上运行 `python3 runtime/engine/patch-simulator-runtime.py <副本>`。两个脚本都比对原始指令，序列变化时报错退出。报错时用 `otool -tv` 对照对应符号，重新确定指令，再改脚本。不要放宽比对。

## 7. 取得正式版的值

`game/game.json` 的 `testedGameVersion`、`unityVersion` 和 `managedAdapters[].sourceSha256` 只能用 **public 分支** 的文件测量。staging 和正式服的构建号不同，字符串和哈希不能通用。

- 判断 public 是否更新：`https://api.steamcmd.net/v1/info/1617400` 的 `depots.branches.public.buildid` 和 `depots.1617401.manifests.public.gid`。App 写入的 `verified-1617401.json` 也记录它校验过的 manifest。
- 取文件：确认本机 Steam 的 `appmanifest_1617400.acf` 没有 `BetaKey` 后，从 Steam 安装目录复制到 `build/steam/download/TheBazaar.app`。Steam 安装目录可能有桌面版模组放入的文件，比如 `Contents/PlugIns/GfxPluginBppReplayVideoToolbox.bundle`，它们不影响构建。
- 同时比对 `build/steam-app/*/launcher-derived/.../Data/Managed/mscorlib.dll` 和游戏的 `mscorlib.dll`。不一致时 `Installer.Prepare` 会拒绝运行。
- 比对新旧版本的原生插件。`fmodstudio`、`resonanceaudio` 或 `libgilzoide-sqlite-net` 变化时，要核对 `toolchain.lock.json` 对应的版本和 `middleware/` 的适配。`abci` 变化时，比对 `nm -gU` 的导出。

### 提前排练

public 还没更新时，可以用本机 Steam 的 staging 副本提前发现问题。这一步的值和改动都不提交。

1. 在 `game/game.json` 临时填 staging 的值，运行 `python3 tools/build.py simulator --test-control`。
2. 在 `SteamService` 的 download 命令里临时跳过 `Downloader.Download`，否则下载器会把内容恢复成 public 版本。
3. 用 `xcrun simctl clone` 克隆一个已登录的模拟器，再把 staging 副本放进克隆的 `Library/Application Support/Games/content/1617400/`。用 `BPP_DEVICE=<克隆名>` 运行 `just ios::regress`。克隆保留 Keychain 里的登录，也不影响其他会话用的模拟器。

## 8. 验证

按 `AGENTS.md` 的验证步骤进行：

1. `just ios::check`、`just ios::test`。
2. `python3 tools/build.py simulator --test-control`，`just ios::regress`。日志要有 `plugin.initialization.succeeded`，不能有 `plugin.patches.degraded`。
3. `python3 tools/build.py device`，`just ios::audit-device`。
4. 真机验证：进入对局，确认模组已加载。

`bazaarplusplus-mod/build/game-libs.lock.json` 和 Android 的引擎版本不属于这个流程。它们有各自的更新步骤。

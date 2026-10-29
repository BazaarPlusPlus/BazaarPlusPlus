# Mono 补丁

上游与运行时依赖的 revision 由根目录 `toolchain.lock.json` 持有。`series` 定义应用顺序。

| 补丁 | 内容 |
|---|---|
| 0001 | Apple SDK 构建修复、程序集加载诊断、避免自动抓取与运行时无关的托管子模块 |
| 0002 | Apple Silicon 的 W^X：arm64 Simulator 和 macOS 上的 AOT 编译器进程需要 MAP_JIT 与 `pthread_jit_write_protect_np` 才能写入跳板。设备不 JIT，不受影响 |
| 0003 | Boehm 保存 GC callbacks 并扫描 Mono 解释器栈 |
| 0004 | 固定基础库语义的字符串哈希解释器 intrinsic |
| 0005 | ARM64 AOT 方法地址解析不把 `BL` 当作跳转岛，并在自循环处停止 |
| 0006 | 禁用 .NET Remoting 时补齐 Unity 分支的声明和条件编译 |
| 0007 | 修正 `nunbox-arbitrary-trampolines` 参数的前缀长度 |
| 0008 | 解释器方法重定向 `mono_interp_redirect_method`：调用被重定向的方法时，`method_entry` 把 frame 换成目标方法，目标代码读到的都是它自己的数据；被重定向的方法不再被内联，内联过它的根方法会重新转换。Harmony 补丁用它代替需要可写代码的原生 detour |
| 0009 | 解释器沿用 JIT 的可见性规则：方法自带 `skip_visibility`，或程序集带 `SkipVerification`（`/unsafe` 编译器会生成）时，不检查调用目标和字段的可见性。桌面 JIT 上能跑的模组代码在解释器上也能跑 |

补丁编号不复用。`BPP_TRACE_LOAD=1` 打开 0001 加入的程序集加载诊断。

使用 `python3 runtime/mono/bootstrap.py --check` 检查可重放性和本地文件等价性。验证从上游提交读取原文件，在临时目录应用完整序列，再与当前工作目录比较；不会反向修改工作中的 Mono checkout。

原实验 checkout 还有若干未用于 native runtime 构建的托管子模块处于不完整/不同 revision 状态。本项目不把它们的 gitlink 漂移当作运行时补丁。仅固定实际依赖的 corefx 和 bdwgc；完整托管类库构建不在当前构建范围内。

更新补丁时同时更新此说明和实验依据。已有的 Mono 本地修改不应通过 reset/checkout 丢弃。

已知限制：0008 重新转换内联过原方法的根方法，这条路径还没有测试。iOS host 在 `BeforeSceneLoad` 应用全部补丁，那时游戏代码还没运行，内联的根方法还不存在。

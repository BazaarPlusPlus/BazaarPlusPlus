# 静态 Mono 运行时

`MONO_AOT_MODE_INTERP_ONLY` 允许动态生成跳板，不能据此声称满足 iOS 设备约束。`runtime/bridge/il2cpp_on_mono.c::il2cpp_init` 在静态构建中注册 corlib AOT 模块并选择 `MONO_AOT_MODE_INTERP`；`runtime/mono/build.sh` 为 device 和 simulator-aot 禁用 JIT。静态目标还对齐 Mono 自带 `sdks/builds/ios.mk` 的配置禁用 .NET Remoting；这不影响普通本地委托，但不承诺支持远程代理和跨 AppDomain remoting。旧 profile Simulator 实验仍保留动态跳板模式；Steam App 的 Simulator 和 device 构建都使用静态路径。

## 编译与加载

`runtime/mono/build-aot.sh` 使用同一份已应用补丁的源码编译 macOS ARM64 Boehm 主机编译器，`tools/mono-aot/main.c` 只负责调用其 `mono_main`。主机和目标都禁用 Remoting，并使用相同 TLS 配置；`runtime/mono/patches/0006-no-remoting-build.patch` 补齐 Unity 分支在此配置下的声明和条件编译；主机保留编译器，设备和对应 Simulator 禁用 JIT。不能只按 Mono revision 选择 Unity 自带编译器：它的 GC、架构或编译功能配置可能不同，生成的 AOT 包装器会与目标运行时不兼容。脚本以对应 Apple SDK 汇编目标平台对象。

`full,interp` 生成 corlib 方法及解释器跳板，`nopagetrampolines` 使用有限的静态跳板池。仅 `interp` 在当前 Mono revision 不生成所有必要的固定池。模块按 corlib 的程序集 GUID 和 GC 等信息校验；更换基础库必须重新生成。应用程序集仍可在安装后加载并解释执行。corlib 中未能 AOT 编译的方法可能回退到解释器，不能把这理解成整个游戏都已 AOT 化。

`runtime/mono/patches/0005-aot-branch-islands.patch` 修复 `mono/mini/aot-runtime.c::method_address_resolve`：ARM64 的 `BL` 会修改返回地址，属于方法体，不能当作链接器跳转岛继续追踪；`B` 到自身也必须停止。UnityFramework 链接后观察到错误方法入口和自循环，原逻辑会在载入 AOT 方法表时一直占用主线程。`tests/test_aot_branch.py` 直接编译该解析函数，验证调用、跳转链和自循环。

## Unity 外壳

`tools/build.py` 基于同版本 Unity 导出创建派生工程。当前关闭链接器代码合并，Simulator 还关闭代码裁剪；普通裁剪/合并配置下观察到初始化失败。设备版需要裁剪 `libiPhone-lib.a` 中未使用的代码，否则会引入玩家包未提供的 Enlighten 路径和 ASTC 编码器符号。脚本从 shim、Mono 和 AOT 归档提取已定义的公共 C 入口（排除编译器 block 描述符等内部符号），通过链接器 `-u` 显式保留，防止仅经动态查找访问的运行时入口被裁掉。此配置偏重保留入口，尚未优化体积。

Simulator 的引擎是 UnityRuntime.framework，设备引擎是 libiPhone-lib.a。两者都将 shim、Mono 和 AOT 模块正常链接进 UnityFramework，无需环境变量注入动态库。设备端保留 Unity 生成的类、模块和 icall 注册文件，而不链接 GameAssembly。

样例保留 iOS 场景资源，换入样例 Mono 程序集及 iOS UnityEngine 程序集。它不验证任意 Mac 资源可在设备上加载。

Steam 设备目标另由 `runtime/engine/prepare-device.py::prepare` 校验原始引擎归档哈希，在派生副本中替换两个纹理解压入口并修正锁定的 SerializedFile 平台读取指令。`runtime/engine/device_texture.cpp::DecompressNativeTexture` 将 BC 格式交给共享 CPU 解码器，其他格式交回引擎。全部修改发生在链接和签名前，不修改设备上的可执行页面。脚本从该归档生成完整 icall 注册清单；`tools/audit-device.py::audit` 检查最终框架保留了清单中的入口。类和模块注册仍沿用 Unity 导出；icall 清单完整不代表类、shader 和全部资源在真机上已兼容。

Steam 目标在两种平台都静态链接从固定源码构建的 SQLite、Alembic；设备链接保留其动态查找入口。构建及未完成项由 `docs/device-readiness.md` 维护。FMOD 与 Resonance Audio 使用供应商的原生静态库，游戏集成使用版本锁定的派生补丁；嵌入式启动器仅 Burst 仍保留 Simulator Mac 重定向。

## 验证层次

运行时由 App 回归验证：Simulator 上恢复登录、校验并准备、启动游戏，要求静态 AOT 初始化、游戏服务器认证和全部启动任务成功。没有物理设备时，验证止于 Simulator 执行和设备链接。

The Bazaar 实际加载先后耗尽默认拆箱池和泛型上下文池。`runtime/mono/patches/0007-unbox-trampoline-option.patch` 修复 `aot-compiler.c::mono_aot_parse_options` 对 `nunbox-arbitrary-trampolines` 的前缀长度计算，否则传入容量会被解析为零。`runtime/mono/build-aot.sh` 拥有各类固定池容量；`tests/test_aot_branch.py` 直接执行解析赋值验证参数生效。扩池不引入动态代码生成，也不代表容量无限或所有游戏可用。更大游戏仍需记录容量失败和长期运行结果。设备签名、内存、长期运行、插件迁移与 Steam iOS 登录仍需单独验证。

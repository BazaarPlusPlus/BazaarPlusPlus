# 设备适配与首次真机验证

设备启动器已完成开发签名构建和物理设备安装，包含静态 Mono/AOT、Steam 登录与下载、独立票据服务、原生 SQLite/Alembic、FMOD/Resonance Audio 和引擎资源适配。**尚不能宣称 The Bazaar 在真机可玩。** 已在 iPhone 16 Pro Max 验证启动器的静态 Mono/AOT 初始化及原生自检，Steam 登录、Steam Guard 与 Keychain 会话恢复已通过；游戏运行尚未验证，`games/the-bazaar/profile.json` 的设备启动门禁保持关闭。具体版本、哈希和能力值以 profile 与 `toolchain.lock.json` 为准。

## 已接入的路径

| 部件 | 实现及验证边界 |
|---|---|
| Mono 与跳板 | 禁用 JIT 的 Mono、corlib AOT 固定池、游戏程序集解释执行；Simulator 已进入游戏主菜单，设备已执行启动器服务初始化与原生自检 |
| SQLite | 游戏对应版本的源码静态构建，覆盖 Gilzoide 所有导入；共享启动自检验证内存库序列化、反序列化和查询 |
| Alembic | Unity 插件、Alembic、IlmBase 从固定源码构建；标量实现替代桌面 ISPC，仅含 Ogawa；共享自检验证写入和读取一个变换节点 |
| BC 资源解码 | 设备引擎的两个 `DecompressNativeTexture` 重载均在链接前接入共享 BC1–BC7 CPU 解码器；非 BC 格式使用引擎实现 |
| SerializedFile | 对锁定引擎的派生归档修正目标平台读取；校验归档哈希和原始指令，未知版本拒绝构建 |
| 引擎注册 | 从设备归档生成完整 icall 注册代码，再审计最终框架是否保留；类与模块仍取自 Unity 导出 |
| 音频 | 官方匹配版本的 iOS/Simulator 库静态链接；原生自检验证版本、三个 Resonance DSP 的幂等注册及创建/释放；游戏 Mono 集成使用限定哈希的派生补丁 |
| Xcode 打包 | 设备工程在签名前打包目录、辅助程序集、Mono 配置和许可；独立 Xcode 构建不依赖 Python 构建脚本的事后修改 |

`tools/audit-device.py` 检查的是最终设备二进制中的符号，**不是 API 行为证明**。尤其 Steam 适配器仍有历史占位入口，不能用导出数量声称完整 Steamworks 支持。BC 单元测试覆盖已知块、裁边、有符号通道、BC6 格式映射、并行解码及非法尺寸；实际游戏资源和 Metal 上传仍待真机验证。

## 尚缺的依赖

- FMOD/Resonance Audio 已接入；真机音频输出、中断、后台恢复和长时间混音仍需设备证据。
- Mono.Data.Sqlite 声明中的 `sqlite3_key`、`sqlite3_rekey` 未提供；当前 SQLite 构建不支持这些加密入口。
- Alembic 声明中的 `aiPropertyGetData` 在游戏原始 Mac 插件中也不存在。保留审计缺口，不伪造别名；若运行实际触发，需要依据调用语义处理。基础 Ogawa 往返不等于完整 mesh、曲线、动画或 ABI 验证。
- 引擎类与模块覆盖、shader 变体、资源平台差异、无原生 Burst 路径、内存峰值、触控、后台恢复及长期运行，均需继续实测。

## 本机构建与审计

### 音频 SDK 获取与接入边界

已从 FMOD 官网登录下载完整 Unity 集成包，包含设备和 arm64 Simulator 原生库。包名、版本、包和成员 SHA-256 由 `toolchain.lock.json::fmodRequired` 拥有。将官方包放在忽略的 `build/vendor/` 中后，`middleware/fmod/prepare.py::prepare` 校验包与成员哈希、托管版本、Mach-O 平台和 Resonance bitcode 目标，再提取 arm64 切片。库不进入 Git，不修改平台标记。SDK 缺失或版本不符时构建失败。

`middleware/fmod/fmod_static.mm::bpp_fmod_register_resonance` 用真实描述符注册 Listener、Soundfield、Source，并通过插件名称查询避免重复注册。`bpp_fmod_selftest` 使用 NOSOUND 输出验证版本、重复注册和 DSP 创建/释放；这不替代实际扬声器输出验证。设备和 Simulator 都将 FMOD P/Invoke 映射到静态链接的 UnityFramework；嵌入式启动器已停止打包 Mac 音频插件。

游戏原始 Mono `FMODUnity.dll` 的静态插件路径只输出警告。`middleware/fmod/adapter/Program.cs` 校验 profile 中的原始程序集哈希后，在派生副本中将动态/静态插件加载改为原生注册，保留 `CheckInitResult` 错误上报；同时桥接音频系统初始化、释放与 mixer 暂停请求。`service/Managed/ManagedAdapters.cs::Apply` 再核对游戏 ID、源文件和派生文件哈希，仅替换运行目录中的副本。Steam 下载原件不变；未知更新会拒绝启动。

原生音频适配器观察前后台、音频中断和 media-services-reset；管理游戏自身的暂停原因，避免与 OS 通知互相覆盖。系统恢复失败时保留暂停状态，下一次相关通知重试，不阻塞主线程等待。Simulator 测试通道提供只读音频快照和合成前后台通知，设备构建不包含该通道。真实来电、蓝牙路由、锁屏和恢复仍需真机。

应用打包官方 EULA、Resonance Apache 许可及 FMOD 署名。当前是本地开发验证；FMOD EULA 包含对作为 engine/toolset 分发的限制，启动器公开分发前需要向供应商确认适用授权，不能套用单个游戏的授权结论。

### 生成工程

先按 README 恢复工具链、Unity 导出和游戏辅助程序集；此外需要 CMake。源码依赖下载由各构建脚本处理。

```sh
python3 tools/build.py device
python3 tools/audit-device.py
python3 -m unittest discover -s tests -v
dotnet run --project service/Tests -c Release
```

`tools/build.py device` 先用 Unity 导出工程构建 `UnityFramework`（`build/steam-app/device/export/`），再构建 `app/BazaarPlusPlus.xcodeproj`，产物在 `build/steam-app/device/launcher-derived/Build/Products/Release-iphoneos/BazaarPlusPlus.app`。两份工程都会重新生成，持久修改应落到脚本或源码。

Simulator 回归使用 `tools/build.py simulator --test-control`、`tools/run-app.py` 和 `tools/app-control.py`。在 App 内恢复 Steam 会话，校验已有内容，再启动；不要将原始认证日志分享或提交。

进入有音频的游戏菜单后运行 `python3 middleware/fmod/verify-simulator.py`，检查 bank、真实播放通道、暂停时钟停止和恢复后时钟推进。该脚本向测试 App 发送合成前后台通知；实际 Home 切换、来电等应另测，不能混记为同一证据。

## Steam CDN 兼容

`service/Managed/ContentServers.cs` 保留目录的原始 TLS 能力字段，仅将声明 `mandatory` 或 `optional` 的节点转为 HTTPS 端点，并先检查代理、区域及应用限制。SteamKit 的默认转换会将 optional TLS 标成 HTTP，因此不能仅按转换后的协议筛选。登录连接返回的区域目录可能只有不支持 HTTPS 的节点；下载器在当前 cell 和默认 cell 均不可用时，回退到 Steam 官方 HTTPS Web API 目录，不降级传输安全。清单下载可轮换节点，所有文件仍按清单校验。

## 下载阶段的内存回收

`service/Managed/Downloader.cs::CollectDownloadBuffers` 在块写入结束、释放当前缓冲引用后，按托管已用内存触发回收。这个预算是回收触发条件，不是 iOS 进程内存上限；并发缓冲、原生库及网络栈还会占用内存。它只作用于下载路径，不在游戏帧循环强制回收。

真机曾在下载中连续累积临时托管缓冲，自动 GC 已启用但未触发回收，进程占用超过 1.6 GB。显式设置自动模式没有改善，最终没有保留该尝试。分块回收后已观察到持续 GC 和内存回落；signal 9 没有对应系统报告，不能将其正式归类为 Jetsam。

通过启动环境变量 `BPP_DOWNLOAD_DIAGNOSTICS=1` 可采样进程物理占用、托管堆和下载进度；`BPP_GC_STATS=1` 记录 GC 暂停。默认不开启，不记录认证内容。

## 拿到设备后的顺序

1. 在 `app/Signing.local.xcconfig` 配置 iOS 开发团队，选择已信任、开启开发者模式的设备运行 `BazaarPlusPlus` scheme。不要沿用无签名构建参数。开发签名和安装已在 iPhone 16 Pro Max 验证；运行结果见验证记录。
2. 先检查游戏库和启动日志。`NATIVE_PREFLIGHT sqlite=pass alembic=pass bc=pass`、成功的 `FMOD_PREFLIGHT` 与静态 AOT 初始化均应出现；自检失败时不继续初始化 Steam 服务。
3. 在 App 内登录 Steam，完成 Steam Guard，测试 Keychain 恢复、下载与完整性校验。Simulator 的 Keychain 和文件不会自动迁移到设备。
4. 开展受控的设备游戏启动验证；将实际资源、icall、原生入口错误记入对应适配层。
5. 游戏启动、认证、音频、触控和资源都取得设备证据后，再调整 `game/game.json` 的设备启动能力。首次菜单成功仍不等于整局、后台恢复或长期运行通过。

无身份信息的本机审计保存在 `build/device-audit/`。

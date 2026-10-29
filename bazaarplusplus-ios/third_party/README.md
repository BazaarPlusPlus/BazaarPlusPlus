# 第三方源码

- `mono/`：由 `runtime/mono/bootstrap.py` 获取的 Unity Mono checkout，revision 和补丁在主仓库管理，checkout 本身忽略。许可文件保留在上游 checkout 中。
- `bcdec/`：原实验使用的 bcdec 0.985，来源 https://github.com/iOrange/bcdec 。保留原头文件及双许可文本；原文件的 SHA-256 固定在 `toolchain.lock.json`，未声称未知的上游提交号。
- Unity 引擎、基础类库、游戏数据和 Mac 插件来自本地安装，不随此源码仓库分发。
- SQLite、Unity Alembic、Alembic 和 IlmBase 源码由 `middleware/sqlite/build.py`、`middleware/alembic/build.py` 下载至忽略的 `build/vendor/`。归档地址、版本和 SHA-256 由 `toolchain.lock.json` 拥有；构建前校验归档，`service/licenses.py` 将对应许可打包到 App 的 `ThirdPartyNotices/`。
- FMOD for Unity 官方包需登录官网获取，放在忽略的 `build/vendor/`；包与必要成员的哈希由 `toolchain.lock.json::fmodRequired.unityPackage` 固定。`middleware/fmod/prepare.py` 提取原生 iOS/Simulator 库，`service/licenses.py` 打包 FMOD EULA、署名和 Resonance Audio Apache 许可。SDK 二进制及派生游戏程序集不随仓库分发；启动器分发授权边界见 `docs/device-readiness.md`。

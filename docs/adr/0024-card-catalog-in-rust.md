# 卡图、卡牌文字和附魔效果由一份 Rust 代码导出

状态：已接受（2026-11-27）

## 背景

[ADR 0020](0020-card-art.md)、[0021](0021-card-text.md) 和 [0022](0022-enchantment-fx.md) 的发布内容由 `bazaarplusplus-bot/tools/card-art/` 下的 Python 脚本导出，依赖 UnityPy 和 Pillow，只在发布工作流里运行。

安装器的历史详情要显示和网站一样的卡名、卡牌文字、卡图和附魔效果，而且断网也要能用。用户的要求是：

- 不从 CDN 下载素材；
- 安装器不附带 Python，也不附带第二个程序；
- 只有一份导出代码。

## 决定

- 导出代码是 Rust crate `card-catalog`，位于 `bazaarplusplus-installer/src-tauri/card-catalog/`，是安装器 Cargo workspace 的成员。
  - 发布工作流运行它的命令行入口：`just installer::card-art`。
  - 安装器直接调用这个库，从本机安装的游戏导出。
- 这个 crate 自己读取 Unity 资源：
  - UnityFS 资源包和其中的 SerializedFile 由自己的读取器按资源包内嵌的类型树解析，只支持 SerializedFile 格式 22 及以后。
  - 纹理用 `texture2ddecoder` 解码。
  - Addressables 目录的解析沿用 ADR 0020 的规则。
- 输出与 Python 版逐字节一致，所以内容摘要和已发布的文件名不变：
  - 缩放照搬 Pillow 12 `libImaging/Resample.c` 的定点实现，RGBA 先预乘 alpha。
  - WebP 用和 Pillow 12 相同的 libwebp 1.6.0 与参数编码，并经过 WebPMux。
  - JSON 按 Python `json.dumps(sort_keys=True, ensure_ascii=False, separators=(",", ":"))` 的格式写出，浮点数按 Python `repr` 格式输出，取整按 Python `round` 的规则（.5 取偶数）。
- 一致性在切换前验证过：Steam build 26592248 的 Windows depot 和 macOS 安装，Python 与 Rust 的输出目录逐文件比对。原 `test_extract.py` 的测试和两个 fixture 已移植到 crate 的测试里。

## 否决的方案

- 安装器从 CDN 下载素材：用户否决，安装器需要断网可用。
- 随安装器附带 Python 或打包后的独立导出程序：多一份运行时和一份代码，安装包大 30–40 MB。
- 用 PyO3 把 Python 嵌进安装器：仍然要附带 Python 运行时和 UnityPy、Pillow 的原生扩展，导出时的崩溃和 3–4 GB 内存会直接影响安装器。
- 让 CDN 开放跨域，由安装器网页直接读取：仍然依赖网络。

## 代价

- Unity 资源格式由我们自己维护。游戏升级 Unity 时，如果 SerializedFile 格式或类型树格式变化，发布的覆盖率检查会失败，需要改读取器。
- 逐字节一致依赖 libwebp 的版本和 Pillow 的算法。升级 `libwebp-sys` 可能改变所有 WebP 的文件名，届时需要重新发布全部卡图。
- 只支持实际用到的纹理格式（BC1、BC3、BC4、BC5、BC7、Unity Crunch、RGBA32、RGB24、ARGB32、BGRA32、Alpha8、R8），以及没有打包、旋转或紧凑网格的普通 Sprite。遇到其他格式时，跳过这张卡并报告。

# Steam 协议依赖

`BazaarPlusPlus.Ios.Service.csproj` 和 `packages.lock.json` 固定 NuGet 依赖及内容哈希。SteamKit2 采用 LGPL-2.1-only；Newtonsoft.Json 采用 MIT。依赖保持独立程序集，并随构建复制许可证。

`AuthTickets.cs` 的协议布局和确认消息处理参考 SteamRE/SteamKit 的 `SteamAuthTicket`（commit `24131c045655ddaa1e0e60880b2b1af28f69e89c`），属于 LGPL-2.1-only 衍生实现。原文和许可证：https://github.com/SteamRE/SteamKit/tree/24131c045655ddaa1e0e60880b2b1af28f69e89c/SteamKit2/SteamKit2 。该实现适配 netstandard2.0，使用按票据 CRC 关联的确认，不输出票据内容。

Zstandard 源码包由根 `toolchain.lock.json` 固定，使用其 BSD 许可证选项。构建仅编译解码器及公共支持代码；原始许可证随 App 打包。

`service/Native/chunks.c` 的 Steam AES 与 VSZa 容器解码参考同一 SteamKit 版本的协议实现；`tests/test_steam_chunks.py` 使用其公开加密块测试向量。协议适配代码按 LGPL-2.1-only 提供，随 App 的通知目录附带适配源文件。

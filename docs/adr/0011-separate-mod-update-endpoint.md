# 分开 mod 与安装器的更新入口

## 背景

mod 自更新只替换 Managed Payload，安装器更新则携带完整 Payload。两者原先读取同一个平台入口。需要明确两种更新入口，同时保留无法使用游戏内下载时通过完整安装器更新的路径。

## 决定

保留单一产品版本与一个版本对应一个提交的规则。每个桌面发布版本，包括补丁版本，都构建并上传完整安装器、安装器 updater 包和 Managed Payload；不引入只发布 mod、复用旧桌面安装器的模式，也不维护 installer 最低版本兼容表。mod 继续由 [SelfUpdatePolicy.Decide](../../bazaarplusplus-mod/src/BazaarPlusPlus.Core/Infrastructure/SelfUpdate/SelfUpdatePolicy.cs) 比较实际安装环境的非托管文件摘要与数据库 schema。

`release/downloads.ts` 的 `platformManifestPath` 保留安装器、官网和旧 mod 的入口，`modManifestPath` 提供新 mod 的入口。两者承载同一份 Platform Release Manifest，使用已有格式和共享 fixtures，不各自维护版本或下载事实。[ReleaseManifestEndpoints.ForPlatformKey](../../bazaarplusplus-mod/src/BazaarPlusPlus.Core/Infrastructure/ReleaseManifest/ReleaseManifestEndpoints.cs) 选择 mod 入口；安装器不增加 mod 检测或下载逻辑。

`release/publish.mjs` 的 `promotePlatform` 与 `promoteRelease` 先校验完整产物和镜像条件，依次发布每个平台的安装器入口与 mod 入口，然后按原 lockstep 规则推进 Release Manifest。沿用 ETag 条件写、版本不回退与同版本事实不可改写的规则。多对象写入不构成原子事务；失败由同一个发布命令重试补齐，确认已发布版本也补齐缺失的 mod 入口。已领先的平台不会被旧 lockstep 版本的确认操作回退。

旧 mod 继续读取保留的入口，因此可以升级到使用新入口的版本。首次迁移先用新版发布工具完成两个入口，再验证新客户端；新入口短暂不可用时更新检查失败，完整安装器下载路径仍然存在。

Android 同样分开 mod 入口与官网 APK 入口；APK 的身份与复用规则仍由 [ADR 0010](0010-android-release-platform.md) 定义，本决定不要求每个 mod 版本重打 APK。

## 影响

本决定补充 [ADR 0003](0003-per-platform-release-promotion.md) 中各消费者共用平台入口的约定，保留 [ADR 0001](0001-product-release-promotion.md) 的完整产品发布与版本语义。安装器 [ADR 0009](../../bazaarplusplus-installer/docs/adr/0009-repair-keeps-self-updated-managed-payload.md) 的 Repair 行为不变：兼容且完整的较新 Managed Payload 可以保留。

代价是每个平台多一个待发布对象。共用格式与发布事实避免了第二套协议、版本线和兼容性求解逻辑；保留完整桌面安装器则继续承担每个版本的打包、签名与镜像上传成本。

2026-11-01 注：[ADR 0012](0012-android-app.md) 取代 ADR 0010。Android 仍分开 mod 入口与官网入口；Android App APK 的形状与规则由 ADR 0012 定义。

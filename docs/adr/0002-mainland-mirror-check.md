# 大陆镜像地址写入发布清单并在记录时核对

大陆镜像是手工上传到蓝奏云的安装包分享页。此前它的地址由 `release/downloads.ts` 里的规则按版本号拼接，官网和 installer 各自计算，蓝奏云上的文件名或分享后缀一旦没按约定上传，链接就失效，而且没有任何检查能发现。现在地址改为发布者的显式输入：`mirror` 逐平台核对分享页确实提供该平台已上传的安装包，再把地址记录到版本目录；`promote` 把记录写进该平台的 Platform Release Manifest（以及双平台的 Release Manifest）的 `downloads[平台键].mainlandUrl`，按平台提升见 [ADR 0003](0003-per-platform-release-promotion.md)。官网和 installer 只读 manifest，installer 直接使用 Tauri updater 已经取回的同一份清单，不再有第二个地址来源。

记录在该平台提升前可以覆盖，提升后拒绝修改，与 [ADR 0001](0001-product-release-promotion.md) 的一次写入一致：已发布的镜像地址和其他已发布事实一样，修改需要新版本。镜像仍然只是网络兜底，不是第二个发布权威，所以 `promote` 缺少记录时拒绝，但 `--without-mainland-mirror` 允许显式、有日志地跳过；`mirror` 核对不通过时拒绝，但 `--allow-unverified-mirror` 允许把地址记录为未核对。没有静默绕过。

否决的方案：保留拼接规则并只加核对，规则会被烧进每个已发布的 installer，换主机或换后缀就让所有旧客户端的兜底链接失效，而且 manifest 里始终缺少可读的事实；发布后允许追加或改写 `mainlandUrl`，会让 `latest.json` 出现第二个写入者和两种有效状态；让 installer 的自动更新走镜像，蓝奏云只有分享页没有可校验签名的直链，做不到。代价是发布多一步逐平台记录、一次对第三方页面格式的依赖，以及 5.4.0 及更早 installer 自行拼接的旧地址不再维护，它们升级到新版本后才读取清单。核对只证明文件名，不证明字节，也只在核对那一刻成立。

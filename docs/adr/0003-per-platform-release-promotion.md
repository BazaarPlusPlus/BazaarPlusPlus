# 按平台提升发布

两个平台的构建各自需要专用主机、签名和公证，一个平台的修复经常要等另一台机器和它的凭据。此前 [ADR 0001](0001-product-release-promotion.md) 要求两个平台来自相同产品版本和提交、完整校验后才提升唯一的 `latest.json`，并否决"某个平台上传后顺手更新 latest"，因为那会暴露半个版本。现在每个平台拥有自己的 Platform Release Manifest（`latest/<平台键>.json`），`promote --platform` 只推进它；`latest.json` 改为"两个平台都发布了的最新版本"，仅在两个平台版本和提交相同时推进。installer 的 updater endpoint 先读 `latest/{{target}}-{{arch}}.json` 再回退 `latest.json`，mod 和官网按平台读各自文件。半个版本的顾虑仍然成立：每个平台文件都描述一个完整校验过的发布，`latest.json` 保留 ADR 0001 的全部保证。

本记录只修改 ADR 0001 里"唯一 latest 必须双平台齐备"这一条，ADR 0001 的其余决定继续有效：单一 VERSION、版本目录不可变、ETag 条件写、不回退、准备与打包共用锁。新增两条规则：一个版本号只对应一个提交，后发的平台要么构建同一提交，要么升版本号，否则版本号在 Bundle 和支持记录里失去意义；切换前构建的客户端永远只读 `latest.json`，所以第一个带新 endpoint 的版本必须双平台同步发布，`promote --platform` 只在 `latest.json` 达到该版本后才允许，避免旧客户端失去向前的路径。

否决的方案：让 `latest.json` 的 `version` 取更高的平台并把落后平台指向旧包，Tauri 只按顶层 version 决定是否更新，旧客户端会反复安装同一个包；从 `latest.json` 里去掉落后平台的 key，Tauri 在查版本前就先查 key，缺失会让该平台所有检查失败；在服务端按请求分发不同的 `latest.json`，请求里没有任何平台信息。代价是 endpoint 路径成为永久契约、多一个 ADR 和一次真机升级验证，以及切换前的客户端要经过两跳才能拿到单平台发布的版本。

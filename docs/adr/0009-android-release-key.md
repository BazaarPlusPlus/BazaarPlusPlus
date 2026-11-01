# Android Release APK 用一把永不轮换的 BPP 发布密钥签名

Android 不允许签名证书不同的覆盖安装：一个已安装的 APK 只能被同一证书签名的新 APK 升级。合作方不提供官方签名密钥，所以 BPP 必须用自己的密钥给每个 Android Release APK 签名（[ADR 0010](0010-android-release-platform.md)）。用户第一次从官方版换成 BPP 版时卸载一次是无法避免的；之后游戏每更新一次，BPP 重新注入的新 APK 都要能直接覆盖安装、不丢数据，这只在每个 APK 都用同一把密钥签名时成立。

决定如下。BPP 只有一把 Android 发布密钥，是一个 PKCS12 keystore，与它的密码、别名一起，本地放在配置目录，GitHub 上放在 `release` 环境。secret 的名字、作用域和本地来源只由 `release/github-secrets.json` 记录，`just secrets-sync` 负责推送；runner 上由 `.github/scripts/release-secrets.sh stage android` 还原进 `BPP_SIGNING_SECRETS_DIR`，布局与本机 `node scripts/workspace.mjs run signing` 暂存的相同。`release::build android` 用 Android SDK build-tools 的 `apksigner` 以这把密钥签名，密码只经文件传给 `apksigner`，不出现在命令行或日志里。签名证书的 SHA-256 摘要记进 fragment 的 `apk.signerSha256`；签名证书与已提升 APK 不同的新 APK，`upload` 与 `promote` 都拒绝发布，所以误签在用户装上之前就会被拦下。

这把密钥**永不轮换**。换密钥等于让每个已安装的用户卸载重装，并且在设备上丢掉登录态与本地数据；没有任何一种迁移可以避开它（APK 签名方案 v3 的密钥轮换要用旧密钥签发轮换证明，旧密钥丢失时做不到，泄露后持有旧密钥的人同样能签发）。因此：

- 密钥的离线备份是发版的前置条件。首次发布 Android 之前，keystore 与密码必须各自存有至少一份脱离这台开发机和 GitHub 的离线副本，并确认能用 `keytool -list` 从副本打开；没有这份备份，不执行 `release::upload android`：`build_only` 构建的 APK 只用于验收，一旦有用户装上发布的 APK，这把密钥就不能再换。
- 生成密钥时就按永久使用来定参数：RSA 4096 或 EC P-256，证书有效期不少于 30 年（Android 文档建议 25 年以上）。
- 不为它建立第二把"备用"密钥或按渠道分钥：每多一把，就多一种用户无法覆盖安装的路径。

签名与分发全部由 BPP 承担：密钥丢失时所有 Android 用户只能卸载重装，泄露时任何人都能签出可以覆盖 BPP 版的 APK。前者靠离线备份防住，后者靠密钥只存在于配置目录、`release` 环境与发版 job 的临时目录里（`release.yml` 在 job 结束时删除暂存目录），不进仓库、不进 Actions 产物。

否决的方案：每个游戏版本或每年换一把新密钥，所有已安装用户都要卸载；用 Google Play App Signing 托管密钥，BPP 的 APK 不经 Play 分发；把密钥只放在 GitHub secret 里而不留本地副本，GitHub 的 secret 不能读回，等于没有备份；在设备上签名，见 ADR 0010 否决的设备端注入。

代价：这把密钥是 Android 用户群的单点。备份与保密完全靠流程，没有技术手段能在丢失后补救；第一次从官方版切换必须卸载，下载页必须把它写成第一步。

2026-11-01 修订：[ADR 0012](0012-android-app.md) 之后，这把密钥签的是 BPP 的 Android App（`com.bazaarplusplus.android`），不再是注入后的游戏 APK；密钥也换成 2026-11-01 生成的新密钥：PKCS12、RSA 4096、有效期 50 年，别名 `bpp-android-host`，证书 SHA-256 `0d53bd4d2011c3e3181eb291c2154a0162f5dd9f4c4330b7bbc3728840851ce9`。本地文件仍是 `keys/android-release.p12`，secret 的名字不变（`release/github-secrets.json`）。这次可以换，是因为 Android App 是新包名，还没有任何用户装过它：换密钥的代价（每个已安装用户都要卸载重装）这次是零。旧密钥只签过 A 方案的 APK，随 A 方案退役，不再用于任何发布。上文"永不轮换"和"首次发布 Android 之前必须有离线备份并用 `keytool -list` 验证"两条规则对新密钥同样适用，从 Android App 第一次 `release::upload android` 起生效。

签名检查改为两层，都在 `upload` 与 `promote --platform android` 里执行。第一层：Android App APK 的签名证书必须等于发布代码里钉住的证书摘要（`release/` 中的 `ANDROID_APP_SIGNER_SHA256`，一个测试把它与本段的值钉在一起）；不是这把密钥签的 APK 一律拒绝。第二层：已提升的平台清单里有 `app` 时，新 APK 的证书必须与它的 `app.signerSha256` 相同。第一次提升 Android App 时，已提升的清单是 A 方案的形状，只有 `apk`、没有 `app`，第二层没有可比的对象，只有第一层起作用；A 版 APK 的旧证书不参与比较，因为它属于另一个包名。这样第一次提升不需要任何跳过检查的开关：用户确认的是"对外发布"本身，而不是放宽某项检查。

# Android 去掉兼容模式与预适配 interop 包

[ADR 0012](0012-android-app.md) 为"游戏已更新、还没有产品版本适配它"的空窗设了兼容模式：游戏代码一变，`android-apk-watch.yml` 就发布这个游戏的预适配 interop 包和一份预适配记录；Android App 让用户主动选择用它启动；mod 读到启动记录的 `mode: "compatibility"` 后关闭全部上传，把对局记成 History-Only Run。到 2026-11-03，Android App 还没有发布过（6.0.x 是 greenfield）。

## 决定

- Android App 只在已装游戏的游戏代码哈希与平台清单的 `game` 一致时用 BPP 启动（开发构建的 Development interop 除外）。游戏代码比适配的新时，启动器显示"等待适配"，没有主操作，次操作是"打开官方游戏"，直到一个产品版本适配这个游戏；Android App 的更新提醒告诉用户下载这个版本。
- 不再发布预适配 interop 包与预适配记录，Android App 也不再读取。`android-apk-watch.yml` 只采集新游戏、发布它的输入并开 PR。interop 声明的用途只剩 `release <产品版本>`，Android 发布密钥只在 `release::build` 里签 Android App 与 interop 声明。
- 启动记录删去 `mode` 和 `interop.source`。`schemaVersion` 仍是 1：v1 在原处修改，因为 Android App 从未发布，没有设备写过旧的形状。这是首次发布前唯一一次就地修改，之后字段的变化照常升 `schemaVersion`。
- 启动记录读不了、`schemaVersion` 不认识、`processId` 不是当前进程，或 `release` 与正在运行的 Mods 版本不同时，mod 在日志里记录（`android.launch_record.read reason=…`），然后正常运行：上传与 Ghost 同步照常，对局照常记成会封包的 Run。
- Android Host 不再写 History-Only Run。History-Only Run 只来自 installer 的 V5 导入（[ADR 0006](0006-v6-data-root.md)）；表结构里的 `history_only` 状态与封包资格条件不变。

## 否决的方案

- **保留兼容模式（ADR 0012 的做法）。** 它是一种关闭上传的运行模式，mod 要为它在 Android 上带一套上传开关，Android Host 成为 History-Only Run 的第二个写入方，History 要把当前进程的 History-Only Run 特别显示为进行中，每次启动还要关闭遗留的 active 行（#760、#774）。预适配还让 Android 发布密钥多了一个用途：`android-apk-watch.yml` 要在一个单独的签名 job 里使用它，公开桶里也要长期留着不进 Platform Release Manifest 的预适配记录。去掉兼容模式，这些都一起删除。

## 代价

- 游戏更新之后、适配它的产品版本发布之前，Android 用户只能原版启动。空窗从"发布预适配包的一到两个小时"变成"发布一个产品版本的时间"。
- 不可信的启动记录不再关闭上传。这个风险已接受：Mods 换入失败，或者用户回退到旧的 Android App，都会让旧的 Mods 运行在新的 interop 上并照常上传。桌面版已经有同样的风险。

## 修订的记录

- [ADR 0012](0012-android-app.md)：删去"兼容模式"与"预适配 interop 包"两节；三档启动规则里"比清单新"一档只显示"等待适配"；interop 声明删去 `pre-adaptation` 用途；启动记录删去 `mode` 与 `interop.source`，不可信的启动记录改为记日志后正常运行；Android 发布密钥不再在 `android-apk-watch.yml` 里使用。
- [ADR 0007](0007-one-public-bucket-and-domain.md)：公开桶的 `installer/android/` 下不再有预适配记录。

# 6.0.0 改用新的 Data Root，V5 历史由 installer 一次性导入

`BazaarPlusPlusV5/` 从 mod 4.7.0 起写入，经过多轮原地迁移到 `user_version` 3，表结构、打开时的修复逻辑和封包资格规则都带着历史包袱。6.0.0 不再原地迁移，改写新的 Data Root `BazaarPlusPlusV6/`。

决定如下。mod 只认当前 Data Root：`PathConstants.DataRootDirectoryName` 改为 `BazaarPlusPlusV6`，mod 不读、不写、不检测任何 Legacy Root，`user_version` 从 1 重新计数，[mod ADR 0011](../../bazaarplusplus-mod/docs/adr/0011-schema-version-names-column-shape.md) 的版本规则从 V6 v1 起照常适用。表结构只有 mod 一个来源：`bazaarplusplus-mod/src/BazaarPlusPlus.Storage/history-database-v6.schema.sql` 是生产输入，installer 用它建库，不手写 DDL。Data Root 的名字由 mod 和 installer（`BAZAAR_DATA_DIRECTORY`）各写一次，`release::check` 校验两者一致；mod 目前没有自更新渠道，切换只能随 installer 整包发布。

V5 的对局历史及其截图、回放和视频由 installer 一次性导入，导入的对局都是 History-Only Run。History-Only 是 V6 表结构里的显式状态，封包资格条件（`BundleQueueStore.SealEligibleRunCondition`，以及 installer 的 `PROTECTED_RUN_PREDICATE`）先判断它；不能靠伪造一条 outbox 记录挡住封包。否则导入的已完成对局会在首次启动时被建封包任务并上传：V5 已上传过且服务端仍保留其元数据（15 天）的以 409 拒绝，超出 8 天窗口的以 410 拒绝，其余作为新的 Bundle 上传，installer 清理也会把它们当成封包候选保护起来。V5 里没上传的对局不进入 V6 的上传队列。导入的范围、互斥和 Legacy Root 的删除规则见 [installer ADR 0008](../../bazaarplusplus-installer/docs/adr/0008-legacy-data-roots-and-v5-import.md)。

发布线随之简化：master 直接做 6.x，5.x 只保留指向 5.7.0 发布提交的存档分支 `5.x`，不接热修、不发版；6.0 开发期间游戏更新让 5.7.0 失效时由 6.x 承接。以后的大版本切换照此办理。

否决的方案：在 V5 目录原地迁移到新表结构，想甩掉的包袱会留在 mod 里；mod 首次启动时导入，mod 要长期带着读 V5 的代码，导入失败会影响游戏内功能；installer 手写 V6 DDL，表结构会有两个来源；导入未上传的对局并在 V6 继续上传，V6 的上传队列就得兼容 V5 的封包状态；维护 `5.x` 热修线，两条线都要 CI、发版权限和 Snapshot Lock，而 6.0 的开发周期内热修需求可以由 6.x 承接。

代价是 V5 期间没上传的对局永久丢失；6.2.0 之后才首次升级的 5.x 用户不会自动导入，`BazaarPlusPlusV5/` 留在磁盘上作为 Legacy Root；6.0 开发期间游戏更新导致的故障没有 5.x 热修，只能等 6.x 发布。

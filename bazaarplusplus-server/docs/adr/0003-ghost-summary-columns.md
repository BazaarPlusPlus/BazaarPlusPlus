# ADR 0003：Ghost 摘要使用普通列

状态：已接受，生产迁移已完成
日期：2026-09-12

## 决策

D1 的最终 Ghost 表为 `ghost_battle_summaries`，包含十五列。以有语义的表名建立影子表，使新 Worker 的读写目标在过渡及最终状态中保持一致，避免切换时重命名活跃表或重建索引。旧 `ghost_battles` 及 `projection_json` 在迁移结束时删除。

保留身份及查询字段 `uploader_account_id`、`battle_id`、`bundle_id`、`opponent_account_id`、`recorded_at_ms`、`is_final_battle`；摘要字段为 `day`、`hour`、`result`、`winner_combatant_id`、`player_display_name`、`player_hero_name`、`opponent_hero_name`、`player_rank`、`player_rating`。可空的胜者、英雄、段位和评分保持 SQL NULL，响应输出 JSON null。

HTTP 仍按上传者视角构造 `player` 和 `opponent` 对象；`player` 是挑战者，`opponent` 是被查询的本地玩家。下载 URL 及到期时间在查询时签发，对象键来自父 Bundle。普通列名不是公开的扁平 API。

上传 manifest 的校验、描述符和完整 R2 Bundle/Run 不变。只从 D1 摘要及发现响应中省去已确认不消费的属性，详见[字段证据](../ghost-projection-field-audit-2026-09-12.md)。Mod 5.1.0、5.2.0 及已核对的 5.3.0 无需配合发布；旧界面所需的挑战者段位和评分仍保留。

## 不变的业务语义

- 主键仍为 `(uploader_account_id, battle_id)`，冲突时 `DO NOTHING`，不合并、不回填首次投影。
- 资格过滤仍只接受自身对手或已经记录的上传者。`bundle_uploaders` 仍为 Bundle 批次的最后一条提交语句，不成为认证事实。
- 两个 Ghost 索引的键结构不变，最终名称为 `idx_ghost_summaries_query` 和 `idx_ghost_summaries_bundle`。前者服务对手及时间查询，后者服务 Bundle 外键级联。
- Ghost 查询五天，D1 按父 Bundle 的 `stored_at_ms` 保留十五天，每十五分钟有界级联删除。R2 生命周期八天、GetObject 能力七天。

## 在线迁移与首次投影保护

`0003` 建立空影子表和旧表到新表的插入触发器。首次插入旧表成功后才同步新表；旧表的重复插入不会触发同步，因此不能由后来的 Bundle 覆盖尚未复制的首次历史。复制从旧表读取已经通过资格筛选的记录，不重新遍历 Bundle 或上传者集合。复制期间拒绝 Ghost 更新，并禁止不存在对应旧记录的新表插入。

操作工具按复合主键每批至多 1,000 行（默认 500）复制，数据和游标在同一 D1 批次提交。每批独立提交；故障后重新运行，从数据库状态继续。复制结束后从起点分批逐列校验十五个值，使用 `IS NOT` 比较 nullable 值；任何差异使本批回滚，禁止进入桥接阶段。阶段门禁防止直接对有数据的生产库顺序执行所有迁移。

两张表均以 Bundle 外键级联。复制语句只从当前存活的旧记录写入，父删除和复制由数据库串行处理，不会重建已删除父记录或让旧 Ghost 孤立。新上传即使排在扫描游标之前，也由插入触发器立即同步。不可变历史的逐页核对不要求阻塞业务获取全表事务快照。

`0004` 在校验后建立反向插入触发器，允许新旧 Worker 重叠。新表首次插入会向旧表写一份客户端可读的精简 JSON；旧表仍存在首次记录时，新表已经有同一条主键，新的写入被拒绝。两条同步触发器均使用 `ON CONFLICT DO NOTHING`，触发器递归开启或关闭时均不会循环。

D1 的 `meta.changes` 包含触发器写入。新 Worker 改用 `INSERT ... RETURNING battle_id` 的结果数计算投影异常；旧 Worker 在过渡期的异常由旧表 BEFORE INSERT 触发器累积到 `ghost_projection_migration.legacy_duplicates`。计数只针对不同 Bundle 的相同主键，镜像同一条记录不增加计数。运维必须采集这一计数，不能仅依赖过渡期旧 Worker 的应用日志。移除状态表前工具输出最终计数。

新 Worker 全量上线并确认旧请求排空后，`0005` 原子移除双向同步，拒绝迟到的旧 Ghost 写入。迟到请求的整个 Bundle D1 批次失败为可重试 503，上传者最后写入也回滚；R2 orphan 仍由既有重试流程恢复。新 Worker 不依赖旧表，继续正常工作。

旧表按主键每批至多 1,000 行清空，不触及新摘要。`0006` 只允许删除已空旧表，同时移除临时迁移状态。最终没有永久双份存储。

## 代价、回滚与替代方案

过渡期间多一份摘要和两个索引，双写增加数据库工作量；因此需要监控容量和延迟，并在验收后完成旧表清理。逐批工具在 SQL 执行超出两秒预算后停止，已提交进度可恢复。生产 D1 的查询/批次请求上限为三十秒，不能把百万行转换放进一个事务。[Cloudflare D1 限制](https://developers.cloudflare.com/d1/platform/limits/)

桥接期间可回滚到旧 Worker，它仍能读取新写入的精简 JSON。`0005` 后不能再回滚 JSON Worker；回退应使用仍访问摘要表的构建。恢复完整旧数据需要明确的数据库恢复方案，Time Travel 是全库恢复，可能撤销迁移后的正常上传和清理。

未采用直接 `DROP COLUMN projection_json`：需要重写大型活跃表且不兼容当前 Worker。未采用五天删整行：会缩短十五天内首次投影保护。未采用永久双写：不能实现最终存储精简。

操作命令、门禁和验证证据见[迁移运行手册](../ghost-summary-migration.md)。

生产执行结果见[迁移验收](../ghost-summary-migration.md)及[聚合记录](../ghost-summary-production-2026-09-12.json)。

# ADR 0003：Ghost 摘要使用普通列

状态：已接受
日期：2026-09-12

## 决策

D1 的 Ghost 表为 `ghost_battle_summaries`，包含十八列普通列，不存投影 JSON。

保留身份及查询字段 `uploader_account_id`、`battle_id`、`bundle_id`、`opponent_account_id`、`recorded_at_ms`、`is_final_battle`；摘要字段为 `day`、`hour`、`result`、`winner_combatant_id`、`player_display_name`、`player_hero_name`、`opponent_hero_name`、`player_rank`、`player_rating`、`opponent_level`、`opponent_prestige`、`opponent_victories`。`winner_combatant_id` 自 Bundle V6 起必填（NOT NULL）；可空的英雄、段位、评分和本地玩家进度保持 SQL NULL，响应输出 JSON null。

HTTP 仍按上传者视角构造 `player` 和 `opponent` 对象；`player` 是挑战者，`opponent` 是被查询的本地玩家。下载 URL 及到期时间在查询时签发，对象键来自父 Bundle。普通列名不是公开的扁平 API。

上传 manifest 的校验、描述符和完整 R2 Bundle/Run 不变。只从 D1 摘要及发现响应中省去 Mod 不消费的属性，省去的字段列在 [`GET /ghost-battles`](../api-reference.md#get-ghost-battles)。挑战者段位和评分仍保留：Mod 在 `bazaarplusplus-mod/src/BazaarPlusPlus.ModApi/Clients/GhostBattleClient.cs` 的 `TryParseBattle` 中读取 `player.rank` 和 `player.rating`，并在 History 的 Ghost 战斗行中显示。

## 修订：本地玩家进度（2026-11-14）

`opponent_level`、`opponent_prestige`、`opponent_victories` 是挑战者遇到的本地玩家进度，由迁移 `0002_ghost_summary_local_progress.sql` 加入。最初省去它们，是因为 Mod 不读；现在 Mod 要用它们判断一场 Ghost 对局打的是自己哪一局 Run。

游戏和 Bundle 都不记录对手快照来自哪一局，只能比对推断。2026-11-14 对生产数据抽样（12 个本地账号、70 场 Ghost 对局、这些账号已上传的 Run）：

- 只用英雄、天数和时间先后，候选 Run 的中位数为 8–10 个，不能区分。
- 加上等级、声望、胜场，第 5 天起 43 场中 34 场只剩一个候选，其余最多 3 个；第 1–4 天 20 场中只有 1 场唯一。
- 有源 Run 的 63 场中，下载后比对物品和技能，63 场都唯一命中，且命中的那天与 Ghost 的 `day` 相同、三项进度相等。

三列取自已校验的 manifest（V6 combatant 必含这三个字段，值可为 null）。加列之前写入的行保持 NULL，不回填；五天 Ghost 窗口过后，查询结果全部带进度。

## 不变的业务语义

- 主键仍为 `(uploader_account_id, battle_id)`，冲突时 `DO NOTHING`，不合并、不回填首次投影。
- 资格过滤仍只接受自身对手或已经记录的上传者。`bundle_uploaders` 仍为 Bundle 批次的最后一条提交语句，不成为认证事实。
- 两个 Ghost 索引为 `idx_ghost_summaries_query` 和 `idx_ghost_summaries_bundle`。前者服务对手及时间查询，后者服务 Bundle 外键级联。
- Ghost 查询五天，D1 按父 Bundle 的 `stored_at_ms` 保留十五天，每十五分钟有界级联删除。R2 生命周期八天、GetObject 能力七天。

## 替代方案

未采用五天删整行：会缩短十五天内首次投影保护。

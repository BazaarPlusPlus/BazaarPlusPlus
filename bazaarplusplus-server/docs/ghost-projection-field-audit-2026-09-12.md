# Ghost 投影字段使用核对

日期：2026-09-12。

## 结论

可以将 D1 的 `projection_json` 替换为普通列，并只保存已确认的客户端消费字段。建议保留现有六个身份、查询及去重字段，增加九个摘要字段，共十五列。接口继续输出客户端要求的嵌套 JSON 对象；数据库无需保存序列化后的 JSON 正文。

摘要仍然需要保存在 D1：Mod 先同步摘要并写入本地历史库，随后才在需要回放或阵容预览时下载 R2 Bundle。回放所需的完整战斗数据来自 Bundle 的 Run 内容，不依赖列表接口携带所有战斗属性。

这是字段核对结果和列设计建议。生产表、上传处理、响应契约及十五天保留策略尚未因本次核对发生变更。

## 核对范围与证据

- 当前 Mod：`d15903cc0217b32f621d7b47992afdb642b50bed`，配置版本 5.3.0。
- 本地版本标签 5.2.0：`12a5dc2c4b60931ca17aa214077f3d062fe9c515`。
- 本地版本标签 5.1.0：`f70363d1ad25af679a1836e2629b21d66aae183a`。
- 三个版本的 `GhostBattleClient.cs` 内容一致；同时核对了当前界面和两个旧版本的字段消费。该范围是源代码兼容性核对，不是线上客户端版本分布统计。
- 已搜索同工作区 analyzer、site、installer 的 Ghost 路由使用，没有发现额外调用代码。

主要消费链：

1. [GhostBattleClient.cs](../../bazaarplusplus-mod/src/BazaarPlusPlus.ModApi/Clients/GhostBattleClient.cs) 解析接口响应；`day`、`hour` 和双方账号缺失时会丢弃整条战斗。
2. [GhostBattleSyncService.cs](../../bazaarplusplus-mod/src/BazaarPlusPlus/Game/HistoryPanel/Ghost/GhostBattleSyncService.cs) 将摘要写入本地历史库。
3. [HistoryPanelRowMapper.cs](../../bazaarplusplus-mod/src/BazaarPlusPlus/Game/HistoryPanel/Storage/HistoryPanelRowMapper.cs) 和 [GhostBattleLocalProjector.cs](../../bazaarplusplus-mod/src/BazaarPlusPlus/Game/HistoryPanel/Ghost/GhostBattleLocalProjector.cs) 将记录者视角转换为本地玩家的列表视角。
4. [HistoryPanelView.cs](../../bazaarplusplus-mod/src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelView.cs) 显示英雄和战斗标题；[HistoryPanel.Ui.cs](../../bazaarplusplus-mod/src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanel.Ui.cs) 使用挑战者名字、结果和时间。
5. [HistoryPanelGhostBattleFilter.cs](../../bazaarplusplus-mod/src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelGhostBattleFilter.cs) 使用天数、胜者及结果；[HistoryPanelFormatter.cs](../../bazaarplusplus-mod/src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelFormatter.cs) 使用最终战斗标记和胜负判定显示挑战者出局提示。

## 建议保留的普通列

数据库和接口中的 `player` 均指原始记录者，也就是上传者、挑战者。接口的 `opponent` 是被查询的本地玩家。不能将数据库存储视角随着界面视角交换。

| D1 列 | 类型 | 响应字段或用途 |
| --- | --- | --- |
| `uploader_account_id` | TEXT，已有 | 去重主键的一部分；`player.account_id`；回放 Bundle 身份校验 |
| `battle_id` | TEXT，已有 | 去重、排序及定位 Bundle 中的战斗 |
| `bundle_id` | TEXT，已有 | Bundle 关联、下载和十五天级联清理 |
| `opponent_account_id` | TEXT，已有 | Ghost 查询索引；`opponent.account_id`；客户端导入必需 |
| `recorded_at_ms` | INTEGER，已有 | 查询窗口、排序、时间展示及客户端导入 |
| `is_final_battle` | INTEGER，已有 | 挑战者出局提示 |
| `day` | INTEGER，新增 | 客户端导入必需、天数展示和十天以上筛选 |
| `hour` | INTEGER，新增 | 客户端导入必需，不能因当前界面没有直接显示就删除 |
| `result` | TEXT，新增 | 胜负显示和结果回退判定 |
| `winner_combatant_id` | TEXT，可空，新增 | 胜负筛选优先依据、出局提示；不能仅用 `result` 替代 |
| `player_display_name` | TEXT，新增 | `player.display_name`，界面中的挑战者名称 |
| `player_hero_name` | TEXT，可空，新增 | `player.hero_name`，挑战者英雄 |
| `opponent_hero_name` | TEXT，可空，新增 | `opponent.hero_name`，本地玩家英雄 |
| `player_rank` | TEXT，可空，新增 | `player.rank`，5.1/5.2 仍使用的挑战者段位 |
| `player_rating` | INTEGER，可空，新增 | `player.rating`，5.1/5.2 仍使用的挑战者评分 |

5.1/5.2 的 `src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Rows.cs:370` 调用 `BindBattleRankPill`，读取本地视角下的 `battle.OpponentRank` 和 `battle.OpponentRating`。它们来自接口的 **`player.rank` 和 `player.rating`**。当前 5.3.0 界面未使用这两个属性，仍应为已核对的旧版本保留。

`download_url` 和 `download_expires_at_ms` 继续在查询时签发，不新增存储列；`object_key` 继续从关联 Bundle 取得。

## 无需继续写入 D1 Ghost 摘要的字段

| 字段 | 核对结果 |
| --- | --- |
| 双方 `hero_id` | 当前及已核对旧版解析器均不读取；头像使用 `hero_name` |
| 双方 `level`、`prestige`、`victories` | 被解析并传入本地存储或数据对象，但未发现 Ghost 界面、筛选或回放准备流程从接口摘要消费这些值 |
| `opponent.display_name` | 被解析和保存，但 Ghost 列表展示的是记录者名字，即 `player.display_name`；行映射不读取原始对手名字 |
| `opponent.rank`、`opponent.rating` | 对应本地玩家一侧；已核对的新旧 Ghost 界面没有消费它们 |
| `encounter_id`、`combat_kind`、`loser_combatant_id` | 被解析和保存，没有在已核对的 Ghost 列表消费链中产生显示、筛选或下载行为；客户端允许缺省 |

这些结论限定在 **D1 Ghost 列表摘要**。完整战斗数据仍保留在 R2 Bundle 中。回放通过 [GhostManifestProjection.cs](../../bazaarplusplus-mod/src/BazaarPlusPlus/Game/HistoryPanel/Ghost/GhostManifestProjection.cs) 从下载后的 `RunBattleV5` 构造事实、双方属性及快照，因此不应同时删除 Bundle 中的对应字段。

## 解析兼容性验证

使用本地实际 `BazaarPlusPlus.ModApi.dll` 的公开 `ModApiSession.QueryGhostBattlesAgainstMeAsync`，以内存 HTTP handler 输入完整响应和精简响应。通过 Portable PDB 的源码校验和确认 DLL 中的 `GhostBattleClient`、`GhostBattleImportRecord`、`ModApiSession` 和 `ModApiResponse` 与当前源码一致。

结果：

- 精简响应正常导入一条战斗，所列保留字段与完整响应解析结果一致。
- 逐一移除 `battle_id`、`bundle_id`、`recorded_at_ms`、`day`、`hour`、`download_url`、`download_expires_at_ms`、`player.account_id`、`opponent.account_id`，均验证了客户端会丢弃该行。
- 三个核对版本的解析器源码一致；旧界面的段位和评分消费另以源码确认。
- 检查采用合成战斗和内存 HTTP 响应，没有读取玩家正文、调用生产接口或下载 R2 对象，也没有运行游戏界面的交互回归。

## 实施时的约束

保留 `(uploader_account_id, battle_id)` 首次投影判定、资格过滤、两个现有 Ghost 索引和 Bundle 外键级联关系。普通列继续随父 Bundle 保留十五天，不需要为这次格式调整缩短生命周期。

现有线上 Worker 仍读写 `projection_json`，不能先在生产直接删除该列。实施需要完成迁移与 Worker 切换顺序设计，保证新旧部署交接期间的写入和读取，并同步更新公开响应契约及兼容性测试。改变数据库格式不意味着改变客户端要求的 `player`、`opponent` 对象结构。

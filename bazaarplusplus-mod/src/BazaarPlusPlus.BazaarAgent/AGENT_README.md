# The Bazaar Agent 使用指南

本文说明 BazaarPlusPlus 可选 BazaarAgent host 的当前 **V3** 协议。它为外部 agent
提供《The Bazaar》的决策上下文和受游戏原生规则约束的动作入口；host 与游戏始终是
权威，不能仅根据本文推断一个动作必然合法。

## 游戏与术语

《The Bazaar》是经济与自动战斗结合的构筑游戏：一局通常在商人、训练师、事件、战利品、
升级/附魔基座与自动战斗之间循环。每次决策都应根据当前卡牌的 `description`、金钱、棋盘
布局、物品联动、生命/声望和可用操作判断，不要依赖固定卡牌榜或固定遭遇顺序。

- **board**：10 格战斗栏位，索引为 `0` 至 `9`。物品占据连续格子。
- **chest**：非战斗存储。host 在移入或购买到 chest 时自行寻找第一个可用位置；但已有物品
  仍会携带 `slots`，供 agent 理解当前整理状态。
- **skill**：英雄持有的持续技能，不占棋盘格。
- **selection**：当前完整选项行，可包含物品、技能、遭遇，或基座目标选择时的已拥有物品。
- **pedestal**：升级/附魔目标选择。此时应从 `selection` 中选择已有物品，不是在购买新物品。

物品/技能的机制以运行时渲染出的 `description` 为准。仅正冷却时间发送
`cooldownSeconds`；仅正最大弹药发送 `ammoMax`，一旦发送则 `ammo` 可为 0。

## 启动 host

```bash
./run.sh build --with-bazaaragent
```

必须通过 Steam 启动游戏。host 只监听 `http://127.0.0.1:47900`；浏览器打开该地址可查看
只读活动面板。macOS 上 DLL 不会热重载：构建后完全退出游戏，再通过 Steam 重启。日志位于
游戏目录的 `BepInEx/LogOutput.log`，确认其中有 `agent.host.initialized` 与
`agent.listener.started`。

## HTTP 协议

```text
GET  /v3/context
POST /v3/actions
```

没有旧版决策路由，也没有卡牌查询接口。该协议是覆盖式 V3：客户端应按本文件实现，而不要
兼容旧字段形态。

### 首次获取与增量循环

1. 首次 `GET /v3/context` 时不带 session/revision 请求头。
2. 保存响应头 `X-Bazaar-Agent-Session` 与 JSON 中的 `revision`。首次响应带
   `"full": true`，应替换整个本地缓存。
3. 之后每次 GET 同时带：

   ```text
   X-Bazaar-Agent-Session: <session>
   X-Bazaar-Agent-Revision: <revision>
   ```

4. 合并响应增量，再保存新 `revision`。
5. 只对当前 revision 发动作。成功动作响应的 `next` 也是一个 context 增量，必须先合并它。

session 或 revision 缺失、未知或过期会得到 `409 {"error":"resync-required"}`。此时丢弃
缓存并重新 bootstrap；不要盲目重发旧动作。一个 session 只能串行轮询和动作，避免并发请求
互相使 revision 过期。

### 状态

`state` 是增量对象：缺少字段表示未变化。常见字段是 `state`、`busy`、`hero`、`day`、`hour`、
`wins`、`losses`、`gold`、`income`、`health`、`maxHealth`、`prestige`、`level`、`rerolls` 与
`rerollCost`。

不会发送 run UUID、游戏模式 GUID、当前遭遇 UUID 或 `freeSelection`。遇到 `encounterType` 时
才发送该字段；它为空时不发送。普通回放外不会发送 `replay`；它仅在回放流程活跃时出现，普通
决策 agent 应以 `operations` 中的 `continue` 为准。

### 卡牌引用与合并

`board`、`chest`、`skills` 是可选增量组：

```json
{
  "upsert": [{ "item": "巧克力棒#00001", "description": "..." }],
  "remove": ["巧克力棒#00001"]
}
```

- board/chest 物品的稳定键是 `item`，格式为 `名称#五位序号`。该完整字符串既用于合并，也是在
  `select`、`move`、`sell` 动作中放入 `id` 的值。
- 已拥有 skill 不发送 `id`。它的 `name` 就是稳定键：`skills.upsert` 中以 `name` 合并，
  `skills.remove` 是名称数组。技能升级/替换表现为旧名称 `remove` 加新技能 `upsert`。
- 普通 `upsert` 仅包含发生改变的字段；`replace: true` 表示整张缓存卡应被替换，未出现字段要
  清除。全量响应时先清空各组再应用 `upsert`。
- 物品或技能首次被 agent 观察到时，`upsert` 会携带完整卡牌数据。之后所有物品与已拥有 skill 的
  `upsert` 都只带稳定键和更新字段；物品在 board/chest 间移动也遵循该规则。agent 应以 `item` 暂存被
  同一 context `remove` 的物品数据，并保留上一份 selection 的卡牌数据以支持加入已拥有区域。仅当
  字段从有值清除为无值时，才会用 `replace: true` 发送完整卡牌。

`selection` 与上述不同：它出现时总是完整数组，必须整体替换旧的 selection，不能逐项合并；字段
缺失表示上一份完整 selection 仍然有效，`[]` 则明确清空旧 selection。首次获取、新的决策状态、
新的日期或小时，以及任何选项内容、顺序或成员变化时都会发送完整数组。

selection 中的可选对象使用字段名表达类型：

```json
[
  { "item": "鱼饵#00017", "size": "Small", "description": "...", "buyPrice": 4 },
  { "skill": "铁锈#00018", "description": "...", "buyPrice": 5 },
  { "encounter": "押注比赛#00019", "description": "...", "buyPrice": 5 }
]
```

selection 中 `item`、`skill`、`encounter` 的完整值均可作为 POST 动作的 `id`。物品还需要明确
放置目标；skill 与 encounter 直接 select 即可。`template` 永不发送。

物品保留布局、体型、品质、附魔、tags、hiddenTags、描述、冷却/弹药与相关价格。技能保留名称、
品质、hiddenTags、描述、冷却/弹药与 selection 购买价；skill 没有 `tags`。遭遇只保留 `encounter`、
`description` 和可能的 `buyPrice`；没有 size、tier、tags、hiddenTags、template 或独立 name。若
Encounter Preview 的动态数据已经就绪，遭遇的 `description` 会在游戏原生说明之后追加该功能算出的
选择结果、奖励池或当前日期品质信息；这是纯文本，不含游戏 Tooltip 富文本标记。

没有原生说明的战斗入口也会明确说明其类型：NPC 战斗会标注 PvE，玩家战斗会标注 PvP，基座会说明
接下来需要选择已有物品作为升级或附魔目标。

每个 NPC PvE 遭遇还会带 `opponent`，它就是游戏中右键该遭遇时展示的怪物阵容：

```json
{
  "encounter": "凯沃斯雄蜂#00019",
  "description": "NPC 战斗：选择后立即进入 PvE 战斗。",
  "opponent": {
    "health": 300,
    "maxHealth": 300,
    "board": [{ "item": "毒刺#00020", "slots": [2], "description": "..." }],
    "skills": [{ "name": "蜂群", "description": "..." }]
  }
}
```

`opponent` 只出现在成功解析到原生怪物模板的 PvE 选项上。其 `board`/`skills` 的卡牌字段与普通
卡牌相同（skill 仍没有 `tags`），但它们是情报而非可操作对象；只能对外层 encounter 的引用发
`select`。通过这份预览比较三个 NPC 后再选择。

如果当前 selection 免费，所有 selection 项目的 `buyPrice` 都会直接是 `0`；不会发送
`freeSelection`。`sellPrice` 仍是卖出所得，不因免费获得而变为 0。

## 战斗边界

Combat/PvpCombat 不流式发送战斗帧。对于已经在上一份 selection 中收到 `opponent` 的 PvE，开始时
不会重复发送双方完整阵容：agent 已有自己的 board/skills 缓存与所选 NPC 的预览。若是在战斗中首次
bootstrap，或 PvP/预览不可用，`battle.phase` 仍为 `starting`，并携带双方完整 board 与 skills。
战斗期间轮询会保持同一 revision。第一份战后可行动 context 将 battle 替换为
`completed`，并包含 result 与双方生命/护盾/燃烧/中毒的起止属性。agent 确认下一次动作后，
`battleCleared: true` 清除已缓存战斗边界。

战斗卡牌遵循同一紧凑字段：物品使用 `item`，skill 使用 `name`；战斗中没有可操作卡，因此 skill
没有 ID。

## 动作

`operations` 是当前值得尝试的权威操作集合；未列出的操作不要请求。`busy: true` 表示原生客户端
正在过渡，应稍后轮询。

| `op` | 必填字段 | 用途 |
| --- | --- | --- |
| `start` | `hero`，可选 `mode`，`revision` | 开始或继续一局。 |
| `select` | `id`，物品还要 `target`/可能的 `slot`，`revision` | 选择选项或基座目标。 |
| `move` | `id`、`target`，board 还要 `slot`、`revision` | 移动已有物品。 |
| `sell` | `id`、`revision` | 卖出已有物品。 |
| `reroll`、`exit`、`continue`、`menu` | `revision` | 执行对应流程操作。 |

示例：

```json
{ "op": "select", "id": "押注比赛#00019", "revision": 42 }
{ "op": "select", "id": "鱼饵#00017", "target": "board", "slot": 3, "revision": 42 }
{ "op": "move", "id": "巧克力棒#00001", "target": "chest", "revision": 42 }
```

`target: "board"` 必须带 `slot`，它是物品首格，且整个体型必须落在 0–9、不碰已有物品或
`lockedBoardSlots`。`target: "chest"` 不带 `slot`，由 host 选择合法位置。host 会重新核验
revision、可用性、空间、锁位和原生交互门禁。`409 stale-or-unavailable` 表示不要重试原动作，
而应先获取并检查当前 context。

## 推荐决策循环

1. Bootstrap 并合并 full context。
2. `StartRun` 且 `operations` 有 `start` 时，再选择 hero/mode。
3. 在 Choice、Encounter、Loot、LevelUp 中比较 selection 的描述和实际价格；免费项的价格已经
   是 0。
4. 购买或移动物品前，依据 `size`、board `slots` 和 `lockedBoardSlots` 选择位置；没有合适 board
   位置时可移入 chest。
5. Pedestal/目标选择时，从 selection 中的已有 item 选取受益最大的目标。
6. 比较 NPC 选项时先阅读各自的 `opponent`；若已收到对应预览，进入 PvE 时无需等待重复阵容。收到
   `completed` 时记录结果后再行动。
7. 每次动作先合并响应 `next`，再继续；只在明确的 `resync-required` 时重新 bootstrap。

## 边界与调试

- host 只控制本地游戏，不能绕过原生输入门禁或远程服务。
- HTTP 入队成功不等于游戏已改变；以 `next` 或后续 context 验证状态转移。
- 保留并增量合并本地缓存，不要反复传输或记录完整缓存。
- 浏览器面板仅用于观察协议活动，不是游戏状态权威来源。
- 运行问题请查看 `BepInEx/LogOutput.log`；BazaarAgent 行以 `[BazaarAgent]` 开头。

实现细节可见 `docs/ARCHITECTURE.md`、
`src/BazaarPlusPlus.BazaarAgent/Transport/BazaarAgentHttpServer.cs` 与
`src/BazaarPlusPlus.BazaarAgent/Runtime/BazaarAgentProtocolV3Projector.cs`。

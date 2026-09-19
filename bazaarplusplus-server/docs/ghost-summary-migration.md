# Ghost 摘要迁移运行手册

## 当前状态

生产已于 2026-09-12T14:12:04.974Z 完成 `0003`–`0006`、逐页复制和十五列校验，并部署摘要 Worker `08cbb9a7-9b06-49ba-b56a-7ebba6c13369`，承接 100% 流量。旧 `ghost_battles`、旧索引、兼容触发器和临时迁移状态已全部移除；最终只保留十五列摘要表及两个业务索引。

已部署性能与十五天保留优化通过 [PR #7](https://github.com/BazaarPlusPlus/bazaarplusplus-server/pull/7) 合并，基线为 `59f3fbf4ac5a12dae94219bfe7fb9c7747af9802`。生产库为 `bazaarplusplus-mod-api-v5-db`，ID `953b02ef-f990-4a30-90ad-69fa9c04f2ea`。完整聚合证据见[生产执行记录](ghost-summary-production-2026-09-12.json)。这些时间点记录不代替后续操作前的实时核对。

## 准备与容量

运行环境为 Node 24、仓库依赖 `npm ci`。CLI 使用 `wrangler.toml` 的账号、Worker 和 D1 标识；远端认证优先使用 `CLOUDFLARE_API_TOKEN`，否则使用本机 Wrangler OAuth 配置，不输出凭据。只读状态无需 `--execute`；所有写操作都要求该参数。

```sh
npx wrangler whoami
npm run check
npm test
npx wrangler deploy --dry-run
node scripts/ghost-projection/migrate.mjs status --remote
npx wrangler d1 info bazaarplusplus-mod-api-v5-db --json
npx wrangler deployments list --json
npx wrangler d1 time-travel info bazaarplusplus-mod-api-v5-db --json
```

保留执行前的 Time Travel bookmark 和部署版本，并记录回滚所需的全库写入损失边界。确认没有其他脚本在修改 Ghost 或同时执行迁移；正常上传和十五分钟 Cron 保持运行。

先读取[迁移前旧表容量测量](ghost-summary-capacity-2026-09-12.json)。以下复测命令仅适用于仍保留旧 `ghost_battles` 表的迁移前数据库；只输出聚合，不导出玩家正文或账号清单：

```sh
node scripts/ghost-projection/capacity.mjs --remote --output /tmp/ghost-capacity.json
```

2026-09-12 的 181 个只读分页覆盖约 1,808,000 行，每页最多 10,000 行，最长 SQL 529 ms。旧字段内容 1,388,266,834 字节，十五列 321,516,732 字节，两个索引字段内容均合计 368,832,000 字节；Ghost 总逻辑内容由 1,757,098,834 降为 690,348,732 字节，减少 **1,066,750,102 字节（60.7%）**。必需 JSON 字段类型和双方账号一致性异常为零。

该口径包含 UTF-8 文本及 SQLite 整数载荷、两个 `WITHOUT ROWID` 索引的主键后缀，不含记录头、B-tree、溢出页、空闲空间。各页期间上传和清理持续运行，不是同一事务快照。查询前后生产物理文件分别为 3,315,617,792 和 3,313,303,552 字节；期间没有执行迁移，不能归因为本次优化。在线 `dbstat` 不可用；生产迁移前后 D1 报告的文件大小另见本文生产验收。

复制期间需要额外约 0.690 GB 的摘要及索引逻辑内容，物理峰值更高。Paid D1 每库上限为 10 GB；开始前确认账号及库均有余量，并在复制期间监测增长。不要基于逻辑差额承诺物理文件立即等量缩小。[Cloudflare 限制](https://developers.cloudflare.com/d1/platform/limits/)

## 执行顺序

以下命令中的 `--execute` 应在授权后使用。不要对现有生产库直接运行一次性 `wrangler d1 migrations apply --remote`：`0004` 和 `0005` 有阶段门禁，必须先完成复制校验和 Worker 切换。不要先部署本次 Worker，它读的是摘要表。

1. **准备**：仅应用 `0003`，建立空摘要表、索引、旧写同步及迁移状态。旧 Worker 继续服务。

   ```sh
   node scripts/ghost-projection/migrate.mjs prepare --remote --execute
   ```

2. **复制**：默认每次最多二十页、每页五百行、页间等待 250 ms。重复运行，直到输出阶段 `verifying`。可以缩小页大小；每条语句绝不会无界转换全表。`--concurrency 1..3` 可控制同一个进程中最多几个独立批次在途；每批的游标仍由数据库原子读取并推进，已完成阶段的迟到批次由门禁回滚。先测量小批次和业务延迟，再增加在途批次数。

   ```sh
   node scripts/ghost-projection/migrate.mjs copy --remote --execute --page-size 500 --max-pages 20
   ```

3. **逐列校验**：重复运行至 `verified`。差异导致数据库约束报错且游标不推进；停止切换并定位数据问题，不修补或覆盖首次投影。不要手动修改 `phase` 绕过门禁。

   ```sh
   node scripts/ghost-projection/migrate.mjs verify --remote --execute --page-size 500 --max-pages 20
   node scripts/ghost-projection/migrate.mjs bridge --remote --execute
   ```

4. **部署摘要 Worker**：桥接成功后部署经测试的当前构建，记录新版本 ID。过渡期新旧实例都可读写，首次投影由两张表的同步主键约束保护。

   ```sh
   npm run deploy
   npx wrangler deployments list --json
   ```

   检查健康、真实授权的 Ghost 非空查询、上传/重复上传、D1 错误及签名 URL；不要把空结果 200 当作字段兼容性验收。默认观察新版本 100% 流量至少十五分钟，确认旧请求已排空，并核对正常保留 Cron。采集 `legacy_duplicates`，过渡期旧应用日志可能低报混合重复。

5. **停止兼容双写**：CLI 重新读取最新部署，要求指定版本为 100% 且至少十五分钟前创建。版本参数必须是上一步确认的摘要构建；这项核对不能代替构建来源与请求排空确认。

   ```sh
   node scripts/ghost-projection/migrate.mjs retire --remote --execute --worker-version SUMMARY_WORKER_VERSION_UUID
   ```

   若操作人明确决定缩短观察窗口，可在该命令增加 `--skip-observation`，并记录授权、已验证的版本和业务检查结果。此参数只跳过十五分钟等待，仍强制核对指定摘要版本承接 100% 流量。

   此时旧 Ghost 插入会使整个 D1 批次回滚为可重试错误。此阶段以后只允许回退到仍访问摘要表的 Worker。部署平台核对与 D1 迁移不是跨系统事务，执行期间不要并行变更部署。

6. **有界移除旧正文**：重复执行至本次 `rows` 为零。新摘要的写入和十五天级联清理继续运行。

   ```sh
   node scripts/ghost-projection/migrate.mjs cleanup --remote --execute --page-size 500 --max-pages 20
   node scripts/ghost-projection/migrate.mjs finish --remote --execute
   ```

   `finish` 先输出最终旧写异常计数，然后只删除已经空的旧表、旧索引和临时迁移状态；不转换或删除新表数据。每个 SQL 迁移与 `d1_migrations` 记录在同一批次提交，重跑已应用阶段安全。

## 故障与验收

CLI 的每个复制、校验或清理页使用一个 D1 批次，游标和数据一同提交。本地使用 `D1Database.batch`，远端将语句作为一个 REST `/query` 请求提交；官方接口将分号连接的多语句作为 batch 执行。[D1 REST 查询](https://developers.cloudflare.com/api/resources/d1/subresources/database/methods/query/)、[D1 批次事务](https://developers.cloudflare.com/d1/worker-api/d1-database/#batch)。

出错即停止；网络错误可能表示响应丢失，重跑时先读数据库阶段和迁移历史，不能假定上一页没有提交。已完成页面不需回滚。工具不进行无限重试或一次性全表转换。Wrangler OAuth 返回 401 或 403 / 7403 时，先运行 `npx wrangler whoami` 刷新，再重新运行当前阶段；使用环境变量 API token 时需换成有效凭据。

若批次超过两秒预算、业务延迟上升、D1 overloaded、容量余量不足或清理 Cron 持续失败，停止启动下一批并缩小页大小。已提交页面保留。桥接阶段可暂时停止推进并回滚旧 Worker；`retired` 后不可回滚到旧 JSON Worker。

结束时检查：`ghost_battles` 和 `ghost_projection_migration` 不存在；`ghost_battle_summaries` 恰好十五列；两个最终 Ghost 索引和 Bundle 的 `ON DELETE CASCADE` 存在；迁移记录包含 `0003`–`0006`；上传者集合未清理；Cron 仍 `*/15 * * * *`。再次记录 D1 物理大小、时间和请求延迟，区分实际物理变化与逻辑估算。

## 可重复本地验证

空库迁移路径可用 `init-local` 初始化，然后将上述阶段的 `--remote` 改为 `--local-dir /tmp/ghost-migration-local` 逐个执行。不要用 `init-local` 连接生产。

```sh
node scripts/ghost-projection/migrate.mjs init-local --local-dir /tmp/ghost-migration-local --execute
npm test -- test/ghost-migration.test.ts test/contracts/ghost-summary-contract.test.ts
node scripts/ghost-projection/rehearse.mjs --database /tmp/ghost-rehearsal-new.sqlite --rows 1800000 --output /tmp/ghost-rehearsal.json
```

规模演练使用原生本地 SQLite 和合成数据，包括完整旧 schema、1,800,000 条 Ghost 与两个索引、600,000 个父 Bundle、实际追加迁移和相同分批 SQL。它测量本机执行耗时和本地物理页，不代表生产 D1 延迟或物理回收。结果见[本地演练](ghost-summary-rehearsal-2026-09-12.json)：每页 500 行，复制 3,601 页，最长 99.8 ms、P95 17.0 ms；校验最长 9.94 ms；旧表删除最长 26.95 ms。结束保留 1,800,000 条摘要、外键错误为零。新增摘要及索引使本地文件从 2.496 GB 增至 3.354 GB；删除后文件仍约 3.354 GB，其中 2.137 GB 为可复用空闲页。这明确展示了逻辑删除不等于文件立即缩小；没有执行 VACUUM。

真实 Mod 解析验证由 `just server::test` 自动执行，也可从 monorepo 根目录单独运行以下 recipe。它先构建相邻 Mod 的 `ModApi.Tests`，再读取其 DLL/PDB；只需 .NET SDK，不需要游戏 Managed 程序集。Portable PDB 校验消费源码，契约文件同时由服务器测试精确比较：

```sh
just server::test-mod-compat
```

探针验证完整及精简响应的消费字段一致、九项导入必需字段缺失会丢行，以及实际服务器合同的挑战者段位评分、胜者、天时和最终战斗标记。5.1/5.2/当前提交的解析器一致性及旧界面消费依据见[字段审计](ghost-projection-field-audit-2026-09-12.md)。这不是游戏 UI 交互回归或线上客户端版本分布统计。

## 已执行验证结果

- `npm run check`：应用及测试 TypeScript、Biome 通过。
- `npm test`：完整套件 23 个文件、162 项测试通过；加入并发分页覆盖后，迁移专项 8 项测试通过。
- `npx wrangler deploy --dry-run`：构建通过；生产部署使用同一应用源码。
- 实际 ModApi DLL 探针：四份消费源码 PDB 校验通过；完整/精简响应消费字段一致；九项必需字段删除均丢行；服务器精确合同被接受。
- 当前 Mod 提交为 `d15903cc0217b32f621d7b47992afdb642b50bed`。5.1.0、5.2.0 解引用提交分别为 `f70363d1ad25af679a1836e2629b21d66aae183a`、`12a5dc2c4b60931ca17aa214077f3d062fe9c515`；三者 `GhostBattleClient.cs` Git blob 均为 `834cce9bcbd83b361f0914f18cd0ba6504da1387`。
- 迁移测试覆盖复制页完整回滚与游标恢复、晚到旧写、新旧并发写及递归触发器两种设置、混合重复异常计数、字段差异阻止切换、缺失状态不能绕过门禁、退休后旧上传原子失败、父级保留清理与上传者保留，以及最终十五列且无旧表。
- 本地 CLI 的初始化至完成全部阶段通过；生产分阶段执行与最终 schema 验收通过。

## 生产验收（2026-09-12，UTC）

`0003` 于 13:33:23 开始准备；13:54:33 的同一查询批次确认旧表与摘要表均为 **1,810,036 行**，十五列逐页比较无差异。13:55:12 建立兼容桥接，13:55:16 部署 `08cbb9a7-9b06-49ba-b56a-7ebba6c13369`。操作人明确授权“可以直接移除”，因此保留指定版本 100% 流量核对并使用 `--skip-observation` 缩短默认等待；14:00:00 进入 `retired`。`0006` 于 2026-09-12 14:11:41 完成。

| 阶段 | 日志累计行数 | 批次数 | 最慢批次 SQL |
| --- | ---: | ---: | ---: |
| 复制 | 1,809,672 | 1,821 | 184.6 ms |
| 校验 | 1,809,534 | 1,811 | 48.4 ms |
| 旧表清理 | 1,809,286 | 1,811 | 216.2 ms |

复制首批每页 500 行，随后每页 1,000 行，最多三个批次在途，页间等待 250 ms。行数是各阶段实际处理的累计值；上传与十五天保留清理持续运行，各阶段不是同一时间点快照。移除状态表前 `legacy_duplicates` 为零。清理至约 177 万行时 API 返回 403 / 7403，工具停止；刷新 Wrangler 认证并确认数据库仍为 `retired` 后继续剩余行，未回滚已提交清理。

D1 API 报告的全库大小由 **3,310,116,864 字节（3.310 GB）**，经观测峰值 4,162,883,584 字节，降至 **1,382,100,992 字节（1.382 GB）**；净减少 **1,928,015,872 字节，58.2%**。这是全库现场测量，期间包含正常上传与保留清理，不等同于只读审计的 Ghost 逻辑内容减少 60.7%。没有执行全库 VACUUM。

最终 schema 核对十五列、两个业务索引及 Bundle 的 `ON DELETE CASCADE` 通过；旧表、临时状态和兼容触发器均不存在。最终读取摘要 1,810,810 行、上传者 23,410 行。停止双写后已有 536 个新 Bundle，其中 490 个具有 Ghost 摘要，确认新写入持续成功。

删除旧表后的非空 Ghost 查询、全部返回字段与 D1 摘要比对、七天签名能力、实际 R2 Bundle 下载和原 Bundle 重复上传均通过。重复上传返回 HTTP 200、`outcome: duplicate`，未生成测试投影。线上日志样本中未观测到新版本异常；Tail 连接出现过空档，因此该样本不代表完整错误统计。

Cron 配置仍为 `*/15 * * * *`。迁移期间捕获旧版本一次正常定时清理，删除 588 个过期父 Bundle；上线后的数据库核对确认没有早于 14:00 调度对应十五天截止点的父记录。由于日志连接空档，未将该边界核对宣称为已捕获新版本 Cron 日志。R2 的 `v5-bundle-retention` 规则仍启用，`bundles/` 对象八天到期。

执行前的 Time Travel bookmark 记在部署操作记录里，不进仓库。它对应全库历史恢复点；完成 `0005` 后只能回退到继续读取摘要表的 Worker，不能单独回滚至旧 JSON Worker。持久聚合证据保存在仓库的生产执行记录中，不含玩家账号、Bundle 正文或签名 URL。

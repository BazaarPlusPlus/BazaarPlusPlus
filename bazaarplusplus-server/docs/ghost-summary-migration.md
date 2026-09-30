# Ghost 摘要迁移运行手册

## 当前状态

生产已于 2026-09-12 完成 `0003`–`0006`：旧 `ghost_battles`、旧索引、兼容触发器和临时迁移状态已移除，只剩十五列摘要表和两个业务索引。只有仍保留旧 `ghost_battles` 表的数据库才需要本手册。设计理由见 [ADR 0003](adr/0003-ghost-summary-columns.md)。

生产执行的聚合证据见[生产执行记录](ghost-summary-production-2026-09-12.json)、[容量记录](ghost-summary-capacity-2026-09-12.json)和[本地演练](ghost-summary-rehearsal-2026-09-12.json)，其中不含玩家账号、Bundle 正文或签名 URL。生产结果：全库从 3.310 GB 降到 1.382 GB（D1 API 报告的现场测量，期间上传与保留清理照常运行，未执行 VACUUM）。

## 准备

CLI 使用 `wrangler.toml` 的账号、Worker 和 D1 标识；远端认证优先读 `CLOUDFLARE_API_TOKEN`，否则用本机 Wrangler OAuth。只读状态不需要 `--execute`，所有写操作都需要。

```sh
npx wrangler whoami
node scripts/ghost-projection/migrate.mjs status --remote
```

开始前记下 Time Travel bookmark 和当前部署版本（记在部署操作记录里，不进仓库），确认没有其他脚本在修改 Ghost。正常上传和十五分钟 Cron 保持运行。复制期间摘要及索引需要约为旧表 40% 的额外逻辑空间，物理峰值更高；确认账号和库都有余量。逻辑删除不会让物理文件立即等量缩小。

## 执行顺序

`0004` 和 `0005` 有阶段门禁，必须先完成复制校验和 Worker 切换，所以既有库只能按以下阶段执行，一次性 `wrangler d1 migrations apply --remote` 会跳过门禁。摘要 Worker 读的是摘要表，要在第 4 步才部署。

1. **准备**：只应用 `0003`，建立空摘要表、索引、旧写同步和迁移状态，旧 Worker 继续服务。

   ```sh
   node scripts/ghost-projection/migrate.mjs prepare --remote --execute
   ```

2. **复制**：重复运行，直到输出阶段 `verifying`。`--concurrency 1..3` 控制同一进程中的在途批次数；先测量小批次和业务延迟，再加大。

   ```sh
   node scripts/ghost-projection/migrate.mjs copy --remote --execute --page-size 500 --max-pages 20
   ```

3. **逐列校验**：重复运行到 `verified`。差异会让数据库约束报错且游标不推进：停止切换并定位数据问题，保留首次投影。`phase` 只由 CLI 推进。

   ```sh
   node scripts/ghost-projection/migrate.mjs verify --remote --execute --page-size 500 --max-pages 20
   node scripts/ghost-projection/migrate.mjs bridge --remote --execute
   ```

4. **部署摘要 Worker**：桥接成功后部署当前构建并记录版本 ID。用真实授权的非空 Ghost 查询验收，空结果的 200 证明不了字段兼容；再检查上传与重复上传、D1 错误和签名 URL。默认观察新版本 100% 流量至少十五分钟。

   ```sh
   npm run deploy
   npx wrangler deployments list --json
   ```

5. **停止兼容双写**：CLI 要求指定版本承接 100% 流量且已部署至少十五分钟。`--skip-observation` 只跳过等待，需操作人明确授权并记录。此后只能回退到仍读摘要表的 Worker；执行期间不要并行变更部署。

   ```sh
   node scripts/ghost-projection/migrate.mjs retire --remote --execute --worker-version SUMMARY_WORKER_VERSION_UUID
   ```

6. **移除旧正文**：重复 `cleanup` 直到本次 `rows` 为零，再执行 `finish`。`finish` 只删除已经为空的旧表、旧索引和临时状态。

   ```sh
   node scripts/ghost-projection/migrate.mjs cleanup --remote --execute --page-size 500 --max-pages 20
   node scripts/ghost-projection/migrate.mjs finish --remote --execute
   ```

## 故障

每个复制、校验或清理页是一个 D1 批次，游标和数据一起提交；已完成的页不需要回滚。出错即停止。网络错误可能只是响应丢失，重跑前先读数据库阶段和迁移历史。Wrangler OAuth 返回 401、403 或 7403 时，先运行 `npx wrangler whoami` 刷新，再重跑当前阶段。

批次超过两秒、业务延迟上升、D1 overloaded、容量余量不足或清理 Cron 持续失败时，停止启动下一批并缩小页大小。

结束时检查：`ghost_battles` 和 `ghost_projection_migration` 不存在；`ghost_battle_summaries` 恰好十五列；两个 Ghost 索引和 Bundle 的 `ON DELETE CASCADE` 存在；迁移记录包含 `0003`–`0006`；Cron 仍为 `*/15 * * * *`。

## 本地验证

用 `init-local` 初始化空库，再把上述阶段的 `--remote` 换成 `--local-dir <目录>` 逐个执行；`init-local` 只用于本地库。

```sh
node scripts/ghost-projection/migrate.mjs init-local --local-dir /tmp/ghost-migration-local --execute
npm test -- test/ghost-migration.test.ts test/contracts/ghost-summary-contract.test.ts
```

`just server::test-mod-compat`（`just server::test` 也会运行）用相邻 mod 的 `ModApi.Tests` 构建产物核对真实 mod 解析器接受服务端响应，需要 .NET SDK 和本机游戏 Managed 程序集。旧版本解析器一致性的依据见[字段审计](ghost-projection-field-audit-2026-09-12.md)。

# BazaarPlusPlus QQ Bot

在群里 @ 机器人查询最近三天战绩；只 @ 不带文字就是查自己。`src/commands.ts` 的 `HELP` 是命令列表和用法的唯一来源。

## 领域术语

跨项目术语遵循[根词汇表](../GLOSSARY.md)（`Bundle`、`Run`、`Card Art`）与[分析器词汇表](../bazaarplusplus-analyzer/GLOSSARY.md)（`Accepted Run`、`Ten-Win Run`、`Build Identity`）。本服务自有两个核心概念：

- **Binding**：将一个 QQ `member_openid` 映射到一个游戏账号及绑定时使用的显示名，独立于群组；多个 QQ 用户可绑定同一游戏账号。它是可见性 opt-in，不是归属证明；只要至少存在一条 Binding，任何人都可以查询该账号，解绑只删除调用者自己的 Binding。
- **Player Report**：针对北京时间当天及前两天内结束的已完成 `Accepted Run` 实时计算的战报视图；任务结束后不保留任何 Run 或 Battle 数据。

## 数据和处理

Webhook 只验签、处理 QQ 验证挑战、将消息放入 Queue 并 ACK。consumer 确定是图片命令后立即启动 Browser Run，与数据处理并行。它下载这个账号的候选 Bundle，逐份校验 SHA-256、流式解压 Run，跳过事件正文与录像，结算截图只校验不绘制，计算后渲染 JPEG。图片生成或上传失败时发送文字。每次回复记录一条 `bot.timing` 日志，含各步骤耗时和 Bundle 数。所有回复携带原始消息 ID 和固定序号；QQ 的重复消息错误视为已处理。超过被动回复时限的任务直接丢弃。

时间固定为北京时间今天和前两天。候选 Bundle 按上传时间选到处理开始，最终按 Run 结束时间过滤，因此支持前一天结束、当天上传的对局。异常客户端时钟不作修正。仅统计已上传、英雄与最终段位可识别的完成局；统计语义以 analyzer 的 `docs/specs/consumer-data-contract.md` 为准。战绩图每局一行：左边是胜负和英雄，右边是最终阵容，新局在上，最多画 `ROWS` 局。图片宽度跟随最宽的阵容。缺失数据保留为空；无法解码的 Bundle 在图中标注为不完整统计。

D1 只存 Binding。绑定不验证归属；任何人可查询仍被绑定的账号，解绑只删自己的绑定。按游戏名查找依赖 server 的合格 Ghost 投影；找不到时可以显式绑定 `account:游戏账号ID`，但该账号必须有近期 Bundle。重名时要求显式选择账号。带空格的名字可直接输入；与英雄或时间词重名的名字使用双引号。

## 本地验证

从根目录 `just setup` 安装依赖。`just bot::check bot::test bot::build` 做类型、lint、Cloudflare 本地测试和两个 Worker 的 dry-run 构建。`just bot::preview <Bundle 目录> [--name <显示名>] [--art <卡图目录>]` 用一位玩家的真实 Bundle 生成 `artifacts/qqbot/` 下的战绩图和英雄图 PNG，需要在本项目执行 `bun run playwright install chromium` 安装预览浏览器。

阵容图用公开的卡图（Card Art，根目录 ADR 0020）。安装器的 Rust crate `card-catalog`（根目录 ADR 0024）从 Steam 文件和游戏的 GameData 提取卡图，`.github/workflows/card-art.yml` 在游戏更新后自动重新提取并发布到 `https://cdn.bazaarplusplus.com/card-art/`。本地提取：`just release::card-art download <目录>`（需要 Steam 登录），然后 `just installer::card-art <目录> <输出目录> --zh-etag <ETag>`。

共享 `config.toml` 的 `[qqbot]` 提供既有 QQ 凭据，`[bot] BOT_STATS_TOKEN` 提供独立的服务 token。`just setup --skip-deps` 将它们投影到 `.dev.vars`，Bot token 同时投影到 server。`just bot::dev` 刷新配置后启动本地双 Worker。

## 部署与验收

生产资源操作需要明确的部署授权。本地验证不会创建云资源。Webhook 的回调地址是 `https://qqbot.bazaarplusplus.com/webhook`（`wrangler.toml` 的自定义域名，因为 QQ 从国内回调，`workers.dev` 在国内常不可达）。

1. 在同一 Cloudflare 账号创建 Queue 和 D1（名字由两个 Wrangler 配置持有），将真实 D1 ID 写入 `wrangler.consumer.toml`。启用 Browser Run。
2. 对 server 应用新增迁移，对 Bot 应用 `migrations/`；在 server 与 consumer 设置相同的 `BOT_STATS_TOKEN`，其格式与现有 service token 相同且值必须不同。在两个 Worker 设置 `QQ_BOT_APP_SECRET`、`QQ_BOT_APP_ID`。本地 `.dev.vars` 不会自动上传。
3. 部署 server、consumer、webhook。将 webhook 的 HTTPS `/webhook` 地址填入 QQ 开放平台，订阅 `GROUP_AT_MESSAGE_CREATE`。
4. 用 `scripts/qqbot/zh-names.ts` 重新生成并发布现有名称数据：新增 `cards` 字段将模板 ID 对应到英文名称，原来的 `names` 再映射为中文。未更新时未知物品显示“物品资料待更新”，不展示 UUID。
5. 测试群依次验证帮助、绑定、只 @、战绩 游戏名、英雄、解绑、未绑定查询；再由同一 QQ 用户在第二个测试群查自己，确认平台的 `member_openid` 跨群稳定。代码不混用 `author.id`；若实测不稳定，应先调整身份方案再开放使用。
6. 检查 Browser Run 的中文字体、真实 Bundle 解码耗时与回复时限，以及 QQ 图片额度。失败日志只记录类型和错误码，不记录凭据、下载 URL 或对局正文。持续图片限额失败时可使用文字回退，或单独申请查询机器人。

QQ 签名和挑战按[官方事件文档](https://bot.q.qq.com/wiki/develop/api-v2/dev-prepare/interface-framework/event-emit.html)及[签名文档](https://bot.q.qq.com/wiki/develop/api-v2/dev-prepare/interface-framework/sign.html)实现，并用官方挑战向量验证。Browser Run 预装 Noto CJK 和文泉驿正黑（[预装字体文档](https://developers.cloudflare.com/browser-run/reference/supported-fonts/)），字体栈先用这两者；线上渲染仍需测试群验收。

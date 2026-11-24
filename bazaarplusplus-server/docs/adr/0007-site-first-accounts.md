# ADR 0007：先在网站注册的 BPP 账号、绑定码与长期保存的对局详情

状态：已接受
日期：2026-11-24
取代：[ADR 0006](0006-bpp-accounts-and-run-summaries.md)

## 背景

ADR 0006 的账号从 Mod 开始：玩家在游戏内填邮箱，收邮件验证码，再拿到设备令牌；网站只能用邮件验证码登录。账号与游戏账号一对一且不可分开，没有密码，网站上也看不到一局的详细过程。上线时生产 D1 已有 0005 迁移，但所有账号表都是空的，Mod 的账号界面也没有发布，所以这次直接替换，不留兼容分支。

## 决策

1. **账号在网站创建**：BPP 账号是“邮箱 + 密码”（`bpp_accounts.account_id` 为随机 UUID）。注册与重置密码都发 6 位邮件验证码（`bpp_email_challenges`，`purpose` 为 `register` 或 `reset`）。密码用 WebCrypto 的 PBKDF2-HMAC-SHA256，100 000 次迭代，16 字节盐，存为 `pbkdf2-sha256$100000$<盐>$<哈希>`。未知邮箱的登录也对一个固定的假哈希做一次 PBKDF2，耗时与错误码都和密码错误相同。登录另有按规范化邮箱计数的限流 `LOGIN_EMAIL_RATE_LIMITER`。重置密码会结束该账号的所有网页会话。
2. **绑定码连接游戏账号**：网站为未绑定的账号生成 8 位**绑定码**（字母表 `23456789ABCDEFGHJKLMNPQRSTUVWXYZ`，10 分钟有效，每个账号同时只有一个，D1 只存 `HMAC(ACCOUNT_CODE_SECRET, 码)`）。玩家在 Mod 里输入它，Mod 带上官方游戏令牌调用 `POST /account/bind`。服务端校验官方令牌后，用一个 D1 batch 完成三件事：写入 `game_account_id`、签发设备令牌、删除绑定码；前两条语句都以“码仍有效”为条件，签发设备令牌的语句改动 1 行才算成功。游戏账号已绑到别的 BPP 账号时返回 `game_account_already_bound`，绑定码不消耗。`game_account_id` 上的 UNIQUE 约束兜住并发：batch 整体回滚，绑定码保留。
3. **解绑与注销**：`POST /api/unlink` 只断开游戏账号，网页会话保留；`POST /api/account/delete` 需要密码，删除账号及其全部数据。两者都先删除 R2 中的对局详情，再执行一个 D1 batch。
4. **对局详情长期保存**：上传的影子鉴权为 `match` 且 manifest 带终局 `projection.run` 时，服务端在 D1 提交之前，用 R2 范围读取把 Bundle 中的 Run 段复制到 `account-runs/<account_id>/<run_id>.run`，并在同一次 `run_summaries` 插入里写 `detail_stored = 1`。复制失败时上传返回可重试的 `storage_unavailable`，什么都不提交；Mod 重试时，现有的“仅 R2 对象”恢复路径接住已写入的 Bundle。这次读取在 Bundle 的流式上传完成之后，最多 2 MiB，整段读入内存再写出；上传本身仍只缓冲前缀与 manifest。服务端只复制字节，从不解码 Run 段。`run_summaries` 只在令牌所属的 BPP 账号仍链接该游戏账号时写入；提交之后，若同一账号下没有指向这份副本的摘要（上传途中被解绑或注销），服务端删除这次写入的副本。网站通过 `GET /api/runs/<run_id>/payload` 把原始字节交给浏览器解码。
5. **Turnstile 拦截机器人**：注册发码、登录、重置发码三个入口要求 Cloudflare Turnstile 令牌，服务端在按客户端限流之后、任何 D1 查询与发信之前调用 siteverify 校验，并要求令牌的 `hostname` 与请求的主机一致。确认步骤、绑定码、解绑、注销与 Mod API 不需要令牌。选 Turnstile 而不是 reCAPTCHA：很多中国大陆玩家打不开 reCAPTCHA；Turnstile 免费，而且和站点、服务端在同一个 Cloudflare 账号里。siteverify 不可用时返回可重试的 `captcha_unavailable`，缺少密钥时返回 `internal_error`，两种情况都不放行。

## 与 ADR 0006 的不同

- ADR 0006 规定长期存储里不含对手账号与昵称。Run 段包含整局的对手信息，所以本决策对**已绑定账号自己的对局**放弃这一条：玩家本人在网站上看自己的对局过程，需要这些数据。未绑定玩家与他人的对局不受影响。
- 删除仍由玩家掌握：解绑或注销时先删除 `account-runs/<account_id>/` 下的全部对象，再删除对应的 `run_summaries` 行。
- 影子鉴权、`run_summaries` 不建指向 `bundles` 的外键、`WebApi` 只经站点同源 `/api/*` 暴露，这些都保持不变。

## R2 保留与成本

- 存储桶的 8 天生命周期规则只覆盖 `bundles/` 前缀（`docs/deployment-runbook.md`），`account-runs/` 不会过期。任何新加的生命周期规则都必须带前缀，否则会删掉对局详情而 `detail_stored` 仍为 1。
- 定时清理删除过期的网页会话与绑定码，以及过期满 24 小时的邮件验证码；从不清理 BPP 账号、设备令牌、`run_summaries` 与对局详情。
- 每局约 0.5 MB（Run 段上限约 2 MiB）。按 R2 标准存储 0.015 美元 / GB·月估算，每 1 万局约 5 GB，每月约 0.075 美元，另加每局一次写入与一次范围读取。

## 后果

- 网站成为账号的入口：注册、登录、重置密码、生成绑定码、看战绩与对局详情。Mod 只剩“输入绑定码”和自动的 `POST /account/devices`。
- 删除了 `/account/bind/start`、`/account/bind/confirm`、`/api/login/start`、`/api/login/confirm`、`/api/unbind` 与错误码 `email_already_bound`。

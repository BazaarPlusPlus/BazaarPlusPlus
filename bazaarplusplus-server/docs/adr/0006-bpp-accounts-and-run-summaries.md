---
superseded-by: 0007-site-first-accounts.md
---

# ADR 0006：游戏令牌校验的 BPP 账号与长期每局摘要

状态：已接受
日期：2026-11-24

## 决策

在 Mod API Worker 内引入一对一绑定的 **BPP 账号**（邮箱 ↔ 游戏账号 id）、**设备令牌**（`bppd_` 前缀）、影子鉴权、长期保存的 **每局摘要**（`run_summaries`），以及仅供站点同源 service binding 调用的命名入口 `WebApi`。

1. **绑定与归属校验**：
   - 玩家在 Mod 内明确确认后，客户端将当前游戏会话的 access token、`game_account_id`、`platform`、`flavor` 与目标邮箱发往 `POST /account/bind/start`。
   - 服务端使用写死的官方只读地址 `https://enter.playthebazaar.com/api/Accounts/me/summary` 校验令牌，只取返回里的 `id` 与 `userName`，其余字段（含官方邮箱）立即丢弃；令牌永不落盘、永不进日志。
   - 若官方 `id` 与 `game_account_id` 不一致返回 `409 account_mismatch`；邮箱与游戏账号强制一对一（`bpp_accounts` 上同时对 `game_account_id` 主键与 `email` 建唯一约束）。
   - 校验通过后发送 6 位 HMAC-SHA256 验证码（10 分钟有效、最多 5 次尝试、按邮箱每小时限流）。确认后签发 32 字节随机设备令牌（`bppd_<base64url>`），D1 仅保存 SHA-256 哈希。
   - 已绑定游戏账号在新设备上可通过 `POST /account/devices` 校验游戏令牌直接签发新的设备令牌。
2. **影子鉴权与每局摘要**：
   - `POST /bundles` 与 `GET /ghost-battles` 接受可选的 `Authorization: Bearer bppd_…`，记录结构化日志字段 `shadow_auth`（`absent` / `match` / `mismatch` / `invalid`），**不改变**现有响应，不阻断旧版或未绑定客户端。
   - 只有当上传携带有效设备令牌、令牌归属账号等于 `run.player_account_id`、且 `manifest.run.projection.run` 含有终局字段时，才在 Bundle 提交的同一 D1 batch 中（`insert_uploader` 之前）以 `INSERT … SELECT … WHERE EXISTS(bpp_accounts) ON CONFLICT(run_id) DO NOTHING` 写入 `run_summaries`。
   - `run_summaries` 所有终局字段均可空，不含对手账号与昵称，且**不建指向 `bundles` 的外键**。
3. **网站入口隔离**：
   - `WebApi`（`WorkerEntrypoint` 命名导出）服务 `/api/login/start`、`/api/login/confirm`、`/api/me`、`/api/logout`、`/api/unbind`，由站点 Worker 通过同源 `/api/*` service binding 转发。`mod-api-v6.bazaarplusplus.com` 默认路由不暴露 `/api/*`。
   - 所有 `POST /api/*` 检查同源 `Origin`；会话 cookie 使用 `__Host-bpp_session`（`HttpOnly; Secure; SameSite=Lax; Path=/`），D1 仅存哈希。
   - `POST /api/unbind` 在单次 D1 batch 中删除该账号的 `run_summaries`、`bpp_device_tokens`、`bpp_web_sessions`、待确认验证码记录与 `bpp_accounts` 行。

## 与 ADR 0002 的关系

[ADR 0002](0002-d1-retention.md) 规定 `bundles` 及其级联的 `ghost_battle_summaries` 在 D1 中保留 15 天。本决策**不改变** `bundles` 与 `ghost_battle_summaries` 的 15 天保留策略，但新增一张独立生命周期的表 `run_summaries`：

- `run_summaries` 不引用 `bundles(bundle_id)`，因此 15 天清理删除 `bundles` 行时不会级联删除已绑定账号的每局摘要。
- 定时清理仅顺带清理过期的 `bpp_web_sessions` 与超过 24 小时的验证码挑战记录，永不清理 `bpp_accounts`、`bpp_device_tokens` 与 `run_summaries`。
- 玩家在网站执行解绑（`POST /api/unbind`）时，其 `run_summaries` 与账号及令牌一起被立即删除。

## 理由与边界

- 服务端仍不解码 Run 段（`run_summaries` 直接读取 manifest 的 `projection.run`），保持根 `AGENTS.md` 中 "server stores it opaque" 的契约。
- 不在每次上传或 Ghost 查询时转发官方游戏令牌，改由首次验证后签发 BPP 自有设备令牌，避免高频依赖官方接口及重复传输玩家 24 小时凭证。
- 上传本身暂不强制鉴权，先通过影子鉴权统计 `mismatch` 比例，再决定未来是否收紧。

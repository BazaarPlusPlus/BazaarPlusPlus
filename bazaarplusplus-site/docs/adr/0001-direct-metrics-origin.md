# 0001. Hero Metrics 直连远端 R2 origin

Status: Accepted

## Context

Hero Metrics Dataset 的数据托管在 `https://bpp-metrics.bazaarplusplus.com`（R2）。此前本地有两条不同的取数路径：

- `dev` 不设 `VITE_METRICS_BASE`，走 `DEFAULT_METRICS_BASE_URL`，直连远端 origin。
- `preview` 设 `VITE_METRICS_BASE=/metrics/`，走 `vite.config.ts` 中的 `remoteMetricsPlugin` 同源代理。

该插件同时挂在 `configureServer` 和 `configurePreviewServer` 上，转发 `/metrics/*` 到上游并复制 `content-type` 与 `cache-control`。它还引入了第二套配置入口（`BPP_REMOTE_METRICS_BASE`、`PUBLIC_METRICS_BASE`）。

代价是 dev 与 preview 的取数路径不一致，而生产环境走的是直连：preview 反而是三者中唯一不代表线上行为的一环。插件是自定义中间件，`npm test` / `npm run typecheck` / `npm run build` 都覆盖不到它，只有真起服务才能验证。

## Decision

删除 `remoteMetricsPlugin`，dev、preview、生产统一直连远端 R2 origin。`preview` 脚本不再注入 `VITE_METRICS_BASE`；`.env.example` 只保留 `VITE_METRICS_BASE` 作为指向其他 origin 的逃生阀。

前提是 origin 已正确下发 CORS。实测确认：带 `Origin: http://localhost:3000` 与 `Origin: https://bazaarplusplus.com` 的 GET 均回显对应 `access-control-allow-origin` 并带 `vary: Origin`，`OPTIONS` 预检返回 204 且 `access-control-allow-methods: GET`。

## Consequences

- 三种运行方式的取数路径一致，preview 真实代表线上行为。
- `vite.config.ts` 回到只有 `react()` 和 `tailwindcss()`，不再有需要起服务才能验证的自定义中间件。
- 取数正确性从此完全依赖上游 CORS 配置。若 origin 的 CORS 被改坏，dev 和 preview 会同时失效——但生产也会，所以失败是同步且可见的，不再出现"本地能跑、线上挂"的偏差。
- `BPP_REMOTE_METRICS_BASE` 和 `PUBLIC_METRICS_BASE` 不再被读取。

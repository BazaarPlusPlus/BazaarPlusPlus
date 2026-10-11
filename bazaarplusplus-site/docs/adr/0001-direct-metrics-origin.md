# 0001. Hero Metrics 直连远端 R2 origin

Status: Accepted

## Context

Hero Metrics Dataset 的数据托管在一个公开的 R2 origin，地址只写在 `src/features/heroes/hero-metrics-dataset.ts` 的 `DEFAULT_METRICS_BASE_URL`（V6 起是 analyzer 的 V6 metrics 桶，见 `bazaarplusplus-analyzer/docs/architecture.md` 的 Publication target）。此前本地有两条不同的取数路径：

- `dev` 不设 `VITE_METRICS_BASE`，走 `DEFAULT_METRICS_BASE_URL`，直连远端 origin。
- `preview` 设 `VITE_METRICS_BASE=/metrics/`，走 `vite.config.ts` 中的 `remoteMetricsPlugin` 同源代理。

该插件同时挂在 `configureServer` 和 `configurePreviewServer` 上，转发 `/metrics/*` 到上游并复制 `content-type` 与 `cache-control`。它还引入了第二套配置入口（`BPP_REMOTE_METRICS_BASE`、`PUBLIC_METRICS_BASE`）。

代价是 dev 与 preview 的取数路径不一致，而生产环境走的是直连：preview 反而是三者中唯一不代表线上行为的一环。插件是自定义中间件，`npm test` / `npm run typecheck` / `npm run build` 都覆盖不到它，只有真起服务才能验证。

## Decision

删除 `remoteMetricsPlugin`，dev、preview、生产统一直连远端 R2 origin。`preview` 脚本不再注入 `VITE_METRICS_BASE`。origin 只写在代码默认值里。

前提是 origin 已正确下发 CORS（桶上的规则见 `bazaarplusplus-analyzer/docs/architecture.md` 的 Publication target）。V5 origin 上实测确认：带 `Origin: http://localhost:3000` 与 `Origin: https://bazaarplusplus.com` 的 GET 均回显对应 `access-control-allow-origin` 并带 `vary: Origin`，`OPTIONS` 预检返回 204 且 `access-control-allow-methods: GET`。换 origin 时，新 origin 要先通过同样的检查再部署生产站点。

## Consequences

- 三种运行方式的取数路径一致，preview 真实代表线上行为。
- `vite.config.ts` 回到只有 `react()` 和 `tailwindcss()`，不再有需要起服务才能验证的自定义中间件。
- 取数正确性从此完全依赖上游 CORS 配置。若 origin 的 CORS 被改坏，dev、preview 和生产会同时失效，失败是同步且可见的，不再出现"本地能跑、线上挂"的偏差。
- `BPP_REMOTE_METRICS_BASE` 和 `PUBLIC_METRICS_BASE` 不再被读取。

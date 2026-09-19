# AGENTS.md

The standalone V5 mod-facing backend, a Cloudflare Worker. Repo-wide rules (commits, pull requests, docs policy, contracts) are in `../AGENTS.md`.

| When you are about to | Read |
|---|---|
| Name a domain concept, or change retention or runtime boundaries | `CONTEXT.md` |
| Change module seams or where code lives | `README.md`, then `docs/adr/0001-v5-deepening-seams.md` |
| Add or change a public route, request, or response | `docs/api-reference.md` |
| Change the Bundle binary format or manifest | `contracts/v5/` |
| Change BazaarDB claim/settle behavior | `docs/bazaardb-delivery-integration.md` (the partner contract) |
| Provision, migrate, or deploy | `docs/deployment-runbook.md`; the Ghost summary migration tooling is `docs/ghost-summary-migration.md` |
| Change D1 retention or Ghost storage columns | `docs/adr/0002-d1-retention.md`, `docs/adr/0003-ghost-summary-columns.md`; the dated audits under `docs/` are their evidence |

## Guardrails

- V5 is standalone. V4 handlers, migrations, bindings, object layouts, and wire behavior are inputs only where a V5 design document requires them.
- `src/env.ts` is the only place to declare Worker bindings, vars, and secrets.
- Migration files are append-only: production D1 and R2 are provisioned, so every schema change is a new file in `migrations/`.
- Bundle upload is intentionally unauthenticated. `uploader_account_id` and `bundle_uploaders` are caller assertions; treat them as data, never as authentication or authorization facts. `bundle_uploaders` records only successful V5 Bundle uploaders and is written as the final Bundle commit statement with `ON CONFLICT DO NOTHING`.
- Ghost projection accepts only a self opponent or an opponent already present in `bundle_uploaders`; filtered history stays filtered. A repeated `(uploader_account_id, battle_id)` across Bundles keeps the first projection and records a metric; fields are never merged across Bundles.
- Ingest streams the Run payload through digest validation without decompressing it, and consumers download Bundles from R2 presigned URLs; the Worker has no download proxy.
- Protected routes authenticate before parsing or querying, and `GET /ghost-battles` calls its rate-limit binding before business query parsing or D1 access.
- Adding a route updates the route table in `src/http/routes.ts`, the contract tests, and `docs/api-reference.md` together.
- Tests exercise public module interfaces. Storage adapter tests may inspect schema and query plans only when the migration or SQL statement is the interface under test.

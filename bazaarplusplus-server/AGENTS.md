# AGENTS.md

The standalone V5 mod-facing backend, a Cloudflare Worker. Repo-wide rules are in `../AGENTS.md`.

| When you are about to | Read |
|---|---|
| Name a domain concept, or change runtime boundaries | `CONTEXT.md` |
| Change module seams or where code lives | `README.md`, then `docs/adr/0001-v5-deepening-seams.md` |
| Add or change a public route, request, or response | `docs/api-reference.md` |
| Change the Bundle binary format or manifest | `contracts/v5/` |
| Change BazaarDB claim/settle behavior | `docs/bazaardb-delivery-integration.md` (the partner contract) |
| Provision, migrate, or deploy | `docs/deployment-runbook.md` |
| Change D1 or R2 retention, or Ghost storage columns | `CONTEXT.md` (Runtime boundaries), then `docs/adr/0002-d1-retention.md` and `docs/adr/0003-ghost-summary-columns.md`; the dated audits under `docs/` are their evidence |

## Guardrails

- V5 is standalone. V4 handlers, migrations, bindings, object layouts, and wire behavior are inputs only where a V5 design document requires them.
- `Env` in `src/env.ts` is the only TypeScript declaration of bindings, vars, and secrets. A new binding or var also needs its `wrangler.toml` entry, and a new secret is set out of band (README, Production).
- Migration files in `migrations/` are append-only because production D1 has applied them; every schema change is a new numbered file.
- Provisioning, remote migrations, and deploys mutate production Cloudflare; run them only when the user explicitly authorizes it.
- Bundle upload is intentionally unauthenticated. `uploader_account_id` and `bundle_uploaders` are caller assertions; treat them as data, never as authentication or authorization facts.
- Before changing ingest, Ghost projection eligibility, or Bundle download delivery, read the invariants in `CONTEXT.md` (Runtime boundaries) and `docs/adr/0001-v5-deepening-seams.md` (Bundle opening, Bundle persistence).
- Protected routes authenticate before parsing or querying, and `GET /ghost-battles` calls its rate-limit binding before business query parsing or D1 access.
- Adding a route updates `src/http/routes.ts`, the `the public route table` cases in `test/worker.test.ts`, and `docs/api-reference.md` together.
- `contracts/mod-api-errors.json` owns HTTP error codes, statuses, and retry flags. A new or changed code also updates the error table in `docs/api-reference.md`, which `test/contracts/mod-api-errors.test.ts` requires to match the JSON row for row.
- Tests exercise public module interfaces. Storage adapter tests may inspect schema and query plans only when the migration or SQL statement is the interface under test.

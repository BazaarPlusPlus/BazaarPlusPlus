# AGENTS.md

The standalone mod-facing backend, a Cloudflare Worker, ingesting Bundle V6. Repo-wide rules are in `../AGENTS.md`.

| When you are about to | Read |
|---|---|
| Name a domain concept, or change runtime boundaries | `GLOSSARY.md` |
| Change module seams or where code lives | `README.md`, then `docs/adr/0001-deep-module-seams.md` |
| Add or change a public route, request, or response | `docs/api-reference.md` |
| Change the Bundle binary format or manifest | `contracts/v6/` |
| Change the BazaarDB Feed | `docs/adr/0004-bazaardb-cursor-feed.md` |
| Provision, migrate, or deploy | `docs/deployment-runbook.md` |
| Change D1 or R2 retention, or Ghost storage columns | `GLOSSARY.md` (Runtime boundaries), then `docs/adr/0002-d1-retention.md` and `docs/adr/0003-ghost-summary-columns.md`; the dated field audit under `docs/` is ADR 0003's evidence |

## Guardrails

- `Env` in `src/env.ts` is the only TypeScript declaration of bindings, vars, and secrets. A new binding or var also needs its `wrangler.toml` entry, and a new secret is set out of band (README, Production).
- `migrations-v6/` builds the V6 D1 (the tests apply it). Migration files are append-only once a production D1 has applied them; every schema change is a new numbered file in `migrations-v6/`.
- Provisioning, remote migrations, and deploys mutate production Cloudflare; run them only when the user explicitly authorizes it.
- Bundle upload is intentionally unauthenticated. `uploader_account_id` and `bundle_uploaders` are caller assertions; treat them as data, never as authentication or authorization facts.
- Before changing ingest, Ghost projection eligibility, or Bundle download delivery, read the invariants in `GLOSSARY.md` (Runtime boundaries) and `docs/adr/0001-deep-module-seams.md` (Bundle opening, Bundle persistence).
- Protected routes authenticate before parsing or querying, and `GET /ghost-battles` calls its rate-limit binding before business query parsing or D1 access.
- Adding a route updates `src/http/routes.ts`, the `the public route table` cases in `test/worker.test.ts`, and `docs/api-reference.md` together.
- `contracts/mod-api-errors.json` owns HTTP error codes, statuses, and retry flags. A new or changed code also updates the error table in `docs/api-reference.md`, which `test/contracts/mod-api-errors.test.ts` requires to match the JSON row for row.
- Tests exercise public module interfaces. Storage adapter tests may inspect schema and query plans only when the migration or SQL statement is the interface under test.

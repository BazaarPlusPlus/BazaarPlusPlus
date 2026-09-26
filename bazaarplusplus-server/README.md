# BazaarPlusPlus Server V5

Cloudflare Worker for the BazaarPlusPlus V5 Bundle pipeline. It receives completed game-run Bundles and makes them discoverable to trusted analyzers, Ghost Battle clients, and BazaarDB. The deployment is fully standalone: its own Worker, D1 database, R2 bucket, migration lineage, and wire contract.

A Bundle is the immutable unit of upload, storage, and delivery — exactly one Run plus at most one optional Screenshot. Three properties shape everything else:

- **Bounded maintenance.** Delivery maintenance converges in bounded batches on claim traffic. A scheduled handler runs every 15 minutes to prune Bundle metadata older than 15 days from D1, with at most 100 Bundles per transaction and 1,000 per invocation. Uploader assertions remain deployment-lifetime state.
- **Streaming, not buffering.** Ingest buffers only the fixed prefix and the bounded manifest; Run and Screenshot bytes stream through incremental digest validation into a single conditional R2 PUT and are never decompressed.
- **No download proxy.** Every consumer downloads the same complete Bundle directly from R2 through a seven-day presigned `GET` URL, inside an 8-day R2 lifecycle window.

The public surface is six routes: liveness, public streaming ingest, token-protected analyzer sync, rate-limited Ghost discovery, and the BazaarDB claim/settle pair. The wire contract is [docs/api-reference.md](docs/api-reference.md); the binary Bundle format and its golden vectors are [contracts/v5](contracts/v5); the domain language is [CONTEXT.md](CONTEXT.md).

## Architecture

Ghost discovery stores fifteen ordinary summary columns while preserving the Mod’s nested response objects and the complete Bundle/Run upload contract. Production completed the staged [Ghost summary migration](docs/ghost-summary-migration.md) on 2026-09-12; only a database still holding the pre-summary `ghost_battles` table needs it before deploying this Worker.

The design is a small number of deep seams, recorded in [ADR 0001](docs/adr/0001-v5-deepening-seams.md):

- `src/http/routes.ts` is the complete public route table, and `src/http/route-shell.ts` is the sole HTTP exit — it owns path and method resolution, authentication ordering, CORS, JSON envelopes, and request IDs. Handlers return status and body data, never a `Response`.
- `src/http/deps.ts` is the single dependency channel. Every handler has the form `(request, env, requestId, deps)`, so tests replace the clock and the download signer through one object without touching Worker bindings.
- `src/bundle/open.ts` owns Bundle opening: bounded prefix and manifest reads, manifest validation, and streaming digest validation, all behind one `openBundle` call.
- `src/modules/bundle-commit.ts` owns immutable-identity decisions and the atomic D1 commit, while `src/modules/bundle-ingest.ts` owns R2 orchestration and orphan recovery.
- `src/modules/d1-retention.ts` owns scheduled D1 pruning by server storage time; foreign keys cascade to Ghost projections, deliveries, and attempt receipts.

Bundle identity reads use Drizzle's D1 query builder with the column mapping in
`src/db-schema.ts`, so selected fields and result types share one definition.
Atomic writes and indexed delivery queries use native D1 statements. SQL files in
`migrations/` remain the schema authority and are applied through Wrangler; the
Drizzle mapping is a read projection, not an input to a migration generator.

`test/` mirrors the `src/` layout (`http/`, `bundle/`, `modules/`), with golden-vector contract tests under `test/contracts/` and migration, schema, and root-module tests at the top level.

## Development

```sh
npm ci
just server::check # tsc over src and tests, then Biome lint and format verification
just server::test  # Vitest in the Workers pool with real local D1 migrations and R2
npm run dev     # wrangler dev; requires .dev.vars (below)
```

`npm run format` rewrites files in place. `npm run dev` needs a git-ignored `.dev.vars` file supplying `R2_PRESIGN_ACCESS_KEY_ID`, `R2_PRESIGN_SECRET_ACCESS_KEY`, `BUNDLE_SYNC_TOKEN`, and `BAZAARDB_DELIVERY_TOKEN`; the Worker fails closed without them. Workspace `just setup` manages this file from the shared configuration, and `just server::dev` refreshes it before starting; see the [development guide](../docs/development.md). Tests inject their own values and need no `.dev.vars`.

TypeScript checks application and test code in strict mode. `skipLibCheck` skips
declaration-file checks because Drizzle's declarations reference optional database
drivers outside this Worker's D1 runtime; query arguments and inferred results are
still checked at their use sites.

## Production

`src/env.ts` is the sole binding declaration. `wrangler.toml` provisions the D1 database, the R2 bucket, and the Ghost rate limiter (60 calls per 60 seconds). The R2 presign key pair and the two service tokens are set out of band (`.dev.vars` locally, `wrangler secret put` in production). The two service tokens are distinct 32-byte random values encoded as 43-character unpadded base64url strings, and the R2 S3 credential grants Object Read only on the V5 bucket.

Provisioning, migration, lifecycle, deploy, and smoke-test commands are in [docs/deployment-runbook.md](docs/deployment-runbook.md) and require explicit Cloudflare deployment authorization. The production D1 database and R2 bucket are provisioned, so schema changes from here on must ship as new migration files — the initial migration is frozen.

## License

Released under the [MIT License](LICENSE).

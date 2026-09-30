# BazaarPlusPlus Server V5

Cloudflare Worker for the BazaarPlusPlus V5 Bundle pipeline. It receives completed game-run Bundles and makes them discoverable to trusted analyzers, Ghost Battle clients, and BazaarDB. The deployment is fully standalone: its own Worker, D1 database, R2 bucket, migration lineage, and wire contract.

A Bundle is the immutable unit of upload, storage, and delivery — exactly one Run plus at most one optional Screenshot. Three properties shape everything else:

- **Bounded maintenance.** Delivery maintenance converges in bounded batches on claim traffic, and a scheduled handler prunes old Bundle metadata from D1 in bounded batches ([ADR 0002](docs/adr/0002-d1-retention.md)). Uploader assertions remain deployment-lifetime state.
- **Streaming, not buffering.** Ingest buffers only the fixed prefix and the bounded manifest; Run and Screenshot bytes stream through incremental digest validation into a single conditional R2 PUT and are never decompressed.
- **No download proxy.** Every consumer downloads the same complete Bundle directly from R2 through a seven-day presigned `GET` URL, inside an 8-day R2 lifecycle window.

The public surface is six routes: liveness, public streaming ingest, token-protected analyzer sync, rate-limited Ghost discovery, and the BazaarDB claim/settle pair. The wire contract is [docs/api-reference.md](docs/api-reference.md); the binary Bundle format and its golden vectors are [contracts/v5](contracts/v5); the domain language is [CONTEXT.md](CONTEXT.md).

## Architecture

The design is a small number of deep seams — the route table and its single HTTP exit, one handler dependency channel, Bundle opening, and the atomic Bundle commit — recorded with their owning files in [ADR 0001](docs/adr/0001-v5-deepening-seams.md). Ghost discovery stores fifteen summary columns ([ADR 0003](docs/adr/0003-ghost-summary-columns.md)).

Bundle identity reads use Drizzle's D1 query builder with the column mapping in
`src/db-schema.ts`, so selected fields and result types share one definition.
Atomic writes and indexed delivery queries use native D1 statements. SQL files in
`migrations/` remain the schema authority and are applied through Wrangler; the
Drizzle mapping is a read projection, not an input to a migration generator.

`test/` mirrors the `src/` layout (`http/`, `bundle/`, `modules/`), with golden-vector contract tests under `test/contracts/` and migration, schema, and root-module tests at the top level.

## Development

```sh
npm ci
just server::check
just server::test  # Also builds the mod's ModApi.Tests: needs the .NET SDK and game assemblies
just server::dev   # Refreshes .dev.vars from the shared configuration first
```

The Worker fails closed without the secrets `src/env.ts` declares. Locally they come from the git-ignored `.dev.vars`, a managed copy of the shared configuration ([development guide](../docs/development.md)). Tests inject their own values and need no `.dev.vars`.

TypeScript checks application and test code in strict mode. `skipLibCheck` skips
declaration-file checks because Drizzle's declarations reference optional database
drivers outside this Worker's D1 runtime; query arguments and inferred results are
still checked at their use sites.

## Production

`src/env.ts` is the sole binding declaration. `wrangler.toml` provisions the D1 database, the R2 bucket, and the Ghost rate limiter. The R2 presign key pair and the two service tokens are set out of band (`.dev.vars` locally, `wrangler secret put` in production). The two service tokens are distinct 32-byte random values encoded as 43-character unpadded base64url strings, and the R2 S3 credential grants Object Read only on the V5 bucket.

Provisioning, migration, lifecycle, deploy, and smoke-test commands are in [docs/deployment-runbook.md](docs/deployment-runbook.md) and require explicit Cloudflare deployment authorization. The production D1 database and R2 bucket are provisioned, so schema changes from here on must ship as new migration files — the initial migration is frozen.

## License

Released under the [MIT License](LICENSE).

# BazaarPlusPlus Server V5

Cloudflare Worker for the BazaarPlusPlus V5 Bundle pipeline. It receives completed game-run Bundles and makes them discoverable to trusted analyzers, Ghost Battle clients, and BazaarDB. The deployment is fully standalone: its own Worker, D1 database, R2 bucket, migration lineage, and wire contract.

A Bundle is the immutable unit of upload, storage, and delivery — exactly one Run plus at most one optional Screenshot. Three properties shape everything else:

- **Bounded maintenance.** Delivery maintenance converges in bounded batches on claim traffic, and a scheduled handler prunes old Bundle metadata from D1 in bounded batches ([ADR 0002](docs/adr/0002-d1-retention.md)). Uploader assertions remain deployment-lifetime state.
- **Streaming, not buffering.** Ingest buffers only the fixed prefix and the bounded manifest; Run and Screenshot bytes stream through incremental digest validation into a single conditional R2 PUT and are never decompressed.
- **No download proxy.** Every consumer downloads the same complete Bundle directly from R2 through a seven-day presigned `GET` URL, inside an 8-day R2 lifecycle window.

The public surface is six routes: liveness, public streaming ingest, token-protected analyzer sync, rate-limited Ghost discovery, and the BazaarDB claim/settle pair. The wire contract is [docs/api-reference.md](docs/api-reference.md); the binary Bundle format and its golden vectors are [contracts/v5](contracts/v5); the domain language is [CONTEXT.md](CONTEXT.md).

## Architecture

The design is a small number of deep seams — the route table and its single HTTP exit, Bundle opening, and the atomic Bundle commit — recorded with their owning files in [ADR 0001](docs/adr/0001-v5-deepening-seams.md). Ghost discovery stores fifteen summary columns ([ADR 0003](docs/adr/0003-ghost-summary-columns.md)).

`test/` mirrors the `src/` layout (`bundle/`, `modules/`), with golden-vector contract tests under `test/contracts/` and route-shell, schema, and root-module tests at the top level. Behavior tests drive the deployed `worker.fetch` and `worker.scheduled` against local D1 and R2.

## Development

```sh
npm ci
just server::check
just server::test
just server::dev   # Refreshes .dev.vars from the shared configuration first
```

The Worker fails closed without the secrets `src/env.ts` declares. Locally they come from the git-ignored `.dev.vars`, a managed copy of the shared configuration ([development guide](../docs/development.md)). Tests inject their own values and need no `.dev.vars`.

TypeScript checks application and test code in strict mode. `skipLibCheck` stays on
because the test configuration loads both `@cloudflare/workers-types` and
`@types/node`, whose global Web API declarations conflict; application and test
sources are still fully checked.

The `miniflare` override in `package.json` patches Undici in both Wrangler's and
the Worker test pool's dependency trees. The test pool pins an older Miniflare,
so updating only the top-level Wrangler does not fix both paths. Remove the
override when both upstream paths resolve a patched Undici without it, then
rerun the full server checks and tests.

## Production

`src/env.ts` is the sole binding declaration. `wrangler.toml` provisions the D1 database, the R2 bucket, and the Ghost rate limiter. The R2 presign key pair and the two service tokens are set out of band (`.dev.vars` locally, `wrangler secret put` in production). The two service tokens are distinct 32-byte random values encoded as 43-character unpadded base64url strings, and the R2 S3 credential grants Object Read only on the V5 bucket.

Provisioning, migration, lifecycle, deploy, and smoke-test commands are in [docs/deployment-runbook.md](docs/deployment-runbook.md) and require explicit Cloudflare deployment authorization. The production D1 database and R2 bucket are provisioned, so schema changes from here on must ship as new migration files — the initial migration is frozen.

## License

Released under the [MIT License](LICENSE).

# BPP V6 Data Pipeline

This Worker receives completed game-run Bundles and makes them discoverable by trusted analyzers, Ghost Battle clients, and BazaarDB.

## Language

**Bundle**, **Run**: Defined in [the root glossary](../GLOSSARY.md). The binary format is [contracts/v6](contracts/v6/bundle-v6.md).

**Screenshot**: The optional end-of-run image stored inside the Bundle. Its absence never invalidates the Bundle.

**BazaarDB Feed**: The Screenshot-bearing subset of Bundle collection, served to BazaarDB at `GET /bazaardb/bundles`. The server keeps no delivery state; BazaarDB owns its cursor and retries. There is no claim, lease, settle, or BazaarDB Delivery.

**Ghost Battle**: A directional query projection derived from a Bundle manifest. It is not authoritative Battle data.

## Runtime boundaries

The Worker exposes liveness, Bundle ingest, stateless Bundle collection, Ghost discovery, and the BazaarDB Feed. A scheduled handler prunes D1 Bundle metadata older than 15 days every 15 minutes in bounded batches. The analyzer runs independently through GitHub Actions ([`analyzer.yml`](../.github/workflows/analyzer.yml)), triggered by `on.schedule` or manually through `on.workflow_dispatch`; the Worker's `scheduled` handler ([`src/scheduled.ts`](src/scheduled.ts)) only performs D1 retention. Bundle upload retries recover matching R2-only objects.

Bundle ingest buffers only the fixed prefix and bounded manifest. Run and Screenshot bytes remain compressed/encoded and flow through incremental digest validation into one conditional R2 PUT. The D1 Bundle row, eligible Ghost projections, and uploader record commit in one batch.

All consumers download the same complete Bundle directly from a seven-day R2 presigned `GET` URL. The Worker has no download proxy. R2 retention is 8 days. Ghost queries expose only the most recent five days, and Bundle collection and the BazaarDB Feed accept only the 8-day R2 window. D1 retention uses `bundles.stored_at_ms`; deleting a Bundle after 15 days cascades to its Ghost projections. Client timestamps do not extend retention. Bundle deduplication records exist only within this metadata retention window.

`bundle_uploaders` is monotonic within one deployment; the V6 D1 started empty. It records successful uploader assertions, not authenticated identities. A projection is eligible only for self or an opponent already present before that Bundle's final uploader insert; rejected history is never backfilled.

Avoid the V4 and abandoned names: Boundle, Pack, Run artifact, Run Bundle, Snapshot, and Screenshot upload.

Ghost discovery stores fifteen columns in `ghost_battle_summaries`. It reconstructs the uploader/challenger `player` and queried-local-player `opponent` objects, retaining the challenger rank/rating the Mod stores on imported Ghost battles. The complete upload manifest and R2 Bundle remain unchanged. The column decision is recorded in [ADR 0003](docs/adr/0003-ghost-summary-columns.md).

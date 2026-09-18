# BPP V5 Data Pipeline

This Worker receives completed game-run Bundles and makes them discoverable by trusted analyzers, Ghost Battle clients, and BazaarDB.

## Language

**Bundle**: The smallest immutable upload, storage, and delivery unit. It contains exactly one Run and, when available, one optional Screenshot.

**Run**: Immutable facts, battles, card state, and replay data for one completed game run. It excludes the Screenshot.

**Screenshot**: The optional end-of-run image stored inside the Bundle. Its absence never invalidates the Bundle.

**BazaarDB Delivery**: A claimable obligation to deliver one Screenshot-bearing Bundle to BazaarDB. Settling a delivery does not delete the Bundle.

**Ghost Battle**: A directional query projection derived from a Bundle manifest. It is not authoritative Battle data.

## Runtime boundaries

The Worker exposes liveness, Bundle ingest, stateless Bundle collection, Ghost discovery, and BazaarDB claim/settle. A scheduled handler prunes D1 Bundle metadata older than 15 days every 15 minutes in bounded batches. Bundle upload retries recover matching R2-only objects, while claim requests lazily converge expired deliveries and exhausted attempts in bounded batches; settle only guards lease ownership. A claim returns a retryable response while expiry maintenance still has work, without granting leases.

Bundle ingest buffers only the fixed prefix and bounded manifest. Run and Screenshot bytes remain compressed/encoded and flow through incremental digest validation into one conditional R2 PUT. The D1 Bundle row, eligible Ghost projections, optional BazaarDB delivery, and uploader record commit in one batch.

All consumers download the same complete Bundle directly from a seven-day R2 presigned `GET` URL. The Worker has no download proxy. R2 retention is 8 days. Ghost queries expose only the most recent five days, and Bundle collection accepts only the 8-day R2 window. D1 retention uses `bundles.stored_at_ms`; deleting a Bundle after 15 days cascades to its Ghost projections, delivery, and attempt receipts. Client timestamps and settlement do not extend retention. Bundle and attempt deduplication records exist only within this metadata retention window.

`bundle_uploaders` is monotonic for the V5 deployment. It records successful uploader assertions, not authenticated identities. A projection is eligible only for self or an opponent already present before that Bundle's final uploader insert; rejected history is never backfilled.

Avoid the V4 and abandoned names: Boundle, Pack, Run artifact, Run Bundle, Snapshot, and Screenshot upload.

Ghost discovery stores fifteen columns in `ghost_battle_summaries`. It reconstructs the uploader/challenger `player` and queried-local-player `opponent` objects, retaining rank/rating for Mod 5.1/5.2. The complete upload manifest and R2 Bundle remain unchanged. The staged migration from JSON is recorded in [ADR 0003](docs/adr/0003-ghost-summary-columns.md).

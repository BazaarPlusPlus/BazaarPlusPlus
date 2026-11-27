# BazaarPlusPlus Server Architecture & Runtime Boundaries

This Worker receives completed game-run Bundles and makes them discoverable by trusted analyzers, Ghost Battle clients, and BazaarDB. Cross-project domain terms (`Bundle`, `Run`, `Screenshot`, `Ghost Battle`, `BazaarDB Feed`, `BPP Account`, `Bind Code`, `Record`) are defined in [the root glossary](../../GLOSSARY.md); the binary format is [contracts/v6](../contracts/v6/bundle-v6.md).

## Runtime boundaries

The Worker exposes liveness, Bundle ingest, stateless Bundle collection, Ghost discovery, the BazaarDB Feed, and token-protected per-account Bot queries. A scheduled handler prunes D1 Bundle metadata older than 15 days every 15 minutes in bounded batches. The analyzer runs independently through GitHub Actions ([`analyzer.yml`](../../.github/workflows/analyzer.yml)), triggered by `on.schedule` or manually through `on.workflow_dispatch`. The Worker's `scheduled` handler ([`src/scheduled.ts`](../src/scheduled.ts)) performs D1 retention and dispatches `analyzer.yml` and the QQ daily report workflow at fixed UTC times ([ADR 0005](adr/0005-workflow-dispatch.md)); it never runs the analyzer itself. Bundle upload retries recover matching R2-only objects.

Bundle ingest buffers only the fixed prefix and bounded manifest. Run and Screenshot bytes remain compressed/encoded and flow through incremental digest validation into one conditional R2 PUT. The D1 Bundle row (with the terminal `projection.run` facts and the Run segment's byte range), eligible Ghost projections, and uploader record commit in one batch.

All consumers download the same complete Bundle directly from a seven-day R2 presigned `GET` URL; the Worker has no Bundle download proxy. It serves only a Record's Run segment, by byte range from its Bundle, through the website's `GET /api/runs/<run_id>/payload` ([ADR 0007](adr/0007-site-first-accounts.md), [ADR 0008](adr/0008-records-from-bundles.md)). R2 keeps Bundles (`bundles/`) 8 days, and Records last exactly as long. Ghost queries expose only the most recent five days, and Bundle collection and the BazaarDB Feed accept only the 8-day R2 window. D1 retention uses `bundles.stored_at_ms`; deleting a Bundle after 15 days cascades to its Ghost projections. Client timestamps do not extend retention. Bundle deduplication records exist only within this metadata retention window.

`bundle_uploaders` is monotonic within one deployment; the V6 D1 started empty. It records successful uploader assertions, not authenticated identities. A projection is eligible only for self or an opponent already present before that Bundle's final uploader insert; rejected history is never backfilled.

A Ghost Battle on the server is a directional query projection derived from a Bundle manifest, never authoritative Battle data. Ghost discovery stores eighteen summary columns in `ghost_battle_summaries`, plus a copy of the parent Bundle's object key for signing. It reconstructs the uploader/challenger `player` and queried-local-player `opponent` objects, retaining the challenger rank/rating the Mod stores on imported Ghost battles and the local player's level, prestige, and victories the Mod uses to find which of its Runs a Ghost Battle fought. The complete upload manifest and R2 Bundle remain unchanged. The column decision is recorded in [ADR 0003](adr/0003-ghost-summary-columns.md).

Bot queries use stored-time candidates and exact names from eligible Ghost projections. The Bot owns binding visibility and Run end-time filtering; the server stores no player report aggregates.

## Retired vocabulary

Avoid V4 and abandoned names in code, routes, and docs: `Boundle`, `Pack`, `Run artifact`, `Run Bundle`, `Snapshot`, and `Screenshot upload`.

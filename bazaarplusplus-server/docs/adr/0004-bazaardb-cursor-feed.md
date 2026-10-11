# ADR 0004: BazaarDB reads a stateless cursor feed

Status: Accepted
Date: 2026-10-11

## Context

V5 delivered Screenshot-bearing Bundles to BazaarDB through a claim/lease/settle queue: per-Bundle delivery rows, immutable attempt receipts, retry backoff, and claim-time convergence of expired and exhausted work ([ADR 0001](0001-v5-deepening-seams.md), Delivery convergence). The V6 D1 is new and empty, so V6 can choose its delivery model without migrating queue state.

## Decision

V6 has no delivery state. `GET /bazaardb/bundles` is `GET /bundles` filtered to `has_screenshot = 1`: the same fixed availability window, settle lag, 8-day lookback, keyset order, and `next_after` cursor, authenticated with the BazaarDB service token. Both routes share one collection module, parameterized by feed. The partial index `idx_bundles_screenshot_available` serves the filtered feed, so Run-only Bundles cost no reads.

The upload receipt keeps `bazaardb_delivery` because the mod upload client (`BundleUploadClient.Classify`) rejects a receipt without it; it is derived from the Screenshot bit and reports only whether the Bundle appears in the feed.

## Consequences

- BazaarDB owns its cursor, retries, and deduplication by `bundle_id`. The server cannot report what BazaarDB accepted or failed.
- A consumer that stops for more than 8 days loses Bundles beyond R2 retention; the route answers `410 window_expired` rather than serving expired objects.
- Ingest writes one fewer row per Screenshot Bundle, and no claim request performs maintenance. D1 retention cascades only to Ghost projections.

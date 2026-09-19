# BazaarPlusPlus Server V5 deployment runbook

These commands create or mutate Cloudflare resources. Run them only with production deployment authorization, from the repository root, using an authenticated Wrangler profile for the intended account.

## 1. Validate the local artifact

```sh
npm ci
npm run check
npm test
npx wrangler deploy --dry-run
git diff --check
```

## 2. Create isolated storage

```sh
npx wrangler d1 create bazaarplusplus-mod-api-v5-db --location apac
npx wrangler r2 bucket create bazaarplusplus-bundle-v5 --location apac
```

Put the returned D1 UUID in `wrangler.toml` as `database_id`. Replace `R2_ACCOUNT_ID` with the Cloudflare account ID.

Create an R2 S3 API credential scoped to `bazaarplusplus-bundle-v5` with Object Read permission only. Put its Access Key ID in `.dev.vars` as `R2_PRESIGN_ACCESS_KEY_ID` locally, and in production with `wrangler secret put R2_PRESIGN_ACCESS_KEY_ID`. Do not commit the value. Do not grant write, delete, or access to another bucket.

Set the bucket lifecycle:

```sh
npx wrangler r2 bucket lifecycle add bazaarplusplus-bundle-v5 v5-bundle-retention bundles/ --expire-days 8
npx wrangler r2 bucket lifecycle list bazaarplusplus-bundle-v5
```

The listing must show the `bundles/` prefix expiring after 8 days.

## 3. Configure rate limiting and secrets

Set `namespace_id` in `wrangler.toml` to a positive integer unique to this Cloudflare account. Keep the configured threshold at 60 calls per 60 seconds.

Generate two independent service tokens:

```sh
openssl rand -base64 32 | tr '+/' '-_' | tr -d '='
openssl rand -base64 32 | tr '+/' '-_' | tr -d '='
```

Each output must be 43 characters, and the values must differ.

Write the secrets without putting them in shell history or repository files:

```sh
npx wrangler secret put R2_PRESIGN_SECRET_ACCESS_KEY
npx wrangler secret put BUNDLE_SYNC_TOKEN
npx wrangler secret put BAZAARDB_DELIVERY_TOKEN
```

## 4. Apply migrations and deploy

```sh
# For a new, empty database only:
npx wrangler d1 migrations apply bazaarplusplus-mod-api-v5-db --remote
npx wrangler deploy
```

Production completed the Ghost summary migration on 2026-09-12. For a database still holding the pre-summary `ghost_battles` table, follow [Ghost summary migration](ghost-summary-migration.md) through `0004`, deploy this Worker, and finish retirement/cleanup through `0006`. Do not deploy the summary Worker before backfill verification or apply all stages in one step.

Migration `0002_hot_path_indexes.sql` must complete before deploying the Worker that queries `idx_bazaardb_pending_retention`. It adds the pending retention projection and database-owned synchronization triggers, backfills only pending deliveries, and indexes Ghost Bundle foreign keys. Index creation reads existing tables; allow for this work when selecting the deployment window. It does not delete records or rewrite historical terminal deliveries.

After migration, verify the two indexes and three triggers exist and that pending delivery `bundle_stored_at_ms` values match `bundles.stored_at_ms`. A Worker rollback may leave this additive migration in place: older code remains compatible. Do not reapply an already recorded migration.

Verify the custom domain resolves only to `bazaarplusplus-mod-api-v5` and that the Worker has the `*/15 * * * *` Cron Trigger from `wrangler.toml`. The trigger runs D1 retention only. Allow up to 15 minutes for trigger configuration to propagate.

## 5. Production smoke test

Use a generated Bundle from the checked-in V5 contract implementation, not a production player's data.

1. `GET /health` returns `200`, a request ID, and a safe-integer server time.
2. Upload a Run-only Bundle with `POST /bundles`; expect `201 stored` and `not_applicable`.
3. Repeat identical bytes; expect `200 duplicate`.
4. Upload a Screenshot-bearing Bundle; expect `201 stored` and `created`.
5. After the 60-second settle lag, list both with the Bundle Sync token. Confirm keyset ordering and `X-Amz-Expires=604800`.
6. Download one URL twice with `GET`; both responses must equal the uploaded bytes.
7. Change the URL object key, use `HEAD`, change `X-Amz-Expires`, and change one signature character. R2 must reject every modified request.
8. Query a seeded eligible Ghost account. Confirm the five-day projection and direct R2 Bundle download.
9. Exceed the Ghost limit from a controlled test address. Confirm `429`, `Retry-After: 60`, and no D1/presign activity in route logs.
10. Claim the Screenshot-bearing Bundle with the BazaarDB token, download it once, and settle `accepted`. Repeating the settle must return `duplicate`.
11. Repeat with injected retryable failures to verify the 60-second and five-minute backoffs and third-attempt exhaustion.
12. Inject a D1 commit failure after a valid R2 PUT, then repeat the original `POST /bundles` and confirm the idempotent retry commits the R2-only object while preserving its R2 upload time.
13. Confirm logs contain no service token, R2 secret, Bundle body, projection JSON, Screenshot bytes, or complete presigned URL.

## 6. Post-deploy checks

### 6.1 Log-based alerts (Workers Observability)

The Worker emits one structured JSON line per alertable condition, so each alert is a
Workers Observability query on the `bazaarplusplus-mod-api-v5` Worker (dashboard:
Compute → Workers → Observability → create a query, then attach an alert with a
notification destination). Suggested starting thresholds; tune against real traffic.

| Alert | Query filter | Suggested threshold |
| --- | --- | --- |
| Server 5xx (storage, presign, unclassified) | `event = "worker.http_error" OR event = "worker.internal_error"` | > 5 in 5 min |
| Presign / storage failure specifically | `event = "worker.http_error" AND code = "storage_unavailable"` | > 5 in 5 min |
| Service token misconfiguration | `event = "worker.http_error" AND code = "invalid_configuration"` | ≥ 1 in 5 min |
| Ingest identity conflicts | invocation logs: request path `/bundles`, method POST, response status 409 | > 10 in 15 min |
| Ghost 429 rate | `event = "ghost.discovery" AND limited = true` | > 100 in 5 min |
| Ingest orphan objects | `event = "bundle.ingest.orphan" OR event = "bundle.orphan.invalid"` | ≥ 1 in 15 min |
| D1 cleanup failure | `event = "d1.retention.failed"` | ≥ 1 in 15 min |
| D1 cleanup backlog | `event = "d1.retention" AND has_more = true` | Four consecutive runs |
| D1 cleanup stopped | No successful `d1.retention` event / failed Cron invocations | No success for 45 min |
| Delivery expiry catching up | `event = "bazaardb.maintenance" AND has_more = true` | Persistent for 15 min |

Classified 4xx responses are intentionally not logged; they are client errors and
would drown the signal.

### 6.2 D1 state checks (external prober)

Aged pending deliveries and exhausted attempts can be inspected with the following
read-only probes from an operator shell or external monitoring service. The retention
Cron reports deletion progress and remaining backlog through `d1.retention` logs.
Use a Cloudflare API token scoped to D1 read for these probes:

```sh
npx wrangler d1 execute bazaarplusplus-mod-api-v5-db --remote --json --command "SELECT COUNT(*) AS aged_pending FROM bazaardb_deliveries WHERE delivery_state = 'pending' AND created_at_ms < (unixepoch() - 86400) * 1000"
```

```sh
npx wrangler d1 execute bazaarplusplus-mod-api-v5-db --remote --json --command "SELECT COUNT(*) AS exhausted FROM bazaardb_deliveries WHERE delivery_state = 'failed' AND failure_reason = 'delivery_attempts_exhausted' AND failed_at_ms > (unixepoch() - 86400) * 1000"
```

```sh
npx wrangler d1 execute bazaarplusplus-mod-api-v5-db --remote --json --command "SELECT stored_at_ms AS oldest_stored_at_ms FROM bundles INDEXED BY idx_bundles_stored_retention ORDER BY stored_at_ms, bundle_id LIMIT 1"
```

Alert when `aged_pending` is nonzero for more than a day (BazaarDB has stopped
claiming or keeps failing), when `exhausted` jumps, or when `oldest_stored_at_ms`
remains older than 15 days plus one hour. Monitor D1 database size in Cloudflare D1
Metrics and alert at 80% of the database's configured storage limit. Retention limits
the history window; it does not cap the volume uploaded within those 15 days or the
deployment-lifetime `bundle_uploaders` table.

### 6.3 Ecosystem gates

- Verify the analyzer and BazaarDB clients compare the decoded Run identity/version with the Bundle manifest and quarantine mismatches.
- Do not release the V5 mod until analyzer and BazaarDB consumers have completed the direct-R2 smoke test.

## 7. D1 retention

The Worker automatically prunes Bundle metadata after 15 days measured from
`bundles.stored_at_ms`, the server-observed R2 storage time. It uses the scheduled
invocation time as a fixed cutoff and deletes only rows strictly older than that
cutoff. Client creation times and delivery settlement do not change the clock.

Each invocation commits up to ten independent transactions of at most 100 parent
Bundles each, oldest first. A Bundle delete cascades to its Ghost projections,
BazaarDB delivery, and attempt receipts. `bundle_uploaders` is deployment-lifetime
trust state and is never deleted. The handler requires only D1, without R2 credentials
or service tokens, and makes no R2 calls. The R2 bucket keeps its separate 8-day rule.

Before deploying this policy, inspect the oldest records with the probe above and
preview the first eligible page:

```sh
npx wrangler d1 execute bazaarplusplus-mod-api-v5-db --remote --json --command "SELECT bundle_id, stored_at_ms FROM bundles INDEXED BY idx_bundles_stored_retention WHERE stored_at_ms < (unixepoch() - 15 * 86400) * 1000 ORDER BY stored_at_ms, bundle_id LIMIT 100"
```

Deploying the Worker enables pruning, including eligible historical records. No new
migration is required beyond `0002_hot_path_indexes.sql`; its child index is required
to keep cascades bounded. Validate the exact cutoff, cascades, interruption recovery,
and query plans with `npm test`. A local scheduled invocation can also be exercised
with Wrangler's `/cdn-cgi/local/scheduled` endpoint; it is not a public application route.

A successful run logs `deleted_bundles` (parent count) and `has_more`. The configured
budget supports up to 96,000 parent deletions per day when every trigger succeeds;
large initial backlogs can take multiple runs. An error fails the scheduled invocation
and emits `d1.retention.failed`; earlier committed batches remain deleted and the next
invocation continues from the remaining rows. Retries and overlapping invocations
are safe because each bounded selection and delete is one SQL statement.

Use the backlog, missing-run, and storage alerts above to detect capacity pressure.
Increase the schedule frequency or per-invocation batch count only after measuring
delete duration and API latency. Keep each delete transaction small. To pause pruning,
deploy `crons = []` in the managed Wrangler configuration; already deleted metadata
requires recovery from an available D1 backup rather than a Worker rollback.

# BazaarPlusPlus Server V6 deployment runbook

The V6 deployment is new and shares nothing with V5: Worker `bazaarplusplus-mod-api-v6` at `mod-api-v6.bazaarplusplus.com`, D1 `bazaarplusplus-mod-api-v6-db` built from `migrations-v6/`, and R2 bucket `bazaarplusplus-bundle-v6`. Mod 6.0 and later upload to it (`ModApiUploadDefaults.ApiBaseUrl`). The V5 Worker `bazaarplusplus-mod-api-v5` at `mod-api-v5.bazaarplusplus.com` keeps serving 5.7.0 (section 8); nothing here touches its Worker, D1, bucket, or secrets.

These commands create or mutate Cloudflare resources. Run them only with production deployment authorization, from `bazaarplusplus-server/`, using an authenticated Wrangler profile for the account in `wrangler.toml` (`R2_ACCOUNT_ID`). Sections 1–5 are the first deployment, in order.

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
npx wrangler d1 create bazaarplusplus-mod-api-v6-db --location apac
npx wrangler r2 bucket create bazaarplusplus-bundle-v6 --location apac
```

`wrangler.toml` holds the `database_id` that `d1 create` printed; if the database is ever recreated, commit the new UUID before deploying. Never point this file at the V5 database `bazaarplusplus-mod-api-v5-db`.

Set the bucket lifecycle:

```sh
npx wrangler r2 bucket lifecycle add bazaarplusplus-bundle-v6 v6-bundle-retention bundles/ --expire-days 8
npx wrangler r2 bucket lifecycle list bazaarplusplus-bundle-v6
```

The listing must show the `bundles/` prefix expiring after 8 days. The Worker never deletes R2 objects; this rule is the only R2 retention.

Create an R2 S3 API credential scoped to `bazaarplusplus-bundle-v6` with Object Read permission only. Do not grant write, delete, or access to another bucket, and do not reuse the V5 credential, which is scoped to the V5 bucket.

## 3. Configure rate limiting and secrets

`[[ratelimits]].namespace_id` must be a positive integer unique in the account. V6 uses `1002`; the V5 Worker uses `1001`, and sharing it would merge the two Workers' Ghost counters. Keep the threshold at 60 calls per 60 seconds.

Generate two independent service tokens for V6. They must differ from each other and from the V5 tokens:

```sh
openssl rand -base64 32 | tr '+/' '-_' | tr -d '='
openssl rand -base64 32 | tr '+/' '-_' | tr -d '='
```

Each output must be 43 characters. The secrets are written in section 4 after the first deploy creates the Worker. Locally, the V6 R2 Access Key ID goes in the shared configuration that `.dev.vars` is copied from ([development guide](../../docs/development.md)) as `R2_PRESIGN_ACCESS_KEY_ID`. Do not commit any value.

## 4. Apply migrations, deploy, and set secrets

```sh
npx wrangler d1 migrations apply bazaarplusplus-mod-api-v6-db --remote
npx wrangler d1 migrations list bazaarplusplus-mod-api-v6-db --remote
npx wrangler deploy
```

The list must show `0001_v6_baseline.sql` applied and nothing pending. From here `migrations-v6/0001_v6_baseline.sql` is frozen; every schema change is a new numbered file, and a recorded migration is never reapplied. `migrations/` is the V5 lineage and is never applied to this database.

`wrangler deploy` attaches the custom domain from `[[routes]]` (`custom_domain = true`). The `bazaarplusplus.com` zone must be on this account and `mod-api-v6` must have no existing DNS record; otherwise the deploy fails and names the conflict.

Set the four secrets on the new Worker without putting them in shell history or repository files:

```sh
npx wrangler secret put R2_PRESIGN_ACCESS_KEY_ID
npx wrangler secret put R2_PRESIGN_SECRET_ACCESS_KEY
npx wrangler secret put BUNDLE_SYNC_TOKEN
npx wrangler secret put BAZAARDB_DELIVERY_TOKEN
```

Until they are set, signing and protected routes fail closed (`500 internal_error` or `503 storage_unavailable`). No released mod points at V6 before 6.0.0, so this window has no player traffic.

Verify the custom domain resolves only to `bazaarplusplus-mod-api-v6` and that the Worker has the `*/15 * * * *` Cron Trigger from `wrangler.toml`. The trigger runs D1 retention only. Allow up to 15 minutes for trigger configuration to propagate.

Then hand the V6 base URL and tokens to their consumers: the analyzer's repository `.env` (`BPP_V5_API_BASE_URL` and `BPP_BUNDLE_SYNC_TOKEN`), and BazaarDB through its integration document ([bazaardb-delivery-integration.md](bazaardb-delivery-integration.md)), with notice before 6.0.0 ships.

## 5. Production smoke test

Run this before 6.0.0 is promoted. Use a generated Bundle from the checked-in V6 contract implementation, not a production player's data.

1. `GET /health` returns `200`, a request ID, and a safe-integer server time.
2. Upload a Run-only Bundle with `POST /bundles`; expect `201 stored` and `not_applicable`.
3. Repeat identical bytes; expect `200 duplicate`.
4. Upload a V5 Bundle; expect it to be rejected without storage.
5. Upload a Screenshot-bearing Bundle; expect `201 stored` and `created`.
6. After the 60-second settle lag, list both with the Bundle Sync token. Confirm keyset ordering, `X-Amz-Expires=604800`, and the `bazaarplusplus-bundle-v6` host.
7. Download one URL twice with `GET`; both responses must equal the uploaded bytes.
8. Change the URL object key, use `HEAD`, change `X-Amz-Expires`, and change one signature character. R2 must reject every modified request.
9. Query a seeded eligible Ghost account. Confirm the five-day projection and direct R2 Bundle download.
10. Exceed the Ghost limit from a controlled test address. Confirm `429`, `Retry-After: 60`, and no D1/presign activity in route logs.
11. Claim the Screenshot-bearing Bundle with the BazaarDB token, download it once, and settle `accepted`. Repeating the settle must return `duplicate`.
12. Repeat with injected retryable failures to verify the 60-second and five-minute backoffs and third-attempt exhaustion.
13. Inject a D1 commit failure after a valid R2 PUT, then repeat the original `POST /bundles` and confirm the idempotent retry commits the R2-only object while preserving its R2 upload time.
14. Confirm logs contain no service token, R2 secret, Bundle body, projection JSON, Screenshot bytes, or complete presigned URL.

### Known consequences of a new deployment

- **Empty Ghost pool.** Ghost discovery reads only this D1, which starts empty, and `bundle_uploaders` is not seeded from V5. 6.0 players see few Ghost Battles until about five days of 6.0 uploads accumulate, and 5.7.0 and 6.0 players never see each other's Ghosts.
- **BazaarDB moves.** BazaarDB must claim from `mod-api-v6.bazaarplusplus.com` with the V6 delivery token and decode Bundle V6. Screenshot-bearing Bundles from 6.0 players are delivered only through V6.
- **Split collection.** Until V5 retires, 5.7.0 uploads land in V5 and 6.0 uploads in V6; a consumer that reads one base URL sees only that population.

## 6. Post-deploy checks

### 6.1 Log-based alerts (Workers Observability)

The Worker emits one structured JSON line per alertable condition, so each alert is a
Workers Observability query on the `bazaarplusplus-mod-api-v6` Worker (dashboard:
Compute → Workers → Observability → create a query, then attach an alert with a
notification destination). Recreate the V5 alerts for V6; V5's stay until it retires.
Suggested starting thresholds; tune against real traffic.

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
npx wrangler d1 execute bazaarplusplus-mod-api-v6-db --remote --json --command "SELECT COUNT(*) AS aged_pending FROM bazaardb_deliveries WHERE delivery_state = 'pending' AND created_at_ms < (unixepoch() - 86400) * 1000"
```

```sh
npx wrangler d1 execute bazaarplusplus-mod-api-v6-db --remote --json --command "SELECT COUNT(*) AS exhausted FROM bazaardb_deliveries WHERE delivery_state = 'failed' AND failure_reason = 'delivery_attempts_exhausted' AND failed_at_ms > (unixepoch() - 86400) * 1000"
```

```sh
npx wrangler d1 execute bazaarplusplus-mod-api-v6-db --remote --json --command "SELECT stored_at_ms AS oldest_stored_at_ms FROM bundles INDEXED BY idx_bundles_stored_retention ORDER BY stored_at_ms, bundle_id LIMIT 1"
```

Alert when `aged_pending` is nonzero for more than a day (BazaarDB has stopped
claiming or keeps failing), when `exhausted` jumps, or when `oldest_stored_at_ms`
remains older than 15 days plus one hour. Monitor D1 database size in Cloudflare D1
Metrics and alert at 80% of the database's configured storage limit. Retention limits
the history window; it does not cap the volume uploaded within those 15 days or the
deployment-lifetime `bundle_uploaders` table.

### 6.3 Ecosystem gates

- Verify the analyzer and BazaarDB clients compare the decoded Run identity/version with the Bundle manifest and quarantine mismatches.

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

Preview the first eligible page:

```sh
npx wrangler d1 execute bazaarplusplus-mod-api-v6-db --remote --json --command "SELECT bundle_id, stored_at_ms FROM bundles INDEXED BY idx_bundles_stored_retention WHERE stored_at_ms < (unixepoch() - 15 * 86400) * 1000 ORDER BY stored_at_ms, bundle_id LIMIT 100"
```

`npm test` covers the exact cutoff, cascades, interruption recovery, and bounded reads.
A local scheduled invocation can be exercised through Wrangler's
`/cdn-cgi/local/scheduled` endpoint; it is not a public application route.

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

## 8. The V5 Worker

The V5 Worker's code is no longer on `master`: a V5 hotfix is built from the 5.7.0 tag, whose `wrangler.toml` targets the V5 resources. Never deploy `master` to `bazaarplusplus-mod-api-v5`. Retiring V5 is outside this runbook.

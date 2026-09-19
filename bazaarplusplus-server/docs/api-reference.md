# BazaarPlusPlus Server V5 API reference

Base URL: `https://mod-api-v5.bazaarplusplus.com`

The Worker exposes exactly these routes:

| Method | Path | Authentication |
|---|---|---|
| `GET` | `/health` | None |
| `POST` | `/bundles` | None; uploader identity is a caller assertion |
| `GET` | `/bundles` | `BUNDLE_SYNC_TOKEN` |
| `GET` | `/ghost-battles` | None; per-IP rate limited |
| `POST` | `/bazaardb/deliveries/claim` | `BAZAARDB_DELIVERY_TOKEN` |
| `POST` | `/bazaardb/deliveries/settle` | `BAZAARDB_DELIVERY_TOKEN` |

There is no Bundle download route. Discovery responses contain a seven-day R2 presigned `GET` URL, and clients download the complete Bundle directly from the R2 S3 endpoint.

## Common HTTP behavior

JSON responses have these headers:

```http
Content-Type: application/json; charset=utf-8
Cache-Control: no-store
X-Request-Id: <CF-Ray or server UUID>
```

`GET /health` and every `/ghost-battles` response also have `Access-Control-Allow-Origin: *`.

Error responses preserve route-specific headers such as `Allow` and `Retry-After`.

A known path with an unsupported method returns `405` with the path's methods in `Allow`. `OPTIONS` returns `204` only for a known path, with `Allow`, `Access-Control-Allow-Methods`, `Access-Control-Allow-Headers`, and `Access-Control-Max-Age`. An unknown path, including an unknown `OPTIONS` path, returns `404 not_found`.

Errors use:

```json
{
  "error": {
    "code": "invalid_query",
    "message": "Diagnostic text",
    "retryable": false,
    "request_id": "request-id",
    "details": { "field": "available_from_ms" }
  }
}
```

`details` is optional. Clients use the status, `code`, and `retryable`; they do not match `message` text.

## Service-token authentication

Protected requests use:

```http
Authorization: Bearer <43-character unpadded base64url token>
```

The two configured tokens must be distinct. A missing, malformed, empty, or unknown token returns `401 unauthorized`. A valid token for the other service scope returns `403 insufficient_scope`. If the deployed token configuration itself is invalid (a token missing, malformed, or both tokens equal), every protected route returns `500 internal_error` with `retryable: true` until the operator fixes the secrets. Authentication runs before JSON parsing, D1, R2, or URL signing.

## Bundle V5 wire format

Content type: `application/x-bpp-bundle-v5`.

The 16-byte prefix uses unsigned big-endian integers:

| Offset | Bytes | Value |
|---:|---:|---|
| 0 | 8 | ASCII `BPPBNDL5` |
| 8 | 4 | Bundle version `5` |
| 12 | 4 | UTF-8 manifest JSON byte length |

The manifest is followed by one Run segment and an optional Screenshot segment. Segment offsets are relative to the first payload byte. The Run starts at offset `0`; a Screenshot starts at the Run length. Gaps, overlap, and trailing bytes are invalid.

Manifest shape:

```json
{
  "bundle_id": "01J00000000000000000000001",
  "bundle_version": 5,
  "created_at_ms": 1785628800000,
  "run": {
    "run_id": "run-id",
    "player_account_id": "account-id",
    "run_format_version": 5,
    "projection": {
      "run": {},
      "battles": [
        {
          "battle_id": "battle-id",
          "recorded_at_ms": 1785628799000,
          "day": 10,
          "hour": 18,
          "encounter_id": null,
          "combat_kind": "pvp",
          "result": "win",
          "winner_combatant_id": "combatant-a",
          "loser_combatant_id": "combatant-b",
          "is_final_battle": true,
          "player": {
            "account_id": "account-id",
            "display_name": "Player",
            "hero_id": null,
            "hero_name": "Vanessa",
            "rank": "Gold",
            "rating": 1234,
            "level": 10,
            "prestige": 2,
            "victories": 9
          },
          "opponent": {
            "account_id": "opponent-id",
            "display_name": "Opponent",
            "hero_id": null,
            "hero_name": "Pygmalien",
            "rank": "Gold",
            "rating": 1200,
            "level": 10,
            "prestige": 3,
            "victories": 8
          }
        }
      ]
    },
    "payload": {
      "offset": 0,
      "length": 123456,
      "sha256": "64-lowercase-hex-characters",
      "content_type": "application/x-bpp-run-v5"
    }
  },
  "screenshot": {
    "offset": 123456,
    "length": 456789,
    "sha256": "64-lowercase-hex-characters",
    "content_type": "image/jpeg",
    "width": 1600,
    "height": 900,
    "quality": 80,
    "captured_at_ms": 1785628800000
  }
}
```

The `screenshot` key is absent for a Run-only Bundle. Every optional combatant scalar is present as either its value or `null`. `bundle_id` is a canonical uppercase ULID, and other identities use 1–128 characters from letters, digits, `.`, `_`, `:`, and `-`, beginning with a letter or digit. Unknown manifest fields are ignored.

Limits:

| Item | Limit |
|---|---:|
| Bundle | 1–8,388,607 bytes |
| Manifest | 1–2,097,152 bytes |
| Run | 1–2,097,151 bytes |
| Screenshot | 1–1,048,576 bytes |
| Run projection | 524,288 UTF-8 bytes |
| Battle projections | 30, with unique IDs inside the Bundle |

The machine-readable contract is in [`../contracts/v5/manifest.schema.json`](../contracts/v5/manifest.schema.json), with prefix details and golden vectors under [`../contracts/v5`](../contracts/v5).

## `GET /health`

Pure liveness probe. It does not access D1, R2, secrets, the limiter, or the presigner.

Response `200`:

```json
{
  "status": "ok",
  "server_time_ms": 1785628800000
}
```

## `POST /bundles`

Public ingest for one sealed Bundle. `run.player_account_id` is not authenticated and must never be treated as an authorization fact.

Required headers:

```http
Content-Type: application/x-bpp-bundle-v5
Content-Length: 1234567
Content-Digest: sha-256=:<base64 SHA-256 of complete Bundle>:
```

The digest field contains exactly one RFC 9530 `sha-256` value. The Worker buffers only the prefix and bounded manifest. It rejects inconsistencies between manifest segment lengths and `Content-Length` before accessing storage. It incrementally validates actual length, the complete digest, and both segment digests while streaming one conditional R2 PUT. A manifest/header length conflict returns `422 invalid_bundle`; a body shorter or longer than an otherwise consistent declaration returns `400 invalid_content_length` (or `413 bundle_too_large` at the size limit).

First logical commit returns `201`:

```json
{
  "bundle_id": "01J00000000000000000000001",
  "run_id": "run-id",
  "outcome": "stored",
  "bazaardb_delivery": "created"
}
```

An identical stored Bundle returns `200` with `outcome: "duplicate"`. `bazaardb_delivery` is `created` for a newly stored Screenshot-bearing Bundle, `existing` for its duplicate, or `not_applicable` for a Run-only Bundle.

Conflicts preserve the first immutable identity:

- same `bundle_id`, different complete digest: `409 bundle_id_conflict`;
- same `run_id`, different `bundle_id`: `409 run_already_bundled`.

Errors:

| Status | Code | Meaning |
|---:|---|---|
| 400 | `invalid_content_length` | Invalid header or actual length mismatch |
| 400 | `invalid_content_digest` | Invalid or unsupported digest field |
| 409 | `bundle_id_conflict` | Bundle identity has different bytes |
| 409 | `run_already_bundled` | Run belongs to another Bundle |
| 411 | `content_length_required` | Header missing |
| 413 | `bundle_too_large` | Declared or actual size reaches 8 MiB |
| 415 | `unsupported_content_type` | Wrong media type |
| 422 | `invalid_bundle` | Prefix, manifest, layout, or bounds invalid |
| 422 | `unsupported_bundle_version` | Bundle version not accepted |
| 422 | `unsupported_run_format` | Run format not accepted |
| 422 | `bundle_digest_mismatch` | Complete digest mismatch |
| 422 | `segment_digest_mismatch` | Run or Screenshot digest mismatch |
| 503 | `storage_unavailable` | R2 or D1 temporary failure |
| 500 | `internal_error` | Unclassified server error |

Stable `invalid_bundle.details.reason` values are `invalid_prefix`, `manifest_too_large`, `manifest_not_json`, `manifest_schema_invalid`, `too_many_battles`, `projection_too_large`, `run_missing`, `run_too_large`, `screenshot_too_large`, `screenshot_type_unsupported`, `segment_out_of_bounds`, `segment_overlap`, and `undeclared_trailing_bytes`.

## `GET /bundles`

Analyzer enumeration over a fixed, stateless availability window.

```http
GET /bundles?available_from_ms=1785628800000&available_before_ms=1785632400000&limit=200
Authorization: Bearer <BUNDLE_SYNC_TOKEN>
```

Query fields:

| Field | Required | Rules |
|---|---|---|
| `available_from_ms` | Yes | Inclusive, non-negative safe integer, no earlier than server time minus 8 days |
| `available_before_ms` | No | Exclusive; defaults to one fixed `server_now_ms - 60000`; may not be later than that settle point |
| `limit` | No | Default 200; range 1–500 |
| `after_available_at_ms` | As a pair | Last returned availability time, within the same window |
| `after_bundle_id` | As a pair | Last returned Bundle ID |

Parameters may appear only once; unknown parameters are invalid. `available_from_ms` must be less than `available_before_ms`.

Response `200`:

```json
{
  "window": {
    "available_from_ms": 1785628800000,
    "available_before_ms": 1785632400000
  },
  "items": [
    {
      "bundle_id": "01J00000000000000000000001",
      "available_at_ms": 1785629000000,
      "download_url": "https://bazaarplusplus-bundle-v5.<ACCOUNT_ID>.r2.cloudflarestorage.com/bundles/...bundle?X-Amz-Expires=604800&...",
      "download_expires_at_ms": 1786233800000
    }
  ],
  "next_after": {
    "available_at_ms": 1785629000000,
    "bundle_id": "01J00000000000000000000001"
  }
}
```

Items sort by `available_at_ms ASC, bundle_id ASC`. `next_after` is `null` on the final page. The Worker queries `limit + 1` rows but signs only returned rows.

Route errors: `400 invalid_query`, `400 window_not_settled`, `401 unauthorized`, `403 insufficient_scope`, `410 window_expired`, `500 internal_error` (invalid service token configuration), and `503 storage_unavailable`.

## `GET /ghost-battles`

Public directional Ghost projection discovery:

```http
GET /ghost-battles?player_account_id=account-id&limit=200
```

The rate-limit binding is called before business query parsing or D1 access, using `CF-Connecting-IP` or the fixed `unknown` key. A denied request returns `429 rate_limited` with `Retry-After: 60` and performs no D1 query or signing.

`player_account_id` is required. `limit` defaults to 200 and accepts 1–200. Both may appear only once; unknown parameters are invalid. The server lookback is exactly five days.

Response `200`:

```json
{
  "battles": [
    {
      "battle_id": "battle-id",
      "bundle_id": "01J00000000000000000000001",
      "recorded_at_ms": 1785628700000,
      "day": 10,
      "hour": 18,
      "result": "win",
      "winner_combatant_id": "combatant-a",
      "is_final_battle": true,
      "player": {
        "account_id": "uploader-account",
        "display_name": "Player",
        "hero_name": "Vanessa",
        "rank": "Gold",
        "rating": 1234
      },
      "opponent": {
        "account_id": "account-id",
        "hero_name": "Pygmalien"
      },
      "download_url": "https://bazaarplusplus-bundle-v5.<ACCOUNT_ID>.r2.cloudflarestorage.com/bundles/...bundle?X-Amz-Expires=604800&...",
      "download_expires_at_ms": 1786233800000
    }
  ]
}
```

Rows sort by `recorded_at_ms DESC, battle_id DESC`. The response is reconstructed from fifteen ordinary summary columns. `player` is the uploader/challenger and `opponent` is the queried local player; both remain nested objects. `day`, `hour`, and both account IDs are required by Mod import. `winner_combatant_id` remains available for result filtering, and `player.rank` / `player.rating` preserve Mod 5.1/5.2 display behavior. Nullable fields are emitted as JSON null.

At ingest, a projection is inserted only when its opponent is the uploader or the opponent already exists in `bundle_uploaders`. The uploader is added only as the final write of a successful Bundle D1 batch. Filtered history is never backfilled. Cross-Bundle duplicate `(uploader_account_id, battle_id)` rows keep the first projection.

Route errors: `400 invalid_query`, `429 rate_limited`, and `503 storage_unavailable`. An unknown account returns `200 {"battles":[]}`.

The discovery summary omits both sides’ `hero_id`, `level`, `prestige`, and `victories`; `opponent.display_name`, `opponent.rank`, and `opponent.rating`; and `encounter_id`, `combat_kind`, and `loser_combatant_id`. The upload manifest contract and complete R2 Bundle/Run remain unchanged. Download capabilities are signed at query time using the parent Bundle object key. Mod 5.1.0, 5.2.0, and the audited 5.3.0 source accept this summary without a coordinated Mod release.

## `POST /bazaardb/deliveries/claim`

Only Screenshot-bearing Bundles create deliveries.

```http
Authorization: Bearer <BAZAARDB_DELIVERY_TOKEN>
Content-Type: application/json

{"limit":50}
```

The JSON body limit is 64 KiB. `limit` defaults to 50 and accepts 1–50.

Response `200`:

```json
{
  "claim_id": "clm_550e8400-e29b-41d4-a716-446655440000",
  "expires_at_ms": 1785629400000,
  "items": [
    {
      "bundle_id": "01J00000000000000000000001",
      "run_id": "run-id",
      "download_url": "https://bazaarplusplus-bundle-v5.<ACCOUNT_ID>.r2.cloudflarestorage.com/bundles/...bundle?X-Amz-Expires=604800&...",
      "download_expires_at_ms": 1786233800000,
      "content_type": "application/x-bpp-bundle-v5",
      "sha256": "64-lowercase-hex-characters"
    }
  ]
}
```

No work returns `{"claim_id":null,"expires_at_ms":null,"items":[]}`.

Each claim first converges at most 1,000 expired Bundles and 1,000 expired final attempts. If expired pending Bundles remain, it returns `503 storage_unavailable` with `retryable: true` and `Retry-After: 1`. This response commits maintenance progress but grants no leases and counts no attempts. Retry with backoff, respecting `Retry-After`; an empty successful response still means there is no claimable work.

The lease is ten minutes. An attempt is counted atomically when claimed, with a maximum of three. Multiple consumers cannot receive the same Bundle under overlapping valid leases. A response lost after claiming leaves the lease to expire naturally. URL-signing failure is compensated by removing this claim's receipts and restoring only rows still owned by this claim; a failed compensation remains recoverable by lease expiry.

Route errors: `400 invalid_json`, `400 invalid_limit`, `401 unauthorized`, `403 insufficient_scope`, `500 internal_error` (invalid service token configuration), and `503 storage_unavailable`.

## `POST /bazaardb/deliveries/settle`

```http
Authorization: Bearer <BAZAARDB_DELIVERY_TOKEN>
Content-Type: application/json

{
  "claim_id": "clm_550e8400-e29b-41d4-a716-446655440000",
  "results": [
    {"bundle_id":"01J00000000000000000000001","outcome":"accepted"},
    {"bundle_id":"01J00000000000000000000002","outcome":"retryable_failure","reason":"timeout"},
    {"bundle_id":"01J00000000000000000000003","outcome":"permanent_failure","reason":"invalid_data"}
  ]
}
```

`results` contains 1–50 unique Bundle IDs. Outcomes are `accepted`, `retryable_failure`, or `permanent_failure`. Failure outcomes require a `^[a-z0-9_]{1,64}$` reason. `accepted` forbids `reason`.

Response `200`:

```json
{
  "claim_id": "clm_550e8400-e29b-41d4-a716-446655440000",
  "items": [
    {
      "bundle_id": "01J00000000000000000000001",
      "status": "applied",
      "state": "done",
      "next_claim_at_ms": null
    }
  ],
  "summary": {
    "applied": 1,
    "duplicate": 0,
    "rejected": 0
  }
}
```

Per-item `status` is:

- `applied`: this request changed the delivery;
- `duplicate`: the same attempt already has the same outcome and reason;
- `stale_claim`: the unsettled attempt no longer owns an unexpired lease;
- `outcome_conflict`: the attempt is settled differently;
- `unknown_item`: no receipt exists for the claim and Bundle.

`state` is `pending`, `done`, or `failed`, and is `null` for an unknown item. `next_claim_at_ms` is non-null only for pending work without an active lease. `summary.rejected` counts stale, conflicting, and unknown items.

`accepted` becomes done. `permanent_failure` becomes failed. The first retryable failure waits 60 seconds, the second waits five minutes, and the third becomes failed with `delivery_attempts_exhausted`. Before selecting a claim page, `POST /bazaardb/deliveries/claim` also marks pending Bundles older than the 8-day R2 retention as `bundle_expired` and expired third leases as `delivery_attempts_exhausted`. Attempt receipts are immutable across later attempts, so response-loss retries remain idempotent.

Route errors: `400 invalid_json`, `400 invalid_settle_request`, `401 unauthorized`, `403 insufficient_scope`, `500 internal_error` (invalid service token configuration), and `503 storage_unavailable`.

## Presigned downloads

All discovery routes use the same signer contract:

- operation: S3 `GetObject` only;
- endpoint: `https://bazaarplusplus-bundle-v5.<ACCOUNT_ID>.r2.cloudflarestorage.com`;
- region: `auto`;
- lifetime: 604,800 seconds;
- key: a validated `bundles/<yyyy-mm-dd>/<bundle_id>.bundle` value read from D1.

One response signs each distinct object key once.

The URL is a bearer capability. The Worker never logs the complete URL and does not receive the subsequent download request.

## Retention and maintenance

R2 object deletion is provided only by the separately provisioned 8-day bucket lifecycle rule. The scheduled handler runs every 15 minutes and deletes D1 Bundles whose `stored_at_ms` is strictly earlier than its scheduled time minus 15 days. Each delete commits at most 100 Bundles, with at most ten batches per invocation; backlog continues on later invocations. A Bundle at the exact 15-day boundary remains until a later run. Deletion cascades to Ghost projections, BazaarDB deliveries, and attempt receipts. `bundle_uploaders` remains deployment-lifetime state and is never pruned by this handler. The handler does not access R2.

Client timestamps and settle activity do not extend D1 retention. Bundle identity deduplication and attempt idempotency end when their metadata is pruned; a later settle returns `unknown_item`. The five-day Ghost and eight-day Bundle collection windows remain unchanged. The 15-day cutoff is eligibility for asynchronous pruning, not a guarantee of deletion at an exact instant.

A matching retry of `POST /bundles` validates and commits an R2-only object left by a prior D1 failure. Pending BazaarDB deliveries older than R2 retention and expired third leases converge when the next claim request runs.

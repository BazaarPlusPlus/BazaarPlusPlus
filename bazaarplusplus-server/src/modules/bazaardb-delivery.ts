import { validBundleId } from "../bundle/manifest";
import type { Env } from "../env";
import { HttpError } from "../errors";
import type { HandlerDeps } from "../http/deps";
import { readJsonObject } from "../http/request";
import {
  CLAIM_DEFAULT_LIMIT,
  CLAIM_LEASE_MS,
  CLAIM_MAX_LIMIT,
  DELIVERY_MAINTENANCE_BATCH_SIZE,
  DELIVERY_RETRY_BACKOFF_MS,
  MAX_DELIVERY_ATTEMPTS,
  R2_RETENTION_MS,
  SETTLE_MAX_RESULTS,
} from "../limits";
import { logEvent } from "../observability";
import { signDownloadPage } from "../presigner";

interface ClaimedBundle {
  bundle_id: string;
  run_id: string;
  download_url: string;
  download_expires_at_ms: number;
  content_type: "application/x-bpp-bundle-v5";
  sha256: string;
}

export type ClaimDeliveriesResponse =
  | { claim_id: null; expires_at_ms: null; items: [] }
  | { claim_id: string; expires_at_ms: number; items: ClaimedBundle[] };

interface SettleDeliveryItem {
  bundle_id: string;
  status: "applied" | "duplicate" | "stale_claim" | "outcome_conflict" | "unknown_item";
  state: "pending" | "done" | "failed" | null;
  next_claim_at_ms: number | null;
}

export interface SettleDeliveriesResponse {
  claim_id: string;
  items: SettleDeliveryItem[];
  summary: { applied: number; duplicate: number; rejected: number };
}

interface ClaimRow {
  bundle_id: string;
  run_id: string;
  object_key: string;
  bundle_sha256: string;
}

type SettleOutcome = "accepted" | "retryable_failure" | "permanent_failure";

interface SettleInput {
  bundleId: string;
  outcome: SettleOutcome;
  reason: string | null;
}

interface IndexedSettleInput extends SettleInput {
  index: number;
}

interface ReceiptRow {
  bundle_id: string;
  claim_id: string | null;
  outcome: SettleOutcome | null;
  reason: string | null;
  delivery_state: "pending" | "done" | "failed" | null;
  active_claim_id: string | null;
  claimable_at_ms: number | null;
}

interface SettleItemPair {
  statements: readonly [D1PreparedStatement, D1PreparedStatement];
  deliveryApplied(writes: readonly D1Result<unknown>[]): boolean;
}

function claimLimit(value: unknown): number {
  if (value === undefined) return CLAIM_DEFAULT_LIMIT;
  if (
    !Number.isSafeInteger(value) ||
    (value as number) < 1 ||
    (value as number) > CLAIM_MAX_LIMIT
  ) {
    throw new HttpError(400, "invalid_limit", "limit must be an integer between 1 and 50", false);
  }
  return value as number;
}

async function compensateClaim(env: Env, claimId: string, now: number): Promise<void> {
  await env.DB.batch([
    env.DB.prepare(`DELETE FROM bazaardb_delivery_attempts WHERE claim_id = ?1`).bind(claimId),
    env.DB.prepare(
      `UPDATE bazaardb_deliveries
       SET active_claim_id = NULL,
           active_claim_order = NULL,
           claimable_at_ms = ?2,
           delivery_attempts = delivery_attempts - 1,
           state_updated_at_ms = ?2
       WHERE active_claim_id = ?1
         AND delivery_state = 'pending'`,
    ).bind(claimId, now),
  ]);
}

// The attempt predicates under INDEXED BY interpolate MAX_DELIVERY_ATTEMPTS as a
// compile-time literal and must never become bound parameters: D1 answers
// "no query solution" for a partial index + INDEXED BY + bound predicate.
//
// The migration maintains bundle_stored_at_ms from bundles in database triggers.
// Its pending-only index lets expiry skip both live backlog and retained history.
function convergeExpiredBundles(db: D1Database, now: number): D1PreparedStatement {
  return db
    .prepare(
      `UPDATE bazaardb_deliveries
       SET delivery_state = 'failed',
           active_claim_id = NULL,
           active_claim_order = NULL,
           state_updated_at_ms = ?1,
           failed_at_ms = ?1,
           failure_reason = 'bundle_expired'
       WHERE bundle_id IN (
         SELECT bundle_id
         FROM bazaardb_deliveries INDEXED BY idx_bazaardb_pending_retention
         WHERE delivery_state = 'pending' AND bundle_stored_at_ms < ?2
         ORDER BY bundle_stored_at_ms ASC, bundle_id ASC
         LIMIT ?3
       )`,
    )
    .bind(now, now - R2_RETENTION_MS, DELIVERY_MAINTENANCE_BATCH_SIZE);
}

function convergeExhaustedAttempts(db: D1Database, now: number): D1PreparedStatement {
  return db
    .prepare(
      `UPDATE bazaardb_deliveries
       SET delivery_state = 'failed',
           active_claim_id = NULL,
           active_claim_order = NULL,
           state_updated_at_ms = ?1,
           failed_at_ms = ?1,
           failure_reason = CASE WHEN bundle_stored_at_ms < ?2
             THEN 'bundle_expired' ELSE 'delivery_attempts_exhausted' END
       WHERE bundle_id IN (
         SELECT bundle_id
         FROM bazaardb_deliveries INDEXED BY idx_bazaardb_exhausted_lease
         WHERE delivery_state = 'pending'
           AND delivery_attempts = ${MAX_DELIVERY_ATTEMPTS}
           AND active_claim_id IS NOT NULL
           AND claimable_at_ms <= ?1
         ORDER BY claimable_at_ms ASC, bundle_id ASC
         LIMIT ?3
       )`,
    )
    .bind(now, now - R2_RETENTION_MS, DELIVERY_MAINTENANCE_BATCH_SIZE);
}

// A zero LIMIT skips the candidate scan until bounded expiry has caught up.
// A WHERE filter would still walk the backlog even for an uncorrelated probe.
function expiredPendingExists(cutoffParameter: "?1" | "?5"): string {
  return `EXISTS (
    SELECT 1 FROM bazaardb_deliveries INDEXED BY idx_bazaardb_pending_retention
    WHERE delivery_state = 'pending' AND bundle_stored_at_ms < ${cutoffParameter}
  )`;
}

function claimDeliveryPage(
  db: D1Database,
  now: number,
  claimId: string,
  expiresAt: number,
  limit: number,
): D1PreparedStatement {
  return db
    .prepare(
      `WITH candidates AS MATERIALIZED (
         SELECT
           bundle_id,
           ROW_NUMBER() OVER (
             ORDER BY claimable_at_ms ASC, created_at_ms ASC, bundle_id ASC
           ) - 1 AS claim_order
         FROM bazaardb_deliveries INDEXED BY idx_bazaardb_claimable
         WHERE delivery_state = 'pending'
           AND delivery_attempts < ${MAX_DELIVERY_ATTEMPTS}
           AND claimable_at_ms <= ?1
         ORDER BY claimable_at_ms ASC, created_at_ms ASC, bundle_id ASC
         LIMIT CASE WHEN ${expiredPendingExists("?5")} THEN 0 ELSE ?4 END
       )
       UPDATE bazaardb_deliveries AS d
       SET active_claim_id = ?2,
           active_claim_order = candidates.claim_order,
           claimable_at_ms = ?3,
           delivery_attempts = delivery_attempts + 1,
           state_updated_at_ms = ?1
       FROM candidates
       WHERE d.bundle_id = candidates.bundle_id
         AND delivery_state = 'pending'
         AND delivery_attempts < ${MAX_DELIVERY_ATTEMPTS}
         AND claimable_at_ms <= ?1`,
    )
    .bind(now, claimId, expiresAt, limit, now - R2_RETENTION_MS);
}

function insertAttemptReceipts(
  db: D1Database,
  claimId: string,
  now: number,
  expiresAt: number,
): D1PreparedStatement {
  return db
    .prepare(
      `INSERT INTO bazaardb_delivery_attempts (
         claim_id, bundle_id, attempt_number, claimed_at_ms, expires_at_ms
       )
       SELECT ?1, bundle_id, delivery_attempts, ?2, ?3
       FROM bazaardb_deliveries
       WHERE active_claim_id = ?1
         AND delivery_state = 'pending'`,
    )
    .bind(claimId, now, expiresAt);
}

function loadClaimedBundles(db: D1Database, claimId: string): D1PreparedStatement {
  return db
    .prepare(
      `SELECT b.bundle_id, b.run_id, b.object_key, b.bundle_sha256
       FROM bazaardb_deliveries AS d INDEXED BY idx_bazaardb_active_claim_order
       JOIN bundles AS b ON b.bundle_id = d.bundle_id
       WHERE d.active_claim_id = ?1
         AND d.delivery_state = 'pending'
       ORDER BY d.active_claim_order ASC, d.bundle_id ASC`,
    )
    .bind(claimId);
}

export async function claimDeliveries(
  request: Request,
  env: Env,
  requestId: string,
  deps: HandlerDeps,
): Promise<ClaimDeliveriesResponse> {
  const body = await readJsonObject(request);
  const limit = claimLimit(body.limit);
  // Read the signer before the D1 claim batch: an invalid presign configuration
  // must fail here with zero D1 writes, not after Bundles are already claimed.
  const signer = deps.signer;
  const now = deps.now();
  const expiresAt = now + CLAIM_LEASE_MS;
  const claimId = `clm_${crypto.randomUUID()}`;
  let rows: ClaimRow[];
  let maintenancePending: boolean;
  try {
    const results = await env.DB.batch([
      convergeExpiredBundles(env.DB, now),
      convergeExhaustedAttempts(env.DB, now),
      env.DB.prepare(`SELECT ${expiredPendingExists("?1")} AS pending`).bind(now - R2_RETENTION_MS),
      claimDeliveryPage(env.DB, now, claimId, expiresAt, limit),
      insertAttemptReceipts(env.DB, claimId, now, expiresAt),
      loadClaimedBundles(env.DB, claimId),
    ]);
    rows = (results.at(-1)?.results ?? []) as unknown as ClaimRow[];
    maintenancePending = Number((results[2].results[0] as { pending: number }).pending) === 1;
  } catch {
    throw new HttpError(503, "storage_unavailable", "BazaarDB claim transaction failed", true);
  }

  if (maintenancePending) {
    logEvent("bazaardb.maintenance", { request_id: requestId, has_more: true });
    throw new HttpError(
      503,
      "storage_unavailable",
      "Delivery maintenance is catching up",
      true,
      undefined,
      { "Retry-After": "1" },
    );
  }

  if (rows.length === 0) {
    return { claim_id: null, expires_at_ms: null, items: [] };
  }
  try {
    const downloads = await signDownloadPage(
      signer,
      rows.map((row) => row.object_key),
      now,
      "BazaarDB claim URL signing failed",
    );
    const items = rows.map(
      (row, index): ClaimedBundle => ({
        bundle_id: row.bundle_id,
        run_id: row.run_id,
        download_url: downloads[index].url,
        download_expires_at_ms: downloads[index].expiresAtMs,
        content_type: "application/x-bpp-bundle-v5",
        sha256: row.bundle_sha256,
      }),
    );
    logEvent("bazaardb.claim", {
      request_id: requestId,
      claim_id: claimId,
      item_count: items.length,
      lease_ms: CLAIM_LEASE_MS,
    });
    return { claim_id: claimId, expires_at_ms: expiresAt, items };
  } catch (error) {
    await compensateClaim(env, claimId, now).catch(() => undefined);
    if (error instanceof HttpError) throw error;
    throw new HttpError(503, "storage_unavailable", "BazaarDB claim URL signing failed", true);
  }
}

function parseSettle(body: Record<string, unknown>): { claimId: string; results: SettleInput[] } {
  if (
    typeof body.claim_id !== "string" ||
    !/^clm_[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/.test(body.claim_id)
  ) {
    throw new HttpError(400, "invalid_settle_request", "claim_id is invalid", false);
  }
  if (
    !Array.isArray(body.results) ||
    body.results.length < 1 ||
    body.results.length > SETTLE_MAX_RESULTS
  ) {
    throw new HttpError(400, "invalid_settle_request", "results must contain 1 to 50 items", false);
  }
  const bundleIds = new Set<string>();
  const results = body.results.map((value): SettleInput => {
    if (typeof value !== "object" || value === null || Array.isArray(value)) {
      throw new HttpError(400, "invalid_settle_request", "Settle result must be an object", false);
    }
    const source = value as Record<string, unknown>;
    if (typeof source.bundle_id !== "string" || !validBundleId(source.bundle_id)) {
      throw new HttpError(400, "invalid_settle_request", "Settle bundle_id is invalid", false);
    }
    if (bundleIds.has(source.bundle_id)) {
      throw new HttpError(
        400,
        "invalid_settle_request",
        "Settle bundle_id values must be unique",
        false,
      );
    }
    bundleIds.add(source.bundle_id);
    if (
      source.outcome !== "accepted" &&
      source.outcome !== "retryable_failure" &&
      source.outcome !== "permanent_failure"
    ) {
      throw new HttpError(400, "invalid_settle_request", "Settle outcome is invalid", false);
    }
    const reason = source.reason;
    if (source.outcome === "accepted") {
      if (reason !== undefined) {
        throw new HttpError(
          400,
          "invalid_settle_request",
          "accepted must not include reason",
          false,
        );
      }
      return { bundleId: source.bundle_id, outcome: source.outcome, reason: null };
    }
    if (typeof reason !== "string" || !/^[a-z0-9_]{1,64}$/.test(reason)) {
      throw new HttpError(400, "invalid_settle_request", "Failure reason is invalid", false);
    }
    return { bundleId: source.bundle_id, outcome: source.outcome, reason };
  });
  return { claimId: body.claim_id, results };
}

function buildSettleItemPair(
  db: D1Database,
  claimId: string,
  item: IndexedSettleInput,
  now: number,
): SettleItemPair {
  const { index } = item;
  const base = index * 2;
  const attempt = db
    .prepare(
      `UPDATE bazaardb_delivery_attempts
       SET settled_at_ms = ?4,
           outcome = ?3,
           reason = ?5
       WHERE claim_id = ?1
         AND bundle_id = ?2
         AND outcome IS NULL
         AND EXISTS (
           SELECT 1
           FROM bazaardb_deliveries AS d
           WHERE d.bundle_id = ?2
             AND d.delivery_state = 'pending'
             AND d.active_claim_id = ?1
             AND d.claimable_at_ms > ?4
         )`,
    )
    .bind(claimId, item.bundleId, item.outcome, now, item.reason);
  const delivery = db
    .prepare(
      `UPDATE bazaardb_deliveries
       SET delivery_state = CASE
             WHEN ?3 = 'accepted' THEN 'done'
             WHEN ?3 = 'permanent_failure' THEN 'failed'
             WHEN delivery_attempts >= ?6 THEN 'failed'
             ELSE 'pending'
           END,
           active_claim_id = NULL,
           active_claim_order = NULL,
           claimable_at_ms = CASE
             WHEN ?3 = 'retryable_failure' AND delivery_attempts = 1 THEN ?4 + ?7
             WHEN ?3 = 'retryable_failure' AND delivery_attempts = 2 THEN ?4 + ?8
             ELSE claimable_at_ms
           END,
           state_updated_at_ms = ?4,
           delivered_at_ms = CASE WHEN ?3 = 'accepted' THEN ?4 ELSE NULL END,
           failed_at_ms = CASE
             WHEN ?3 = 'permanent_failure'
               OR (?3 = 'retryable_failure' AND delivery_attempts >= ?6)
               THEN ?4
             ELSE NULL
           END,
           failure_reason = CASE
             WHEN ?3 = 'permanent_failure' THEN ?5
             WHEN ?3 = 'retryable_failure' AND delivery_attempts >= ?6
               THEN 'delivery_attempts_exhausted'
             ELSE NULL
           END
       WHERE bundle_id = ?2
         AND delivery_state = 'pending'
         AND active_claim_id = ?1
         AND claimable_at_ms > ?4
         AND EXISTS (
           SELECT 1 FROM bazaardb_delivery_attempts AS a
           WHERE a.claim_id = ?1
             AND a.bundle_id = ?2
             AND a.outcome = ?3
             AND a.settled_at_ms = ?4
             AND (a.reason = ?5 OR (a.reason IS NULL AND ?5 IS NULL))
         )`,
    )
    .bind(
      claimId,
      item.bundleId,
      item.outcome,
      now,
      item.reason,
      MAX_DELIVERY_ATTEMPTS,
      DELIVERY_RETRY_BACKOFF_MS[0],
      DELIVERY_RETRY_BACKOFF_MS[1],
    );
  return {
    statements: [attempt, delivery],
    deliveryApplied(writes) {
      return Number(writes.at(base + 1)?.meta.changes ?? 0) === 1;
    },
  };
}

export async function settleDeliveries(
  request: Request,
  env: Env,
  requestId: string,
  deps: HandlerDeps,
): Promise<SettleDeliveriesResponse> {
  const input = parseSettle(await readJsonObject(request));
  const now = deps.now();
  const pairs = input.results.map((result, index) =>
    buildSettleItemPair(env.DB, input.claimId, { ...result, index }, now),
  );
  const statements = pairs.flatMap(({ statements: pair }) => pair);

  let writes: D1Result<unknown>[];
  let receipts: ReceiptRow[];
  try {
    writes = await env.DB.batch(statements);
    const found = await env.DB.prepare(
      `SELECT
           a.bundle_id,
           a.claim_id,
           a.outcome,
           a.reason,
           d.delivery_state,
           d.active_claim_id,
           d.claimable_at_ms
         FROM bazaardb_delivery_attempts AS a
         JOIN bazaardb_deliveries AS d ON d.bundle_id = a.bundle_id
         WHERE a.claim_id = ?1 AND a.bundle_id IN (
           ${input.results.map((_, index) => `?${index + 2}`).join(", ")}
         )`,
    )
      .bind(input.claimId, ...input.results.map((result) => result.bundleId))
      .all<ReceiptRow>();
    const byBundle = new Map(found.results.map((row) => [row.bundle_id, row]));
    receipts = input.results.map(
      (result) =>
        byBundle.get(result.bundleId) ?? {
          bundle_id: result.bundleId,
          claim_id: null,
          outcome: null,
          reason: null,
          delivery_state: null,
          active_claim_id: null,
          claimable_at_ms: null,
        },
    );
  } catch {
    throw new HttpError(503, "storage_unavailable", "BazaarDB settle transaction failed", true);
  }

  let applied = 0;
  let duplicate = 0;
  let rejected = 0;
  const items = input.results.map((result, index): SettleDeliveryItem => {
    const receipt = receipts[index];
    let status: SettleDeliveryItem["status"];
    if (receipt.claim_id === null) {
      status = "unknown_item";
      rejected += 1;
    } else if (receipt.outcome === null) {
      status = "stale_claim";
      rejected += 1;
    } else if (receipt.outcome !== result.outcome || receipt.reason !== result.reason) {
      status = "outcome_conflict";
      rejected += 1;
    } else if (pairs[index].deliveryApplied(writes)) {
      status = "applied";
      applied += 1;
    } else {
      status = "duplicate";
      duplicate += 1;
    }
    return {
      bundle_id: result.bundleId,
      status,
      state: receipt.delivery_state,
      next_claim_at_ms:
        receipt.delivery_state === "pending" && receipt.active_claim_id === null
          ? receipt.claimable_at_ms
          : null,
    };
  });
  logEvent("bazaardb.settle", {
    request_id: requestId,
    claim_id: input.claimId,
    applied,
    duplicate,
    rejected,
  });
  return {
    claim_id: input.claimId,
    items,
    summary: { applied, duplicate, rejected },
  };
}

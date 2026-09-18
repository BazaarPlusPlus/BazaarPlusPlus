import { validBundleId } from "../bundle/manifest";
import type { Env } from "../env";
import { HttpError } from "../errors";
import type { HandlerDeps } from "../http/deps";
import { oneQueryValue } from "../http/request";
import {
  SYNC_DEFAULT_LIMIT,
  SYNC_MAX_LIMIT,
  SYNC_MAX_LOOKBACK_MS,
  SYNC_SETTLE_LAG_MS,
} from "../limits";
import { logEvent } from "../observability";
import { signDownloadPage } from "../presigner";

interface BundleCollectionItem {
  bundle_id: string;
  available_at_ms: number;
  download_url: string;
  download_expires_at_ms: number;
}

export interface BundleCollectionPage {
  window: { available_from_ms: number; available_before_ms: number };
  items: BundleCollectionItem[];
  next_after: { available_at_ms: number; bundle_id: string } | null;
}

interface CollectionRow {
  bundle_id: string;
  available_at_ms: number;
  object_key: string;
}

function integer(value: string | null, field: string): number {
  if (value === null || !/^(0|[1-9][0-9]*)$/.test(value)) {
    throw new HttpError(
      400,
      "invalid_query",
      `${field} must be a non-negative safe integer`,
      false,
      {
        field,
      },
    );
  }
  const result = Number(value);
  if (!Number.isSafeInteger(result)) {
    throw new HttpError(
      400,
      "invalid_query",
      `${field} must be a non-negative safe integer`,
      false,
      {
        field,
      },
    );
  }
  return result;
}

export async function collectBundles(
  request: Request,
  env: Env,
  requestId: string,
  deps: HandlerDeps,
): Promise<BundleCollectionPage> {
  const url = new URL(request.url);
  const allowed = new Set([
    "available_from_ms",
    "available_before_ms",
    "limit",
    "after_available_at_ms",
    "after_bundle_id",
  ]);
  for (const key of url.searchParams.keys()) {
    if (!allowed.has(key)) {
      throw new HttpError(400, "invalid_query", `Unknown query parameter: ${key}`, false, {
        field: key,
      });
    }
  }

  const now = deps.now();
  const settlePoint = now - SYNC_SETTLE_LAG_MS;
  const from = integer(
    oneQueryValue(url.searchParams, "available_from_ms", true),
    "available_from_ms",
  );
  if (from < now - SYNC_MAX_LOOKBACK_MS) {
    throw new HttpError(410, "window_expired", "Bundle window is outside R2 retention", false);
  }
  const beforeValue = oneQueryValue(url.searchParams, "available_before_ms", false);
  const before = beforeValue === null ? settlePoint : integer(beforeValue, "available_before_ms");
  if (before > settlePoint) {
    throw new HttpError(400, "window_not_settled", "Bundle window end is not settled", false);
  }
  if (from >= before) {
    throw new HttpError(
      400,
      "invalid_query",
      "available_from_ms must be before available_before_ms",
      false,
    );
  }

  const limitValue = oneQueryValue(url.searchParams, "limit", false);
  const limit = limitValue === null ? SYNC_DEFAULT_LIMIT : integer(limitValue, "limit");
  if (limit < 1 || limit > SYNC_MAX_LIMIT) {
    throw new HttpError(400, "invalid_query", "limit must be between 1 and 500", false, {
      field: "limit",
    });
  }
  const afterTimeValue = oneQueryValue(url.searchParams, "after_available_at_ms", false);
  const afterId = oneQueryValue(url.searchParams, "after_bundle_id", false);
  if ((afterTimeValue === null) !== (afterId === null)) {
    throw new HttpError(400, "invalid_query", "Both keyset position fields are required", false);
  }
  const afterTime =
    afterTimeValue === null ? null : integer(afterTimeValue, "after_available_at_ms");
  if (
    afterTime !== null &&
    (afterTime < from || afterTime >= before || afterId === null || !validBundleId(afterId))
  ) {
    throw new HttpError(400, "invalid_query", "Keyset position is outside the fixed window", false);
  }

  let rows: CollectionRow[];
  try {
    const lowerBound =
      afterTime === null ? "available_at_ms >= ?1" : "(available_at_ms, bundle_id) > (?1, ?4)";
    const result = await env.DB.prepare(
      `SELECT bundle_id, available_at_ms, object_key
       FROM bundles INDEXED BY idx_bundles_available
       WHERE ${lowerBound} AND available_at_ms < ?2
       ORDER BY available_at_ms ASC, bundle_id ASC
       LIMIT ?3`,
    );
    const page =
      afterTime === null
        ? result.bind(from, before, limit + 1)
        : result.bind(afterTime, before, limit + 1, afterId);
    const found = await page.all<CollectionRow>();
    rows = found.results;
  } catch {
    throw new HttpError(503, "storage_unavailable", "Bundle index query failed", true);
  }

  const hasNext = rows.length > limit;
  const returned = rows.slice(0, limit);
  const signer = deps.signer;
  const downloads = await signDownloadPage(
    signer,
    returned.map((row) => row.object_key),
    now,
    "Bundle URL signing failed",
  );
  const items = returned.map(
    (row, index): BundleCollectionItem => ({
      bundle_id: row.bundle_id,
      available_at_ms: row.available_at_ms,
      download_url: downloads[index].url,
      download_expires_at_ms: downloads[index].expiresAtMs,
    }),
  );
  const last = returned.at(-1);
  logEvent("bundle.collection", {
    request_id: requestId,
    available_from_ms: from,
    available_before_ms: before,
    row_count: returned.length,
    has_next: hasNext,
  });
  return {
    window: { available_from_ms: from, available_before_ms: before },
    items,
    next_after:
      hasNext && last !== undefined
        ? { available_at_ms: last.available_at_ms, bundle_id: last.bundle_id }
        : null,
  };
}

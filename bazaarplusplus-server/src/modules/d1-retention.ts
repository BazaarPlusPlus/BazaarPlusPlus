import type { Env } from "../env";
import { D1_RETENTION_BATCH_SIZE, D1_RETENTION_MAX_BATCHES, D1_RETENTION_MS } from "../limits";
import { logError, logEvent } from "../observability";

export async function pruneExpiredBundles(
  db: D1Database,
  scheduledAtMs: number,
): Promise<{ deleted: number; hasMore: boolean }> {
  const cutoff = scheduledAtMs - D1_RETENTION_MS;
  let deleted = 0;
  let hasMore = false;
  try {
    for (let batch = 0; batch < D1_RETENTION_MAX_BATCHES; batch += 1) {
      // RETURNING counts parent Bundles only; D1 changes also includes cascades.
      // Each statement commits independently, so an interrupted run keeps progress.
      const result = await db
        .prepare(
          `DELETE FROM bundles
         WHERE bundle_id IN (
           SELECT bundle_id FROM bundles INDEXED BY idx_bundles_stored_retention
           WHERE stored_at_ms < ?1
           ORDER BY stored_at_ms ASC, bundle_id ASC
           LIMIT ?2
         )
         RETURNING bundle_id`,
        )
        .bind(cutoff, D1_RETENTION_BATCH_SIZE)
        .all<{ bundle_id: string }>();
      deleted += result.results.length;
      if (result.results.length < D1_RETENTION_BATCH_SIZE) break;
      if (batch === D1_RETENTION_MAX_BATCHES - 1) {
        const remaining = await db
          .prepare(
            `SELECT bundle_id FROM bundles INDEXED BY idx_bundles_stored_retention
           WHERE stored_at_ms < ?1 LIMIT 1`,
          )
          .bind(cutoff)
          .first();
        hasMore = remaining !== null;
      }
    }
  } catch (error) {
    logError("d1.retention.failed", {
      scheduled_at_ms: scheduledAtMs,
      cutoff_ms: cutoff,
      deleted_bundles: deleted,
    });
    throw error;
  }
  logEvent("d1.retention", {
    scheduled_at_ms: scheduledAtMs,
    cutoff_ms: cutoff,
    deleted_bundles: deleted,
    has_more: hasMore,
  });
  return { deleted, hasMore };
}

export async function scheduled(controller: ScheduledController, env: Env): Promise<void> {
  await pruneExpiredBundles(env.DB, controller.scheduledTime);
}

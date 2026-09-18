export const MAX_BATTLES_PER_BUNDLE = 30;
export const MAX_BUNDLE_BYTES = 8_388_607;
export const MAX_MANIFEST_BYTES = 2_097_152;
export const MAX_RUN_BYTES = 2_097_151;
export const MAX_SCREENSHOT_BYTES = 1_048_576;
export const MAX_PROJECTION_BYTES = 524_288;
export const PRESIGNED_GET_TTL_SECONDS = 604_800;
export const R2_RETENTION_MS = 8 * 86_400_000;
export const D1_RETENTION_MS = 15 * 86_400_000;
export const D1_RETENTION_BATCH_SIZE = 100;
export const D1_RETENTION_MAX_BATCHES = 10;
export const SYNC_SETTLE_LAG_MS = 60_000;
export const SYNC_MAX_LOOKBACK_MS = R2_RETENTION_MS;
export const SYNC_DEFAULT_LIMIT = 200;
export const SYNC_MAX_LIMIT = 500;
export const GHOST_LOOKBACK_MS = 432_000_000;
export const GHOST_DEFAULT_LIMIT = 200;
export const GHOST_MAX_LIMIT = 200;
export const CLAIM_LEASE_MS = 600_000;
export const CLAIM_DEFAULT_LIMIT = 50;
export const CLAIM_MAX_LIMIT = 50;
export const DELIVERY_MAINTENANCE_BATCH_SIZE = 1_000;
export const MAX_DELIVERY_ATTEMPTS = 3;
// The schedule has one delay between each pair of allowed attempts.
export const DELIVERY_RETRY_BACKOFF_MS = [60_000, 300_000] as const;
export const SETTLE_MAX_RESULTS = 50;

// The settle SQL binds one backoff parameter per non-final attempt number.
if (DELIVERY_RETRY_BACKOFF_MS.length !== MAX_DELIVERY_ATTEMPTS - 1) {
  throw new Error("DELIVERY_RETRY_BACKOFF_MS must have MAX_DELIVERY_ATTEMPTS - 1 entries");
}

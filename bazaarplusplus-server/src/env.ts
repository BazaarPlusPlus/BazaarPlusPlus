export interface Env {
  DB: D1Database;
  BUNDLE_BUCKET: R2Bucket;
  GHOST_BATTLE_RATE_LIMITER: RateLimit;

  BUNDLE_BUCKET_NAME: "bazaarplusplus-bundle-v5";
  R2_ACCOUNT_ID: string;
  R2_PRESIGN_ACCESS_KEY_ID: string;

  R2_PRESIGN_SECRET_ACCESS_KEY: string;
  BUNDLE_SYNC_TOKEN: string;
  BAZAARDB_DELIVERY_TOKEN: string;
}

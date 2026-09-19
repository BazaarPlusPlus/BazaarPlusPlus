/// <reference types="@cloudflare/vitest-pool-workers/types" />

declare namespace Cloudflare {
  interface Env {
    DB: D1Database;
    BUNDLE_BUCKET: R2Bucket;
    GHOST_BATTLE_RATE_LIMITER: RateLimit;
    BUNDLE_BUCKET_NAME: "bazaarplusplus-bundle-v5";
    R2_ACCOUNT_ID: string;
    R2_PRESIGN_ACCESS_KEY_ID: string;
    R2_PRESIGN_SECRET_ACCESS_KEY: string;
    BUNDLE_SYNC_TOKEN: string;
    BAZAARDB_DELIVERY_TOKEN: string;
    TEST_MIGRATIONS: import("@cloudflare/vitest-pool-workers").D1Migration[];
  }
}

declare module "*?raw" {
  const content: string;
  export default content;
}

type WorkerEnv = import("../src/env").Env;
type Assignable<Target, Source extends Target> = Source;
type _CloudflareEnvIncludesWorkerEnv = Assignable<WorkerEnv, Cloudflare.Env>;
type _WorkerEnvMatchesCloudflareEnv = Assignable<
  Omit<Cloudflare.Env, "TEST_MIGRATIONS">,
  WorkerEnv
>;

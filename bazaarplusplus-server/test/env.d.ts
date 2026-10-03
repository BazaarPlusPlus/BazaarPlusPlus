/// <reference types="@cloudflare/vitest-pool-workers/types" />

type WorkerEnv = import("../src/env").Env;

declare namespace Cloudflare {
  interface Env extends WorkerEnv {
    TEST_MIGRATIONS: import("@cloudflare/vitest-pool-workers").D1Migration[];
  }
}

declare module "*?raw" {
  const content: string;
  export default content;
}

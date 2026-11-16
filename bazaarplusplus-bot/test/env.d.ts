/// <reference types="@cloudflare/vitest-pool-workers/types" />
type BotEnv = import('../src/env').Env;
declare namespace Cloudflare {
  interface Env extends BotEnv {
    TEST_MIGRATIONS: import('@cloudflare/vitest-pool-workers').D1Migration[];
  }
}
declare module '*?raw' {
  const content: string;
  export default content;
}

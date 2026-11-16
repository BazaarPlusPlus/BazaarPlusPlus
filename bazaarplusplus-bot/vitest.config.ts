import {
  readD1Migrations,
  cloudflareTest
} from '@cloudflare/vitest-pool-workers';
import { defineConfig } from 'vitest/config';
export default defineConfig({
  plugins: [
    cloudflareTest(async () => ({
      main: './src/webhook.ts',
      miniflare: {
        compatibilityDate: '2026-08-02',
        compatibilityFlags: ['nodejs_compat'],
        d1Databases: ['DB'],
        isolatedStorage: true,
        bindings: {
          TEST_MIGRATIONS: await readD1Migrations('./migrations'),
          QQ_BOT_APP_ID: '11111111',
          QQ_BOT_APP_SECRET: 'DG5g3B4j9X2KOErG',
          SERVER_ORIGIN: 'https://mod-api.example.test',
          BOT_STATS_TOKEN: 'C'.repeat(43)
        }
      }
    }))
  ],
  test: { include: ['test/**/*.test.ts'], setupFiles: ['./test/setup.ts'] }
});

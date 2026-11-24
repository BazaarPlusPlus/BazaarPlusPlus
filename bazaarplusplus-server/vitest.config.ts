import path from 'node:path';
import { fileURLToPath } from 'node:url';

import {
  cloudflareTest,
  readD1Migrations
} from '@cloudflare/vitest-pool-workers';
import { defineConfig } from 'vitest/config';

const rootDir = path.dirname(fileURLToPath(import.meta.url));
const migrationsPath = path.join(rootDir, 'migrations-v6');

export default defineConfig({
  plugins: [
    cloudflareTest(async () => ({
      main: './src/index.ts',
      wrangler: {
        configPath: './wrangler.toml'
      },
      miniflare: {
        bindings: {
          TEST_MIGRATIONS: await readD1Migrations(migrationsPath),
          R2_ACCOUNT_ID: 'test-account-id',
          R2_PRESIGN_ACCESS_KEY_ID: 'test-access-key-id',
          R2_PRESIGN_SECRET_ACCESS_KEY: 'test-secret-access-key',
          BOT_STATS_TOKEN: 'CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC',
          BUNDLE_SYNC_TOKEN: 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA',
          BAZAARDB_DELIVERY_TOKEN:
            'BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB',
          ACCOUNT_CODE_SECRET: 'test-account-code-secret-for-vitest-only',
          ACCOUNT_EMAIL_FROM: 'noreply@bazaarplusplus.com'
        },
        isolatedStorage: true
      }
    }))
  ],
  test: {
    include: ['test/**/*.test.ts'],
    setupFiles: ['./test/apply-migrations.ts'],
    // BPP_UPDATE_GOLDENS=1 is the repository-wide alias for vitest's -u.
    update: process.env.BPP_UPDATE_GOLDENS === '1' ? true : undefined
  }
});

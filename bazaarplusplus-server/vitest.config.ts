import path from 'node:path';
import { fileURLToPath } from 'node:url';

import {
  cloudflareTest,
  readD1Migrations
} from '@cloudflare/vitest-pool-workers';
import { defineConfig } from 'vitest/config';

const rootDir = path.dirname(fileURLToPath(import.meta.url));
const migrationsPath = path.join(rootDir, 'migrations-v6');

const TURNSTILE_PASS_SECRET = '1x0000000000000000000000000000000AA';
const TURNSTILE_FAIL_SECRET = '2x0000000000000000000000000000000AA';

// Answers Turnstile siteverify the way Cloudflare answers its test secrets,
// solved on bazaarplusplus.com; every other outbound request fails. A test
// token `<anything>:<action>` reports that widget action, as a real token
// carries the action its widget was rendered with.
async function fakeSiteverify(request: Request): Promise<Response> {
  const url = new URL(request.url);
  if (
    request.method !== 'POST' ||
    url.href !== 'https://challenges.cloudflare.com/turnstile/v0/siteverify'
  ) {
    return new Response('No outbound network in tests', { status: 502 });
  }
  const form = await request.formData();
  const secret = form.get('secret');
  const response = form.get('response');
  const token = typeof response === 'string' ? response : '';
  const pass = secret === TURNSTILE_PASS_SECRET && token !== '';
  const errorCodes =
    secret === TURNSTILE_PASS_SECRET || secret === TURNSTILE_FAIL_SECRET
      ? pass
        ? []
        : ['invalid-input-response']
      : ['invalid-input-secret'];
  return Response.json({
    success: pass,
    hostname: pass ? 'bazaarplusplus.com' : undefined,
    action: pass ? token.split(':')[1] : undefined,
    'error-codes': errorCodes
  });
}

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
          ACCOUNT_EMAIL_FROM: 'noreply@bazaarplusplus.com',
          // Cloudflare's documented always-pass test secret.
          TURNSTILE_SECRET_KEY: TURNSTILE_PASS_SECRET
        },
        // Keeps tests offline: a fetch no test stubs reaches this stand-in.
        outboundService: fakeSiteverify,
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

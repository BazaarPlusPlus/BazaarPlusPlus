import { env } from 'cloudflare:test';
import { afterEach, expect, test, vi } from 'vitest';
import worker from '../../src/index';
import { commitBundle } from '../../src/modules/bundle-commit';
import type {
  BotBundlesResponse,
  BotPlayersResponse
} from '../../src/http/schemas';
import { bundleData } from '../fixtures/bundle';
const NOW = Date.UTC(2026, 10, 16, 4);
const FROM = Date.UTC(2026, 10, 13, 16);
afterEach(() => vi.restoreAllMocks());
function request(
  path: string,
  token: string | undefined = env.BOT_STATS_TOKEN
) {
  return worker.fetch(
    new Request(`https://test.invalid${path}`, {
      headers: token ? { Authorization: `Bearer ${token}` } : {}
    }),
    env
  );
}
async function seed(account: string, id: number, at: number) {
  const fixture = await bundleData({
    bundleId: `01J${String(id).padStart(23, '0')}`,
    runId: `run-${id}`,
    uploaderAccountId: account,
    battleId: `battle-${id}`,
    createdAtMs: NOW
  });
  await commitBundle(env.DB, fixture.descriptor, fixture.digest, {
    storedAtMs: at,
    availableAtMs: at
  });
}
test('Bot scope rejects missing and other tokens before parsing, and cannot enumerate the whole feed', async () => {
  expect((await request('/bot/players?wrong=1', '')).status).toBe(401);
  expect(
    (await request('/bot/bundles?wrong=1', env.BUNDLE_SYNC_TOKEN)).status
  ).toBe(403);
  expect(
    (await request('/bot/players?name=x', env.BAZAARDB_DELIVERY_TOKEN)).status
  ).toBe(403);
  expect((await request('/bundles?wrong=1')).status).toBe(403);
});
test('exact name lookup deduplicates accounts, returns collisions, and excludes stale uploads', async () => {
  vi.spyOn(Date, 'now').mockReturnValue(NOW);
  await seed('a', 1, FROM);
  await seed('a', 2, NOW - 10);
  await seed('b', 3, NOW - 1);
  await seed('old', 4, FROM - 1);
  const response = await request('/bot/players?name=Uploader');
  expect(response.status).toBe(200);
  expect(await response.json()).toEqual({
    players: [
      { account_id: 'a', display_name: 'Uploader' },
      { account_id: 'b', display_name: 'Uploader' }
    ],
    has_more: false
  } satisfies BotPlayersResponse);
  expect(await (await request('/bot/players?name=upload')).json()).toEqual({
    players: [],
    has_more: false
  });
});
test('account pages preserve the fixed window and equal-timestamp cursor without truncation', async () => {
  vi.spyOn(Date, 'now').mockReturnValue(NOW);
  const values = Array.from({ length: 103 }, (_, i) => {
    const id = `01J${String(i + 100).padStart(23, '0')}`;
    return env.DB.prepare(
      `INSERT INTO bundles (
        bundle_id, run_id, uploader_account_id, object_key, bundle_sha256,
        object_bytes, client_created_at_ms, stored_at_ms, available_at_ms, has_screenshot
      ) VALUES (?, ?, 'pages', ?, ?, 10, ?, ?, ?, 0)`
    ).bind(
      id,
      `page-${i}`,
      `bundles/2026-11-16/${id}.bundle`,
      'a'.repeat(64),
      NOW,
      FROM,
      FROM
    );
  });
  await env.DB.batch(values);
  await seed('elsewhere', 5, FROM);
  await seed('pages', 6, NOW);
  await seed('pages', 7, FROM - 1);
  const firstResponse = await request('/bot/bundles?account_id=pages');
  expect(firstResponse.status).toBe(200);
  const first = await firstResponse.json<BotBundlesResponse>();
  expect(first.items).toHaveLength(100);
  expect(first.window).toEqual({ stored_from_ms: FROM, stored_before_ms: NOW });
  expect(first.items[0].download_url).toContain('X-Amz-Signature=');
  const last = first.next_after!;
  const second = await (
    await request(
      `/bot/bundles?account_id=pages&stored_from_ms=${FROM}&stored_before_ms=${NOW}&after_stored_at_ms=${last.stored_at_ms}&after_bundle_id=${last.bundle_id}`
    )
  ).json<BotBundlesResponse>();
  expect(second.items).toHaveLength(3);
  expect(second.next_after).toBeNull();
  expect(
    new Set([...first.items, ...second.items].map((i) => i.bundle_id)).size
  ).toBe(103);
});
test.each([
  '/bot/players?name=',
  '/bot/players?name=a&name=b',
  '/bot/bundles?account_id=a&extra=1',
  '/bot/bundles?account_id=a&after_bundle_id=x',
  '/bot/bundles?account_id=a&stored_from_ms=0',
  '/bot/bundles?account_id=a&stored_before_ms=9999999999999'
])('validates %s', async (path) => {
  vi.spyOn(Date, 'now').mockReturnValue(NOW);
  expect((await request(path)).status).toBe(400);
});
test('the Bot secret can be provisioned independently of the existing consumers', async () => {
  const response = await worker.fetch(
    new Request('https://test.invalid/bot/bundles?account_id=a'),
    { ...env, BOT_STATS_TOKEN: undefined }
  );
  expect(response.status).toBe(500);
  const existing = await worker.fetch(
    new Request('https://test.invalid/bundles', {
      headers: { Authorization: `Bearer ${env.BUNDLE_SYNC_TOKEN}` }
    }),
    { ...env, BOT_STATS_TOKEN: undefined }
  );
  expect(existing.status).toBe(400);
});

test('a request queued just before Beijing midnight retains its original three-day start', async () => {
  const start = Date.parse('2026-11-14T00:00:00+08:00');
  const now = Date.parse('2026-11-17T00:02:00+08:00');
  vi.spyOn(Date, 'now').mockReturnValue(now);
  expect(
    (
      await request(
        `/bot/bundles?account_id=a&stored_from_ms=${start}&stored_before_ms=${now}`
      )
    ).status
  ).toBe(200);
});

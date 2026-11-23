import { createScheduledController, env } from 'cloudflare:test';
import { afterEach, expect, test, vi } from 'vitest';

import worker from '../../src/index';
import { WORKFLOW_DISPATCHES } from '../../src/modules/workflow-dispatch';
import wranglerToml from '../../wrangler.toml?raw';

const NOW = 1_785_628_800_000;
const TOKEN = 'github_pat_test';
const WORKFLOWS =
  'https://api.github.com/repos/BazaarPlusPlus/BazaarPlusPlus/actions/workflows';

afterEach(() => vi.restoreAllMocks());

function runCron(cron: string, extra: Partial<Cloudflare.Env> = {}) {
  return worker.scheduled(
    createScheduledController({ cron, scheduledTime: NOW }),
    { DB: env.DB, ...extra } as Cloudflare.Env
  );
}

test('wrangler.toml schedules every dispatch cron', () => {
  for (const cron of Object.keys(WORKFLOW_DISPATCHES)) {
    expect(wranglerToml).toContain(`"${cron}"`);
  }
});

test.each([
  ['0 1 * * *', 'analyzer.yml', {}],
  ['0 12 * * *', 'analyzer.yml', {}],
  ['50 1 * * *', 'daily-report.yml', { send: 'report' }]
])('%s dispatches %s', async (cron, workflow, inputs) => {
  vi.spyOn(console, 'log').mockImplementation(() => undefined);
  const fetchSpy = vi
    .spyOn(globalThis, 'fetch')
    .mockResolvedValue(new Response(null, { status: 204 }));

  await runCron(cron, { GITHUB_DISPATCH_TOKEN: TOKEN });

  expect(fetchSpy).toHaveBeenCalledTimes(1);
  const [url, init] = fetchSpy.mock.calls[0];
  expect(url).toBe(`${WORKFLOWS}/${workflow}/dispatches`);
  const headers = new Headers(init?.headers);
  expect(init?.method).toBe('POST');
  expect(headers.get('authorization')).toBe(`Bearer ${TOKEN}`);
  expect(headers.get('user-agent')).toBeTruthy();
  expect(JSON.parse(init?.body as string)).toEqual({ ref: 'master', inputs });
});

test('a rejected dispatch fails the invocation without logging the token', async () => {
  const error = vi.spyOn(console, 'error').mockImplementation(() => undefined);
  vi.spyOn(globalThis, 'fetch').mockResolvedValue(
    new Response('Bad credentials', { status: 401 })
  );

  await expect(
    runCron('50 1 * * *', { GITHUB_DISPATCH_TOKEN: TOKEN })
  ).rejects.toThrow('401');
  const line = String(error.mock.calls[0]?.[0]);
  expect(line).toContain('"event":"workflow.dispatch.failed"');
  expect(line).toContain('"workflow":"daily-report.yml"');
  expect(line).toContain('"status":401');
  expect(line).not.toContain(TOKEN);
});

test('a network failure fails the invocation and is logged', async () => {
  const error = vi.spyOn(console, 'error').mockImplementation(() => undefined);
  vi.spyOn(globalThis, 'fetch').mockRejectedValue(new TypeError('network'));

  await expect(
    runCron('0 1 * * *', { GITHUB_DISPATCH_TOKEN: TOKEN })
  ).rejects.toThrow('network');
  expect(String(error.mock.calls[0]?.[0])).toContain(
    '"event":"workflow.dispatch.failed"'
  );
});

test('a missing token fails the invocation before any request', async () => {
  vi.spyOn(console, 'error').mockImplementation(() => undefined);
  const fetchSpy = vi.spyOn(globalThis, 'fetch');

  await expect(runCron('50 1 * * *')).rejects.toThrow('GITHUB_DISPATCH_TOKEN');
  expect(fetchSpy).not.toHaveBeenCalled();
});

test('the retention cron never dispatches a workflow', async () => {
  vi.spyOn(console, 'log').mockImplementation(() => undefined);
  const fetchSpy = vi.spyOn(globalThis, 'fetch');

  await runCron('*/15 * * * *', { GITHUB_DISPATCH_TOKEN: TOKEN });

  expect(fetchSpy).not.toHaveBeenCalled();
});

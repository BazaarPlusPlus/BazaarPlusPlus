import { env } from 'cloudflare:test';
import { expect, test, vi } from 'vitest';

import contract from '../../contracts/mod-api-errors.json';
import apiReference from '../../docs/api-reference.md?raw';
import type { Env } from '../../src/env';
import { ApiErrorResponse } from '../../src/http/schemas';
import worker from '../../src/index';
import { handleWebApiRequest } from '../../src/modules/web-account-api';
import {
  contentDigest,
  makeBundleFixture,
  sealBundle,
  sha256Hex,
  uploadRequest
} from '../fixtures/bundle';

test('the API error table exactly projects the server-owned error contract', () => {
  const rows = [
    ...apiReference.matchAll(
      /^\| (\d+) \| `([^`]+)` \| `(true|false)` \| ([^|]+) \|$/gm
    )
  ].map(([, status, code, retryable, meaning]) => ({
    code,
    status: Number(status),
    retryable: retryable === 'true',
    meaning: meaning.trim()
  }));
  expect(rows).toEqual(contract.codes);
  expect(new Set(contract.codes.map(({ code }) => code)).size).toBe(
    contract.codes.length
  );
});

test('real HTTP failures emit exactly the declared codes, statuses, and retry flags', async () => {
  const observed = new Map<
    string,
    { code: string; status: number; retryable: boolean }
  >();
  async function record(
    request: Request,
    bindings: Env = env,
    handler: (request: Request, env: Env) => Promise<Response> = worker.fetch
  ) {
    const response = await handler(request, bindings);
    const body = await response.json<{
      error: { code: string; retryable: boolean };
    }>();
    expect(body, `${request.method} ${request.url}`).toHaveProperty(
      'error.code'
    );
    expect(ApiErrorResponse.safeParse(body).error?.issues).toBeUndefined();
    const actual = {
      code: body.error.code,
      status: response.status,
      retryable: body.error.retryable
    };
    expect(
      contract.codes.find(({ code }) => code === actual.code)
    ).toMatchObject(actual);
    observed.set(actual.code, actual);
  }
  function recordWeb(request: Request, bindings: Env) {
    return record(request, bindings, handleWebApiRequest);
  }
  function request(path: string, init?: RequestInit) {
    return new Request(`https://worker.test${path}`, init);
  }
  function collection(query: string) {
    return request(`/bundles${query}`, {
      headers: { Authorization: `Bearer ${env.BUNDLE_SYNC_TOKEN}` }
    });
  }

  await record(request('/missing'));
  await record(request('/health', { method: 'POST' }));
  await record(request('/bundles'));
  await record(
    request('/bundles', {
      headers: { Authorization: `Bearer ${env.BAZAARDB_DELIVERY_TOKEN}` }
    })
  );
  await record(collection('?unknown=1'));
  await record(collection('?available_from_ms=0'));
  const now = Date.now();
  await record(
    collection(`?available_from_ms=${now - 120_000}&available_before_ms=${now}`)
  );
  await record(request('/ghost-battles?player_account_id=account-local'), {
    ...env,
    GHOST_BATTLE_RATE_LIMITER: { limit: async () => ({ success: false }) }
  });
  await record(request('/ghost-battles?player_account_id=account-local'), {
    ...env,
    GHOST_BATTLE_RATE_LIMITER: {
      limit: async () => {
        throw new Error('unavailable');
      }
    }
  });
  const errorLog = vi
    .spyOn(console, 'error')
    .mockImplementation(() => undefined);
  try {
    await record(request('/bundles'), { ...env, BUNDLE_SYNC_TOKEN: 'invalid' });
  } finally {
    errorLog.mockRestore();
  }

  const fixture = await makeBundleFixture();
  for (const [name, value] of [
    ['Content-Type', 'text/plain'],
    ['Content-Length', null],
    ['Content-Length', 'invalid'],
    ['Content-Length', '8388608'],
    ['Content-Digest', 'invalid'],
    ['Content-Digest', `sha-256=:${'A'.repeat(43)}=:`]
  ] as const) {
    const headers = new Headers(fixture.headers);
    if (value === null) headers.delete(name);
    else headers.set(name, value);
    await record(uploadRequest(fixture.body, headers));
  }
  for (const mutation of ['magic', 'version', 'segment'] as const) {
    const body = fixture.body.slice();
    if (mutation === 'magic') body[0] = 0;
    else if (mutation === 'version')
      new DataView(body.buffer).setUint32(8, 4, false);
    else body[body.length - 1] ^= 0xff;
    const headers = new Headers(fixture.headers);
    headers.set('Content-Digest', await contentDigest(body));
    await record(uploadRequest(body, headers));
  }
  const manifest = structuredClone(fixture.manifest);
  (manifest.run as Record<string, unknown>).run_format_version = 4;
  const unsupportedRun = await sealBundle(
    manifest,
    new Uint8Array([0x1f, 0x8b, 0x08, 0, 5, 4, 3, 2, 1])
  );
  await record(uploadRequest(unsupportedRun.body, unsupportedRun.headers));
  const withoutBattles = await makeBundleFixture({
    bundleId: '01J00000000000000000000003',
    runId: 'run-without-battles',
    battles: []
  });
  await record(uploadRequest(withoutBattles.body, withoutBattles.headers));

  const stored = await worker.fetch(
    uploadRequest(fixture.body, fixture.headers),
    env
  );
  expect(stored.status).toBe(201);
  for (const options of [
    { runId: 'different-run' },
    { bundleId: '01J00000000000000000000002' }
  ]) {
    const conflicting = await makeBundleFixture(options);
    await record(uploadRequest(conflicting.body, conflicting.headers));
  }

  // Account route error codes exercised through real HTTP requests: the mod's
  // routes through the default entry, the website's through WebApi.
  const jsonPost = (path: string, body: unknown) =>
    request(path, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: typeof body === 'string' ? body : JSON.stringify(body)
    });
  const sent: string[] = [];
  const accountEnv: Env = {
    ...env,
    ACCOUNT_RATE_LIMITER: { limit: async () => ({ success: true }) },
    LOGIN_EMAIL_RATE_LIMITER: { limit: async () => ({ success: true }) },
    EMAIL: {
      send: async (message: unknown) => {
        sent.push((message as { text: string }).text);
        return { messageId: 'msg-error-contract' };
      }
    }
  };
  const site = 'https://bazaarplusplus.com';
  const webPost = (path: string, body: unknown, cookie?: string) =>
    new Request(`${site}${path}`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Origin: site,
        ...(cookie === undefined ? {} : { Cookie: cookie })
      },
      body: JSON.stringify(body)
    });
  const lastCode = () => /验证码：([0-9]{6})/.exec(sent.at(-1) ?? '')?.[1];
  const claim = (gameAccountId: string) => ({
    game_token: 'tok',
    game_account_id: gameAccountId,
    platform: 'Steam',
    flavor: 'Paid'
  });
  const official = (id: string) =>
    new Response(JSON.stringify({ id, userName: 'Name' }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' }
    });

  await record(jsonPost('/account/devices', '{not-json'), accountEnv);
  await record(
    jsonPost('/account/bind', { bind_code: 'bad', ...claim('acct-err-1') }),
    accountEnv
  );

  // The test siteverify reports the action after the colon (vitest.config.ts).
  const turnstile_token = 'turnstile-test-token:login';
  await handleWebApiRequest(
    webPost('/api/register/start', {
      email: 'err@example.com',
      turnstile_token: 'turnstile-test-token:register'
    }),
    accountEnv
  );
  const code = lastCode();
  await recordWeb(
    webPost('/api/register/confirm', {
      email: 'err@example.com',
      code,
      password: 'short'
    }),
    accountEnv
  );
  await recordWeb(
    webPost('/api/register/confirm', {
      email: 'err@example.com',
      code: code === '000000' ? '111111' : '000000',
      password: 'long enough password'
    }),
    accountEnv
  );
  await env.DB.prepare(
    `INSERT INTO bpp_accounts (account_id, email, password_hash, created_at_ms, updated_at_ms)
     VALUES ('acct-taken', 'err@example.com', 'x', 1, 1)`
  ).run();
  await recordWeb(
    webPost('/api/register/confirm', {
      email: 'err@example.com',
      code,
      password: 'long enough password'
    }),
    accountEnv
  );
  await recordWeb(
    webPost('/api/login', {
      email: 'err@example.com',
      password: 'long enough password',
      turnstile_token
    }),
    accountEnv
  );
  await recordWeb(
    webPost('/api/login', {
      email: 'err@example.com',
      password: 'long enough password'
    }),
    accountEnv
  );

  await env.DB.batch([
    env.DB.prepare(
      `INSERT INTO bpp_accounts (account_id, email, password_hash, game_account_id, created_at_ms, updated_at_ms)
       VALUES ('acct-linked', 'linked@example.com', 'x', 'acct-bound', 1, 1)`
    ),
    env.DB.prepare(
      `INSERT INTO bpp_web_sessions (session_hash, account_id, created_at_ms, expires_at_ms)
       VALUES (?1, 'acct-linked', 1, ?2)`
    ).bind(
      await sha256Hex(new TextEncoder().encode('A'.repeat(43))),
      Date.now() + 60_000
    )
  ]);
  await recordWeb(
    webPost('/api/bind-code', {}, `__Host-bpp_session=${'A'.repeat(43)}`),
    accountEnv
  );

  const fetchSpy = vi.spyOn(globalThis, 'fetch');
  try {
    fetchSpy.mockRejectedValueOnce(new Error('siteverify timed out'));
    await recordWeb(
      webPost('/api/login', {
        email: 'err@example.com',
        password: 'long enough password',
        turnstile_token
      }),
      accountEnv
    );

    fetchSpy.mockResolvedValueOnce(new Response('denied', { status: 401 }));
    await record(jsonPost('/account/devices', claim('acct-err-1')), accountEnv);

    fetchSpy.mockResolvedValueOnce(
      new Response('bad gateway', { status: 502 })
    );
    await record(jsonPost('/account/devices', claim('acct-err-1')), accountEnv);

    fetchSpy.mockResolvedValueOnce(official('acct-other'));
    await record(jsonPost('/account/devices', claim('acct-err-1')), accountEnv);

    fetchSpy.mockResolvedValueOnce(official('acct-unbound'));
    await record(
      jsonPost('/account/devices', claim('acct-unbound')),
      accountEnv
    );
  } finally {
    fetchSpy.mockRestore();
  }

  expect(
    [...observed.values()].sort((a, b) => a.code.localeCompare(b.code))
  ).toEqual(
    contract.codes
      .map(({ code, status, retryable }) => ({ code, status, retryable }))
      .sort((a, b) => a.code.localeCompare(b.code))
  );
});

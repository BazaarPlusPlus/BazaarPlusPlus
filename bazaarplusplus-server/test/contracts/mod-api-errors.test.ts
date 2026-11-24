import { env } from 'cloudflare:test';
import { expect, test, vi } from 'vitest';

import contract from '../../contracts/mod-api-errors.json';
import apiReference from '../../docs/api-reference.md?raw';
import type { Env } from '../../src/env';
import { ApiErrorResponse } from '../../src/http/schemas';
import worker from '../../src/index';
import {
  contentDigest,
  makeBundleFixture,
  sealBundle,
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
  async function record(request: Request, bindings: Env = env) {
    const response = await worker.fetch(request, bindings);
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

  // Account route error codes exercised through real HTTP requests.
  const jsonPost = (path: string, body: unknown) =>
    request(path, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: typeof body === 'string' ? body : JSON.stringify(body)
    });
  const accountEnv: Env = {
    ...env,
    EMAIL: {
      send: async () => ({ messageId: 'msg-error-contract' })
    }
  };
  await record(jsonPost('/account/bind/start', '{not-json'), accountEnv);
  await record(
    jsonPost('/account/bind/confirm', {
      pending_id: 'missing-pending-id',
      code: '123456'
    }),
    accountEnv
  );

  const fetchSpy = vi.spyOn(globalThis, 'fetch');
  try {
    fetchSpy.mockResolvedValueOnce(new Response('denied', { status: 401 }));
    await record(
      jsonPost('/account/devices', {
        game_token: 'tok-rejected',
        game_account_id: 'acct-err-1',
        platform: 'Steam',
        flavor: 'Paid'
      }),
      accountEnv
    );

    fetchSpy.mockResolvedValueOnce(
      new Response('bad gateway', { status: 502 })
    );
    await record(
      jsonPost('/account/devices', {
        game_token: 'tok-down',
        game_account_id: 'acct-err-1',
        platform: 'Steam',
        flavor: 'Paid'
      }),
      accountEnv
    );

    fetchSpy.mockResolvedValueOnce(
      new Response(JSON.stringify({ id: 'acct-other', userName: 'Other' }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' }
      })
    );
    await record(
      jsonPost('/account/devices', {
        game_token: 'tok-mismatch',
        game_account_id: 'acct-err-1',
        platform: 'Steam',
        flavor: 'Paid'
      }),
      accountEnv
    );

    fetchSpy.mockResolvedValueOnce(
      new Response(
        JSON.stringify({ id: 'acct-unbound', userName: 'Unbound' }),
        {
          status: 200,
          headers: { 'Content-Type': 'application/json' }
        }
      )
    );
    await record(
      jsonPost('/account/devices', {
        game_token: 'tok-unbound',
        game_account_id: 'acct-unbound',
        platform: 'Steam',
        flavor: 'Paid'
      }),
      accountEnv
    );

    await env.DB.prepare(
      `INSERT INTO bpp_accounts (game_account_id, email, user_name, created_at_ms, updated_at_ms)
       VALUES ('acct-bound', 'taken@example.com', 'BoundUser', 1000, 1000)`
    ).run();

    fetchSpy.mockResolvedValueOnce(
      new Response(
        JSON.stringify({ id: 'acct-bound', userName: 'BoundUser' }),
        {
          status: 200,
          headers: { 'Content-Type': 'application/json' }
        }
      )
    );
    await record(
      jsonPost('/account/bind/start', {
        email: 'fresh@example.com',
        game_token: 'tok-bound-game',
        game_account_id: 'acct-bound',
        platform: 'Steam',
        flavor: 'Paid'
      }),
      accountEnv
    );

    fetchSpy.mockResolvedValueOnce(
      new Response(JSON.stringify({ id: 'acct-new', userName: 'NewUser' }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' }
      })
    );
    await record(
      jsonPost('/account/bind/start', {
        email: 'taken@example.com',
        game_token: 'tok-taken-email',
        game_account_id: 'acct-new',
        platform: 'Steam',
        flavor: 'Paid'
      }),
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

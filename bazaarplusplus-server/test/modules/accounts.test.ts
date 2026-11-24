import { env } from 'cloudflare:test';
import { afterEach, describe, expect, test, vi } from 'vitest';

import type { Env } from '../../src/env';
import worker, { WebApi } from '../../src/index';
import { pruneExpiredBundles } from '../../src/modules/d1-retention';
import { OFFICIAL_ACCOUNT_SUMMARY_URL } from '../../src/modules/game-account-verifier';
import { handleWebApiRequest } from '../../src/modules/web-account-api';
import { makeBundleFixture, uploadRequest } from '../fixtures/bundle';

const MOD_ORIGIN = 'https://mod-api-v6.bazaarplusplus.com';
const SITE_ORIGIN = 'https://bazaarplusplus.com';
const PREVIEW_ORIGIN = 'https://bazaarplusplus-site-preview.cauyxy.workers.dev';
const PASSWORD = 'correct horse battery';
const BIND_CODE = /^[23456789ABCDEFGHJKLMNPQRSTUVWXYZ]{8}$/;
const DEVICE_TOKEN = /^bppd_[A-Za-z0-9_-]{43}$/;
// Any token passes under the test secret in vitest.config.ts.
const TURNSTILE_TOKEN = 'turnstile-test-token';
// The test siteverify reports the action after the colon (vitest.config.ts).
const tokenFor = (action: 'login' | 'register' | 'reset') =>
  `${TURNSTILE_TOKEN}:${action}`;

interface CapturedEmail {
  to: string;
  subject: string;
  text: string;
  html: string;
}

interface Harness {
  bindings: Env;
  sent: CapturedEmail[];
}

function makeHarness(overrides: Partial<Env> = {}): Harness {
  const sent: CapturedEmail[] = [];
  const bindings: Env = {
    ...env,
    ACCOUNT_RATE_LIMITER: { limit: async () => ({ success: true }) },
    LOGIN_EMAIL_RATE_LIMITER: { limit: async () => ({ success: true }) },
    EMAIL: {
      send: async (message: unknown) => {
        const builder = message as CapturedEmail;
        sent.push({
          to: builder.to,
          subject: builder.subject,
          text: builder.text ?? '',
          html: builder.html ?? ''
        });
        return { messageId: `msg-${sent.length}` };
      }
    },
    ...overrides
  };
  return { bindings, sent };
}

function lastCode(sent: CapturedEmail[]): string {
  const match = /验证码：([0-9]{6})/.exec(sent.at(-1)?.text ?? '');
  if (match === null) throw new Error('No verification code was sent');
  return match[1];
}

function webGet(
  harness: Harness,
  path: string,
  cookie?: string
): Promise<Response> {
  return handleWebApiRequest(
    new Request(`${SITE_ORIGIN}${path}`, {
      headers: cookie === undefined ? {} : { Cookie: cookie }
    }),
    harness.bindings
  );
}

// The routes that need a Turnstile token, with the widget action each expects;
// webPost adds a token unless the body already names `turnstile_token` (even
// as undefined, which drops it).
const TURNSTILE_PATHS = new Map<string, 'login' | 'register' | 'reset'>([
  ['/api/register/start', 'register'],
  ['/api/login', 'login'],
  ['/api/password/reset/start', 'reset']
]);

function webPost(
  harness: Harness,
  path: string,
  body: Record<string, unknown> = {},
  cookie?: string,
  origin = SITE_ORIGIN
): Promise<Response> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    Origin: origin
  };
  if (cookie !== undefined) headers.Cookie = cookie;
  const withToken =
    TURNSTILE_PATHS.has(path) && !('turnstile_token' in body)
      ? { ...body, turnstile_token: tokenFor(TURNSTILE_PATHS.get(path)!) }
      : body;
  return handleWebApiRequest(
    new Request(`${origin}${path}`, {
      method: 'POST',
      headers,
      body: JSON.stringify(withToken)
    }),
    harness.bindings
  );
}

function modPost(harness: Harness, path: string, body: unknown) {
  return worker.fetch(
    new Request(`${MOD_ORIGIN}${path}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    }),
    harness.bindings
  );
}

function cookieOf(response: Response): string {
  const header = response.headers.get('Set-Cookie') ?? '';
  expect(header).toContain('__Host-bpp_session=');
  return header.split(';')[0];
}

async function errorCode(response: Response): Promise<string> {
  return (await response.json<{ error: { code: string } }>()).error.code;
}

function officialSummary(id: string, userName: string): Response {
  return new Response(
    JSON.stringify({
      id,
      userName,
      email: 'official@secret.com',
      hasViewedDailySpecial: true
    }),
    { status: 200, headers: { 'Content-Type': 'application/json' } }
  );
}

async function register(
  harness: Harness,
  email: string,
  password = PASSWORD
): Promise<string> {
  expect(
    (await webPost(harness, '/api/register/start', { email })).status
  ).toBe(200);
  const confirmed = await webPost(harness, '/api/register/confirm', {
    email,
    code: lastCode(harness.sent),
    password
  });
  expect(confirmed.status).toBe(200);
  return cookieOf(confirmed);
}

async function accountIdOf(email: string): Promise<string> {
  const row = await env.DB.prepare(
    `SELECT account_id FROM bpp_accounts WHERE email = ?1`
  )
    .bind(email)
    .first<{ account_id: string }>();
  if (row === null) throw new Error(`No account for ${email}`);
  return row.account_id;
}

function bindBody(bindCode: string, gameAccountId: string) {
  return {
    bind_code: bindCode,
    game_token: 'jwe.game.token.secret',
    game_account_id: gameAccountId,
    platform: 'Steam',
    flavor: 'Paid'
  };
}

async function newBindCode(harness: Harness, cookie: string): Promise<string> {
  const response = await webPost(harness, '/api/bind-code', {}, cookie);
  expect(response.status).toBe(200);
  return (await response.json<{ code: string }>()).code;
}

// Registers, generates a Bind Code on the site, and links it from the mod.
async function linkedAccount(
  harness: Harness,
  email: string,
  gameAccountId: string,
  userName = 'Player'
): Promise<{ cookie: string; deviceToken: string; accountId: string }> {
  const cookie = await register(harness, email);
  const code = await newBindCode(harness, cookie);
  const fetchSpy = vi
    .spyOn(globalThis, 'fetch')
    .mockResolvedValueOnce(officialSummary(gameAccountId, userName));
  const bound = await modPost(
    harness,
    '/account/bind',
    bindBody(code, gameAccountId)
  );
  fetchSpy.mockRestore();
  expect(bound.status).toBe(200);
  const { device_token } = await bound.json<{ device_token: string }>();
  return {
    cookie,
    deviceToken: device_token,
    accountId: await accountIdOf(email)
  };
}

// A D1 binding that runs `before` once, right before the first statement whose
// SQL matches `pattern` executes: a deterministic stand-in for a concurrent
// request landing between a handler's check and its write.
function interleavedDb(pattern: RegExp, before: () => Promise<unknown>) {
  let fired = false;
  const fire = async () => {
    if (fired) return;
    fired = true;
    await before();
  };
  const wrap = (statement: D1PreparedStatement, sql: string) =>
    new Proxy(statement, {
      get(target, property) {
        if (property === 'bind') {
          return (...values: unknown[]) => wrap(target.bind(...values), sql);
        }
        if (
          pattern.test(sql) &&
          (property === 'run' || property === 'first' || property === 'all')
        ) {
          return async (...args: unknown[]) => {
            await fire();
            return (
              Reflect.get(target, property) as (...a: unknown[]) => unknown
            ).apply(target, args);
          };
        }
        const value = Reflect.get(target, property);
        return typeof value === 'function' ? value.bind(target) : value;
      }
    });
  return new Proxy(env.DB, {
    get(target, property) {
      if (property === 'prepare') {
        return (sql: string) => wrap(target.prepare(sql), sql);
      }
      if (property === 'batch') {
        return async (statements: D1PreparedStatement[]) => {
          await fire();
          return target.batch(statements);
        };
      }
      const value = Reflect.get(target, property);
      return typeof value === 'function' ? value.bind(target) : value;
    }
  });
}

async function count(sql: string, ...params: unknown[]): Promise<number> {
  const row = await env.DB.prepare(sql)
    .bind(...params)
    .first<{ c: number }>();
  return Number(row?.c);
}

const RUN_PROJECTION = {
  status: 'completed',
  game_mode: 'Ranked',
  hero: 'Vanessa',
  started_at_ms: 1_785_620_000_000,
  ended_at_ms: 1_785_625_000_000,
  day: 12,
  victories: 10,
  losses: 2,
  final_player_rank: 'Legend',
  final_player_rating: 1450,
  final_player_rating_delta: 28
};

afterEach(() => {
  vi.restoreAllMocks();
});

describe('website registration, login, and password reset', () => {
  test('register start sends a code only for a new email, and confirm creates the account and a session', async () => {
    const harness = makeHarness();
    const start = await webPost(harness, '/api/register/start', {
      email: '  New@Example.COM '
    });
    expect(start.status).toBe(200);
    expect(await start.json()).toEqual({ ok: true });
    expect(harness.sent).toHaveLength(1);
    expect(harness.sent[0].to).toBe('new@example.com');
    const code = lastCode(harness.sent);
    expect(harness.sent[0].subject).toBe(`BazaarPlusPlus 注册验证码：${code}`);
    expect(harness.sent[0].html).toContain(`>${code}</td>`);

    // A short password is rejected before the code is spent.
    const short = await webPost(harness, '/api/register/confirm', {
      email: 'new@example.com',
      code,
      password: 'short'
    });
    expect(short.status).toBe(400);
    expect(await errorCode(short)).toBe('invalid_password');

    const wrong = await webPost(harness, '/api/register/confirm', {
      email: 'new@example.com',
      code: code === '000000' ? '111111' : '000000',
      password: PASSWORD
    });
    expect(wrong.status).toBe(401);
    expect(await errorCode(wrong)).toBe('invalid_verification_code');

    const confirmed = await webPost(harness, '/api/register/confirm', {
      email: 'NEW@example.com',
      code,
      password: PASSWORD
    });
    expect(confirmed.status).toBe(200);
    expect(await confirmed.json()).toEqual({ email: 'new@example.com' });
    const setCookie = confirmed.headers.get('Set-Cookie') ?? '';
    for (const attribute of ['HttpOnly', 'Secure', 'SameSite=Lax', 'Path=/']) {
      expect(setCookie).toContain(attribute);
    }

    // PBKDF2-SHA256 at 100 000 iterations runs in workerd and is stored self-describing.
    const stored = await env.DB.prepare(
      `SELECT password_hash FROM bpp_accounts WHERE email = 'new@example.com'`
    ).first<{ password_hash: string }>();
    expect(stored?.password_hash).toMatch(
      /^pbkdf2-sha256\$100000\$[A-Za-z0-9_-]{22}\$[A-Za-z0-9_-]{43}$/
    );

    const me = await webGet(harness, '/api/me', cookieOf(confirmed));
    expect(me.status).toBe(200);
    expect(await me.json()).toEqual({
      email: 'new@example.com',
      game: null,
      stats: { runs: 0, victories: 0, ten_win_runs: 0, heroes: [], rating: [] }
    });

    // The code is spent; an existing email gets 200 and no code.
    expect(
      (
        await webPost(harness, '/api/register/confirm', {
          email: 'new@example.com',
          code,
          password: PASSWORD
        })
      ).status
    ).toBe(401);
    expect(
      (
        await webPost(harness, '/api/register/start', {
          email: 'new@example.com'
        })
      ).status
    ).toBe(200);
    expect(harness.sent).toHaveLength(1);
  });

  test('a registration that loses the email race gets email_already_registered and no session', async () => {
    const harness = makeHarness();
    await webPost(harness, '/api/register/start', {
      email: 'race@example.com'
    });
    const code = lastCode(harness.sent);
    await env.DB.prepare(
      `INSERT INTO bpp_accounts (account_id, email, password_hash, created_at_ms, updated_at_ms)
       VALUES ('winner', 'race@example.com', 'x', 1, 1)`
    ).run();
    const sessionsBefore = await count(
      `SELECT COUNT(*) AS c FROM bpp_web_sessions`
    );

    const confirmed = await webPost(harness, '/api/register/confirm', {
      email: 'race@example.com',
      code,
      password: PASSWORD
    });
    expect(confirmed.status).toBe(409);
    expect(await errorCode(confirmed)).toBe('email_already_registered');
    // The session insert shared the batch and rolled back with the account insert.
    expect(await count(`SELECT COUNT(*) AS c FROM bpp_web_sessions`)).toBe(
      sessionsBefore
    );
    expect(await accountIdOf('race@example.com')).toBe('winner');
    // The challenge is consumed in the same batch, so the rollback kept the code.
    expect(
      await count(
        `SELECT COUNT(*) AS c FROM bpp_email_challenges
         WHERE email = 'race@example.com' AND consumed_at_ms IS NULL`
      )
    ).toBe(1);
  });

  test('a reset whose write fails leaves the code usable for the retry', async () => {
    const harness = makeHarness();
    const cookie = await register(harness, 'retry-reset@example.com');
    await webPost(harness, '/api/password/reset/start', {
      email: 'retry-reset@example.com'
    });
    const code = lastCode(harness.sent);
    const body = {
      email: 'retry-reset@example.com',
      code,
      password: 'brand new password'
    };

    const failingDb = new Proxy(env.DB, {
      get(target, property) {
        if (property === 'batch') {
          return () => Promise.reject(new Error('D1 is down'));
        }
        const value = Reflect.get(target, property);
        return typeof value === 'function' ? value.bind(target) : value;
      }
    });
    const failed = await handleWebApiRequest(
      new Request(`${SITE_ORIGIN}/api/password/reset/confirm`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Origin: SITE_ORIGIN },
        body: JSON.stringify(body)
      }),
      { ...harness.bindings, DB: failingDb }
    );
    expect(failed.status).toBe(503);
    // The old session still works: nothing was written.
    expect((await webGet(harness, '/api/me', cookie)).status).toBe(200);

    const retried = await webPost(harness, '/api/password/reset/confirm', body);
    expect(retried.status).toBe(200);
    expect((await webGet(harness, '/api/me', cookie)).status).toBe(401);
  });

  test('a login verified against a password that a reset replaces meanwhile gets no session', async () => {
    const harness = makeHarness();
    await register(harness, 'reset-race@example.com');
    const accountId = await accountIdOf('reset-race@example.com');
    const sessionsBefore = await count(
      `SELECT COUNT(*) AS c FROM bpp_web_sessions WHERE account_id = ?1`,
      accountId
    );
    // The reset commits after the login checked the old hash, before its insert.
    const racingDb = interleavedDb(/INSERT INTO bpp_web_sessions/, () =>
      env.DB.prepare(
        `UPDATE bpp_accounts SET password_hash = 'replaced' WHERE account_id = ?1`
      )
        .bind(accountId)
        .run()
    );
    const login = await handleWebApiRequest(
      new Request(`${SITE_ORIGIN}/api/login`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Origin: SITE_ORIGIN },
        body: JSON.stringify({
          email: 'reset-race@example.com',
          password: PASSWORD,
          turnstile_token: tokenFor('login')
        })
      }),
      { ...harness.bindings, DB: racingDb }
    );
    expect(login.status).toBe(401);
    expect(await errorCode(login)).toBe('invalid_credentials');
    expect(login.headers.get('Set-Cookie')).toBeNull();
    expect(
      await count(
        `SELECT COUNT(*) AS c FROM bpp_web_sessions WHERE account_id = ?1`,
        accountId
      )
    ).toBe(sessionsBefore);
  });

  test('register start caps sends per email per hour and still answers 200', async () => {
    const harness = makeHarness();
    for (let index = 0; index < 6; index += 1) {
      const response = await webPost(harness, '/api/register/start', {
        email: 'cap@example.com'
      });
      expect(response.status).toBe(200);
    }
    expect(harness.sent).toHaveLength(5);
  });

  test('login accepts the right password and answers invalid_credentials for a wrong password or an unknown email', async () => {
    const harness = makeHarness();
    await register(harness, 'login@example.com');

    const good = await webPost(harness, '/api/login', {
      email: 'Login@Example.com',
      password: PASSWORD
    });
    expect(good.status).toBe(200);
    expect(await good.json()).toEqual({ email: 'login@example.com' });
    expect((await webGet(harness, '/api/me', cookieOf(good))).status).toBe(200);

    // Both failures cost exactly one PBKDF2 derive, so timing does not reveal the email.
    const derive = vi.spyOn(crypto.subtle, 'deriveBits');
    for (const body of [
      { email: 'login@example.com', password: 'wrong password!' },
      { email: 'nobody@example.com', password: PASSWORD }
    ]) {
      derive.mockClear();
      const failed = await webPost(harness, '/api/login', body);
      expect(derive).toHaveBeenCalledOnce();
      expect(derive.mock.calls[0][0]).toMatchObject({
        name: 'PBKDF2',
        hash: 'SHA-256',
        iterations: 100_000
      });
      expect(failed.status).toBe(401);
      expect(failed.headers.get('Set-Cookie')).toBeNull();
      expect(await errorCode(failed)).toBe('invalid_credentials');
    }
  });

  test('the per-email login limiter keys on the normalized email, unknown emails included', async () => {
    const counts = new Map<string, number>();
    const harness = makeHarness({
      LOGIN_EMAIL_RATE_LIMITER: {
        limit: async ({ key }: { key: string }) => {
          const next = (counts.get(key) ?? 0) + 1;
          counts.set(key, next);
          return { success: next <= 2 };
        }
      }
    });
    await register(harness, 'limited@example.com');

    expect(
      (
        await webPost(harness, '/api/login', {
          email: 'limited@example.com',
          password: PASSWORD
        })
      ).status
    ).toBe(200);
    expect(
      (
        await webPost(harness, '/api/login', {
          email: ' LIMITED@example.com',
          password: 'wrong password!'
        })
      ).status
    ).toBe(401);
    const third = await webPost(harness, '/api/login', {
      email: 'Limited@Example.com',
      password: PASSWORD
    });
    expect(third.status).toBe(429);
    expect(third.headers.get('Retry-After')).toBe('60');
    expect(await errorCode(third)).toBe('rate_limited');

    for (let index = 0; index < 2; index += 1) {
      expect(
        (
          await webPost(harness, '/api/login', {
            email: 'ghost@example.com',
            password: PASSWORD
          })
        ).status
      ).toBe(401);
    }
    expect(
      (
        await webPost(harness, '/api/login', {
          email: 'ghost@example.com',
          password: PASSWORD
        })
      ).status
    ).toBe(429);
    expect([...counts.keys()].sort()).toEqual([
      'ghost@example.com',
      'limited@example.com'
    ]);
  });

  test('password reset sets the new password, signs out every session, and signs this browser in', async () => {
    const harness = makeHarness();
    const first = await register(harness, 'reset@example.com');
    const second = cookieOf(
      await webPost(harness, '/api/login', {
        email: 'reset@example.com',
        password: PASSWORD
      })
    );

    const unknown = await webPost(harness, '/api/password/reset/start', {
      email: 'unknown@example.com'
    });
    expect(unknown.status).toBe(200);
    expect(harness.sent).toHaveLength(1);

    expect(
      (
        await webPost(harness, '/api/password/reset/start', {
          email: 'reset@example.com'
        })
      ).status
    ).toBe(200);
    expect(harness.sent).toHaveLength(2);
    expect(harness.sent[1].subject).toContain('重置密码验证码');
    const reset = await webPost(harness, '/api/password/reset/confirm', {
      email: 'reset@example.com',
      code: lastCode(harness.sent),
      password: 'a brand new password'
    });
    expect(reset.status).toBe(200);
    expect(await reset.json()).toEqual({ email: 'reset@example.com' });

    for (const old of [first, second]) {
      expect((await webGet(harness, '/api/me', old)).status).toBe(401);
    }
    expect((await webGet(harness, '/api/me', cookieOf(reset))).status).toBe(
      200
    );
    expect(
      (
        await webPost(harness, '/api/login', {
          email: 'reset@example.com',
          password: PASSWORD
        })
      ).status
    ).toBe(401);
    expect(
      (
        await webPost(harness, '/api/login', {
          email: 'reset@example.com',
          password: 'a brand new password'
        })
      ).status
    ).toBe(200);
  });
});

describe('Turnstile on register start, login, and reset start', () => {
  const STARTS = [
    ['/api/register/start', { email: 'bot@example.com' }],
    ['/api/password/reset/start', { email: 'bot@example.com' }],
    ['/api/login', { email: 'bot@example.com', password: PASSWORD }]
  ] as const;

  // Answers every route without touching D1, email, or the per-email limiter.
  function sealedHarness(overrides: Partial<Env> = {}): {
    harness: Harness;
    loginLimits: () => number;
  } {
    let limits = 0;
    const harness = makeHarness({
      DB: new Proxy(env.DB, {
        get() {
          throw new Error('D1 must not be reached before Turnstile passes');
        }
      }),
      LOGIN_EMAIL_RATE_LIMITER: {
        limit: async () => {
          limits += 1;
          return { success: true };
        }
      },
      ...overrides
    });
    return { harness, loginLimits: () => limits };
  }

  async function expectRefused(
    harness: Harness,
    body: Record<string, unknown>,
    status: number,
    code: string,
    retryable: boolean
  ): Promise<void> {
    for (const [path, fields] of STARTS) {
      const response = await webPost(harness, path, { ...fields, ...body });
      expect(response.status, path).toBe(status);
      const { error } = await response.json<{
        error: { code: string; retryable: boolean };
      }>();
      expect(error, path).toMatchObject({ code, retryable });
    }
    expect(harness.sent).toEqual([]);
  }

  test('a missing or failed token gets captcha_failed before any D1 query, email, or login limit', async () => {
    const { harness, loginLimits } = sealedHarness();
    await expectRefused(
      harness,
      { turnstile_token: undefined },
      403,
      'captcha_failed',
      false
    );
    await expectRefused(
      harness,
      { turnstile_token: 'x'.repeat(2049) },
      403,
      'captcha_failed',
      false
    );

    // Cloudflare's always-fail test secret.
    const failing = sealedHarness({
      TURNSTILE_SECRET_KEY: '2x0000000000000000000000000000000AA'
    });
    await expectRefused(failing.harness, {}, 403, 'captcha_failed', false);
    expect(loginLimits() + failing.loginLimits()).toBe(0);
  });

  test('a token solved on another host gets captcha_failed', async () => {
    const { harness } = sealedHarness();
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (_url, init) => {
      const token = (init!.body as FormData).get('response');
      return Response.json({
        success: true,
        hostname: 'evil.example',
        action: typeof token === 'string' ? token.split(':')[1] : undefined
      });
    });
    await expectRefused(harness, {}, 403, 'captcha_failed', false);
  });

  test('a token solved for another form gets captcha_failed', async () => {
    const { harness } = sealedHarness();
    // A login token replayed on the sign-up and reset forms, and the reverse.
    for (const [path, fields] of STARTS) {
      const other = path === '/api/login' ? 'register' : 'login';
      const response = await webPost(harness, path, {
        ...fields,
        turnstile_token: tokenFor(other)
      });
      expect(response.status, path).toBe(403);
      expect(await errorCode(response), path).toBe('captcha_failed');
    }
    expect(harness.sent).toEqual([]);
  });

  test('a siteverify outage gets retryable captcha_unavailable', async () => {
    const { harness } = sealedHarness();
    const fetchSpy = vi
      .spyOn(globalThis, 'fetch')
      .mockRejectedValue(new Error('timeout'));
    await expectRefused(harness, {}, 503, 'captcha_unavailable', true);
    fetchSpy.mockResolvedValue(new Response('oops', { status: 500 }));
    await expectRefused(harness, {}, 503, 'captcha_unavailable', true);
  });

  test('a missing or rejected secret fails closed with internal_error', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const missing = sealedHarness({ TURNSTILE_SECRET_KEY: undefined });
    await expectRefused(missing.harness, {}, 500, 'internal_error', true);
    const wrong = sealedHarness({ TURNSTILE_SECRET_KEY: 'not-our-secret' });
    await expectRefused(wrong.harness, {}, 500, 'internal_error', true);
  });

  test('a passing token reaches the route, and siteverify gets the secret, token, and client address', async () => {
    const harness = makeHarness();
    const fetchSpy = vi.spyOn(globalThis, 'fetch');
    const response = await handleWebApiRequest(
      new Request(`${SITE_ORIGIN}/api/register/start`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Origin: SITE_ORIGIN,
          'CF-Connecting-IP': '203.0.113.7'
        },
        body: JSON.stringify({
          email: 'human@example.com',
          turnstile_token: 'solved-token:register'
        })
      }),
      harness.bindings
    );
    expect(response.status).toBe(200);
    expect(harness.sent).toHaveLength(1);

    expect(fetchSpy).toHaveBeenCalledTimes(1);
    const [url, init] = fetchSpy.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(
      'https://challenges.cloudflare.com/turnstile/v0/siteverify'
    );
    const form = init.body as FormData;
    expect(Object.fromEntries(form)).toEqual({
      secret: env.TURNSTILE_SECRET_KEY,
      response: 'solved-token:register',
      remoteip: '203.0.113.7'
    });
  });

  test('confirm steps, Bind Codes, and logout need no token', async () => {
    const harness = makeHarness();
    const cookie = await register(harness, 'no-token@example.com');
    const fetchSpy = vi.spyOn(globalThis, 'fetch');
    expect((await webPost(harness, '/api/bind-code', {}, cookie)).status).toBe(
      200
    );
    expect((await webPost(harness, '/api/logout', {}, cookie)).status).toBe(
      200
    );
    expect(fetchSpy).not.toHaveBeenCalled();
  });
});

describe('Bind Codes and linking from the mod', () => {
  test('a Bind Code replaces the previous one, expires, and is refused once the account is linked', async () => {
    const harness = makeHarness();
    const cookie = await register(harness, 'codes@example.com');
    const before = Date.now();
    const response = await webPost(harness, '/api/bind-code', {}, cookie);
    const first = await response.json<{
      code: string;
      expires_at_ms: number;
    }>();
    expect(first.code).toMatch(BIND_CODE);
    expect(first.expires_at_ms).toBeGreaterThanOrEqual(before + 10 * 60_000);
    expect(first.expires_at_ms).toBeLessThanOrEqual(Date.now() + 10 * 60_000);

    const second = await newBindCode(harness, cookie);
    expect(second).not.toBe(first.code);
    const accountId = await accountIdOf('codes@example.com');
    expect(
      await count(
        `SELECT COUNT(*) AS c FROM bpp_bind_codes WHERE account_id = ?1`,
        accountId
      )
    ).toBe(1);
    const stored = await env.DB.prepare(
      `SELECT code_hash FROM bpp_bind_codes WHERE account_id = ?1`
    )
      .bind(accountId)
      .first<{ code_hash: string }>();
    expect(stored?.code_hash).toMatch(/^[0-9a-f]{64}$/);

    const fetchSpy = vi.spyOn(globalThis, 'fetch');
    fetchSpy.mockResolvedValueOnce(officialSummary('acct-codes', 'Codes'));
    const replaced = await modPost(
      harness,
      '/account/bind',
      bindBody(first.code, 'acct-codes')
    );
    expect(replaced.status).toBe(400);
    expect(await errorCode(replaced)).toBe('invalid_bind_code');

    await env.DB.prepare(
      `UPDATE bpp_bind_codes SET expires_at_ms = 1 WHERE account_id = ?1`
    )
      .bind(accountId)
      .run();
    fetchSpy.mockResolvedValueOnce(officialSummary('acct-codes', 'Codes'));
    const expired = await modPost(
      harness,
      '/account/bind',
      bindBody(second, 'acct-codes')
    );
    expect(await errorCode(expired)).toBe('invalid_bind_code');

    const third = await newBindCode(harness, cookie);
    fetchSpy.mockResolvedValueOnce(officialSummary('acct-codes', 'Codes'));
    expect(
      (await modPost(harness, '/account/bind', bindBody(third, 'acct-codes')))
        .status
    ).toBe(200);
    const linked = await webPost(harness, '/api/bind-code', {}, cookie);
    expect(linked.status).toBe(409);
    expect(await errorCode(linked)).toBe('game_account_already_bound');
  });

  test('POST /account/bind links the game account, consumes the code, and issues a Device Token', async () => {
    const harness = makeHarness();
    const logSpy = vi.spyOn(console, 'log').mockImplementation(() => undefined);
    const errSpy = vi
      .spyOn(console, 'error')
      .mockImplementation(() => undefined);
    const cookie = await register(harness, 'alice@example.com');
    const code = await newBindCode(harness, cookie);

    const fetchSpy = vi.spyOn(globalThis, 'fetch');
    // Malformed codes fail before the official API is called.
    const malformed = await modPost(
      harness,
      '/account/bind',
      bindBody('XYZ', 'acct-alpha')
    );
    expect(malformed.status).toBe(400);
    expect(await errorCode(malformed)).toBe('invalid_bind_code');
    expect(fetchSpy).not.toHaveBeenCalled();

    fetchSpy.mockResolvedValueOnce(officialSummary('acct-other', 'Other'));
    const mismatch = await modPost(
      harness,
      '/account/bind',
      bindBody(code, 'acct-alpha')
    );
    expect(mismatch.status).toBe(409);
    expect(await errorCode(mismatch)).toBe('account_mismatch');

    fetchSpy.mockResolvedValueOnce(officialSummary('acct-alpha', 'Alice'));
    const unknown = await modPost(
      harness,
      '/account/bind',
      bindBody(code === '22222222' ? '33333333' : '22222222', 'acct-alpha')
    );
    expect(await errorCode(unknown)).toBe('invalid_bind_code');

    // Spaces, dashes, and lowercase are normalized.
    fetchSpy.mockResolvedValueOnce(officialSummary('acct-alpha', 'Alice'));
    const typed = ` ${code.slice(0, 4).toLowerCase()}-${code.slice(4)} `;
    const bound = await modPost(
      harness,
      '/account/bind',
      bindBody(typed, 'acct-alpha')
    );
    expect(bound.status).toBe(200);
    const [calledUrl, calledInit] = fetchSpy.mock.calls.at(-1) as [
      string,
      RequestInit
    ];
    expect(calledUrl).toBe(OFFICIAL_ACCOUNT_SUMMARY_URL);
    expect(new Headers(calledInit.headers).get('Authorization')).toBe(
      'Bearer jwe.game.token.secret'
    );
    const body = await bound.json<{
      device_token: string;
      user_name: string;
      email_masked: string;
    }>();
    expect(body.device_token).toMatch(DEVICE_TOKEN);
    expect(body).toMatchObject({
      user_name: 'Alice',
      email_masked: 'a***@example.com'
    });
    const aliceId = await accountIdOf('alice@example.com');
    expect(
      await count(
        `SELECT COUNT(*) AS c FROM bpp_bind_codes WHERE account_id = ?1`,
        aliceId
      )
    ).toBe(0);
    const token = await env.DB.prepare(
      `SELECT token_hash, account_id, game_account_id FROM bpp_device_tokens WHERE account_id = ?1`
    )
      .bind(aliceId)
      .first<{
        token_hash: string;
        account_id: string;
        game_account_id: string;
      }>();
    expect(token).toEqual({
      token_hash: expect.stringMatching(/^[0-9a-f]{64}$/),
      account_id: aliceId,
      game_account_id: 'acct-alpha'
    });

    const me = await webGet(harness, '/api/me', cookie);
    const meBody = await me.json<{
      game: { user_name: string; linked_at_ms: number };
    }>();
    expect(meBody.game.user_name).toBe('Alice');
    expect(meBody.game.linked_at_ms).toBeGreaterThan(0);

    // The spent code cannot link again.
    fetchSpy.mockResolvedValueOnce(officialSummary('acct-alpha', 'Alice'));
    expect(
      await errorCode(
        await modPost(harness, '/account/bind', bindBody(code, 'acct-alpha'))
      )
    ).toBe('invalid_bind_code');

    const modMe = await worker.fetch(
      new Request(`${MOD_ORIGIN}/account/me`, {
        headers: { Authorization: `Bearer ${body.device_token}` }
      }),
      harness.bindings
    );
    expect(await modMe.json()).toEqual({
      user_name: 'Alice',
      email_masked: 'a***@example.com'
    });

    const logs = JSON.stringify([...logSpy.mock.calls, ...errSpy.mock.calls]);
    for (const secret of [
      code,
      body.device_token,
      'jwe.game.token.secret',
      'alice@example.com',
      PASSWORD
    ]) {
      expect(logs).not.toContain(secret);
    }
  });

  test('a game account linked elsewhere is refused and the Bind Code stays usable', async () => {
    const harness = makeHarness();
    await linkedAccount(harness, 'first@example.com', 'acct-shared');
    const cookie = await register(harness, 'second@example.com');
    const code = await newBindCode(harness, cookie);

    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(
      officialSummary('acct-shared', 'Shared')
    );
    const refused = await modPost(
      harness,
      '/account/bind',
      bindBody(code, 'acct-shared')
    );
    expect(refused.status).toBe(409);
    expect(await errorCode(refused)).toBe('game_account_already_bound');
    expect(
      await count(
        `SELECT COUNT(*) AS c FROM bpp_bind_codes WHERE account_id = ?1`,
        await accountIdOf('second@example.com')
      )
    ).toBe(1);
  });

  test('a link that loses the UNIQUE race rolls the whole batch back and keeps the code', async () => {
    const harness = makeHarness();
    await linkedAccount(harness, 'winner@example.com', 'acct-raced');
    const cookie = await register(harness, 'loser@example.com');
    const code = await newBindCode(harness, cookie);

    // Hide the winner from the pre-check once, as if it linked in between.
    let hidden = false;
    const db = new Proxy(env.DB, {
      get(target, property) {
        if (property === 'prepare') {
          return (sql: string) =>
            !hidden &&
            sql.startsWith(
              'SELECT account_id FROM bpp_accounts WHERE game_account_id'
            )
              ? ((hidden = true), { bind: () => ({ first: async () => null }) })
              : target.prepare(sql);
        }
        const value = Reflect.get(target, property);
        return typeof value === 'function' ? value.bind(target) : value;
      }
    });
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(
      officialSummary('acct-raced', 'Raced')
    );
    const raced = await worker.fetch(
      new Request(`${MOD_ORIGIN}/account/bind`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(bindBody(code, 'acct-raced'))
      }),
      { ...harness.bindings, DB: db }
    );
    expect(hidden).toBe(true);
    expect(raced.status).toBe(409);
    expect(await errorCode(raced)).toBe('game_account_already_bound');
    const loser = await accountIdOf('loser@example.com');
    expect(
      await count(
        `SELECT COUNT(*) AS c FROM bpp_bind_codes WHERE account_id = ?1`,
        loser
      )
    ).toBe(1);
    expect(
      await count(
        `SELECT COUNT(*) AS c FROM bpp_device_tokens WHERE account_id = ?1`,
        loser
      )
    ).toBe(0);
  });

  test('D1 rolls back earlier statements of a batch when a later one breaks UNIQUE', async () => {
    const harness = makeHarness();
    await linkedAccount(harness, 'held@example.com', 'acct-held');
    const cookie = await register(harness, 'other@example.com');
    await newBindCode(harness, cookie);
    const other = await accountIdOf('other@example.com');

    await expect(
      env.DB.batch([
        env.DB.prepare(`DELETE FROM bpp_bind_codes WHERE account_id = ?1`).bind(
          other
        ),
        env.DB.prepare(
          `UPDATE bpp_accounts SET game_account_id = 'acct-held' WHERE account_id = ?1`
        ).bind(other)
      ])
    ).rejects.toThrow(/UNIQUE constraint failed/);
    expect(
      await count(
        `SELECT COUNT(*) AS c FROM bpp_bind_codes WHERE account_id = ?1`,
        other
      )
    ).toBe(1);

    // POST /account/bind reads success from the token INSERT ... SELECT alone:
    // D1 reports its own changes, not the batch's.
    const results = await env.DB.batch([
      env.DB.prepare(
        `UPDATE bpp_accounts SET updated_at_ms = 2 WHERE account_id = ?1`
      ).bind(other),
      env.DB.prepare(
        `INSERT INTO bpp_device_tokens (token_hash, account_id, game_account_id, created_at_ms)
         SELECT ?2, account_id, game_account_id, 1
         FROM bpp_accounts WHERE account_id = ?1 AND game_account_id = 'acct-none'`
      ).bind(other, 'f'.repeat(64))
    ]);
    expect(results.map(({ meta }) => meta.changes)).toEqual([1, 0]);
  });

  test('POST /account/devices issues a token for a linked game account and refuses an unlinked one', async () => {
    const harness = makeHarness();
    const { deviceToken } = await linkedAccount(
      harness,
      'devices@example.com',
      'acct-devices',
      'Before'
    );
    const fetchSpy = vi.spyOn(globalThis, 'fetch');
    fetchSpy.mockResolvedValueOnce(officialSummary('acct-devices', 'Renamed'));
    const issued = await modPost(harness, '/account/devices', {
      game_token: 'tok',
      game_account_id: 'acct-devices',
      platform: 'Steam',
      flavor: 'Paid'
    });
    expect(issued.status).toBe(200);
    const body = await issued.json<{
      device_token: string;
      user_name: string;
      email_masked: string;
    }>();
    expect(body.device_token).toMatch(DEVICE_TOKEN);
    expect(body.device_token).not.toBe(deviceToken);
    expect(body).toMatchObject({
      user_name: 'Renamed',
      email_masked: 'd***@example.com'
    });

    fetchSpy.mockResolvedValueOnce(officialSummary('acct-free', 'Free'));
    const unlinked = await modPost(harness, '/account/devices', {
      game_token: 'tok',
      game_account_id: 'acct-free',
      platform: 'Steam',
      flavor: 'Paid'
    });
    expect(unlinked.status).toBe(404);
    expect(await errorCode(unlinked)).toBe('account_not_bound');

    // A linked game account id alone is not enough: the official API must
    // accept the token and name that same account.
    const claim = {
      game_token: 'forged',
      game_account_id: 'acct-devices',
      platform: 'Steam',
      flavor: 'Paid'
    };
    fetchSpy.mockResolvedValueOnce(new Response(null, { status: 401 }));
    const rejected = await modPost(harness, '/account/devices', claim);
    expect(await errorCode(rejected)).toBe('game_token_rejected');

    fetchSpy.mockResolvedValueOnce(officialSummary('acct-other', 'Other'));
    const foreign = await modPost(harness, '/account/devices', claim);
    expect(foreign.status).toBe(409);
    expect(await errorCode(foreign)).toBe('account_mismatch');

    const tokens = await harness.bindings.DB.prepare(
      `SELECT COUNT(*) AS n FROM bpp_device_tokens WHERE game_account_id = 'acct-devices'`
    ).first<{ n: number }>();
    expect(tokens?.n).toBe(2);
  });
});

describe('Run Summaries and stored Run detail', () => {
  const runBytes = new Uint8Array([
    0x1f, 0x8b, 0x08, 0, 0xde, 0xad, 0xbe, 0xef, 0x42, 0x17
  ]);
  const screenshotBytes = new Uint8Array([0xff, 0xd8, 0x55, 0x66, 0xff, 0xd9]);

  test('a matching upload copies exactly the Run segment and marks detail_stored; others store nothing', async () => {
    const harness = makeHarness();
    const { deviceToken, accountId } = await linkedAccount(
      harness,
      'player@example.com',
      'acct-player'
    );
    const logSpy = vi.spyOn(console, 'log').mockImplementation(() => undefined);

    const upload = async (
      options: Parameters<typeof makeBundleFixture>[0],
      authorization: string | null
    ) => {
      const fixture = await makeBundleFixture(options);
      if (authorization !== null) {
        fixture.headers.set('Authorization', authorization);
      }
      const response = await worker.fetch(
        uploadRequest(fixture.body, fixture.headers),
        harness.bindings
      );
      expect(response.status).toBe(201);
    };

    await upload(
      {
        bundleId: '01J00000000000000000001001',
        runId: 'run-match',
        uploaderAccountId: 'acct-player',
        runProjection: RUN_PROJECTION,
        runBytes,
        screenshotBytes
      },
      `Bearer ${deviceToken}`
    );
    await upload(
      {
        bundleId: '01J00000000000000000001002',
        runId: 'run-legacy',
        uploaderAccountId: 'acct-player'
      },
      `Bearer ${deviceToken}`
    );
    await upload(
      {
        bundleId: '01J00000000000000000001003',
        runId: 'run-absent',
        uploaderAccountId: 'acct-player',
        runProjection: RUN_PROJECTION
      },
      null
    );
    await upload(
      {
        bundleId: '01J00000000000000000001004',
        runId: 'run-mismatch',
        uploaderAccountId: 'acct-other',
        runProjection: RUN_PROJECTION
      },
      `Bearer ${deviceToken}`
    );
    await upload(
      {
        bundleId: '01J00000000000000000001005',
        runId: 'run-invalid',
        uploaderAccountId: 'acct-player',
        runProjection: RUN_PROJECTION
      },
      'Bearer bppd_not-a-valid-token'
    );

    const ingestEvents = logSpy.mock.calls
      .map(([line]) => JSON.parse(String(line)) as Record<string, unknown>)
      .filter((entry) => entry.event === 'bundle.ingest');
    expect(
      ingestEvents.map((event) => [event.shadow_auth, event.detail_stored])
    ).toEqual([
      ['match', true],
      ['match', false],
      ['absent', false],
      ['mismatch', false],
      ['invalid', false]
    ]);

    const copy = await env.BUNDLE_BUCKET.get(
      `account-runs/${accountId}/run-match.run`
    );
    expect(copy?.httpMetadata?.contentType).toBe('application/x-bpp-run-v6');
    expect(new Uint8Array(await copy!.arrayBuffer())).toEqual(runBytes);
    const listed = await env.BUNDLE_BUCKET.list({
      prefix: `account-runs/${accountId}/`
    });
    expect(listed.objects.map(({ key }) => key)).toEqual([
      `account-runs/${accountId}/run-match.run`
    ]);

    const summaries = await env.DB.prepare(
      `SELECT run_id, hero, victories, final_player_rating, detail_stored
       FROM run_summaries WHERE game_account_id = 'acct-player'`
    ).all();
    expect(summaries.results).toEqual([
      {
        run_id: 'run-match',
        hero: 'Vanessa',
        victories: 10,
        final_player_rating: 1450,
        detail_stored: 1
      }
    ]);

    // Ghost discovery shadow auth never changes the response.
    logSpy.mockClear();
    for (const [account, authorization] of [
      ['acct-player', `Bearer ${deviceToken}`],
      ['acct-player', null],
      ['acct-other', `Bearer ${deviceToken}`],
      ['acct-player', 'Bearer bppd_wrong']
    ] as const) {
      const headers = new Headers();
      if (authorization !== null) headers.set('Authorization', authorization);
      const response = await worker.fetch(
        new Request(
          `${MOD_ORIGIN}/ghost-battles?player_account_id=${account}`,
          { headers }
        ),
        harness.bindings
      );
      expect(response.status).toBe(200);
    }
    expect(
      logSpy.mock.calls
        .map(([line]) => JSON.parse(String(line)) as Record<string, unknown>)
        .filter((entry) => entry.event === 'ghost.discovery')
        .map((entry) => entry.shadow_auth)
    ).toEqual(['match', 'absent', 'mismatch', 'invalid']);

    // Bundle retention leaves Run Summaries and their detail in place.
    await pruneExpiredBundles(env.DB, Date.now() + 20 * 24 * 60 * 60 * 1000);
    expect(await count(`SELECT COUNT(*) AS c FROM bundles`)).toBe(0);
    expect(
      await count(
        `SELECT COUNT(*) AS c FROM run_summaries WHERE game_account_id = 'acct-player'`
      )
    ).toBe(1);
  });

  test('a failed Run copy fails the upload as retryable before the commit, and the retry stores it', async () => {
    const harness = makeHarness();
    const { deviceToken, accountId } = await linkedAccount(
      harness,
      'retry@example.com',
      'acct-retry'
    );
    const fixture = await makeBundleFixture({
      bundleId: '01J00000000000000000002001',
      runId: 'run-retry',
      uploaderAccountId: 'acct-retry',
      runProjection: RUN_PROJECTION,
      runBytes,
      screenshotBytes
    });
    fixture.headers.set('Authorization', `Bearer ${deviceToken}`);

    const failingBucket = new Proxy(env.BUNDLE_BUCKET, {
      get(target, property) {
        if (property === 'put') {
          return (key: string, ...rest: unknown[]) =>
            key.startsWith('account-runs/')
              ? Promise.reject(new Error('R2 is down'))
              : (target.put as (...args: unknown[]) => unknown)(key, ...rest);
        }
        const value = Reflect.get(target, property);
        return typeof value === 'function' ? value.bind(target) : value;
      }
    });
    vi.spyOn(console, 'log').mockImplementation(() => undefined);
    const failed = await worker.fetch(
      uploadRequest(fixture.body, fixture.headers),
      { ...harness.bindings, BUNDLE_BUCKET: failingBucket }
    );
    expect(failed.status).toBe(503);
    expect(await failed.json()).toMatchObject({
      error: { code: 'storage_unavailable', retryable: true }
    });
    expect(
      await count(
        `SELECT COUNT(*) AS c FROM bundles WHERE run_id = 'run-retry'`
      )
    ).toBe(0);
    expect(
      await count(
        `SELECT COUNT(*) AS c FROM run_summaries WHERE run_id = 'run-retry'`
      )
    ).toBe(0);

    const retried = await worker.fetch(
      uploadRequest(fixture.body, fixture.headers),
      harness.bindings
    );
    expect(retried.status).toBe(201);
    expect(
      await count(
        `SELECT COUNT(*) AS c FROM run_summaries WHERE run_id = 'run-retry' AND detail_stored = 1`
      )
    ).toBe(1);
    const copy = await env.BUNDLE_BUCKET.get(
      `account-runs/${accountId}/run-retry.run`
    );
    expect(new Uint8Array(await copy!.arrayBuffer())).toEqual(runBytes);
  });

  test('an unlink that lands during an upload leaves neither a Run Summary nor its copy', async () => {
    const harness = makeHarness();
    const { deviceToken, accountId } = await linkedAccount(
      harness,
      'unlink-race@example.com',
      'acct-unlink-race'
    );
    const fixture = await makeBundleFixture({
      bundleId: '01J00000000000000000009001',
      runId: 'run-unlink-race',
      uploaderAccountId: 'acct-unlink-race',
      runProjection: RUN_PROJECTION,
      runBytes,
      screenshotBytes
    });
    fixture.headers.set('Authorization', `Bearer ${deviceToken}`);

    // Unlink clears R2 first, then D1. Here its D1 half commits after this
    // upload authenticated and copied, right before the upload's commit batch.
    const racingDb = interleavedDb(/^$/, () =>
      env.DB.prepare(
        `UPDATE bpp_accounts SET game_account_id = NULL WHERE account_id = ?1`
      )
        .bind(accountId)
        .run()
    );
    const response = await worker.fetch(
      uploadRequest(fixture.body, fixture.headers),
      { ...harness.bindings, DB: racingDb }
    );
    expect(response.status).toBe(201);
    expect(
      await count(
        `SELECT COUNT(*) AS c FROM run_summaries WHERE run_id = 'run-unlink-race'`
      )
    ).toBe(0);
    expect(
      await env.BUNDLE_BUCKET.head(
        `account-runs/${accountId}/run-unlink-race.run`
      )
    ).toBeNull();
  });
});

describe('website account API', () => {
  async function seedRuns(gameAccountId: string): Promise<void> {
    const statements = [];
    for (let index = 0; index < 51; index += 1) {
      statements.push(
        env.DB.prepare(
          `INSERT INTO run_summaries (
            run_id, game_account_id, hero, victories, losses, final_player_rank,
            final_player_rating, final_player_rating_delta, game_mode, ended_at_ms, stored_at_ms,
            detail_stored
          ) VALUES (?1, ?2, ?3, ?4, 1, 'Legend', ?5, 15, 'Ranked', ?6, 1, 0)`
        ).bind(
          `${gameAccountId}-run-${String(index).padStart(3, '0')}`,
          gameAccountId,
          index % 3 === 0 ? 'Dooley' : 'Vanessa',
          index % 11,
          1000 + index,
          1_785_600_000_000 + index * 1000
        )
      );
    }
    statements.push(
      env.DB.prepare(
        `INSERT INTO run_summaries (
          run_id, game_account_id, hero, victories, losses, final_player_rank,
          final_player_rating, final_player_rating_delta, game_mode, ended_at_ms, stored_at_ms
        ) VALUES (?2, ?1, NULL, NULL, 3, 'Gold', 900, -10, 'Ranked', NULL, 1)`
      ).bind(gameAccountId, `${gameAccountId}-run-null-time`)
    );
    await env.DB.batch(statements);
  }

  test('serves /api/* only through WebApi, checks Origin on POST, and answers 404 and 405', async () => {
    const harness = makeHarness();
    expect(
      (
        await worker.fetch(
          new Request(`${MOD_ORIGIN}/api/me`),
          harness.bindings
        )
      ).status
    ).toBe(404);
    expect(typeof WebApi).toBe('function');

    const noOrigin = await handleWebApiRequest(
      new Request(`${SITE_ORIGIN}/api/login`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email: 'a@example.com', password: PASSWORD })
      }),
      harness.bindings
    );
    expect(noOrigin.status).toBe(403);
    expect(
      (
        await handleWebApiRequest(
          new Request(`${SITE_ORIGIN}/api/login`, {
            method: 'POST',
            headers: { Origin: 'https://evil.example' },
            body: '{}'
          }),
          harness.bindings
        )
      ).status
    ).toBe(403);
    // A token solved on the preview host passes there.
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(
      Response.json({
        success: true,
        hostname: new URL(PREVIEW_ORIGIN).hostname,
        action: 'register'
      })
    );
    expect(
      (
        await webPost(
          harness,
          '/api/register/start',
          { email: 'preview@example.com' },
          undefined,
          PREVIEW_ORIGIN
        )
      ).status
    ).toBe(200);

    for (const removed of [
      '/api/login/start',
      '/api/login/confirm',
      '/api/unbind'
    ]) {
      expect((await webPost(harness, removed)).status).toBe(404);
    }
    expect((await webGet(harness, '/api/runs/run-1/extra')).status).toBe(404);
    const wrongMethod = await webPost(harness, '/api/runs/run-1/payload');
    expect(wrongMethod.status).toBe(405);
    expect(wrongMethod.headers.get('Allow')).toBe('GET');
    expect((await webGet(harness, '/api/me')).status).toBe(401);
  });

  test('/api/me aggregates the linked account and /api/runs pages its Run Summaries', async () => {
    const harness = makeHarness();
    const { cookie } = await linkedAccount(
      harness,
      'stats@example.com',
      'acct-stats',
      'Stats'
    );
    await seedRuns('acct-stats');

    const me = await (
      await webGet(harness, '/api/me', cookie)
    ).json<{
      game: { user_name: string };
      stats: {
        runs: number;
        victories: number;
        ten_win_runs: number;
        heroes: Array<Record<string, unknown>>;
        rating: Array<{ ended_at_ms: number; rating: number; rank: string }>;
      };
    }>();
    expect(me.game.user_name).toBe('Stats');
    const victories = Array.from({ length: 51 }, (_, index) => index % 11);
    expect(me.stats.runs).toBe(52);
    expect(me.stats.victories).toBe(victories.reduce((a, b) => a + b, 0));
    expect(me.stats.ten_win_runs).toBe(victories.filter((v) => v >= 10).length);
    const dooley = victories.filter((_, index) => index % 3 === 0);
    expect(me.stats.heroes).toEqual([
      {
        hero: 'Vanessa',
        runs: 34,
        avg_victories: expect.any(Number),
        best_victories: 10
      },
      {
        hero: 'Dooley',
        runs: 17,
        avg_victories: dooley.reduce((a, b) => a + b, 0) / dooley.length,
        best_victories: Math.max(...dooley)
      }
    ]);
    expect(me.stats.rating).toHaveLength(30);
    expect(me.stats.rating[0]).toEqual({
      ended_at_ms: 1_785_600_000_000 + 21_000,
      rating: 1021,
      rank: 'Legend'
    });
    expect(me.stats.rating.at(-1)?.rating).toBe(1050);

    const page1 = await (
      await webGet(harness, '/api/runs', cookie)
    ).json<{
      runs: Array<{ run_id: string; has_detail: boolean }>;
      next_cursor: string | null;
    }>();
    expect(page1.runs).toHaveLength(50);
    expect(page1.runs[0]).toMatchObject({
      run_id: 'acct-stats-run-050',
      has_detail: false
    });
    const page2 = await (
      await webGet(
        harness,
        `/api/runs?cursor=${encodeURIComponent(page1.next_cursor ?? '')}`,
        cookie
      )
    ).json<{ runs: Array<{ run_id: string }>; next_cursor: string | null }>();
    expect(page2.runs.map(({ run_id }) => run_id)).toEqual([
      'acct-stats-run-000',
      'acct-stats-run-null-time'
    ]);
    expect(page2.next_cursor).toBeNull();
    expect((await webGet(harness, '/api/runs?other=1', cookie)).status).toBe(
      400
    );

    const unlinkedCookie = await register(harness, 'empty@example.com');
    expect(
      await (await webGet(harness, '/api/runs', unlinkedCookie)).json()
    ).toEqual({ runs: [], next_cursor: null });
  });

  test('/api/runs/<id> and its payload serve only the signed-in owner', async () => {
    const harness = makeHarness();
    const owner = await linkedAccount(harness, 'owner@example.com', 'acct-own');
    const stranger = await linkedAccount(
      harness,
      'stranger@example.com',
      'acct-stranger'
    );
    const runBytes = new Uint8Array([0x1f, 0x8b, 0x08, 9, 8, 7]);
    const fixture = await makeBundleFixture({
      bundleId: '01J00000000000000000003001',
      runId: 'run-owned',
      uploaderAccountId: 'acct-own',
      runProjection: RUN_PROJECTION,
      runBytes
    });
    fixture.headers.set('Authorization', `Bearer ${owner.deviceToken}`);
    vi.spyOn(console, 'log').mockImplementation(() => undefined);
    expect(
      (
        await worker.fetch(
          uploadRequest(fixture.body, fixture.headers),
          harness.bindings
        )
      ).status
    ).toBe(201);
    await env.DB.prepare(
      `INSERT INTO run_summaries (run_id, game_account_id, hero, stored_at_ms)
       VALUES ('run-summary-only', 'acct-own', 'Mak', 1)`
    ).run();

    const run = await webGet(harness, '/api/runs/run-owned', owner.cookie);
    expect(run.status).toBe(200);
    expect(await run.json()).toEqual({
      run: {
        run_id: 'run-owned',
        hero: 'Vanessa',
        victories: 10,
        losses: 2,
        final_player_rank: 'Legend',
        final_player_rating: 1450,
        final_player_rating_delta: 28,
        game_mode: 'Ranked',
        ended_at_ms: 1_785_625_000_000,
        has_detail: true
      }
    });

    const payload = await webGet(
      harness,
      '/api/runs/run-owned/payload',
      owner.cookie
    );
    expect(payload.status).toBe(200);
    expect(payload.headers.get('Content-Type')).toBe(
      'application/x-bpp-run-v6'
    );
    expect(payload.headers.get('Cache-Control')).toBe('private, no-store');
    expect(new Uint8Array(await payload.arrayBuffer())).toEqual(runBytes);

    for (const [path, cookie] of [
      ['/api/runs/run-owned', stranger.cookie],
      ['/api/runs/run-owned/payload', stranger.cookie],
      ['/api/runs/run-summary-only/payload', owner.cookie],
      ['/api/runs/run-missing', owner.cookie]
    ] as const) {
      const response = await webGet(harness, path, cookie);
      expect(response.status, path).toBe(404);
      expect(await errorCode(response)).toBe('not_found');
    }
    expect((await webGet(harness, '/api/runs/run-owned')).status).toBe(401);
  });

  test('unlink deletes the Run detail objects first, then the game link, its Runs, tokens, and code', async () => {
    const harness = makeHarness();
    const linked = await linkedAccount(harness, 'unlink@example.com', 'acct-u');
    const other = await linkedAccount(harness, 'keep@example.com', 'acct-k');
    await seedRuns('acct-u');
    await env.DB.prepare(
      `INSERT INTO run_summaries (run_id, game_account_id, stored_at_ms) VALUES ('run-kept', 'acct-k', 1)`
    ).run();
    for (let index = 0; index < 3; index += 1) {
      await env.BUNDLE_BUCKET.put(
        `account-runs/${linked.accountId}/run-${index}.run`,
        new Uint8Array([index])
      );
    }
    await env.BUNDLE_BUCKET.put(
      `account-runs/${other.accountId}/run-kept.run`,
      new Uint8Array([9])
    );

    const unlinked = await webPost(harness, '/api/unlink', {}, linked.cookie);
    expect(unlinked.status).toBe(200);
    expect(await unlinked.json()).toEqual({ ok: true });
    expect(unlinked.headers.get('Set-Cookie')).toBeNull();

    for (const [accountId, keys] of [
      [linked.accountId, []],
      [other.accountId, [`account-runs/${other.accountId}/run-kept.run`]]
    ] as const) {
      const listed = await env.BUNDLE_BUCKET.list({
        prefix: `account-runs/${accountId}/`
      });
      expect(listed.objects.map(({ key }) => key)).toEqual(keys);
    }
    const account = await env.DB.prepare(
      `SELECT game_account_id, game_user_name, linked_at_ms FROM bpp_accounts WHERE account_id = ?1`
    )
      .bind(linked.accountId)
      .first();
    expect(account).toEqual({
      game_account_id: null,
      game_user_name: null,
      linked_at_ms: null
    });
    expect(
      await count(
        `SELECT COUNT(*) AS c FROM run_summaries WHERE game_account_id = 'acct-u'`
      )
    ).toBe(0);
    expect(
      await count(
        `SELECT COUNT(*) AS c FROM run_summaries WHERE game_account_id = 'acct-k'`
      )
    ).toBe(1);
    expect(
      await count(
        `SELECT COUNT(*) AS c FROM bpp_device_tokens WHERE account_id = ?1`,
        linked.accountId
      )
    ).toBe(0);

    // The website session stays signed in; the mod's token is revoked.
    const me = await webGet(harness, '/api/me', linked.cookie);
    expect(me.status).toBe(200);
    expect(await me.json()).toMatchObject({ game: null, stats: { runs: 0 } });
    expect(
      (
        await worker.fetch(
          new Request(`${MOD_ORIGIN}/account/me`, {
            headers: { Authorization: `Bearer ${linked.deviceToken}` }
          }),
          harness.bindings
        )
      ).status
    ).toBe(401);
    // A fresh Bind Code can link again.
    await newBindCode(harness, linked.cookie);
  });

  test('account delete checks the password, then removes R2 objects, rows, and the cookie', async () => {
    const harness = makeHarness();
    const linked = await linkedAccount(harness, 'delete@example.com', 'acct-d');
    await seedRuns('acct-d');
    await env.BUNDLE_BUCKET.put(
      `account-runs/${linked.accountId}/run-000.run`,
      new Uint8Array([1])
    );
    const prefix = `account-runs/${linked.accountId}/`;
    await webPost(harness, '/api/password/reset/start', {
      email: 'delete@example.com'
    });

    const wrong = await webPost(
      harness,
      '/api/account/delete',
      { password: 'not the password' },
      linked.cookie
    );
    expect(wrong.status).toBe(401);
    expect(await errorCode(wrong)).toBe('invalid_credentials');
    expect((await env.BUNDLE_BUCKET.list({ prefix })).objects).toHaveLength(1);

    const deleted = await webPost(
      harness,
      '/api/account/delete',
      { password: PASSWORD },
      linked.cookie
    );
    expect(deleted.status).toBe(200);
    expect(deleted.headers.get('Set-Cookie')).toContain('Max-Age=0');
    expect((await env.BUNDLE_BUCKET.list({ prefix })).objects).toHaveLength(0);
    for (const [table, column, value] of [
      ['bpp_accounts', 'account_id', linked.accountId],
      ['bpp_web_sessions', 'account_id', linked.accountId],
      ['bpp_device_tokens', 'account_id', linked.accountId],
      ['bpp_bind_codes', 'account_id', linked.accountId],
      ['bpp_email_challenges', 'email', 'delete@example.com'],
      ['run_summaries', 'game_account_id', 'acct-d']
    ]) {
      expect(
        await count(
          `SELECT COUNT(*) AS c FROM ${table} WHERE ${column} = ?1`,
          value
        ),
        table
      ).toBe(0);
    }
    expect((await webGet(harness, '/api/me', linked.cookie)).status).toBe(401);
  });

  test('logout deletes the session and clears the cookie', async () => {
    const harness = makeHarness();
    const cookie = await register(harness, 'logout@example.com');
    const out = await webPost(harness, '/api/logout', {}, cookie);
    expect(out.status).toBe(200);
    expect(out.headers.get('Set-Cookie')).toContain('Max-Age=0');
    expect((await webGet(harness, '/api/me', cookie)).status).toBe(401);
  });
});

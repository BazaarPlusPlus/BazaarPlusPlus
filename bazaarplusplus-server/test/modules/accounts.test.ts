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

interface CapturedEmail {
  to: string;
  subject: string;
  text: string;
  html: string;
}

function makeAccountEnv(sent: CapturedEmail[]): Env {
  return {
    ...env,
    ACCOUNT_RATE_LIMITER: {
      limit: async () => ({ success: true })
    },
    EMAIL: {
      send: async (message: unknown) => {
        const builder = message as {
          to: string;
          subject: string;
          text?: string;
          html?: string;
        };
        sent.push({
          to: builder.to,
          subject: builder.subject,
          text: builder.text ?? '',
          html: builder.html ?? ''
        });
        return { messageId: `msg-${sent.length}` };
      }
    }
  };
}

function extractSixDigitCode(text: string): string {
  const match = /验证码：([0-9]{6})/.exec(text);
  if (match === null) {
    throw new Error(`No verification code found in email: ${text}`);
  }
  return match[1];
}

function jsonRequest(
  origin: string,
  path: string,
  body: unknown,
  headers: Record<string, string> = {}
): Request {
  return new Request(`${origin}${path}`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...headers
    },
    body: JSON.stringify(body)
  });
}

function mockOfficialSummary(
  id: string,
  userName: string,
  email = 'off@ex.com'
) {
  return new Response(
    JSON.stringify({
      id,
      userName,
      email,
      hasViewedDailySpecial: true
    }),
    { status: 200, headers: { 'Content-Type': 'application/json' } }
  );
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe('Mod API BPP Account flows', () => {
  test('bind/start -> bind/confirm -> GET /account/me -> POST /account/devices works end to end', async () => {
    const sent: CapturedEmail[] = [];
    const bindings = makeAccountEnv(sent);
    const logSpy = vi.spyOn(console, 'log').mockImplementation(() => undefined);
    const errSpy = vi
      .spyOn(console, 'error')
      .mockImplementation(() => undefined);

    const fetchSpy = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(
        mockOfficialSummary(
          'acct-alpha',
          'OfficialAlice',
          'official@secret.com'
        )
      );

    const startRes = await worker.fetch(
      jsonRequest(MOD_ORIGIN, '/account/bind/start', {
        email: '  Alice@Example.COM  ',
        game_token: 'jwe.live.token.secret.value',
        game_account_id: 'acct-alpha',
        platform: 'Steam',
        flavor: 'Paid'
      }),
      bindings
    );

    expect(startRes.status).toBe(200);
    expect(fetchSpy).toHaveBeenCalledOnce();
    const [calledUrl, calledInit] = fetchSpy.mock.calls[0] as [
      string,
      RequestInit
    ];
    expect(calledUrl).toBe(OFFICIAL_ACCOUNT_SUMMARY_URL);
    const calledHeaders = new Headers(calledInit.headers);
    expect(calledHeaders.get('Authorization')).toBe(
      'Bearer jwe.live.token.secret.value'
    );
    expect(calledHeaders.get('X-Platform')).toBe('Steam');
    expect(calledHeaders.get('X-ClientFlavor')).toBe('Paid');

    const startBody = await startRes.json<{
      pending_id: string;
      user_name: string;
      email_masked: string;
      expires_at_ms: number;
    }>();
    expect(startBody.user_name).toBe('OfficialAlice');
    expect(startBody.email_masked).toBe('a***@example.com');
    expect(sent).toHaveLength(1);
    expect(sent[0].to).toBe('alice@example.com');
    expect(sent[0].text).toContain('游戏账号：OfficialAlice');
    const code = extractSixDigitCode(sent[0].text);
    expect(sent[0].subject).toBe(`BazaarPlusPlus 绑定验证码：${code}`);
    expect(sent[0].html).toContain(`>${code}</td>`);

    // Wrong code increments attempts and rejects.
    const wrongCode = code === '000000' ? '111111' : '000000';
    const badConfirm = await worker.fetch(
      jsonRequest(MOD_ORIGIN, '/account/bind/confirm', {
        pending_id: startBody.pending_id,
        code: wrongCode
      }),
      bindings
    );
    expect(badConfirm.status).toBe(401);
    expect(await badConfirm.json()).toMatchObject({
      error: { code: 'invalid_verification_code' }
    });

    // Correct code commits 1:1 binding and issues a bppd_ device token.
    const confirmRes = await worker.fetch(
      jsonRequest(MOD_ORIGIN, '/account/bind/confirm', {
        pending_id: startBody.pending_id,
        code
      }),
      bindings
    );
    expect(confirmRes.status).toBe(200);
    const confirmBody = await confirmRes.json<{
      device_token: string;
      user_name: string;
      email_masked: string;
    }>();
    expect(confirmBody.device_token).toMatch(/^bppd_[A-Za-z0-9_-]{43}$/);
    expect(confirmBody.user_name).toBe('OfficialAlice');
    expect(confirmBody.email_masked).toBe('a***@example.com');

    // Replaying the same pending_id fails.
    const replayRes = await worker.fetch(
      jsonRequest(MOD_ORIGIN, '/account/bind/confirm', {
        pending_id: startBody.pending_id,
        code
      }),
      bindings
    );
    expect(replayRes.status).toBe(401);

    // GET /account/me returns the bound account with the device token and 401 without.
    const meRes = await worker.fetch(
      new Request(`${MOD_ORIGIN}/account/me`, {
        headers: { Authorization: `Bearer ${confirmBody.device_token}` }
      }),
      bindings
    );
    expect(meRes.status).toBe(200);
    expect(await meRes.json()).toEqual({
      user_name: 'OfficialAlice',
      email_masked: 'a***@example.com'
    });
    expect(
      (await worker.fetch(new Request(`${MOD_ORIGIN}/account/me`), bindings))
        .status
    ).toBe(401);

    // Raw device token is not stored in D1.
    const tokenRow = await env.DB.prepare(
      `SELECT token_hash FROM bpp_device_tokens WHERE game_account_id = 'acct-alpha'`
    ).first<{ token_hash: string }>();
    expect(tokenRow?.token_hash).toMatch(/^[0-9a-f]{64}$/);
    expect(tokenRow?.token_hash).not.toBe(confirmBody.device_token);

    // Second device activation via POST /account/devices updates user_name and issues a second token.
    fetchSpy.mockResolvedValueOnce(
      mockOfficialSummary('acct-alpha', 'RenamedAlice')
    );
    const devRes = await worker.fetch(
      jsonRequest(MOD_ORIGIN, '/account/devices', {
        game_token: 'jwe.second.device.token',
        game_account_id: 'acct-alpha',
        platform: 'Steam',
        flavor: 'Paid'
      }),
      bindings
    );
    expect(devRes.status).toBe(200);
    const devBody = await devRes.json<{
      device_token: string;
      user_name: string;
      email_masked: string;
    }>();
    expect(devBody.device_token).toMatch(/^bppd_[A-Za-z0-9_-]{43}$/);
    expect(devBody.device_token).not.toBe(confirmBody.device_token);
    expect(devBody.user_name).toBe('RenamedAlice');

    // Never log game tokens, emails, or verification codes.
    const combinedLogs = JSON.stringify([
      ...logSpy.mock.calls,
      ...errSpy.mock.calls
    ]);
    expect(combinedLogs).not.toContain('jwe.live.token.secret.value');
    expect(combinedLogs).not.toContain('alice@example.com');
    expect(combinedLogs).not.toContain('official@secret.com');
    expect(combinedLogs).not.toContain(code);
  });

  test('enforces 5-attempt lock, 1:1 conflicts with email_masked detail, and hourly email limit', async () => {
    const sent: CapturedEmail[] = [];
    const bindings = makeAccountEnv(sent);
    const fetchSpy = vi.spyOn(globalThis, 'fetch');

    // Hourly limit: 5 sends succeed, 6th gets 429 rate_limited.
    let lastPendingId = '';
    for (let i = 0; i < 5; i += 1) {
      fetchSpy.mockResolvedValueOnce(mockOfficialSummary('acct-rate', 'RateU'));
      const res = await worker.fetch(
        jsonRequest(MOD_ORIGIN, '/account/bind/start', {
          email: 'hourly@example.com',
          game_token: 'tok',
          game_account_id: 'acct-rate',
          platform: 'Steam',
          flavor: 'Paid'
        }),
        bindings
      );
      expect(res.status).toBe(200);
      lastPendingId = (await res.json<{ pending_id: string }>()).pending_id;
    }

    fetchSpy.mockResolvedValueOnce(mockOfficialSummary('acct-rate', 'RateU'));
    const sixth = await worker.fetch(
      jsonRequest(MOD_ORIGIN, '/account/bind/start', {
        email: 'HOURLY@example.com',
        game_token: 'tok',
        game_account_id: 'acct-rate',
        platform: 'Steam',
        flavor: 'Paid'
      }),
      bindings
    );
    expect(sixth.status).toBe(429);
    expect(sixth.headers.get('retry-after')).toBe('60');

    // 5 wrong attempts permanently lock out even the correct code.
    const correctCode = extractSixDigitCode(sent[4].text);
    const wrongCode = correctCode === '000000' ? '111111' : '000000';
    for (let i = 0; i < 5; i += 1) {
      const bad = await worker.fetch(
        jsonRequest(MOD_ORIGIN, '/account/bind/confirm', {
          pending_id: lastPendingId,
          code: wrongCode
        }),
        bindings
      );
      expect(bad.status).toBe(401);
    }
    const afterLock = await worker.fetch(
      jsonRequest(MOD_ORIGIN, '/account/bind/confirm', {
        pending_id: lastPendingId,
        code: correctCode
      }),
      bindings
    );
    expect(afterLock.status).toBe(401);

    // Bound game account returns 409 game_account_already_bound with email_masked.
    await env.DB.prepare(
      `INSERT INTO bpp_accounts (game_account_id, email, user_name, created_at_ms, updated_at_ms)
       VALUES ('acct-one', 'owner@example.com', 'Owner', 1000, 1000)`
    ).run();
    fetchSpy.mockResolvedValueOnce(mockOfficialSummary('acct-one', 'Owner'));
    const gameConflict = await worker.fetch(
      jsonRequest(MOD_ORIGIN, '/account/bind/start', {
        email: 'other@example.com',
        game_token: 'tok',
        game_account_id: 'acct-one',
        platform: 'Steam',
        flavor: 'Paid'
      }),
      bindings
    );
    expect(gameConflict.status).toBe(409);
    expect(await gameConflict.json()).toMatchObject({
      error: {
        code: 'game_account_already_bound',
        details: { email_masked: 'o***@example.com' }
      }
    });
  });
});

describe('shadow authentication, run summaries, retention, and WebApi', () => {
  test('writes run_summaries only for matching device token + non-empty projection.run and preserves across 15-day retention', async () => {
    const sent: CapturedEmail[] = [];
    const bindings = makeAccountEnv(sent);
    const fetchSpy = vi.spyOn(globalThis, 'fetch');

    // Bind acct-player and obtain device token.
    fetchSpy.mockResolvedValueOnce(
      mockOfficialSummary('acct-player', 'PlayerOne')
    );
    const startRes = await worker.fetch(
      jsonRequest(MOD_ORIGIN, '/account/bind/start', {
        email: 'player@example.com',
        game_token: 'tok-p1',
        game_account_id: 'acct-player',
        platform: 'Steam',
        flavor: 'Paid'
      }),
      bindings
    );
    const { pending_id } = await startRes.json<{ pending_id: string }>();
    const confirmRes = await worker.fetch(
      jsonRequest(MOD_ORIGIN, '/account/bind/confirm', {
        pending_id,
        code: extractSixDigitCode(sent[0].text)
      }),
      bindings
    );
    const { device_token: deviceToken } = await confirmRes.json<{
      device_token: string;
    }>();

    const logSpy = vi.spyOn(console, 'log').mockImplementation(() => undefined);

    const runProjection = {
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

    // 1. Matching device token + projection.run -> writes run_summaries, logs shadow_auth: 'match'.
    const matchBundle = await makeBundleFixture({
      bundleId: '01J00000000000000000001001',
      runId: 'run-match',
      uploaderAccountId: 'acct-player',
      runProjection
    });
    matchBundle.headers.set('Authorization', `Bearer ${deviceToken}`);
    expect(
      (
        await worker.fetch(
          uploadRequest(matchBundle.body, matchBundle.headers),
          bindings
        )
      ).status
    ).toBe(201);

    // 2. Matching device token + legacy {} projection.run -> accepted, NO summary row.
    const legacyBundle = await makeBundleFixture({
      bundleId: '01J00000000000000000001002',
      runId: 'run-legacy',
      uploaderAccountId: 'acct-player'
    });
    legacyBundle.headers.set('Authorization', `Bearer ${deviceToken}`);
    expect(
      (
        await worker.fetch(
          uploadRequest(legacyBundle.body, legacyBundle.headers),
          bindings
        )
      ).status
    ).toBe(201);

    // 3. Absent token -> accepted (shadow_auth: 'absent'), NO summary row.
    const absentBundle = await makeBundleFixture({
      bundleId: '01J00000000000000000001003',
      runId: 'run-absent',
      uploaderAccountId: 'acct-player',
      runProjection
    });
    expect(
      (
        await worker.fetch(
          uploadRequest(absentBundle.body, absentBundle.headers),
          bindings
        )
      ).status
    ).toBe(201);

    // 4. Mismatched token -> accepted (shadow_auth: 'mismatch'), NO summary row.
    const mismatchBundle = await makeBundleFixture({
      bundleId: '01J00000000000000000001004',
      runId: 'run-mismatch',
      uploaderAccountId: 'acct-other',
      runProjection
    });
    mismatchBundle.headers.set('Authorization', `Bearer ${deviceToken}`);
    expect(
      (
        await worker.fetch(
          uploadRequest(mismatchBundle.body, mismatchBundle.headers),
          bindings
        )
      ).status
    ).toBe(201);

    // 5. Invalid token -> accepted (shadow_auth: 'invalid'), NO summary row.
    const invalidBundle = await makeBundleFixture({
      bundleId: '01J00000000000000000001005',
      runId: 'run-invalid',
      uploaderAccountId: 'acct-player',
      runProjection
    });
    invalidBundle.headers.set('Authorization', 'Bearer bppd_not-a-valid-token');
    expect(
      (
        await worker.fetch(
          uploadRequest(invalidBundle.body, invalidBundle.headers),
          bindings
        )
      ).status
    ).toBe(201);

    const ingestEvents = logSpy.mock.calls
      .map(([line]) => JSON.parse(String(line)) as Record<string, unknown>)
      .filter((entry) => entry.event === 'bundle.ingest');
    expect(ingestEvents.map((e) => e.shadow_auth)).toEqual([
      'match',
      'match',
      'absent',
      'mismatch',
      'invalid'
    ]);

    // Ghost discovery shadow auth logs match / absent / mismatch / invalid without altering 200 response.
    logSpy.mockClear();
    for (const [acct, auth] of [
      ['acct-player', `Bearer ${deviceToken}`],
      ['acct-player', null],
      ['acct-other', `Bearer ${deviceToken}`],
      ['acct-player', 'Bearer bppd_wrong']
    ] as const) {
      const headers = new Headers();
      if (auth !== null) headers.set('Authorization', auth);
      const res = await worker.fetch(
        new Request(`${MOD_ORIGIN}/ghost-battles?player_account_id=${acct}`, {
          headers
        }),
        bindings
      );
      expect(res.status).toBe(200);
    }
    const ghostEvents = logSpy.mock.calls
      .map(([line]) => JSON.parse(String(line)) as Record<string, unknown>)
      .filter((entry) => entry.event === 'ghost.discovery');
    expect(ghostEvents.map((e) => e.shadow_auth)).toEqual([
      'match',
      'absent',
      'mismatch',
      'invalid'
    ]);

    // Only run-match was written to run_summaries.
    const summaries = await env.DB.prepare(
      `SELECT run_id, hero, victories, losses, final_player_rank, final_player_rating, final_player_rating_delta
       FROM run_summaries`
    ).all();
    expect(summaries.results).toEqual([
      {
        run_id: 'run-match',
        hero: 'Vanessa',
        victories: 10,
        losses: 2,
        final_player_rank: 'Legend',
        final_player_rating: 1450,
        final_player_rating_delta: 28
      }
    ]);

    // 15-day retention deletes bundles, but preserves run_summaries and bpp_accounts.
    await pruneExpiredBundles(env.DB, Date.now() + 20 * 24 * 60 * 60 * 1000);
    const remainingBundles = await env.DB.prepare(
      `SELECT COUNT(*) AS count FROM bundles`
    ).first<{ count: number }>();
    const remainingSummaries = await env.DB.prepare(
      `SELECT COUNT(*) AS count FROM run_summaries`
    ).first<{ count: number }>();
    expect(Number(remainingBundles?.count)).toBe(0);
    expect(Number(remainingSummaries?.count)).toBe(1);
  });

  test('WebApi entrypoint serves /api/* with Origin checks, HttpOnly cookie, paged /api/me, logout, and unbind', async () => {
    const sent: CapturedEmail[] = [];
    const bindings = makeAccountEnv(sent);

    // Default mod-api-v6 fetch does not expose /api/*.
    expect(
      (await worker.fetch(new Request(`${MOD_ORIGIN}/api/me`), bindings)).status
    ).toBe(404);
    expect(typeof WebApi).toBe('function');

    // Seed bound account, device token, and 52 run summaries (including a null ended_at_ms row).
    const now = Date.now();
    await env.DB.batch([
      env.DB.prepare(
        `INSERT INTO bpp_accounts (game_account_id, email, user_name, created_at_ms, updated_at_ms)
         VALUES ('acct-web', 'web@example.com', 'Web<b>&Player', ?1, ?1)`
      ).bind(now),
      env.DB.prepare(
        `INSERT INTO bpp_device_tokens (token_hash, game_account_id, created_at_ms)
         VALUES (?1, 'acct-web', ?2)`
      ).bind('a'.repeat(64), now)
    ]);
    for (let i = 0; i < 51; i += 1) {
      await env.DB.prepare(
        `INSERT INTO run_summaries (
          run_id, game_account_id, hero, victories, losses, final_player_rank,
          final_player_rating, final_player_rating_delta, game_mode, ended_at_ms, stored_at_ms
        ) VALUES (?1, 'acct-web', 'Vanessa', 10, 1, 'Legend', 1500, 15, 'Ranked', ?2, ?3)`
      )
        .bind(
          `run-${String(i).padStart(3, '0')}`,
          1_785_600_000_000 + i * 1000,
          now
        )
        .run();
    }
    await env.DB.prepare(
      `INSERT INTO run_summaries (
        run_id, game_account_id, hero, victories, losses, final_player_rank,
        final_player_rating, final_player_rating_delta, game_mode, ended_at_ms, stored_at_ms
      ) VALUES ('run-null-time', 'acct-web', 'Dooley', 4, 3, 'Gold', 900, -10, 'Ranked', NULL, ?1)`
    )
      .bind(now)
      .run();

    // POST without Origin or with cross-site Origin -> 403.
    expect(
      (
        await handleWebApiRequest(
          jsonRequest(SITE_ORIGIN, '/api/login/start', {
            email: 'web@example.com'
          }),
          bindings
        )
      ).status
    ).toBe(403);
    expect(
      (
        await handleWebApiRequest(
          jsonRequest(
            SITE_ORIGIN,
            '/api/login/start',
            { email: 'web@example.com' },
            { Origin: 'https://evil.example' }
          ),
          bindings
        )
      ).status
    ).toBe(403);

    // Unbound email still returns 200 without sending email.
    const unboundStart = await handleWebApiRequest(
      jsonRequest(
        PREVIEW_ORIGIN,
        '/api/login/start',
        { email: 'nobody@example.com' },
        { Origin: PREVIEW_ORIGIN }
      ),
      bindings
    );
    expect(unboundStart.status).toBe(200);
    expect(sent).toHaveLength(0);

    // Bound email sends code and returns 200.
    const boundStart = await handleWebApiRequest(
      jsonRequest(
        SITE_ORIGIN,
        '/api/login/start',
        { email: 'WEB@example.com' },
        { Origin: SITE_ORIGIN }
      ),
      bindings
    );
    expect(boundStart.status).toBe(200);
    expect(sent).toHaveLength(1);
    const loginCode = extractSixDigitCode(sent[0].text);
    expect(sent[0].html).toContain(`>${loginCode}</td>`);
    expect(sent[0].html).toContain('>Web&lt;b&gt;&amp;Player</span>');
    expect(sent[0].html).not.toContain('<b>&Player');

    // Confirm sets HttpOnly, Secure, SameSite=Lax cookie.
    const confirmRes = await handleWebApiRequest(
      jsonRequest(
        SITE_ORIGIN,
        '/api/login/confirm',
        { email: 'web@example.com', code: loginCode },
        { Origin: SITE_ORIGIN }
      ),
      bindings
    );
    expect(confirmRes.status).toBe(200);
    const setCookie = confirmRes.headers.get('Set-Cookie') ?? '';
    expect(setCookie).toContain('__Host-bpp_session=');
    expect(setCookie).toContain('HttpOnly');
    expect(setCookie).toContain('Secure');
    expect(setCookie).toContain('SameSite=Lax');
    const cookieHeader = setCookie.split(';')[0];

    // GET /api/me page 1 (50 rows) + page 2 (2 rows, ending with null ended_at_ms).
    const page1Res = await handleWebApiRequest(
      new Request(`${SITE_ORIGIN}/api/me`, {
        headers: { Cookie: cookieHeader }
      }),
      bindings
    );
    expect(page1Res.status).toBe(200);
    const page1 = await page1Res.json<{
      user_name: string;
      email_masked: string;
      runs: Array<{ run_id: string; ended_at_ms: number | null }>;
      next_cursor: string | null;
    }>();
    expect(page1.user_name).toBe('Web<b>&Player');
    expect(page1.email_masked).toBe('w***@example.com');
    expect(page1.runs).toHaveLength(50);
    expect(page1.runs[0].run_id).toBe('run-050');
    expect(page1.next_cursor).toBeTruthy();

    const page2Res = await handleWebApiRequest(
      new Request(
        `${SITE_ORIGIN}/api/me?cursor=${encodeURIComponent(page1.next_cursor ?? '')}`,
        { headers: { Cookie: cookieHeader } }
      ),
      bindings
    );
    expect(page2Res.status).toBe(200);
    const page2 = await page2Res.json<{
      runs: Array<{ run_id: string; ended_at_ms: number | null }>;
      next_cursor: string | null;
    }>();
    expect(page2.runs.map((r) => r.run_id)).toEqual([
      'run-000',
      'run-null-time'
    ]);
    expect(page2.next_cursor).toBeNull();

    // Unbind deletes account, device tokens, sessions, and run summaries.
    const unbindRes = await handleWebApiRequest(
      jsonRequest(
        SITE_ORIGIN,
        '/api/unbind',
        {},
        { Origin: SITE_ORIGIN, Cookie: cookieHeader }
      ),
      bindings
    );
    expect(unbindRes.status).toBe(200);
    expect(unbindRes.headers.get('Set-Cookie')).toContain('Max-Age=0');

    expect(
      (
        await handleWebApiRequest(
          new Request(`${SITE_ORIGIN}/api/me`, {
            headers: { Cookie: cookieHeader }
          }),
          bindings
        )
      ).status
    ).toBe(401);

    const counts = await Promise.all([
      env.DB.prepare(
        `SELECT COUNT(*) AS c FROM bpp_accounts WHERE game_account_id = 'acct-web'`
      ).first<{ c: number }>(),
      env.DB.prepare(
        `SELECT COUNT(*) AS c FROM bpp_device_tokens WHERE game_account_id = 'acct-web'`
      ).first<{ c: number }>(),
      env.DB.prepare(
        `SELECT COUNT(*) AS c FROM bpp_web_sessions WHERE game_account_id = 'acct-web'`
      ).first<{ c: number }>(),
      env.DB.prepare(
        `SELECT COUNT(*) AS c FROM run_summaries WHERE game_account_id = 'acct-web'`
      ).first<{ c: number }>()
    ]);
    expect(counts.map((row) => Number(row?.c))).toEqual([0, 0, 0, 0]);
  });
});

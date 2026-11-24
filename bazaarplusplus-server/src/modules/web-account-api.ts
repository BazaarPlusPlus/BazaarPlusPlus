import { validIdentifier } from '../bundle/manifest';
import type { Env } from '../env';
import { apiError, HttpError } from '../errors';
import { jsonError, jsonResponse } from '../http/json';
import { logError } from '../observability';
import { enforceAccountRateLimit } from './account-auth';
import {
  constantTimeHexEquals,
  fromBase64Url,
  generateSessionToken,
  generateSixDigitCode,
  hmacVerificationCode,
  HOURLY_EMAIL_SEND_LIMIT,
  HOURLY_WINDOW_MS,
  maskEmail,
  normalizeEmail,
  sha256Hex,
  toBase64Url,
  validSixDigitCode,
  VERIFICATION_CODE_MAX_ATTEMPTS,
  VERIFICATION_CODE_TTL_MS,
  WEB_SESSION_TTL_MS
} from './account-crypto';
import { renderVerificationEmail } from './account-email';
import {
  readJsonObject,
  requireCodeSecret,
  sendAccountEmail
} from './account-service';

const SESSION_COOKIE_NAME = '__Host-bpp_session';
const SESSION_MAX_AGE_SECONDS = Math.floor(WEB_SESSION_TTL_MS / 1000);
const RECORDS_PAGE_SIZE = 50;

export interface WebRunSummary {
  run_id: string;
  hero: string | null;
  victories: number | null;
  losses: number | null;
  final_player_rank: string | null;
  final_player_rating: number | null;
  final_player_rating_delta: number | null;
  game_mode: string | null;
  ended_at_ms: number | null;
}

export interface WebMeResponse {
  user_name: string;
  email_masked: string;
  runs: WebRunSummary[];
  next_cursor: string | null;
}

interface EncodedCursor {
  ended_at_ms: number | null;
  run_id: string;
}

function isAllowedOrigin(origin: string, requestUrl: URL): boolean {
  let parsed: URL;
  try {
    parsed = new URL(origin);
  } catch {
    return false;
  }
  if (parsed.origin !== requestUrl.origin) return false;
  if (parsed.protocol === 'https:') {
    return (
      parsed.hostname === 'bazaarplusplus.com' ||
      parsed.hostname === 'www.bazaarplusplus.com' ||
      parsed.hostname.endsWith('.workers.dev')
    );
  }
  return (
    parsed.protocol === 'http:' &&
    (parsed.hostname === 'localhost' || parsed.hostname === '127.0.0.1')
  );
}

function requireValidPostOrigin(request: Request, requestUrl: URL): void {
  const origin = request.headers.get('Origin');
  if (origin === null || !isAllowedOrigin(origin, requestUrl)) {
    throw new HttpError(403, 'forbidden_origin', 'Origin is not allowed');
  }
}

function readSessionCookie(request: Request): string | null {
  const header = request.headers.get('Cookie');
  if (header === null) return null;
  for (const part of header.split(';')) {
    const equals = part.indexOf('=');
    if (equals <= 0) continue;
    const name = part.slice(0, equals).trim();
    if (name !== SESSION_COOKIE_NAME) continue;
    const value = part.slice(equals + 1).trim();
    return /^[A-Za-z0-9_-]{43}$/.test(value) ? value : null;
  }
  return null;
}

function buildSessionCookie(token: string | null): string {
  if (token === null) {
    return `${SESSION_COOKIE_NAME}=; HttpOnly; Secure; SameSite=Lax; Path=/; Max-Age=0`;
  }
  return `${SESSION_COOKIE_NAME}=${token}; HttpOnly; Secure; SameSite=Lax; Path=/; Max-Age=${SESSION_MAX_AGE_SECONDS}`;
}

function encodeCursor(cursor: EncodedCursor): string {
  return toBase64Url(new TextEncoder().encode(JSON.stringify(cursor)));
}

function decodeCursor(raw: string): EncodedCursor {
  const bytes = fromBase64Url(raw);
  if (bytes === null || bytes.byteLength === 0 || bytes.byteLength > 512) {
    throw apiError('invalid_query', 'cursor is invalid', {
      details: { field: 'cursor' }
    });
  }
  let parsed: unknown;
  try {
    parsed = JSON.parse(new TextDecoder().decode(bytes));
  } catch {
    throw apiError('invalid_query', 'cursor is invalid', {
      details: { field: 'cursor' }
    });
  }
  if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) {
    throw apiError('invalid_query', 'cursor is invalid', {
      details: { field: 'cursor' }
    });
  }
  const record = parsed as Record<string, unknown>;
  const endedAtMs = record.ended_at_ms;
  const runId = record.run_id;
  if (
    (endedAtMs !== null &&
      (typeof endedAtMs !== 'number' || !Number.isSafeInteger(endedAtMs))) ||
    typeof runId !== 'string' ||
    !validIdentifier(runId)
  ) {
    throw apiError('invalid_query', 'cursor is invalid', {
      details: { field: 'cursor' }
    });
  }
  return { ended_at_ms: endedAtMs, run_id: runId };
}

interface AuthenticatedWebSession {
  readonly sessionHash: string;
  readonly gameAccountId: string;
  readonly email: string;
  readonly userName: string;
}

async function authenticateWebSession(
  request: Request,
  db: D1Database
): Promise<AuthenticatedWebSession> {
  const token = readSessionCookie(request);
  if (token === null) {
    throw apiError('unauthorized', 'Sign in is required');
  }
  const sessionHash = await sha256Hex(token);
  const now = Date.now();
  let row: {
    game_account_id: string;
    email: string;
    user_name: string;
    expires_at_ms: number;
  } | null;
  try {
    row = await db
      .prepare(
        `SELECT s.game_account_id, a.email, a.user_name, s.expires_at_ms
         FROM bpp_web_sessions AS s
         JOIN bpp_accounts AS a ON a.game_account_id = s.game_account_id
         WHERE s.session_hash = ?1`
      )
      .bind(sessionHash)
      .first<{
        game_account_id: string;
        email: string;
        user_name: string;
        expires_at_ms: number;
      }>();
  } catch {
    throw apiError('storage_unavailable', 'Session storage is unavailable');
  }
  if (row === null || row.expires_at_ms < now) {
    throw apiError('unauthorized', 'Sign in is required');
  }
  return {
    sessionHash,
    gameAccountId: row.game_account_id,
    email: row.email,
    userName: row.user_name
  };
}

async function handleLoginStart(
  request: Request,
  env: Env,
  requestId: string
): Promise<Response> {
  await enforceAccountRateLimit(request, env);
  const secret = requireCodeSecret(env);
  const body = await readJsonObject(request);
  const rawEmail = typeof body.email === 'string' ? body.email : '';
  const email = normalizeEmail(rawEmail);
  if (email === null) {
    throw apiError('invalid_body', 'email is invalid', {
      details: { field: 'email' }
    });
  }

  const now = Date.now();
  let account: { user_name: string } | null;
  let recentSends: { count: number } | null;
  try {
    [account, recentSends] = await Promise.all([
      env.DB.prepare(`SELECT user_name FROM bpp_accounts WHERE email = ?1`)
        .bind(email)
        .first<{ user_name: string }>(),
      env.DB.prepare(
        `SELECT COUNT(*) AS count
         FROM bpp_web_login_challenges
         WHERE email = ?1 AND created_at_ms >= ?2`
      )
        .bind(email, now - HOURLY_WINDOW_MS)
        .first<{ count: number }>()
    ]);
  } catch {
    throw apiError('storage_unavailable', 'Account storage is unavailable');
  }

  // Always return 200 regardless of whether the email is bound to prevent enumeration.
  if (account !== null && (recentSends?.count ?? 0) < HOURLY_EMAIL_SEND_LIMIT) {
    const challengeId = crypto.randomUUID();
    const verificationCode = generateSixDigitCode();
    const codeHmac = await hmacVerificationCode(
      secret,
      'web_login',
      challengeId,
      email,
      verificationCode
    );
    const expiresAtMs = now + VERIFICATION_CODE_TTL_MS;
    try {
      await env.DB.prepare(
        `INSERT INTO bpp_web_login_challenges (
          challenge_id, email, code_hmac, attempts, created_at_ms, expires_at_ms
        ) VALUES (?1, ?2, ?3, 0, ?4, ?5)`
      )
        .bind(challengeId, email, codeHmac, now, expiresAtMs)
        .run();
    } catch {
      throw apiError('storage_unavailable', 'Failed to store login challenge');
    }

    try {
      await sendAccountEmail(
        env,
        email,
        renderVerificationEmail({
          purpose: 'login',
          userName: account.user_name,
          code: verificationCode
        })
      );
    } catch (error) {
      await env.DB.prepare(
        `DELETE FROM bpp_web_login_challenges WHERE challenge_id = ?1`
      )
        .bind(challengeId)
        .run()
        .catch(() => undefined);
      throw error;
    }
  }

  return jsonResponse({ ok: true }, { status: 200, requestId });
}

interface WebLoginChallengeRow {
  challenge_id: string;
  email: string;
  code_hmac: string;
  attempts: number;
  expires_at_ms: number;
  consumed_at_ms: number | null;
}

async function handleLoginConfirm(
  request: Request,
  env: Env,
  requestId: string
): Promise<Response> {
  await enforceAccountRateLimit(request, env);
  const secret = requireCodeSecret(env);
  const body = await readJsonObject(request);
  const rawEmail = typeof body.email === 'string' ? body.email : '';
  const email = normalizeEmail(rawEmail);
  const verificationCode =
    typeof body.code === 'string' ? body.code.trim() : '';
  if (email === null) {
    throw apiError('invalid_body', 'email is invalid', {
      details: { field: 'email' }
    });
  }
  if (!validSixDigitCode(verificationCode)) {
    throw apiError(
      'invalid_verification_code',
      'Verification code is invalid, expired, or exceeded max attempts'
    );
  }

  const now = Date.now();
  let challenge: WebLoginChallengeRow | null;
  try {
    challenge = await env.DB.prepare(
      `SELECT challenge_id, email, code_hmac, attempts, expires_at_ms, consumed_at_ms
       FROM bpp_web_login_challenges
       WHERE email = ?1
       ORDER BY created_at_ms DESC
       LIMIT 1`
    )
      .bind(email)
      .first<WebLoginChallengeRow>();
  } catch {
    throw apiError('storage_unavailable', 'Account storage is unavailable');
  }

  if (
    challenge === null ||
    challenge.consumed_at_ms !== null ||
    challenge.expires_at_ms < now ||
    challenge.attempts >= VERIFICATION_CODE_MAX_ATTEMPTS
  ) {
    throw apiError(
      'invalid_verification_code',
      'Verification code is invalid, expired, or exceeded max attempts'
    );
  }

  const expectedHmac = await hmacVerificationCode(
    secret,
    'web_login',
    challenge.challenge_id,
    challenge.email,
    verificationCode
  );
  if (!constantTimeHexEquals(expectedHmac, challenge.code_hmac)) {
    try {
      await env.DB.prepare(
        `UPDATE bpp_web_login_challenges
         SET attempts = attempts + 1
         WHERE challenge_id = ?1 AND consumed_at_ms IS NULL AND attempts < ?2`
      )
        .bind(challenge.challenge_id, VERIFICATION_CODE_MAX_ATTEMPTS)
        .run();
    } catch {
      throw apiError('storage_unavailable', 'Account storage is unavailable');
    }
    throw apiError(
      'invalid_verification_code',
      'Verification code is invalid, expired, or exceeded max attempts'
    );
  }

  let account: { game_account_id: string; user_name: string } | null;
  try {
    const consume = await env.DB.prepare(
      `UPDATE bpp_web_login_challenges
       SET consumed_at_ms = ?1
       WHERE challenge_id = ?2
         AND consumed_at_ms IS NULL
         AND attempts < ?3
         AND expires_at_ms >= ?1`
    )
      .bind(now, challenge.challenge_id, VERIFICATION_CODE_MAX_ATTEMPTS)
      .run();
    if ((consume.meta?.changes ?? 0) !== 1) {
      throw apiError(
        'invalid_verification_code',
        'Verification code is invalid, expired, or exceeded max attempts'
      );
    }

    account = await env.DB.prepare(
      `SELECT game_account_id, user_name FROM bpp_accounts WHERE email = ?1`
    )
      .bind(email)
      .first<{ game_account_id: string; user_name: string }>();
  } catch (error) {
    if (error instanceof HttpError) throw error;
    throw apiError('storage_unavailable', 'Account storage is unavailable');
  }

  if (account === null) {
    throw apiError(
      'invalid_verification_code',
      'Verification code is invalid, expired, or exceeded max attempts'
    );
  }

  const sessionToken = generateSessionToken();
  const sessionHash = await sha256Hex(sessionToken);
  try {
    await env.DB.prepare(
      `INSERT INTO bpp_web_sessions (session_hash, game_account_id, created_at_ms, expires_at_ms)
       VALUES (?1, ?2, ?3, ?4)`
    )
      .bind(sessionHash, account.game_account_id, now, now + WEB_SESSION_TTL_MS)
      .run();
  } catch {
    throw apiError('storage_unavailable', 'Failed to create web session');
  }

  return jsonResponse(
    {
      user_name: account.user_name,
      email_masked: maskEmail(email)
    },
    {
      status: 200,
      requestId,
      headers: {
        'Set-Cookie': buildSessionCookie(sessionToken)
      }
    }
  );
}

async function handleGetMe(
  request: Request,
  requestUrl: URL,
  env: Env,
  requestId: string
): Promise<Response> {
  const session = await authenticateWebSession(request, env.DB);

  for (const key of requestUrl.searchParams.keys()) {
    if (key !== 'cursor') {
      throw apiError('invalid_query', `Unknown query parameter: ${key}`, {
        details: { field: key }
      });
    }
  }
  const cursorValues = requestUrl.searchParams.getAll('cursor');
  if (cursorValues.length > 1) {
    throw apiError('invalid_query', 'cursor must appear at most once', {
      details: { field: 'cursor' }
    });
  }
  const cursor =
    cursorValues.length === 1 && cursorValues[0] !== ''
      ? decodeCursor(cursorValues[0])
      : null;

  let rows: WebRunSummary[];
  try {
    if (cursor === null) {
      const result = await env.DB.prepare(
        `SELECT run_id, hero, victories, losses, final_player_rank,
                final_player_rating, final_player_rating_delta, game_mode, ended_at_ms
         FROM run_summaries INDEXED BY idx_run_summaries_account_ended
         WHERE game_account_id = ?1
         ORDER BY ended_at_ms DESC, run_id DESC
         LIMIT ?2`
      )
        .bind(session.gameAccountId, RECORDS_PAGE_SIZE + 1)
        .all<WebRunSummary>();
      rows = result.results;
    } else if (cursor.ended_at_ms !== null) {
      const result = await env.DB.prepare(
        `SELECT run_id, hero, victories, losses, final_player_rank,
                final_player_rating, final_player_rating_delta, game_mode, ended_at_ms
         FROM run_summaries INDEXED BY idx_run_summaries_account_ended
         WHERE game_account_id = ?1
           AND (
             ended_at_ms < ?2
             OR (ended_at_ms = ?2 AND run_id < ?3)
             OR ended_at_ms IS NULL
           )
         ORDER BY ended_at_ms DESC, run_id DESC
         LIMIT ?4`
      )
        .bind(
          session.gameAccountId,
          cursor.ended_at_ms,
          cursor.run_id,
          RECORDS_PAGE_SIZE + 1
        )
        .all<WebRunSummary>();
      rows = result.results;
    } else {
      const result = await env.DB.prepare(
        `SELECT run_id, hero, victories, losses, final_player_rank,
                final_player_rating, final_player_rating_delta, game_mode, ended_at_ms
         FROM run_summaries INDEXED BY idx_run_summaries_account_ended
         WHERE game_account_id = ?1
           AND ended_at_ms IS NULL
           AND run_id < ?2
         ORDER BY ended_at_ms DESC, run_id DESC
         LIMIT ?3`
      )
        .bind(session.gameAccountId, cursor.run_id, RECORDS_PAGE_SIZE + 1)
        .all<WebRunSummary>();
      rows = result.results;
    }
  } catch {
    throw apiError('storage_unavailable', 'Run summaries query failed');
  }

  const hasMore = rows.length > RECORDS_PAGE_SIZE;
  const page = hasMore ? rows.slice(0, RECORDS_PAGE_SIZE) : rows;
  const last = page.at(-1);
  const responseBody: WebMeResponse = {
    user_name: session.userName,
    email_masked: maskEmail(session.email),
    runs: page,
    next_cursor:
      hasMore && last !== undefined
        ? encodeCursor({ ended_at_ms: last.ended_at_ms, run_id: last.run_id })
        : null
  };
  return jsonResponse(responseBody, { status: 200, requestId });
}

async function handleLogout(
  request: Request,
  env: Env,
  requestId: string
): Promise<Response> {
  const token = readSessionCookie(request);
  if (token !== null) {
    const sessionHash = await sha256Hex(token);
    await env.DB.prepare(`DELETE FROM bpp_web_sessions WHERE session_hash = ?1`)
      .bind(sessionHash)
      .run()
      .catch(() => undefined);
  }
  return jsonResponse(
    { ok: true },
    {
      status: 200,
      requestId,
      headers: { 'Set-Cookie': buildSessionCookie(null) }
    }
  );
}

async function handleUnbind(
  request: Request,
  env: Env,
  requestId: string
): Promise<Response> {
  const session = await authenticateWebSession(request, env.DB);
  try {
    await env.DB.batch([
      env.DB.prepare(
        `DELETE FROM run_summaries WHERE game_account_id = ?1`
      ).bind(session.gameAccountId),
      env.DB.prepare(
        `DELETE FROM bpp_device_tokens WHERE game_account_id = ?1`
      ).bind(session.gameAccountId),
      env.DB.prepare(
        `DELETE FROM bpp_web_sessions WHERE game_account_id = ?1`
      ).bind(session.gameAccountId),
      env.DB.prepare(
        `DELETE FROM bpp_account_bind_challenges WHERE game_account_id = ?1 OR email = ?2`
      ).bind(session.gameAccountId, session.email),
      env.DB.prepare(
        `DELETE FROM bpp_web_login_challenges WHERE email = ?1`
      ).bind(session.email),
      env.DB.prepare(
        `DELETE FROM bpp_accounts WHERE game_account_id = ?1`
      ).bind(session.gameAccountId)
    ]);
  } catch {
    throw apiError('storage_unavailable', 'Failed to unbind account');
  }
  return jsonResponse(
    { ok: true },
    {
      status: 200,
      requestId,
      headers: { 'Set-Cookie': buildSessionCookie(null) }
    }
  );
}

const WEB_ROUTE_METHODS = new Map<string, readonly ('GET' | 'POST')[]>([
  ['/api/login/start', ['POST']],
  ['/api/login/confirm', ['POST']],
  ['/api/me', ['GET']],
  ['/api/logout', ['POST']],
  ['/api/unbind', ['POST']]
]);

export async function handleWebApiRequest(
  request: Request,
  env: Env
): Promise<Response> {
  const requestId = request.headers.get('CF-Ray') ?? crypto.randomUUID();
  const url = new URL(request.url);
  const allowed = WEB_ROUTE_METHODS.get(url.pathname);
  if (allowed === undefined) {
    return jsonError(
      { code: 'not_found', message: 'Route not found', retryable: false },
      404,
      requestId
    );
  }
  if (!allowed.includes(request.method as 'GET' | 'POST')) {
    return jsonError(
      {
        code: 'method_not_allowed',
        message: 'Method not allowed',
        retryable: false
      },
      405,
      requestId,
      { Allow: allowed.join(', ') }
    );
  }

  try {
    if (request.method === 'POST') {
      requireValidPostOrigin(request, url);
    }
    switch (url.pathname) {
      case '/api/login/start':
        return await handleLoginStart(request, env, requestId);
      case '/api/login/confirm':
        return await handleLoginConfirm(request, env, requestId);
      case '/api/me':
        return await handleGetMe(request, url, env, requestId);
      case '/api/logout':
        return await handleLogout(request, env, requestId);
      case '/api/unbind':
        return await handleUnbind(request, env, requestId);
      default:
        throw apiError('not_found', 'Route not found');
    }
  } catch (error) {
    if (error instanceof HttpError) {
      if (error.status >= 500) {
        logError('web_api.http_error', {
          request_id: requestId,
          route: url.pathname,
          status: error.status,
          code: error.code
        });
      }
      return jsonError(
        {
          code: error.code,
          message: error.message,
          retryable: error.retryable,
          details: error.details
        },
        error.status,
        requestId,
        error.headers
      );
    }
    logError('web_api.internal_error', {
      request_id: requestId,
      route: url.pathname,
      error_name: error instanceof Error ? error.name : 'unknown',
      reason: 'unclassified_exception'
    });
    return jsonError(
      {
        code: 'internal_error',
        message: 'An internal error occurred',
        retryable: true
      },
      500,
      requestId
    );
  }
}

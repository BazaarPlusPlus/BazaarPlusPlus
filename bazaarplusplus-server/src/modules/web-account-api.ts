import { validIdentifier } from '../bundle/manifest';
import { RUN_CONTENT_TYPE } from '../bundle/prefix';
import type { Env } from '../env';
import { apiError, HttpError } from '../errors';
import { jsonError, jsonResponse } from '../http/json';
import { logError } from '../observability';
import { enforceAccountRateLimit, enforceRateLimit } from './account-auth';
import {
  BIND_CODE_TTL_MS,
  constantTimeHexEquals,
  fromBase64Url,
  generateBindCode,
  generateSessionToken,
  generateSixDigitCode,
  hashPassword,
  hmacBindCode,
  hmacVerificationCode,
  HOURLY_EMAIL_SEND_LIMIT,
  HOURLY_WINDOW_MS,
  normalizeEmail,
  sha256Hex,
  toBase64Url,
  validPasswordLength,
  validSixDigitCode,
  VERIFICATION_CODE_MAX_ATTEMPTS,
  VERIFICATION_CODE_TTL_MS,
  verifyPassword,
  WEB_SESSION_TTL_MS
} from './account-crypto';
import { renderVerificationEmail } from './account-email';
import { deleteAccountRunDetails, readRunDetail } from './account-run-detail';
import {
  readJsonObject,
  requireCodeSecret,
  sendAccountEmail
} from './account-service';
import { verifyTurnstile } from './turnstile';

const SESSION_COOKIE_NAME = '__Host-bpp_session';
const SESSION_MAX_AGE_SECONDS = Math.floor(WEB_SESSION_TTL_MS / 1000);
const RECORDS_PAGE_SIZE = 50;
const RATING_POINTS = 30;
const TEN_WIN_VICTORIES = 10;
const RUN_DETAIL_CACHE_CONTROL = 'private, no-store';

type EmailPurpose = 'register' | 'reset';

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
  has_detail: boolean;
}

export interface WebAccountStats {
  runs: number;
  victories: number;
  ten_win_runs: number;
  heroes: Array<{
    hero: string;
    runs: number;
    avg_victories: number;
    best_victories: number;
  }>;
  rating: Array<{ ended_at_ms: number; rating: number; rank: string | null }>;
}

export interface WebMeResponse {
  email: string;
  game: { user_name: string | null; linked_at_ms: number } | null;
  stats: WebAccountStats;
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

function invalidCursor(): HttpError {
  return apiError('invalid_query', 'cursor is invalid', {
    details: { field: 'cursor' }
  });
}

function decodeCursor(raw: string): EncodedCursor {
  const bytes = fromBase64Url(raw);
  if (bytes === null || bytes.byteLength === 0 || bytes.byteLength > 512) {
    throw invalidCursor();
  }
  let parsed: unknown;
  try {
    parsed = JSON.parse(new TextDecoder().decode(bytes));
  } catch {
    throw invalidCursor();
  }
  if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) {
    throw invalidCursor();
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
    throw invalidCursor();
  }
  return { ended_at_ms: endedAtMs, run_id: runId };
}

interface WebSession {
  readonly accountId: string;
  readonly email: string;
  readonly gameAccountId: string | null;
  readonly gameUserName: string | null;
  readonly linkedAtMs: number | null;
}

async function authenticateWebSession(
  request: Request,
  db: D1Database
): Promise<WebSession> {
  const token = readSessionCookie(request);
  if (token === null) {
    throw apiError('unauthorized', 'Sign in is required');
  }
  const sessionHash = await sha256Hex(token);
  let row: {
    account_id: string;
    email: string;
    game_account_id: string | null;
    game_user_name: string | null;
    linked_at_ms: number | null;
    expires_at_ms: number;
  } | null;
  try {
    row = await db
      .prepare(
        `SELECT a.account_id, a.email, a.game_account_id, a.game_user_name, a.linked_at_ms,
                s.expires_at_ms
         FROM bpp_web_sessions AS s
         JOIN bpp_accounts AS a ON a.account_id = s.account_id
         WHERE s.session_hash = ?1`
      )
      .bind(sessionHash)
      .first();
  } catch {
    throw apiError('storage_unavailable', 'Session storage is unavailable');
  }
  if (row === null || row.expires_at_ms < Date.now()) {
    throw apiError('unauthorized', 'Sign in is required');
  }
  return {
    accountId: row.account_id,
    email: row.email,
    gameAccountId: row.game_account_id,
    gameUserName: row.game_user_name,
    linkedAtMs: row.linked_at_ms
  };
}

// The session row is inserted only while `guard` (SQL over ?5 and later) holds,
// so a session never outlives the check that granted it: the guard runs in the
// same statement as the insert.
async function newSession(
  db: D1Database,
  accountId: string,
  now: number,
  guard: string,
  ...guardBindings: unknown[]
): Promise<{ cookie: string; statement: D1PreparedStatement }> {
  const token = generateSessionToken();
  const statement = db
    .prepare(
      `INSERT INTO bpp_web_sessions (session_hash, account_id, created_at_ms, expires_at_ms)
       SELECT ?1, ?2, ?3, ?4 WHERE ${guard}`
    )
    .bind(
      await sha256Hex(token),
      accountId,
      now,
      now + WEB_SESSION_TTL_MS,
      ...guardBindings
    );
  return { cookie: buildSessionCookie(token), statement };
}

function readEmail(body: Record<string, unknown>): string {
  const email =
    typeof body.email === 'string' ? normalizeEmail(body.email) : null;
  if (email === null) {
    throw apiError('invalid_body', 'email is invalid', {
      details: { field: 'email' }
    });
  }
  return email;
}

// Passwords are not trimmed: every character counts.
function readPassword(body: Record<string, unknown>): string {
  if (typeof body.password !== 'string') {
    throw apiError('invalid_body', 'password must be a string', {
      details: { field: 'password' }
    });
  }
  return body.password;
}

function readNewPassword(body: Record<string, unknown>): string {
  const password = readPassword(body);
  if (!validPasswordLength(password)) {
    throw apiError(
      'invalid_password',
      'Password must be 8 to 128 characters long'
    );
  }
  return password;
}

function invalidVerificationCode(): HttpError {
  return apiError(
    'invalid_verification_code',
    'Verification code is invalid, expired, or exceeded max attempts'
  );
}

function readVerificationCode(body: Record<string, unknown>): string {
  const code = typeof body.code === 'string' ? body.code.trim() : '';
  if (!validSixDigitCode(code)) throw invalidVerificationCode();
  return code;
}

async function findAccountByEmail(
  db: D1Database,
  email: string
): Promise<{ account_id: string; password_hash: string } | null> {
  try {
    return await db
      .prepare(
        `SELECT account_id, password_hash FROM bpp_accounts WHERE email = ?1`
      )
      .bind(email)
      .first();
  } catch {
    throw apiError('storage_unavailable', 'Account storage is unavailable');
  }
}

// Sends a code unless the email reached its hourly cap; the caller answers
// 200 either way, so the response never tells whether a code went out.
async function sendEmailChallenge(
  env: Env,
  secret: string,
  email: string,
  purpose: EmailPurpose
): Promise<void> {
  const now = Date.now();
  let recentSends: { count: number } | null;
  try {
    recentSends = await env.DB.prepare(
      `SELECT COUNT(*) AS count FROM bpp_email_challenges
       WHERE email = ?1 AND created_at_ms >= ?2`
    )
      .bind(email, now - HOURLY_WINDOW_MS)
      .first();
  } catch {
    throw apiError('storage_unavailable', 'Account storage is unavailable');
  }
  if ((recentSends?.count ?? 0) >= HOURLY_EMAIL_SEND_LIMIT) return;

  const challengeId = crypto.randomUUID();
  const code = generateSixDigitCode();
  const codeHmac = await hmacVerificationCode(
    secret,
    purpose,
    challengeId,
    email,
    code
  );
  try {
    await env.DB.prepare(
      `INSERT INTO bpp_email_challenges (
        challenge_id, purpose, email, code_hmac, attempts, created_at_ms, expires_at_ms
      ) VALUES (?1, ?2, ?3, ?4, 0, ?5, ?6)`
    )
      .bind(
        challengeId,
        purpose,
        email,
        codeHmac,
        now,
        now + VERIFICATION_CODE_TTL_MS
      )
      .run();
  } catch {
    throw apiError('storage_unavailable', 'Failed to store email challenge');
  }
  try {
    await sendAccountEmail(
      env,
      email,
      renderVerificationEmail({ purpose, code })
    );
  } catch (error) {
    await env.DB.prepare(
      `DELETE FROM bpp_email_challenges WHERE challenge_id = ?1`
    )
      .bind(challengeId)
      .run()
      .catch(() => undefined);
    throw error;
  }
}

interface EmailChallengeRow {
  challenge_id: string;
  code_hmac: string;
  attempts: number;
  expires_at_ms: number;
  consumed_at_ms: number | null;
}

interface VerifiedChallenge {
  // Marks the challenge consumed; it changes one row only while the challenge
  // is still unconsumed, unexpired and under its attempt cap.
  readonly consume: D1PreparedStatement;
  // SQL true once this request's `consume` ran (binds ?5 = challenge id, ?6 = now).
  readonly consumedGuard: string;
  readonly guardBindings: readonly [string, number];
}

// Checks the code against the newest challenge for the email and purpose, or
// throws invalid_verification_code; a wrong code costs one of five attempts.
// The challenge is consumed only by the caller's batch, together with the change
// it authorizes, so a failed write leaves the code usable for a retry.
async function verifyEmailChallenge(
  env: Env,
  secret: string,
  email: string,
  purpose: EmailPurpose,
  code: string,
  now: number
): Promise<VerifiedChallenge> {
  let challenge: EmailChallengeRow | null;
  try {
    challenge = await env.DB.prepare(
      `SELECT challenge_id, code_hmac, attempts, expires_at_ms, consumed_at_ms
       FROM bpp_email_challenges
       WHERE email = ?1 AND purpose = ?2
       ORDER BY created_at_ms DESC
       LIMIT 1`
    )
      .bind(email, purpose)
      .first<EmailChallengeRow>();
  } catch {
    throw apiError('storage_unavailable', 'Account storage is unavailable');
  }
  if (
    challenge === null ||
    challenge.consumed_at_ms !== null ||
    challenge.expires_at_ms < now ||
    challenge.attempts >= VERIFICATION_CODE_MAX_ATTEMPTS
  ) {
    throw invalidVerificationCode();
  }

  const expected = await hmacVerificationCode(
    secret,
    purpose,
    challenge.challenge_id,
    email,
    code
  );
  if (!constantTimeHexEquals(expected, challenge.code_hmac)) {
    try {
      await env.DB.prepare(
        `UPDATE bpp_email_challenges
         SET attempts = attempts + 1
         WHERE challenge_id = ?1 AND consumed_at_ms IS NULL AND attempts < ?2`
      )
        .bind(challenge.challenge_id, VERIFICATION_CODE_MAX_ATTEMPTS)
        .run();
    } catch {
      throw apiError('storage_unavailable', 'Account storage is unavailable');
    }
    throw invalidVerificationCode();
  }

  return {
    consume: env.DB.prepare(
      `UPDATE bpp_email_challenges
       SET consumed_at_ms = ?1
       WHERE challenge_id = ?2
         AND consumed_at_ms IS NULL
         AND attempts < ?3
         AND expires_at_ms >= ?1`
    ).bind(now, challenge.challenge_id, VERIFICATION_CODE_MAX_ATTEMPTS),
    consumedGuard: `EXISTS (
      SELECT 1 FROM bpp_email_challenges
      WHERE challenge_id = ?5 AND consumed_at_ms = ?6
    )`,
    guardBindings: [challenge.challenge_id, now]
  };
}

function signedIn(email: string, cookie: string, requestId: string): Response {
  return jsonResponse(
    { email },
    { status: 200, requestId, headers: { 'Set-Cookie': cookie } }
  );
}

function ok(requestId: string, headers?: HeadersInit): Response {
  return jsonResponse({ ok: true }, { status: 200, requestId, headers });
}

async function handleRegisterStart(
  request: Request,
  env: Env,
  requestId: string
): Promise<Response> {
  await enforceAccountRateLimit(request, env);
  const secret = requireCodeSecret(env);
  const body = await readJsonObject(request);
  const email = readEmail(body);
  await verifyTurnstile(request, env, body);
  if ((await findAccountByEmail(env.DB, email)) === null) {
    await sendEmailChallenge(env, secret, email, 'register');
  }
  return ok(requestId);
}

async function handleRegisterConfirm(
  request: Request,
  env: Env,
  requestId: string
): Promise<Response> {
  await enforceAccountRateLimit(request, env);
  const secret = requireCodeSecret(env);
  const body = await readJsonObject(request);
  const email = readEmail(body);
  const password = readNewPassword(body);
  const code = readVerificationCode(body);
  const now = Date.now();
  const challenge = await verifyEmailChallenge(
    env,
    secret,
    email,
    'register',
    code,
    now
  );

  const accountId = crypto.randomUUID();
  const passwordHash = await hashPassword(password);
  const session = await newSession(
    env.DB,
    accountId,
    now,
    `EXISTS (SELECT 1 FROM bpp_accounts WHERE account_id = ?5)`,
    accountId
  );
  let results: D1Result[];
  try {
    // One batch: the code is spent only if the account is created with it.
    results = await env.DB.batch([
      challenge.consume,
      env.DB.prepare(
        `INSERT INTO bpp_accounts (account_id, email, password_hash, created_at_ms, updated_at_ms)
         SELECT ?1, ?2, ?3, ?4, ?4 WHERE ${challenge.consumedGuard}`
      ).bind(accountId, email, passwordHash, now, ...challenge.guardBindings),
      session.statement
    ]);
  } catch {
    // The batch rolled back whole; a concurrent registration took the email.
    const existing = await findAccountByEmail(env.DB, email).catch(() => null);
    if (existing !== null) {
      throw apiError(
        'email_already_registered',
        'A BPP Account already uses this email'
      );
    }
    throw apiError('storage_unavailable', 'Failed to create account');
  }
  // A concurrent confirm spent the code first.
  if ((results[1]?.meta?.changes ?? 0) !== 1) throw invalidVerificationCode();
  return signedIn(email, session.cookie, requestId);
}

async function handleLogin(
  request: Request,
  env: Env,
  requestId: string
): Promise<Response> {
  await enforceAccountRateLimit(request, env);
  const body = await readJsonObject(request);
  const email = readEmail(body);
  const password = readPassword(body);
  // Before the per-email limiter, so requests without a solved check cannot
  // spend another player's login quota.
  await verifyTurnstile(request, env, body);
  // Counted for unknown emails too, so 429 never tells which emails exist.
  await enforceRateLimit(env.LOGIN_EMAIL_RATE_LIMITER, email);

  const account = await findAccountByEmail(env.DB, email);
  const valid = await verifyPassword(password, account?.password_hash ?? null);
  if (account === null || !valid) {
    throw apiError('invalid_credentials', 'Email or password is wrong');
  }
  // The session is created only while the verified hash is still current, so a
  // password reset that lands during verification is never undone by this login.
  const session = await newSession(
    env.DB,
    account.account_id,
    Date.now(),
    `EXISTS (SELECT 1 FROM bpp_accounts WHERE account_id = ?2 AND password_hash = ?5)`,
    account.password_hash
  );
  let inserted: D1Result;
  try {
    inserted = await session.statement.run();
  } catch {
    throw apiError('storage_unavailable', 'Failed to create web session');
  }
  if ((inserted.meta?.changes ?? 0) !== 1) {
    throw apiError('invalid_credentials', 'Email or password is wrong');
  }
  return signedIn(email, session.cookie, requestId);
}

async function handleResetStart(
  request: Request,
  env: Env,
  requestId: string
): Promise<Response> {
  await enforceAccountRateLimit(request, env);
  const secret = requireCodeSecret(env);
  const body = await readJsonObject(request);
  const email = readEmail(body);
  await verifyTurnstile(request, env, body);
  if ((await findAccountByEmail(env.DB, email)) !== null) {
    await sendEmailChallenge(env, secret, email, 'reset');
  }
  return ok(requestId);
}

async function handleResetConfirm(
  request: Request,
  env: Env,
  requestId: string
): Promise<Response> {
  await enforceAccountRateLimit(request, env);
  const secret = requireCodeSecret(env);
  const body = await readJsonObject(request);
  const email = readEmail(body);
  const password = readNewPassword(body);
  const code = readVerificationCode(body);
  const now = Date.now();
  const challenge = await verifyEmailChallenge(
    env,
    secret,
    email,
    'reset',
    code,
    now
  );

  const account = await findAccountByEmail(env.DB, email);
  if (account === null) throw invalidVerificationCode();
  const passwordHash = await hashPassword(password);
  const session = await newSession(
    env.DB,
    account.account_id,
    now,
    challenge.consumedGuard,
    ...challenge.guardBindings
  );
  let results: D1Result[];
  try {
    // One batch: the code is spent only together with the new password. It also
    // signs out every other browser, since the old password may have leaked.
    results = await env.DB.batch([
      challenge.consume,
      env.DB.prepare(
        `UPDATE bpp_accounts SET password_hash = ?1, updated_at_ms = ?2
         WHERE account_id = ?3 AND ${challenge.consumedGuard}`
      ).bind(
        passwordHash,
        now,
        account.account_id,
        null,
        ...challenge.guardBindings
      ),
      env.DB.prepare(
        `DELETE FROM bpp_web_sessions
         WHERE account_id = ?1 AND ${challenge.consumedGuard}`
      ).bind(account.account_id, null, null, null, ...challenge.guardBindings),
      session.statement
    ]);
  } catch {
    throw apiError('storage_unavailable', 'Failed to reset password');
  }
  // A concurrent confirm spent the code first.
  if ((results[1]?.meta?.changes ?? 0) !== 1) throw invalidVerificationCode();
  return signedIn(email, session.cookie, requestId);
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
  return ok(requestId, { 'Set-Cookie': buildSessionCookie(null) });
}

const EMPTY_STATS: WebAccountStats = {
  runs: 0,
  victories: 0,
  ten_win_runs: 0,
  heroes: [],
  rating: []
};

async function readAccountStats(
  db: D1Database,
  gameAccountId: string
): Promise<WebAccountStats> {
  let results: D1Result[];
  try {
    results = await db.batch([
      db
        .prepare(
          `SELECT COUNT(*) AS runs,
                  COALESCE(SUM(victories), 0) AS victories,
                  COALESCE(SUM(victories >= ?2), 0) AS ten_win_runs
           FROM run_summaries
           WHERE game_account_id = ?1`
        )
        .bind(gameAccountId, TEN_WIN_VICTORIES),
      db
        .prepare(
          `SELECT hero,
                  COUNT(*) AS runs,
                  COALESCE(AVG(victories), 0) AS avg_victories,
                  COALESCE(MAX(victories), 0) AS best_victories
           FROM run_summaries
           WHERE game_account_id = ?1 AND hero IS NOT NULL
           GROUP BY hero
           ORDER BY runs DESC, hero ASC`
        )
        .bind(gameAccountId),
      db
        .prepare(
          `SELECT ended_at_ms, rating, rank FROM (
             SELECT run_id, ended_at_ms, final_player_rating AS rating,
                    final_player_rank AS rank
             FROM run_summaries INDEXED BY idx_run_summaries_account_ended
             WHERE game_account_id = ?1
               AND ended_at_ms IS NOT NULL
               AND final_player_rating IS NOT NULL
             ORDER BY ended_at_ms DESC, run_id DESC
             LIMIT ?2
           )
           ORDER BY ended_at_ms ASC, run_id ASC`
        )
        .bind(gameAccountId, RATING_POINTS)
    ]);
  } catch {
    throw apiError('storage_unavailable', 'Account stats query failed');
  }
  const totals = results[0].results[0] as {
    runs: number;
    victories: number;
    ten_win_runs: number;
  };
  return {
    runs: totals.runs,
    victories: totals.victories,
    ten_win_runs: totals.ten_win_runs,
    heroes: results[1].results as WebAccountStats['heroes'],
    rating: results[2].results as WebAccountStats['rating']
  };
}

async function handleGetMe(
  request: Request,
  env: Env,
  requestId: string
): Promise<Response> {
  const session = await authenticateWebSession(request, env.DB);
  const linked =
    session.gameAccountId !== null && session.linkedAtMs !== null
      ? {
          gameAccountId: session.gameAccountId,
          linkedAtMs: session.linkedAtMs
        }
      : null;
  const body: WebMeResponse = {
    email: session.email,
    game:
      linked === null
        ? null
        : { user_name: session.gameUserName, linked_at_ms: linked.linkedAtMs },
    stats:
      linked === null
        ? EMPTY_STATS
        : await readAccountStats(env.DB, linked.gameAccountId)
  };
  return jsonResponse(body, { status: 200, requestId });
}

const RUN_SUMMARY_COLUMNS = `run_id, hero, victories, losses, final_player_rank,
  final_player_rating, final_player_rating_delta, game_mode, ended_at_ms, detail_stored`;

type RunSummaryRow = Omit<WebRunSummary, 'has_detail'> & {
  detail_stored: number;
};

function toWebRunSummary({
  detail_stored,
  ...row
}: RunSummaryRow): WebRunSummary {
  return { ...row, has_detail: detail_stored === 1 };
}

function readCursorQuery(requestUrl: URL): EncodedCursor | null {
  for (const key of requestUrl.searchParams.keys()) {
    if (key !== 'cursor') {
      throw apiError('invalid_query', `Unknown query parameter: ${key}`, {
        details: { field: key }
      });
    }
  }
  const values = requestUrl.searchParams.getAll('cursor');
  if (values.length > 1) {
    throw apiError('invalid_query', 'cursor must appear at most once', {
      details: { field: 'cursor' }
    });
  }
  return values.length === 1 && values[0] !== ''
    ? decodeCursor(values[0])
    : null;
}

async function queryRunPage(
  db: D1Database,
  gameAccountId: string,
  cursor: EncodedCursor | null
): Promise<RunSummaryRow[]> {
  const from = `FROM run_summaries INDEXED BY idx_run_summaries_account_ended`;
  const order = `ORDER BY ended_at_ms DESC, run_id DESC`;
  let statement: D1PreparedStatement;
  if (cursor === null) {
    statement = db
      .prepare(
        `SELECT ${RUN_SUMMARY_COLUMNS} ${from}
         WHERE game_account_id = ?1 ${order} LIMIT ?2`
      )
      .bind(gameAccountId, RECORDS_PAGE_SIZE + 1);
  } else if (cursor.ended_at_ms !== null) {
    statement = db
      .prepare(
        `SELECT ${RUN_SUMMARY_COLUMNS} ${from}
         WHERE game_account_id = ?1
           AND (
             ended_at_ms < ?2
             OR (ended_at_ms = ?2 AND run_id < ?3)
             OR ended_at_ms IS NULL
           )
         ${order} LIMIT ?4`
      )
      .bind(
        gameAccountId,
        cursor.ended_at_ms,
        cursor.run_id,
        RECORDS_PAGE_SIZE + 1
      );
  } else {
    statement = db
      .prepare(
        `SELECT ${RUN_SUMMARY_COLUMNS} ${from}
         WHERE game_account_id = ?1 AND ended_at_ms IS NULL AND run_id < ?2
         ${order} LIMIT ?3`
      )
      .bind(gameAccountId, cursor.run_id, RECORDS_PAGE_SIZE + 1);
  }
  try {
    return (await statement.all<RunSummaryRow>()).results;
  } catch {
    throw apiError('storage_unavailable', 'Run summaries query failed');
  }
}

async function handleListRuns(
  request: Request,
  requestUrl: URL,
  env: Env,
  requestId: string
): Promise<Response> {
  const session = await authenticateWebSession(request, env.DB);
  const cursor = readCursorQuery(requestUrl);
  const rows =
    session.gameAccountId === null
      ? []
      : await queryRunPage(env.DB, session.gameAccountId, cursor);
  const hasMore = rows.length > RECORDS_PAGE_SIZE;
  const page = hasMore ? rows.slice(0, RECORDS_PAGE_SIZE) : rows;
  const last = page.at(-1);
  return jsonResponse(
    {
      runs: page.map(toWebRunSummary),
      next_cursor:
        hasMore && last !== undefined
          ? encodeCursor({ ended_at_ms: last.ended_at_ms, run_id: last.run_id })
          : null
    },
    { status: 200, requestId }
  );
}

function runNotFound(): HttpError {
  return apiError('not_found', 'Run not found');
}

async function findOwnedRun(
  db: D1Database,
  session: WebSession,
  runId: string
): Promise<RunSummaryRow> {
  if (session.gameAccountId === null) throw runNotFound();
  let row: RunSummaryRow | null;
  try {
    row = await db
      .prepare(
        `SELECT ${RUN_SUMMARY_COLUMNS} FROM run_summaries
         WHERE run_id = ?1 AND game_account_id = ?2`
      )
      .bind(runId, session.gameAccountId)
      .first<RunSummaryRow>();
  } catch {
    throw apiError('storage_unavailable', 'Run summaries query failed');
  }
  if (row === null) throw runNotFound();
  return row;
}

async function handleGetRun(
  request: Request,
  env: Env,
  requestId: string,
  runId: string
): Promise<Response> {
  const session = await authenticateWebSession(request, env.DB);
  const row = await findOwnedRun(env.DB, session, runId);
  return jsonResponse(
    { run: toWebRunSummary(row) },
    { status: 200, requestId }
  );
}

async function handleGetRunPayload(
  request: Request,
  env: Env,
  requestId: string,
  runId: string
): Promise<Response> {
  const session = await authenticateWebSession(request, env.DB);
  const row = await findOwnedRun(env.DB, session, runId);
  if (row.detail_stored !== 1) throw runNotFound();
  const object = await readRunDetail(
    env.BUNDLE_BUCKET,
    session.accountId,
    runId
  );
  if (object === null) throw runNotFound();
  return new Response(object.body, {
    status: 200,
    headers: {
      'Content-Type': RUN_CONTENT_TYPE,
      'Content-Length': String(object.size),
      'Cache-Control': RUN_DETAIL_CACHE_CONTROL,
      'X-Request-Id': requestId
    }
  });
}

async function handleBindCode(
  request: Request,
  env: Env,
  requestId: string
): Promise<Response> {
  await enforceAccountRateLimit(request, env);
  const secret = requireCodeSecret(env);
  const session = await authenticateWebSession(request, env.DB);
  if (session.gameAccountId !== null) {
    throw apiError(
      'game_account_already_bound',
      'This BPP Account is already linked to a game account'
    );
  }
  const now = Date.now();
  const code = generateBindCode();
  const expiresAtMs = now + BIND_CODE_TTL_MS;
  try {
    // One live code per account: a new code replaces the previous one.
    await env.DB.prepare(
      `INSERT INTO bpp_bind_codes (code_hash, account_id, created_at_ms, expires_at_ms)
       VALUES (?1, ?2, ?3, ?4)
       ON CONFLICT(account_id) DO UPDATE SET
         code_hash = excluded.code_hash,
         created_at_ms = excluded.created_at_ms,
         expires_at_ms = excluded.expires_at_ms`
    )
      .bind(
        await hmacBindCode(secret, code),
        session.accountId,
        now,
        expiresAtMs
      )
      .run();
  } catch {
    throw apiError('storage_unavailable', 'Failed to store Bind Code');
  }
  return jsonResponse(
    { code, expires_at_ms: expiresAtMs },
    { status: 200, requestId }
  );
}

// R2 goes first: if the D1 batch then fails, a retry finds the rows and
// deletes them, and no row ever points at a deleted detail object.
async function handleUnlink(
  request: Request,
  env: Env,
  requestId: string
): Promise<Response> {
  const session = await authenticateWebSession(request, env.DB);
  await deleteAccountRunDetails(env.BUNDLE_BUCKET, session.accountId);
  try {
    await env.DB.batch([
      env.DB.prepare(
        `DELETE FROM run_summaries WHERE game_account_id = ?1`
      ).bind(session.gameAccountId),
      env.DB.prepare(
        `DELETE FROM bpp_device_tokens WHERE account_id = ?1`
      ).bind(session.accountId),
      env.DB.prepare(`DELETE FROM bpp_bind_codes WHERE account_id = ?1`).bind(
        session.accountId
      ),
      env.DB.prepare(
        `UPDATE bpp_accounts
         SET game_account_id = NULL, game_user_name = NULL, linked_at_ms = NULL, updated_at_ms = ?2
         WHERE account_id = ?1`
      ).bind(session.accountId, Date.now())
    ]);
  } catch {
    throw apiError('storage_unavailable', 'Failed to unlink game account');
  }
  return ok(requestId);
}

async function handleDeleteAccount(
  request: Request,
  env: Env,
  requestId: string
): Promise<Response> {
  await enforceAccountRateLimit(request, env);
  const session = await authenticateWebSession(request, env.DB);
  const password = readPassword(await readJsonObject(request));
  const account = await findAccountByEmail(env.DB, session.email);
  if (
    account === null ||
    !(await verifyPassword(password, account.password_hash))
  ) {
    throw apiError('invalid_credentials', 'Email or password is wrong');
  }
  await deleteAccountRunDetails(env.BUNDLE_BUCKET, session.accountId);
  try {
    // Deleting the account cascades to sessions, Device Tokens, and Bind Codes.
    await env.DB.batch([
      env.DB.prepare(
        `DELETE FROM run_summaries WHERE game_account_id = ?1`
      ).bind(session.gameAccountId),
      env.DB.prepare(`DELETE FROM bpp_email_challenges WHERE email = ?1`).bind(
        session.email
      ),
      env.DB.prepare(`DELETE FROM bpp_accounts WHERE account_id = ?1`).bind(
        session.accountId
      )
    ]);
  } catch {
    throw apiError('storage_unavailable', 'Failed to delete account');
  }
  return ok(requestId, { 'Set-Cookie': buildSessionCookie(null) });
}

type WebRoute =
  | { name: 'register_start' | 'register_confirm' | 'login' }
  | { name: 'reset_start' | 'reset_confirm' | 'logout' | 'me' }
  | { name: 'runs' | 'bind_code' | 'unlink' | 'account_delete' }
  | { name: 'run' | 'run_payload'; runId: string };

const STATIC_ROUTES = new Map<string, [WebRoute['name'], 'GET' | 'POST']>([
  ['/api/register/start', ['register_start', 'POST']],
  ['/api/register/confirm', ['register_confirm', 'POST']],
  ['/api/login', ['login', 'POST']],
  ['/api/password/reset/start', ['reset_start', 'POST']],
  ['/api/password/reset/confirm', ['reset_confirm', 'POST']],
  ['/api/logout', ['logout', 'POST']],
  ['/api/me', ['me', 'GET']],
  ['/api/runs', ['runs', 'GET']],
  ['/api/bind-code', ['bind_code', 'POST']],
  ['/api/unlink', ['unlink', 'POST']],
  ['/api/account/delete', ['account_delete', 'POST']]
]);

const RUN_PATH = /^\/api\/runs\/([^/]+)(\/payload)?$/;

function resolveRoute(
  pathname: string
): { route: WebRoute; method: 'GET' | 'POST' } | null {
  const fixed = STATIC_ROUTES.get(pathname);
  if (fixed !== undefined) {
    return { route: { name: fixed[0] } as WebRoute, method: fixed[1] };
  }
  const match = RUN_PATH.exec(pathname);
  if (match === null) return null;
  let runId: string;
  try {
    runId = decodeURIComponent(match[1]);
  } catch {
    return null;
  }
  if (!validIdentifier(runId)) return null;
  return {
    route: { name: match[2] === undefined ? 'run' : 'run_payload', runId },
    method: 'GET'
  };
}

async function dispatch(
  route: WebRoute,
  request: Request,
  url: URL,
  env: Env,
  requestId: string
): Promise<Response> {
  switch (route.name) {
    case 'register_start':
      return handleRegisterStart(request, env, requestId);
    case 'register_confirm':
      return handleRegisterConfirm(request, env, requestId);
    case 'login':
      return handleLogin(request, env, requestId);
    case 'reset_start':
      return handleResetStart(request, env, requestId);
    case 'reset_confirm':
      return handleResetConfirm(request, env, requestId);
    case 'logout':
      return handleLogout(request, env, requestId);
    case 'me':
      return handleGetMe(request, env, requestId);
    case 'runs':
      return handleListRuns(request, url, env, requestId);
    case 'run':
      return handleGetRun(request, env, requestId, route.runId);
    case 'run_payload':
      return handleGetRunPayload(request, env, requestId, route.runId);
    case 'bind_code':
      return handleBindCode(request, env, requestId);
    case 'unlink':
      return handleUnlink(request, env, requestId);
    case 'account_delete':
      return handleDeleteAccount(request, env, requestId);
  }
}

export async function handleWebApiRequest(
  request: Request,
  env: Env
): Promise<Response> {
  const requestId = request.headers.get('CF-Ray') ?? crypto.randomUUID();
  const url = new URL(request.url);
  const resolved = resolveRoute(url.pathname);
  if (resolved === null) {
    return jsonError(
      { code: 'not_found', message: 'Route not found', retryable: false },
      404,
      requestId
    );
  }
  if (request.method !== resolved.method) {
    return jsonError(
      {
        code: 'method_not_allowed',
        message: 'Method not allowed',
        retryable: false
      },
      405,
      requestId,
      { Allow: resolved.method }
    );
  }

  try {
    if (request.method === 'POST') {
      requireValidPostOrigin(request, url);
    }
    return await dispatch(resolved.route, request, url, env, requestId);
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

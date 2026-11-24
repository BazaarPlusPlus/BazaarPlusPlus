import { validIdentifier } from '../bundle/manifest';
import type { Env } from '../env';
import { apiError, HttpError } from '../errors';
import type {
  AccountBindConfirmResponse,
  AccountBindStartResponse,
  AccountDevicesResponse,
  AccountMeResponse
} from '../http/schemas';
import {
  authenticateDeviceToken,
  enforceAccountRateLimit
} from './account-auth';
import {
  constantTimeHexEquals,
  generateDeviceToken,
  generateSixDigitCode,
  hmacVerificationCode,
  HOURLY_EMAIL_SEND_LIMIT,
  HOURLY_WINDOW_MS,
  maskEmail,
  normalizeEmail,
  sha256Hex,
  validSixDigitCode,
  VERIFICATION_CODE_MAX_ATTEMPTS,
  VERIFICATION_CODE_TTL_MS
} from './account-crypto';
import { verifyOfficialGameAccount } from './game-account-verifier';

const DEFAULT_EMAIL_FROM = 'noreply@bazaarplusplus.com';
const SAFE_HEADER_TOKEN = /^[^\r\n\t]{1,64}$/;
const MAX_JSON_BODY_BYTES = 16 * 1024;

export function requireCodeSecret(env: Env): string {
  const secret = env.ACCOUNT_CODE_SECRET;
  if (typeof secret !== 'string' || secret.trim().length < 16) {
    throw apiError(
      'internal_error',
      'Account verification secret is not configured'
    );
  }
  return secret;
}

export async function readJsonObject(
  request: Request
): Promise<Record<string, unknown>> {
  const contentType = request.headers.get('Content-Type') ?? '';
  if (
    contentType !== '' &&
    !contentType.toLowerCase().startsWith('application/json')
  ) {
    throw apiError('invalid_body', 'Request body must be JSON');
  }
  let raw: string;
  try {
    raw = await request.text();
  } catch {
    throw apiError('invalid_body', 'Failed to read request body');
  }
  if (
    raw.length === 0 ||
    new TextEncoder().encode(raw).byteLength > MAX_JSON_BODY_BYTES
  ) {
    throw apiError('invalid_body', 'Request body is empty or too large');
  }
  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    throw apiError('invalid_body', 'Request body is not valid JSON');
  }
  if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) {
    throw apiError('invalid_body', 'Request body must be a JSON object');
  }
  return parsed as Record<string, unknown>;
}

function requiredString(
  body: Record<string, unknown>,
  field: string,
  maxLength = 4096
): string {
  const value = body[field];
  if (typeof value !== 'string') {
    throw apiError('invalid_body', `${field} must be a string`, {
      details: { field }
    });
  }
  const trimmed = value.trim();
  if (trimmed.length === 0 || trimmed.length > maxLength) {
    throw apiError('invalid_body', `${field} is invalid`, {
      details: { field }
    });
  }
  return trimmed;
}

function requiredHeaderToken(
  body: Record<string, unknown>,
  field: string
): string {
  const value = requiredString(body, field, 64);
  if (!SAFE_HEADER_TOKEN.test(value)) {
    throw apiError('invalid_body', `${field} is invalid`, {
      details: { field }
    });
  }
  return value;
}

export async function sendAccountEmail(
  env: Env,
  to: string,
  subject: string,
  text: string
): Promise<void> {
  if (env.EMAIL === undefined) {
    throw apiError('storage_unavailable', 'Email delivery is not configured');
  }
  const from = env.ACCOUNT_EMAIL_FROM?.trim() || DEFAULT_EMAIL_FROM;
  try {
    await env.EMAIL.send({ from, to, subject, text });
  } catch {
    throw apiError('storage_unavailable', 'Failed to send verification email');
  }
}

export async function startAccountBind(
  request: Request,
  env: Env
): Promise<AccountBindStartResponse> {
  await enforceAccountRateLimit(request, env);
  const secret = requireCodeSecret(env);
  const body = await readJsonObject(request);

  const rawEmail = requiredString(body, 'email', 254);
  const email = normalizeEmail(rawEmail);
  if (email === null) {
    throw apiError('invalid_body', 'email is invalid', {
      details: { field: 'email' }
    });
  }
  const gameToken = requiredString(body, 'game_token', 8192);
  const gameAccountId = requiredString(body, 'game_account_id', 128);
  if (!validIdentifier(gameAccountId)) {
    throw apiError('invalid_body', 'game_account_id is invalid', {
      details: { field: 'game_account_id' }
    });
  }
  const platform = requiredHeaderToken(body, 'platform');
  const flavor = requiredHeaderToken(body, 'flavor');

  const official = await verifyOfficialGameAccount({
    gameToken,
    platform,
    flavor
  });
  if (official.id !== gameAccountId) {
    throw apiError(
      'account_mismatch',
      'Official game account ID differs from the claimed game account ID'
    );
  }

  const now = Date.now();
  let existingByGame: { email: string } | null;
  let existingByEmail: { game_account_id: string } | null;
  let recentSends: { count: number } | null;
  try {
    [existingByGame, existingByEmail, recentSends] = await Promise.all([
      env.DB.prepare(
        `SELECT email FROM bpp_accounts WHERE game_account_id = ?1`
      )
        .bind(official.id)
        .first<{ email: string }>(),
      env.DB.prepare(
        `SELECT game_account_id FROM bpp_accounts WHERE email = ?1`
      )
        .bind(email)
        .first<{ game_account_id: string }>(),
      env.DB.prepare(
        `SELECT COUNT(*) AS count
         FROM bpp_account_bind_challenges
         WHERE email = ?1 AND created_at_ms >= ?2`
      )
        .bind(email, now - HOURLY_WINDOW_MS)
        .first<{ count: number }>()
    ]);
  } catch {
    throw apiError('storage_unavailable', 'Account storage is unavailable');
  }

  if (existingByGame !== null) {
    throw apiError(
      'game_account_already_bound',
      'Game account is already bound; unbind on bazaarplusplus.com or activate this device',
      {
        details: {
          email_masked: maskEmail(existingByGame.email)
        }
      }
    );
  }
  if (existingByEmail !== null) {
    throw apiError(
      'email_already_bound',
      'Email is already bound to another game account; unbind on bazaarplusplus.com first'
    );
  }
  if ((recentSends?.count ?? 0) >= HOURLY_EMAIL_SEND_LIMIT) {
    throw apiError(
      'rate_limited',
      'Too many verification emails sent to this address',
      { headers: { 'Retry-After': '60' } }
    );
  }

  const pendingId = crypto.randomUUID();
  const verificationCode = generateSixDigitCode();
  const codeHmac = await hmacVerificationCode(
    secret,
    'bind',
    pendingId,
    email,
    verificationCode
  );
  const expiresAtMs = now + VERIFICATION_CODE_TTL_MS;

  try {
    await env.DB.prepare(
      `INSERT INTO bpp_account_bind_challenges (
        pending_id, game_account_id, email, user_name, code_hmac, attempts, created_at_ms, expires_at_ms
      ) VALUES (?1, ?2, ?3, ?4, ?5, 0, ?6, ?7)`
    )
      .bind(
        pendingId,
        official.id,
        email,
        official.userName,
        codeHmac,
        now,
        expiresAtMs
      )
      .run();
  } catch {
    throw apiError('storage_unavailable', 'Failed to store bind challenge');
  }

  try {
    await sendAccountEmail(
      env,
      email,
      'BazaarPlusPlus 账号绑定验证码',
      `你正在把游戏账号「${official.userName}」绑定到这个邮箱。\n\n验证码：${verificationCode}\n\n验证码 10 分钟内有效。如果不是本人操作，请忽略此邮件。`
    );
  } catch (error) {
    await env.DB.prepare(
      `DELETE FROM bpp_account_bind_challenges WHERE pending_id = ?1`
    )
      .bind(pendingId)
      .run()
      .catch(() => undefined);
    throw error;
  }

  return {
    pending_id: pendingId,
    user_name: official.userName,
    email_masked: maskEmail(email),
    expires_at_ms: expiresAtMs
  };
}

interface BindChallengeRow {
  pending_id: string;
  game_account_id: string;
  email: string;
  user_name: string;
  code_hmac: string;
  attempts: number;
  expires_at_ms: number;
  consumed_at_ms: number | null;
}

export async function confirmAccountBind(
  request: Request,
  env: Env
): Promise<AccountBindConfirmResponse> {
  await enforceAccountRateLimit(request, env);
  const secret = requireCodeSecret(env);
  const body = await readJsonObject(request);

  const pendingId = requiredString(body, 'pending_id', 128);
  const verificationCode = requiredString(body, 'code', 16);
  if (!validSixDigitCode(verificationCode)) {
    throw apiError(
      'invalid_verification_code',
      'Verification code is invalid, expired, or exceeded max attempts'
    );
  }

  const now = Date.now();
  let challenge: BindChallengeRow | null;
  try {
    challenge = await env.DB.prepare(
      `SELECT pending_id, game_account_id, email, user_name, code_hmac, attempts, expires_at_ms, consumed_at_ms
       FROM bpp_account_bind_challenges
       WHERE pending_id = ?1`
    )
      .bind(pendingId)
      .first<BindChallengeRow>();
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
    'bind',
    challenge.pending_id,
    challenge.email,
    verificationCode
  );
  if (!constantTimeHexEquals(expectedHmac, challenge.code_hmac)) {
    try {
      await env.DB.prepare(
        `UPDATE bpp_account_bind_challenges
         SET attempts = attempts + 1
         WHERE pending_id = ?1 AND consumed_at_ms IS NULL AND attempts < ?2`
      )
        .bind(challenge.pending_id, VERIFICATION_CODE_MAX_ATTEMPTS)
        .run();
    } catch {
      throw apiError('storage_unavailable', 'Account storage is unavailable');
    }
    throw apiError(
      'invalid_verification_code',
      'Verification code is invalid, expired, or exceeded max attempts'
    );
  }

  const deviceToken = generateDeviceToken();
  const tokenHash = await sha256Hex(deviceToken);

  try {
    const consume = await env.DB.prepare(
      `UPDATE bpp_account_bind_challenges
       SET consumed_at_ms = ?1
       WHERE pending_id = ?2
         AND consumed_at_ms IS NULL
         AND attempts < ?3
         AND expires_at_ms >= ?1`
    )
      .bind(now, challenge.pending_id, VERIFICATION_CODE_MAX_ATTEMPTS)
      .run();
    if ((consume.meta?.changes ?? 0) !== 1) {
      throw apiError(
        'invalid_verification_code',
        'Verification code is invalid, expired, or exceeded max attempts'
      );
    }

    await env.DB.batch([
      env.DB.prepare(
        `INSERT INTO bpp_accounts (game_account_id, email, user_name, created_at_ms, updated_at_ms)
         VALUES (?1, ?2, ?3, ?4, ?4)`
      ).bind(
        challenge.game_account_id,
        challenge.email,
        challenge.user_name,
        now
      ),
      env.DB.prepare(
        `INSERT INTO bpp_device_tokens (token_hash, game_account_id, created_at_ms)
         VALUES (?1, ?2, ?3)`
      ).bind(tokenHash, challenge.game_account_id, now)
    ]);
  } catch (error) {
    if (error instanceof HttpError) throw error;
    try {
      const [existingByGame, existingByEmail] = await Promise.all([
        env.DB.prepare(
          `SELECT email FROM bpp_accounts WHERE game_account_id = ?1`
        )
          .bind(challenge.game_account_id)
          .first<{ email: string }>(),
        env.DB.prepare(
          `SELECT game_account_id FROM bpp_accounts WHERE email = ?1`
        )
          .bind(challenge.email)
          .first<{ game_account_id: string }>()
      ]);
      if (existingByGame !== null) {
        throw apiError(
          'game_account_already_bound',
          'Game account is already bound; unbind on bazaarplusplus.com or activate this device',
          { details: { email_masked: maskEmail(existingByGame.email) } }
        );
      }
      if (existingByEmail !== null) {
        throw apiError(
          'email_already_bound',
          'Email is already bound to another game account; unbind on bazaarplusplus.com first'
        );
      }
    } catch (lookupError) {
      if (lookupError instanceof HttpError) throw lookupError;
    }
    throw apiError('storage_unavailable', 'Failed to persist account binding');
  }

  return {
    device_token: deviceToken,
    user_name: challenge.user_name,
    email_masked: maskEmail(challenge.email)
  };
}

export async function issueAccountDeviceToken(
  request: Request,
  env: Env
): Promise<AccountDevicesResponse> {
  await enforceAccountRateLimit(request, env);
  const body = await readJsonObject(request);

  const gameToken = requiredString(body, 'game_token', 8192);
  const gameAccountId = requiredString(body, 'game_account_id', 128);
  if (!validIdentifier(gameAccountId)) {
    throw apiError('invalid_body', 'game_account_id is invalid', {
      details: { field: 'game_account_id' }
    });
  }
  const platform = requiredHeaderToken(body, 'platform');
  const flavor = requiredHeaderToken(body, 'flavor');

  const official = await verifyOfficialGameAccount({
    gameToken,
    platform,
    flavor
  });
  if (official.id !== gameAccountId) {
    throw apiError(
      'account_mismatch',
      'Official game account ID differs from the claimed game account ID'
    );
  }

  let account: { email: string; user_name: string } | null;
  try {
    account = await env.DB.prepare(
      `SELECT email, user_name FROM bpp_accounts WHERE game_account_id = ?1`
    )
      .bind(official.id)
      .first<{ email: string; user_name: string }>();
  } catch {
    throw apiError('storage_unavailable', 'Account storage is unavailable');
  }

  if (account === null) {
    throw apiError(
      'account_not_bound',
      'Game account is not bound to a BPP Account'
    );
  }

  const now = Date.now();
  const deviceToken = generateDeviceToken();
  const tokenHash = await sha256Hex(deviceToken);
  try {
    await env.DB.batch([
      env.DB.prepare(
        `UPDATE bpp_accounts SET user_name = ?1, updated_at_ms = ?2 WHERE game_account_id = ?3`
      ).bind(official.userName, now, official.id),
      env.DB.prepare(
        `INSERT INTO bpp_device_tokens (token_hash, game_account_id, created_at_ms)
         VALUES (?1, ?2, ?3)`
      ).bind(tokenHash, official.id, now)
    ]);
  } catch {
    throw apiError('storage_unavailable', 'Failed to issue device token');
  }

  return {
    device_token: deviceToken,
    user_name: official.userName,
    email_masked: maskEmail(account.email)
  };
}

export async function getAccountMe(
  request: Request,
  env: Env
): Promise<AccountMeResponse> {
  await enforceAccountRateLimit(request, env);
  const account = await authenticateDeviceToken(request, env.DB);
  return {
    user_name: account.userName,
    email_masked: maskEmail(account.email)
  };
}

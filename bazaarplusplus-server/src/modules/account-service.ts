import { validIdentifier } from '../bundle/manifest';
import type { Env } from '../env';
import { apiError, type HttpError } from '../errors';
import type {
  AccountBindResponse,
  AccountDevicesResponse,
  AccountMeResponse
} from '../http/schemas';
import {
  authenticateDeviceToken,
  enforceAccountRateLimit
} from './account-auth';
import {
  generateDeviceToken,
  hmacBindCode,
  maskEmail,
  normalizeBindCode,
  sha256Hex
} from './account-crypto';
import type { VerificationEmail } from './account-email';
import {
  type OfficialGameAccount,
  verifyOfficialGameAccount
} from './game-account-verifier';
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
  message: VerificationEmail
): Promise<void> {
  if (env.EMAIL === undefined) {
    throw apiError('storage_unavailable', 'Email delivery is not configured');
  }
  const from = env.ACCOUNT_EMAIL_FROM?.trim() || DEFAULT_EMAIL_FROM;
  try {
    await env.EMAIL.send({
      from: { email: from, name: 'BazaarPlusPlus' },
      to,
      ...message
    });
  } catch {
    throw apiError('storage_unavailable', 'Failed to send verification email');
  }
}

interface GameAccountClaim {
  gameToken: string;
  gameAccountId: string;
  platform: string;
  flavor: string;
}

function readGameAccountClaim(body: Record<string, unknown>): GameAccountClaim {
  const gameToken = requiredString(body, 'game_token', 8192);
  const gameAccountId = requiredString(body, 'game_account_id', 128);
  if (!validIdentifier(gameAccountId)) {
    throw apiError('invalid_body', 'game_account_id is invalid', {
      details: { field: 'game_account_id' }
    });
  }
  return {
    gameToken,
    gameAccountId,
    platform: requiredHeaderToken(body, 'platform'),
    flavor: requiredHeaderToken(body, 'flavor')
  };
}

async function verifyClaim(
  claim: GameAccountClaim
): Promise<OfficialGameAccount> {
  const official = await verifyOfficialGameAccount(claim);
  if (official.id !== claim.gameAccountId) {
    throw apiError(
      'account_mismatch',
      'Official game account ID differs from the claimed game account ID'
    );
  }
  return official;
}

function invalidBindCode(): HttpError {
  return apiError(
    'invalid_bind_code',
    'Bind Code is malformed, unknown, or expired'
  );
}

function alreadyBound(): HttpError {
  return apiError(
    'game_account_already_bound',
    'Game account is linked to another BPP Account; unlink it on bazaarplusplus.com first'
  );
}

export async function bindGameAccount(
  request: Request,
  env: Env
): Promise<AccountBindResponse> {
  await enforceAccountRateLimit(request, env);
  const secret = requireCodeSecret(env);
  const body = await readJsonObject(request);
  const rawCode = body.bind_code;
  if (typeof rawCode !== 'string') {
    throw apiError('invalid_body', 'bind_code must be a string', {
      details: { field: 'bind_code' }
    });
  }
  const bindCode = normalizeBindCode(rawCode);
  const claim = readGameAccountClaim(body);
  if (bindCode === null) throw invalidBindCode();

  const official = await verifyClaim(claim);
  const codeHash = await hmacBindCode(secret, bindCode);
  const now = Date.now();

  let code: { account_id: string; email: string } | null;
  let linkedTo: { account_id: string } | null;
  try {
    [code, linkedTo] = await Promise.all([
      env.DB.prepare(
        `SELECT c.account_id, a.email
         FROM bpp_bind_codes AS c
         JOIN bpp_accounts AS a ON a.account_id = c.account_id
         WHERE c.code_hash = ?1 AND c.expires_at_ms >= ?2`
      )
        .bind(codeHash, now)
        .first<{ account_id: string; email: string }>(),
      env.DB.prepare(
        `SELECT account_id FROM bpp_accounts WHERE game_account_id = ?1`
      )
        .bind(official.id)
        .first<{ account_id: string }>()
    ]);
  } catch {
    throw apiError('storage_unavailable', 'Account storage is unavailable');
  }
  if (code === null) throw invalidBindCode();
  // Leaves the code in place, so the player can unlink the other account and retry.
  if (linkedTo !== null && linkedTo.account_id !== code.account_id) {
    throw alreadyBound();
  }

  const deviceToken = generateDeviceToken();
  const tokenHash = await sha256Hex(deviceToken);
  const liveCode = `EXISTS (
    SELECT 1 FROM bpp_bind_codes
    WHERE code_hash = ?2 AND account_id = ?1 AND expires_at_ms >= ?3
  )`;
  let results: D1Result[];
  try {
    results = await env.DB.batch([
      env.DB.prepare(
        `UPDATE bpp_accounts
         SET game_account_id = ?4, game_user_name = ?5, linked_at_ms = ?3, updated_at_ms = ?3
         WHERE account_id = ?1 AND game_account_id IS NULL AND ${liveCode}`
      ).bind(code.account_id, codeHash, now, official.id, official.userName),
      env.DB.prepare(
        `INSERT INTO bpp_device_tokens (token_hash, account_id, game_account_id, created_at_ms)
         SELECT ?4, account_id, game_account_id, ?3
         FROM bpp_accounts
         WHERE account_id = ?1 AND game_account_id = ?5 AND ${liveCode}`
      ).bind(code.account_id, codeHash, now, tokenHash, official.id),
      env.DB.prepare(`DELETE FROM bpp_bind_codes WHERE code_hash = ?1`).bind(
        codeHash
      )
    ]);
  } catch {
    // The batch rolled back as a whole, so the code survives. The only
    // constraint it can break is the game account's UNIQUE link.
    const raced = await env.DB.prepare(
      `SELECT account_id FROM bpp_accounts WHERE game_account_id = ?1`
    )
      .bind(official.id)
      .first<{ account_id: string }>()
      .catch(() => null);
    if (raced !== null && raced.account_id !== code.account_id) {
      throw alreadyBound();
    }
    throw apiError('storage_unavailable', 'Failed to link game account');
  }
  if ((results[1]?.meta?.changes ?? 0) !== 1) throw invalidBindCode();

  return {
    device_token: deviceToken,
    user_name: official.userName,
    email_masked: maskEmail(code.email)
  };
}

export async function issueAccountDeviceToken(
  request: Request,
  env: Env
): Promise<AccountDevicesResponse> {
  await enforceAccountRateLimit(request, env);
  const body = await readJsonObject(request);
  const official = await verifyClaim(readGameAccountClaim(body));

  let account: { account_id: string; email: string } | null;
  try {
    account = await env.DB.prepare(
      `SELECT account_id, email FROM bpp_accounts WHERE game_account_id = ?1`
    )
      .bind(official.id)
      .first<{ account_id: string; email: string }>();
  } catch {
    throw apiError('storage_unavailable', 'Account storage is unavailable');
  }
  if (account === null) {
    throw apiError(
      'account_not_bound',
      'Game account is not linked to a BPP Account'
    );
  }

  const now = Date.now();
  const deviceToken = generateDeviceToken();
  const tokenHash = await sha256Hex(deviceToken);
  try {
    await env.DB.batch([
      env.DB.prepare(
        `UPDATE bpp_accounts SET game_user_name = ?1, updated_at_ms = ?2 WHERE account_id = ?3`
      ).bind(official.userName, now, account.account_id),
      env.DB.prepare(
        `INSERT INTO bpp_device_tokens (token_hash, account_id, game_account_id, created_at_ms)
         VALUES (?1, ?2, ?3, ?4)`
      ).bind(tokenHash, account.account_id, official.id, now)
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
    user_name: account.gameUserName ?? '',
    email_masked: maskEmail(account.email)
  };
}

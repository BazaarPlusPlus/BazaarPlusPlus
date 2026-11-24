import type { Env } from '../env';
import { apiError } from '../errors';
import { clientRateLimitKey } from '../http/request';
import { DEVICE_TOKEN_PREFIX, sha256Hex } from './account-crypto';

export type ShadowAuthOutcome = 'absent' | 'match' | 'mismatch' | 'invalid';

export interface ShadowAuthResult {
  readonly status: ShadowAuthOutcome;
  // The game account ID the token is linked to; set only on `match`.
  readonly verifiedAccountId: string | null;
  // The BPP Account that owns the token; set only on `match`.
  readonly accountId: string | null;
  readonly linkedAtMs: number | null;
}

export interface AuthenticatedDeviceAccount {
  readonly accountId: string;
  readonly gameAccountId: string;
  readonly email: string;
  readonly gameUserName: string | null;
}

const VALID_DEVICE_TOKEN = /^bppd_[A-Za-z0-9_-]{43}$/;

const UNVERIFIED = {
  verifiedAccountId: null,
  accountId: null,
  linkedAtMs: null
};

export function extractBearerToken(request: Request): string | null {
  const header = request.headers.get('Authorization');
  if (header === null || !header.startsWith('Bearer ')) return null;
  const token = header.slice(7).trim();
  return token.length > 0 ? token : null;
}

export async function enforceAccountRateLimit(
  request: Request,
  env: Env
): Promise<void> {
  await enforceRateLimit(env.ACCOUNT_RATE_LIMITER, clientRateLimitKey(request));
}

export async function enforceRateLimit(
  limiter: RateLimit,
  key: string
): Promise<void> {
  let outcome: RateLimitOutcome;
  try {
    outcome = await limiter.limit({ key });
  } catch {
    throw apiError(
      'storage_unavailable',
      'Account rate limiter is unavailable'
    );
  }
  if (!outcome.success) {
    throw apiError('rate_limited', 'Account request rate exceeded', {
      headers: { 'Retry-After': '60' }
    });
  }
}

// A token authenticates only while its account is still linked to the game
// account the token was issued for.
const DEVICE_TOKEN_ACCOUNT_SQL = `
  SELECT a.account_id, a.game_account_id, a.email, a.game_user_name, a.linked_at_ms
  FROM bpp_device_tokens AS d
  JOIN bpp_accounts AS a
    ON a.account_id = d.account_id AND a.game_account_id = d.game_account_id
  WHERE d.token_hash = ?1`;

interface DeviceTokenAccountRow {
  account_id: string;
  game_account_id: string;
  email: string;
  game_user_name: string | null;
  linked_at_ms: number | null;
}

export async function evaluateDeviceTokenShadowAuth(
  request: Request,
  db: D1Database,
  claimedAccountId: string
): Promise<ShadowAuthResult> {
  const token = extractBearerToken(request);
  if (token === null) {
    return { status: 'absent', ...UNVERIFIED };
  }
  if (
    !token.startsWith(DEVICE_TOKEN_PREFIX) ||
    !VALID_DEVICE_TOKEN.test(token)
  ) {
    return { status: 'invalid', ...UNVERIFIED };
  }
  try {
    const tokenHash = await sha256Hex(token);
    const row = await db
      .prepare(DEVICE_TOKEN_ACCOUNT_SQL)
      .bind(tokenHash)
      .first<DeviceTokenAccountRow>();
    if (row === null) {
      return { status: 'invalid', ...UNVERIFIED };
    }
    if (row.game_account_id !== claimedAccountId) {
      return { status: 'mismatch', ...UNVERIFIED };
    }
    return {
      status: 'match',
      verifiedAccountId: row.game_account_id,
      accountId: row.account_id,
      linkedAtMs: row.linked_at_ms
    };
  } catch {
    // Shadow authentication never blocks Bundle ingest or Ghost discovery.
    return { status: 'invalid', ...UNVERIFIED };
  }
}

export async function authenticateDeviceToken(
  request: Request,
  db: D1Database
): Promise<AuthenticatedDeviceAccount> {
  const token = extractBearerToken(request);
  if (
    token === null ||
    !token.startsWith(DEVICE_TOKEN_PREFIX) ||
    !VALID_DEVICE_TOKEN.test(token)
  ) {
    throw apiError('unauthorized', 'A valid device token is required');
  }
  const tokenHash = await sha256Hex(token);
  let row: DeviceTokenAccountRow | null;
  try {
    row = await db
      .prepare(DEVICE_TOKEN_ACCOUNT_SQL)
      .bind(tokenHash)
      .first<DeviceTokenAccountRow>();
  } catch {
    throw apiError('storage_unavailable', 'Account storage is unavailable');
  }
  if (row === null) {
    throw apiError('unauthorized', 'A valid device token is required');
  }
  return {
    accountId: row.account_id,
    gameAccountId: row.game_account_id,
    email: row.email,
    gameUserName: row.game_user_name
  };
}

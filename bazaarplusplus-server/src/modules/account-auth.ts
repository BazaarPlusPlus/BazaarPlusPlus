import type { Env } from '../env';
import { apiError } from '../errors';
import { clientRateLimitKey } from '../http/request';
import { DEVICE_TOKEN_PREFIX, sha256Hex } from './account-crypto';

export type ShadowAuthOutcome = 'absent' | 'match' | 'mismatch' | 'invalid';

export interface ShadowAuthResult {
  readonly status: ShadowAuthOutcome;
  readonly verifiedAccountId: string | null;
}

export interface AuthenticatedDeviceAccount {
  readonly gameAccountId: string;
  readonly email: string;
  readonly userName: string;
}

const VALID_DEVICE_TOKEN = /^bppd_[A-Za-z0-9_-]{43}$/;

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
  let outcome: RateLimitOutcome;
  try {
    outcome = await env.ACCOUNT_RATE_LIMITER.limit({
      key: clientRateLimitKey(request)
    });
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

export async function evaluateDeviceTokenShadowAuth(
  request: Request,
  db: D1Database,
  claimedAccountId: string
): Promise<ShadowAuthResult> {
  const token = extractBearerToken(request);
  if (token === null) {
    return { status: 'absent', verifiedAccountId: null };
  }
  if (
    !token.startsWith(DEVICE_TOKEN_PREFIX) ||
    !VALID_DEVICE_TOKEN.test(token)
  ) {
    return { status: 'invalid', verifiedAccountId: null };
  }
  try {
    const tokenHash = await sha256Hex(token);
    const row = await db
      .prepare(
        `SELECT game_account_id FROM bpp_device_tokens WHERE token_hash = ?1`
      )
      .bind(tokenHash)
      .first<{ game_account_id: string }>();
    if (row === null) {
      return { status: 'invalid', verifiedAccountId: null };
    }
    if (row.game_account_id !== claimedAccountId) {
      return { status: 'mismatch', verifiedAccountId: null };
    }
    return { status: 'match', verifiedAccountId: row.game_account_id };
  } catch {
    // Shadow authentication never blocks Bundle ingest or Ghost discovery.
    return { status: 'invalid', verifiedAccountId: null };
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
  let row: {
    game_account_id: string;
    email: string;
    user_name: string;
  } | null;
  try {
    row = await db
      .prepare(
        `SELECT a.game_account_id, a.email, a.user_name
         FROM bpp_device_tokens AS d
         JOIN bpp_accounts AS a ON a.game_account_id = d.game_account_id
         WHERE d.token_hash = ?1`
      )
      .bind(tokenHash)
      .first<{
        game_account_id: string;
        email: string;
        user_name: string;
      }>();
  } catch {
    throw apiError('storage_unavailable', 'Account storage is unavailable');
  }
  if (row === null) {
    throw apiError('unauthorized', 'A valid device token is required');
  }
  return {
    gameAccountId: row.game_account_id,
    email: row.email,
    userName: row.user_name
  };
}

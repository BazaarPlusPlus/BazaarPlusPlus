import type { Env } from '../env';
import { apiError } from '../errors';
import { clientRateLimitKey } from '../http/request';

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

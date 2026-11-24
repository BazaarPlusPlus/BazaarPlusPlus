import type { Env } from '../env';
import { apiError } from '../errors';

// Hardcoded verification endpoint; never accept a client-supplied URL.
export const TURNSTILE_SITEVERIFY_URL =
  'https://challenges.cloudflare.com/turnstile/v0/siteverify';

const SITEVERIFY_TIMEOUT_MS = 10_000;
// Cloudflare documents tokens up to 2048 characters.
const MAX_TOKEN_LENGTH = 2048;
// Siteverify reports these when our secret is wrong: a server fault, not the visitor's.
const SECRET_ERRORS = new Set(['missing-input-secret', 'invalid-input-secret']);

function captchaFailed(): ReturnType<typeof apiError> {
  return apiError('captcha_failed', 'Bot check failed');
}

/** The form a token was solved for; the site sets it as the widget's `action`. */
export type TurnstileAction = 'login' | 'register' | 'reset';

/**
 * Checks the body's `turnstile_token` with Cloudflare siteverify, or throws
 * captcha_failed, captcha_unavailable, or internal_error (secret missing or
 * rejected). The token must have been solved for this form's `action`, on the
 * host this request came to.
 */
export async function verifyTurnstile(
  request: Request,
  env: Env,
  body: Record<string, unknown>,
  action: TurnstileAction
): Promise<void> {
  const secret = env.TURNSTILE_SECRET_KEY?.trim();
  if (!secret) {
    throw apiError('internal_error', 'Turnstile secret is not configured');
  }
  const token = body.turnstile_token;
  if (
    typeof token !== 'string' ||
    token.length === 0 ||
    token.length > MAX_TOKEN_LENGTH
  ) {
    throw captchaFailed();
  }

  const form = new FormData();
  form.append('secret', secret);
  form.append('response', token);
  const remoteIp = request.headers.get('CF-Connecting-IP');
  if (remoteIp !== null) form.append('remoteip', remoteIp);

  let outcome: unknown;
  try {
    const response = await fetch(TURNSTILE_SITEVERIFY_URL, {
      method: 'POST',
      body: form,
      signal: AbortSignal.timeout(SITEVERIFY_TIMEOUT_MS)
    });
    if (response.status !== 200) throw new Error('siteverify status');
    outcome = await response.json();
  } catch {
    throw apiError('captcha_unavailable', 'Bot check is unavailable');
  }
  if (typeof outcome !== 'object' || outcome === null) {
    throw apiError('captcha_unavailable', 'Bot check is unavailable');
  }

  const result = outcome as {
    success?: unknown;
    hostname?: unknown;
    action?: unknown;
    'error-codes'?: unknown;
  };
  const errorCodes = Array.isArray(result['error-codes'])
    ? result['error-codes']
    : [];
  if (errorCodes.some((code) => SECRET_ERRORS.has(code))) {
    throw apiError('internal_error', 'Turnstile secret was rejected');
  }
  if (
    result.success !== true ||
    result.action !== action ||
    result.hostname !== new URL(request.url).hostname
  ) {
    throw captchaFailed();
  }
}

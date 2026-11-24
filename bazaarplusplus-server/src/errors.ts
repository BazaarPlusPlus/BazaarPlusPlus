import contract from '../contracts/mod-api-errors.json';

export class HttpError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    message: string,
    readonly retryable = false,
    readonly details?: Record<string, unknown>,
    readonly headers?: HeadersInit
  ) {
    super(message);
    this.name = 'HttpError';
  }
}

// TypeScript widens JSON string values, so the literal union needs a runtime check against the contract.
export const API_ERROR_CODES = [
  'invalid_content_length',
  'invalid_content_digest',
  'invalid_query',
  'invalid_body',
  'window_not_settled',
  'unauthorized',
  'game_token_rejected',
  'invalid_verification_code',
  'insufficient_scope',
  'not_found',
  'account_not_bound',
  'method_not_allowed',
  'bundle_id_conflict',
  'run_already_bundled',
  'account_mismatch',
  'email_already_bound',
  'game_account_already_bound',
  'window_expired',
  'content_length_required',
  'bundle_too_large',
  'unsupported_content_type',
  'invalid_bundle',
  'unsupported_bundle_version',
  'unsupported_run_format',
  'bundle_digest_mismatch',
  'segment_digest_mismatch',
  'run_without_battles',
  'rate_limited',
  'internal_error',
  'game_api_unavailable',
  'storage_unavailable'
] as const;

export type ApiErrorCode = (typeof API_ERROR_CODES)[number];

const API_ERRORS = new Map(
  contract.codes.map(({ code, status, retryable }) => [
    code,
    { status, retryable }
  ])
);
if (
  API_ERRORS.size !== API_ERROR_CODES.length ||
  API_ERROR_CODES.some((code) => !API_ERRORS.has(code))
) {
  throw new Error('ApiErrorCode differs from contracts/mod-api-errors.json');
}

export function apiError(
  code: ApiErrorCode,
  message: string,
  extras: { details?: Record<string, unknown>; headers?: HeadersInit } = {}
): HttpError {
  const { status, retryable } = API_ERRORS.get(code) as {
    status: number;
    retryable: boolean;
  };
  return new HttpError(
    status,
    code,
    message,
    retryable,
    extras.details,
    extras.headers
  );
}

export function invalidBundle(reason: string, message: string): HttpError {
  return apiError('invalid_bundle', message, { details: { reason } });
}

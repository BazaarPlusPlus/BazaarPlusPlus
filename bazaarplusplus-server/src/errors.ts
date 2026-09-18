export class HttpError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    message: string,
    readonly retryable = false,
    readonly details?: Record<string, unknown>,
    readonly headers?: HeadersInit,
  ) {
    super(message);
    this.name = "HttpError";
  }
}

export function invalidBundle(reason: string, message: string): HttpError {
  return new HttpError(422, "invalid_bundle", message, false, { reason });
}

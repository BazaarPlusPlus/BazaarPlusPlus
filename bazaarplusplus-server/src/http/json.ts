export function jsonResponse(body: unknown, init: ResponseInit & { requestId: string }): Response {
  const headers = new Headers(init.headers);
  headers.set("Content-Type", "application/json; charset=utf-8");
  headers.set("Cache-Control", "no-store");
  headers.set("X-Request-Id", init.requestId);

  return new Response(JSON.stringify(body), {
    ...init,
    headers,
  });
}

export function jsonError(
  error: {
    code: string;
    message: string;
    retryable: boolean;
    details?: Record<string, unknown>;
  },
  status: number,
  requestId: string,
  headers?: HeadersInit,
): Response {
  return jsonResponse(
    {
      error: {
        ...error,
        request_id: requestId,
      },
    },
    {
      status,
      requestId,
      headers,
    },
  );
}

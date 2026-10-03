import { env } from "cloudflare:test";
import { afterEach, describe, expect, test, vi } from "vitest";

import worker from "../src/index";
import { makeBundleFixture, uploadRequest } from "./fixtures/bundle";

const ORIGIN = "https://mod-api-v5.bazaarplusplus.com";

afterEach(() => vi.restoreAllMocks());

function collection(query: string, bindings: Cloudflare.Env = env): Promise<Response> {
  return worker.fetch(
    new Request(`${ORIGIN}/bundles${query}`, {
      headers: { Authorization: `Bearer ${env.BUNDLE_SYNC_TOKEN}`, "CF-Ray": "shell-ray" },
    }),
    bindings,
  );
}

test("GET /health reports V5 liveness without dependency access", async () => {
  const response = await worker.fetch(new Request(`${ORIGIN}/health`), {} as Cloudflare.Env);

  expect(response.status).toBe(200);
  expect(response.headers.get("content-type")).toBe("application/json; charset=utf-8");
  expect(response.headers.get("cache-control")).toBe("no-store");
  expect(response.headers.get("x-request-id")).toBeTruthy();
  expect(response.headers.get("access-control-allow-origin")).toBe("*");

  const body = (await response.json()) as {
    status: string;
    server_time_ms: number;
  };
  expect(body).toMatchObject({ status: "ok" });
  expect(Number.isSafeInteger(body.server_time_ms)).toBe(true);
});

test("an unknown route returns the canonical not_found error", async () => {
  const response = await worker.fetch(
    new Request("https://mod-api-v5.bazaarplusplus.com/unknown"),
    {} as Cloudflare.Env,
  );

  expect(response.status).toBe(404);
  expect(response.headers.get("content-type")).toBe("application/json; charset=utf-8");
  expect(response.headers.get("cache-control")).toBe("no-store");

  const body = await response.json();
  expect(body).toEqual({
    error: {
      code: "not_found",
      message: "Route not found",
      retryable: false,
      request_id: response.headers.get("x-request-id"),
    },
  });
});

test("a known route with the wrong method returns method_not_allowed", async () => {
  const response = await worker.fetch(
    new Request("https://mod-api-v5.bazaarplusplus.com/health", {
      method: "POST",
    }),
    {} as Cloudflare.Env,
  );

  expect(response.status).toBe(405);
  expect(response.headers.get("allow")).toBe("GET");
  expect(await response.json()).toEqual({
    error: {
      code: "method_not_allowed",
      message: "Method not allowed",
      retryable: false,
      request_id: response.headers.get("x-request-id"),
    },
  });
});

describe("the public route table", () => {
  test.each([
    ["/health", "GET"],
    ["/bundles", "GET, POST"],
    ["/ghost-battles", "GET"],
    ["/bazaardb/deliveries/claim", "POST"],
    ["/bazaardb/deliveries/settle", "POST"],
  ])("OPTIONS %s only advertises the real route", async (path, allow) => {
    const response = await worker.fetch(
      new Request(`https://mod-api-v5.bazaarplusplus.com${path}`, {
        method: "OPTIONS",
      }),
      env,
    );

    expect(response.status).toBe(204);
    expect(response.body).toBeNull();
    expect(response.headers.get("allow")).toBe(allow);
    expect(response.headers.get("access-control-allow-methods")).toBe(allow);
    const cors = path === "/health" || path === "/ghost-battles";
    expect([...response.headers.keys()]).toEqual([
      "access-control-allow-headers",
      "access-control-allow-methods",
      ...(cors ? ["access-control-allow-origin"] : []),
      "access-control-max-age",
      "allow",
    ]);
  });

  test("OPTIONS does not make an unknown route appear available", async () => {
    const response = await worker.fetch(
      new Request("https://mod-api-v5.bazaarplusplus.com/not-a-route", {
        method: "OPTIONS",
      }),
      env,
    );

    expect(response.status).toBe(404);
  });
});

describe("service-token scopes", () => {
  test("Bundle collection rejects a missing token before query parsing", async () => {
    const response = await worker.fetch(
      new Request("https://mod-api-v5.bazaarplusplus.com/bundles"),
      env,
    );

    expect(response.status).toBe(401);
    expect((await response.json()) as object).toMatchObject({
      error: { code: "unauthorized", retryable: false },
    });
  });

  test("Bundle collection rejects a valid BazaarDB token with insufficient_scope", async () => {
    const response = await worker.fetch(
      new Request("https://mod-api-v5.bazaarplusplus.com/bundles", {
        headers: {
          Authorization: `Bearer ${env.BAZAARDB_DELIVERY_TOKEN}`,
        },
      }),
      env,
    );

    expect(response.status).toBe(403);
    expect((await response.json()) as object).toMatchObject({
      error: { code: "insufficient_scope", retryable: false },
    });
  });

  test("BazaarDB routes reject a valid Bundle Sync token with insufficient_scope", async () => {
    const response = await worker.fetch(
      new Request("https://mod-api-v5.bazaarplusplus.com/bazaardb/deliveries/claim", {
        method: "POST",
        headers: {
          Authorization: `Bearer ${env.BUNDLE_SYNC_TOKEN}`,
        },
      }),
      env,
    );

    expect(response.status).toBe(403);
    expect((await response.json()) as object).toMatchObject({
      error: { code: "insufficient_scope", retryable: false },
    });
  });
});

describe("route shell outcomes", () => {
  test("forwards route error headers and uses the CF-Ray request ID in the envelope", async () => {
    const response = await worker.fetch(
      new Request(`${ORIGIN}/ghost-battles?player_account_id=account-local`, {
        headers: { "CF-Ray": "limited-ray" },
      }),
      { ...env, GHOST_BATTLE_RATE_LIMITER: { limit: async () => ({ success: false }) } },
    );

    expect(response.status).toBe(429);
    expect(response.headers.get("retry-after")).toBe("60");
    expect(response.headers.get("x-request-id")).toBe("limited-ray");
    expect(await response.json()).toEqual({
      error: {
        code: "rate_limited",
        message: "Ghost Battle request rate exceeded",
        retryable: true,
        request_id: "limited-ray",
      },
    });
  });

  test("logs classified 5xx errors for alerting but stays silent on 4xx", async () => {
    const errorLog = vi.spyOn(console, "error").mockImplementation(() => undefined);
    const failingDb = {
      ...env,
      DB: {
        prepare() {
          throw new Error("injected D1 query failure");
        },
      },
    } as unknown as Cloudflare.Env;

    expect((await collection(`?available_from_ms=${Date.now() - 120_000}`, failingDb)).status).toBe(
      503,
    );
    expect(errorLog).toHaveBeenCalledWith(
      JSON.stringify({
        event: "worker.http_error",
        request_id: "shell-ray",
        route: "/bundles",
        status: 503,
        code: "storage_unavailable",
      }),
    );

    errorLog.mockClear();
    expect((await collection("?unknown=1")).status).toBe(400);
    expect(errorLog).not.toHaveBeenCalled();
  });

  test.each([
    { name: "malformed token", overrides: { BUNDLE_SYNC_TOKEN: "too-short" } },
    { name: "missing Bundle Sync token", overrides: { BUNDLE_SYNC_TOKEN: undefined } },
    { name: "missing BazaarDB token", overrides: { BAZAARDB_DELIVERY_TOKEN: undefined } },
    {
      name: "both tokens missing",
      overrides: { BUNDLE_SYNC_TOKEN: undefined, BAZAARDB_DELIVERY_TOKEN: undefined },
    },
  ])("rejects and logs invalid service configuration: $name", async ({ overrides }) => {
    const errorLog = vi.spyOn(console, "error").mockImplementation(() => undefined);
    const response = await collection("?available_from_ms=0", {
      ...env,
      ...overrides,
    } as Cloudflare.Env);

    expect(response.status).toBe(500);
    expect(await response.json()).toEqual({
      error: {
        code: "internal_error",
        message: "Service token configuration is invalid",
        retryable: true,
        request_id: "shell-ray",
      },
    });
    expect(errorLog).toHaveBeenCalledOnce();
    expect(errorLog).toHaveBeenCalledWith(
      JSON.stringify({
        event: "worker.http_error",
        request_id: "shell-ray",
        route: "/bundles",
        status: 500,
        code: "invalid_configuration",
      }),
    );
  });

  test("applies route CORS to route errors but not to non-CORS routes or shell errors", async () => {
    const ghostError = await worker.fetch(
      new Request(`${ORIGIN}/ghost-battles?bad=1`, {
        headers: { "CF-Connecting-IP": "192.0.2.44" },
      }),
      env,
    );
    expect(ghostError.status).toBe(400);
    expect(ghostError.headers.get("access-control-allow-origin")).toBe("*");

    for (const response of [
      await collection("?unknown=1"),
      await worker.fetch(new Request(`${ORIGIN}/missing`), env),
      await worker.fetch(new Request(`${ORIGIN}/ghost-battles`, { method: "POST" }), env),
    ]) {
      expect(response.status).toBeGreaterThanOrEqual(400);
      expect(response.headers.has("access-control-allow-origin")).toBe(false);
    }
  });

  test("logs and maps an unclassified exception to the canonical 500 envelope", async () => {
    const errorLog = vi.spyOn(console, "error").mockImplementation(() => undefined);
    const fixture = await makeBundleFixture({
      bundleId: "01J00000000000000000000901",
      runId: "shell-unclassified-run",
      battles: [],
    });
    const headers = new Headers(fixture.headers);
    headers.set("CF-Ray", "error-ray");
    // A conditional PUT conflict whose read-back throws is not an HttpError.
    const response = await worker.fetch(uploadRequest(fixture.body, headers), {
      ...env,
      BUNDLE_BUCKET: {
        put: async () => null,
        get: async () => {
          throw new TypeError("secret implementation detail");
        },
      },
    } as unknown as Cloudflare.Env);

    expect(response.status).toBe(500);
    expect(response.headers.get("x-request-id")).toBe("error-ray");
    expect(await response.json()).toEqual({
      error: {
        code: "internal_error",
        message: "An internal error occurred",
        retryable: true,
        request_id: "error-ray",
      },
    });
    expect(errorLog).toHaveBeenCalledWith(
      JSON.stringify({
        event: "worker.internal_error",
        request_id: "error-ray",
        route: "/bundles",
        error_name: "TypeError",
        reason: "unclassified_exception",
      }),
    );
  });
});

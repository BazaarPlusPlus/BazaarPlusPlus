import { afterEach, describe, expect, test, vi } from "vitest";

import type { Env } from "../../src/env";
import { HttpError } from "../../src/errors";
import type { HandlerDeps } from "../../src/http/deps";
import {
  createFetchHandler,
  type RouteDefinition,
  type RouteHandler,
} from "../../src/http/route-shell";

const SYNC_TOKEN = "s".repeat(43);
const DELIVERY_TOKEN = "d".repeat(43);
const deps: HandlerDeps = {
  signer: {
    async sign() {
      throw new Error("signer must not be called");
    },
  },
  now: () => 1_785_628_800_000,
};

function testEnv(overrides: Partial<Env> = {}): Env {
  return {
    BUNDLE_SYNC_TOKEN: SYNC_TOKEN,
    BAZAARDB_DELIVERY_TOKEN: DELIVERY_TOKEN,
    ...overrides,
  } as Env;
}

function route(
  path: string,
  handler: RouteHandler,
  options: Partial<Omit<RouteDefinition, "path" | "handler">> = {},
): RouteDefinition {
  return { path, method: "GET", ...options, handler };
}

function handlerFor(routes: readonly RouteDefinition[], onCreateDeps?: () => void) {
  return createFetchHandler(routes, {
    createDeps: () => {
      onCreateDeps?.();
      return deps;
    },
  });
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe("route shell", () => {
  test("forwards HttpError headers and uses one request ID in the envelope", async () => {
    const fetch = handlerFor([
      route("/error", async () => {
        throw new HttpError(429, "rate_limited", "Slow down", true, undefined, {
          "Retry-After": "60",
        });
      }),
    ]);
    const response = await fetch(
      new Request("https://example.test/error", { headers: { "CF-Ray": "fixture-ray" } }),
      testEnv(),
    );

    expect(response.status).toBe(429);
    expect(response.headers.get("retry-after")).toBe("60");
    expect(response.headers.get("x-request-id")).toBe("fixture-ray");
    expect(await response.json()).toEqual({
      error: {
        code: "rate_limited",
        message: "Slow down",
        retryable: true,
        request_id: "fixture-ray",
      },
    });
  });

  test("logs and maps an unclassified exception to the canonical 500 envelope", async () => {
    const errorLog = vi.spyOn(console, "error").mockImplementation(() => undefined);
    const fetch = handlerFor([
      route("/explode", async () => {
        throw new TypeError("secret implementation detail");
      }),
    ]);
    const response = await fetch(
      new Request("https://example.test/explode", { headers: { "CF-Ray": "error-ray" } }),
      testEnv(),
    );

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
        route: "/explode",
        error_name: "TypeError",
        reason: "unclassified_exception",
      }),
    );
  });

  test("logs classified 5xx errors for alerting but stays silent on 4xx", async () => {
    const errorLog = vi.spyOn(console, "error").mockImplementation(() => undefined);
    const fetch = handlerFor([
      route("/unavailable", async () => {
        throw new HttpError(503, "storage_unavailable", "R2 Bundle write failed", true);
      }),
      route("/rejected", async () => {
        throw new HttpError(422, "invalid_bundle", "Bad Bundle");
      }),
    ]);

    const unavailable = await fetch(
      new Request("https://example.test/unavailable", { headers: { "CF-Ray": "alert-ray" } }),
      testEnv(),
    );
    expect(unavailable.status).toBe(503);
    expect(errorLog).toHaveBeenCalledWith(
      JSON.stringify({
        event: "worker.http_error",
        request_id: "alert-ray",
        route: "/unavailable",
        status: 503,
        code: "storage_unavailable",
      }),
    );

    errorLog.mockClear();
    const rejected = await fetch(new Request("https://example.test/rejected"), testEnv());
    expect(rejected.status).toBe(422);
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
    const handler = vi.fn(async () => ({ status: 200, body: {} }));
    const createDeps = vi.fn();
    const fetch = handlerFor([route("/protected", handler, { auth: "bundle_sync" })], createDeps);

    const response = await fetch(
      new Request("https://example.test/protected", { headers: { "CF-Ray": "config-ray" } }),
      testEnv(overrides),
    );
    expect(response.status).toBe(500);
    expect(response.headers.get("x-request-id")).toBe("config-ray");
    expect(await response.json()).toEqual({
      error: {
        code: "internal_error",
        message: "Service token configuration is invalid",
        retryable: true,
        request_id: "config-ray",
      },
    });
    expect(errorLog).toHaveBeenCalledOnce();
    expect(errorLog).toHaveBeenCalledWith(
      JSON.stringify({
        event: "worker.http_error",
        request_id: "config-ray",
        route: "/protected",
        status: 500,
        code: "invalid_configuration",
      }),
    );
    expect(createDeps).not.toHaveBeenCalled();
    expect(handler).not.toHaveBeenCalled();
  });

  test("applies route CORS to handler outcomes but not shell errors or non-CORS routes", async () => {
    const fetch = handlerFor([
      route("/cors-ok", async () => ({ status: 200, body: { ok: true } }), { cors: true }),
      route(
        "/cors-http-error",
        async () => {
          throw new HttpError(400, "bad", "Bad request");
        },
        { cors: true },
      ),
      route(
        "/cors-explode",
        async () => {
          throw new Error("boom");
        },
        { cors: true },
      ),
      route("/private", async () => ({ status: 200, body: { ok: true } })),
    ]);
    vi.spyOn(console, "error").mockImplementation(() => undefined);

    for (const path of ["/cors-ok", "/cors-http-error", "/cors-explode"]) {
      const response = await fetch(new Request(`https://example.test${path}`), testEnv());
      expect(response.headers.get("access-control-allow-origin")).toBe("*");
    }
    for (const request of [
      new Request("https://example.test/private"),
      new Request("https://example.test/missing"),
      new Request("https://example.test/cors-ok", { method: "POST" }),
    ]) {
      const response = await fetch(request, testEnv());
      expect(response.headers.has("access-control-allow-origin")).toBe(false);
    }
  });

  test("returns a raw OPTIONS response with exactly the contract headers", async () => {
    const fetch = handlerFor([
      route("/cors", async () => ({ status: 200, body: null }), { cors: true }),
      route("/plain", async () => ({ status: 200, body: null })),
    ]);
    const cors = await fetch(
      new Request("https://example.test/cors", { method: "OPTIONS" }),
      testEnv(),
    );
    const plain = await fetch(
      new Request("https://example.test/plain", { method: "OPTIONS" }),
      testEnv(),
    );

    expect(cors.status).toBe(204);
    expect(cors.body).toBeNull();
    expect([...cors.headers.keys()]).toEqual([
      "access-control-allow-headers",
      "access-control-allow-methods",
      "access-control-allow-origin",
      "access-control-max-age",
      "allow",
    ]);
    expect([...plain.headers.keys()]).toEqual([
      "access-control-allow-headers",
      "access-control-allow-methods",
      "access-control-max-age",
      "allow",
    ]);
  });

  test("authenticates before creating deps or invoking the handler and maps every denial", async () => {
    let depsCreated = false;
    let handlerCalled = false;
    const fetch = handlerFor(
      [
        route(
          "/protected",
          async () => {
            handlerCalled = true;
            return { status: 200, body: null };
          },
          { auth: "bundle_sync" },
        ),
      ],
      () => {
        depsCreated = true;
      },
    );
    const unauthorized = await fetch(new Request("https://example.test/protected"), testEnv());
    const insufficient = await fetch(
      new Request("https://example.test/protected", {
        headers: { Authorization: `Bearer ${DELIVERY_TOKEN}` },
      }),
      testEnv(),
    );
    const invalidConfiguration = await fetch(
      new Request("https://example.test/protected"),
      testEnv({ BAZAARDB_DELIVERY_TOKEN: SYNC_TOKEN }),
    );

    expect([unauthorized.status, insufficient.status, invalidConfiguration.status]).toEqual([
      401, 403, 500,
    ]);
    expect(await unauthorized.json()).toMatchObject({
      error: { code: "unauthorized", message: "A valid service token is required" },
    });
    expect(await insufficient.json()).toMatchObject({
      error: {
        code: "insufficient_scope",
        message: "The service token does not grant access to this route",
      },
    });
    expect(await invalidConfiguration.json()).toMatchObject({
      error: {
        code: "internal_error",
        message: "Service token configuration is invalid",
        retryable: true,
      },
    });
    expect(depsCreated).toBe(false);
    expect(handlerCalled).toBe(false);
  });

  test("aggregates Allow methods in declaration order", async () => {
    const fetch = handlerFor([
      route("/both", async () => ({ status: 200, body: null })),
      route("/both", async () => ({ status: 200, body: null }), { method: "POST" }),
    ]);
    const response = await fetch(
      new Request("https://example.test/both", { method: "DELETE" }),
      testEnv(),
    );

    expect(response.status).toBe(405);
    expect(response.headers.get("allow")).toBe("GET, POST");
  });

  test("rejects duplicate methods and conflicting CORS policies during construction", () => {
    const ok = async () => ({ status: 200, body: null });
    expect(() => handlerFor([route("/duplicate", ok), route("/duplicate", ok)])).toThrow(
      "Duplicate route: GET /duplicate",
    );
    expect(() =>
      handlerFor([
        route("/cors-conflict", ok, { cors: true }),
        route("/cors-conflict", ok, { method: "POST" }),
      ]),
    ).toThrow("Routes for /cors-conflict have conflicting CORS policies");
  });
});

import { env } from "cloudflare:test";
import { describe, expect, test } from "vitest";

import worker from "../src/index";

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

test.each([
  "/bundles/01J00000000000000000000001",
  "/capabilities",
  "/installations",
  "/snapshots/upload",
  "/ghost-battles/battle-id/replay-link",
])("unsupported legacy or download path %s is not exposed", async (path) => {
  const response = await worker.fetch(
    new Request(`https://mod-api-v5.bazaarplusplus.com${path}`),
    env,
  );
  expect(response.status).toBe(404);
  expect((await response.json()) as object).toMatchObject({
    error: { code: "not_found" },
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
    expect(response.headers.get("allow")).toBe(allow);
    expect(response.headers.get("access-control-allow-methods")).toBe(allow);
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

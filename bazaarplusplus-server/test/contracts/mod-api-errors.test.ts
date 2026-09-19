import { env } from "cloudflare:test";
import { expect, test, vi } from "vitest";

import contract from "../../contracts/mod-api-errors.json";
import apiReference from "../../docs/api-reference.md?raw";
import type { Env } from "../../src/env";
import { createFetchHandler } from "../../src/http/route-shell";
import { V5_ROUTES } from "../../src/http/routes";
import { contentDigest, makeBundleFixture, sealBundle, uploadRequest } from "../fixtures/bundle";
import { createTestDeps } from "../fixtures/deps";

test("the API error table exactly projects the server-owned error contract", () => {
  const rows = [
    ...apiReference.matchAll(/^\| (\d+) \| `([^`]+)` \| `(true|false)` \| ([^|]+) \|$/gm),
  ].map(([, status, code, retryable, meaning]) => ({
    code,
    status: Number(status),
    retryable: retryable === "true",
    meaning: meaning.trim(),
  }));
  expect(rows).toEqual(contract.codes);
  expect(new Set(contract.codes.map(({ code }) => code)).size).toBe(contract.codes.length);
});

test("real HTTP failures emit exactly the declared codes, statuses, and retry flags", async () => {
  const now = 1_785_628_800_000;
  const fetch = createFetchHandler(V5_ROUTES, {
    createDeps: () => createTestDeps({ now: () => now }),
  });
  const observed = new Map<string, { code: string; status: number; retryable: boolean }>();
  async function record(request: Request, bindings: Env = env) {
    const response = await fetch(request, bindings);
    const body = await response.json<{ error: { code: string; retryable: boolean } }>();
    expect(body, `${request.method} ${request.url}`).toHaveProperty("error.code");
    const actual = {
      code: body.error.code,
      status: response.status,
      retryable: body.error.retryable,
    };
    expect(contract.codes.find(({ code }) => code === actual.code)).toMatchObject(actual);
    observed.set(actual.code, actual);
  }
  function request(path: string, init?: RequestInit) {
    return new Request(`https://worker.test${path}`, init);
  }
  function collection(query: string) {
    return request(`/bundles${query}`, {
      headers: { Authorization: `Bearer ${env.BUNDLE_SYNC_TOKEN}` },
    });
  }
  function delivery(action: string, body: string) {
    return request(`/bazaardb/deliveries/${action}`, {
      method: "POST",
      headers: {
        Authorization: `Bearer ${env.BAZAARDB_DELIVERY_TOKEN}`,
        "Content-Type": "application/json",
      },
      body,
    });
  }

  await record(request("/missing"));
  await record(request("/health", { method: "POST" }));
  await record(request("/bundles"));
  await record(
    request("/bundles", {
      headers: { Authorization: `Bearer ${env.BAZAARDB_DELIVERY_TOKEN}` },
    }),
  );
  await record(collection("?unknown=1"));
  await record(collection("?available_from_ms=0"));
  await record(collection(`?available_from_ms=${now - 120_000}&available_before_ms=${now}`));
  await record(delivery("claim", "{"));
  await record(delivery("claim", '{"limit":0}'));
  await record(delivery("settle", "{}"));
  await record(request("/ghost-battles?player_account_id=account-local"), {
    ...env,
    GHOST_BATTLE_RATE_LIMITER: { limit: async () => ({ success: false }) },
  });
  await record(request("/ghost-battles?player_account_id=account-local"), {
    ...env,
    GHOST_BATTLE_RATE_LIMITER: {
      limit: async () => {
        throw new Error("unavailable");
      },
    },
  });
  // Invalid service configuration emits internal_error, not the diagnostic log code.
  const errorLog = vi.spyOn(console, "error").mockImplementation(() => undefined);
  try {
    await record(request("/bundles"), { ...env, BUNDLE_SYNC_TOKEN: "invalid" });
  } finally {
    errorLog.mockRestore();
  }

  const fixture = await makeBundleFixture();
  for (const [name, value] of [
    ["Content-Type", "text/plain"],
    ["Content-Length", null],
    ["Content-Length", "invalid"],
    ["Content-Length", "8388608"],
    ["Content-Digest", "invalid"],
    ["Content-Digest", `sha-256=:${"A".repeat(43)}=:`],
  ] as const) {
    const headers = new Headers(fixture.headers);
    if (value === null) headers.delete(name);
    else headers.set(name, value);
    await record(uploadRequest(fixture.body, headers));
  }
  for (const mutation of ["magic", "version", "segment"] as const) {
    const body = fixture.body.slice();
    if (mutation === "magic") body[0] = 0;
    else if (mutation === "version") new DataView(body.buffer).setUint32(8, 4, false);
    else body[body.length - 1] ^= 0xff;
    const headers = new Headers(fixture.headers);
    headers.set("Content-Digest", await contentDigest(body));
    await record(uploadRequest(body, headers));
  }
  const manifest = structuredClone(fixture.manifest);
  (manifest.run as Record<string, unknown>).run_format_version = 4;
  const unsupportedRun = await sealBundle(
    manifest,
    new Uint8Array([0x1f, 0x8b, 0x08, 0, 5, 4, 3, 2, 1]),
  );
  await record(uploadRequest(unsupportedRun.body, unsupportedRun.headers));

  const stored = await fetch(uploadRequest(fixture.body, fixture.headers), env);
  expect(stored.status).toBe(201);
  for (const options of [{ runId: "different-run" }, { bundleId: "01J00000000000000000000002" }]) {
    const conflicting = await makeBundleFixture(options);
    await record(uploadRequest(conflicting.body, conflicting.headers));
  }

  expect([...observed.values()].sort((a, b) => a.code.localeCompare(b.code))).toEqual(
    contract.codes
      .map(({ code, status, retryable }) => ({ code, status, retryable }))
      .sort((a, b) => a.code.localeCompare(b.code)),
  );
});

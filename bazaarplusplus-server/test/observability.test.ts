import { env } from "cloudflare:test";
import { afterEach, expect, test, vi } from "vitest";

import worker from "../src/index";
import { logError, logEvent } from "../src/observability";
import { makeBundleFixture, uploadRequest } from "./fixtures/bundle";

afterEach(() => {
  vi.restoreAllMocks();
});

test("logEvent writes one line of structured JSON to stdout", () => {
  const log = vi.spyOn(console, "log").mockImplementation(() => undefined);

  logEvent("test.event", { request_id: "request-1", count: 2 });

  expect(log).toHaveBeenCalledOnce();
  expect(log).toHaveBeenCalledWith(
    JSON.stringify({ event: "test.event", request_id: "request-1", count: 2 }),
  );
  expect(log.mock.calls[0]?.[0]).not.toContain("\n");
});

test("logError writes structured JSON to stderr", () => {
  const log = vi.spyOn(console, "log").mockImplementation(() => undefined);
  const error = vi.spyOn(console, "error").mockImplementation(() => undefined);

  logError("test.error", { request_id: "request-2", reason: "failed" });

  expect(log).not.toHaveBeenCalled();
  expect(error).toHaveBeenCalledOnce();
  expect(error).toHaveBeenCalledWith(
    JSON.stringify({ event: "test.error", request_id: "request-2", reason: "failed" }),
  );
});

test("allowed production fields preserve their JSON bytes", () => {
  const log = vi.spyOn(console, "log").mockImplementation(() => undefined);
  const cases: Array<[string, Record<string, unknown>]> = [
    ["bundle.projection.duplicate", { bundle_id: "bundle-1", dropped: 1 }],
    [
      "bazaardb.claim",
      { request_id: "request-3", claim_id: "claim-1", item_count: 2, lease_ms: 60_000 },
    ],
    [
      "bazaardb.settle",
      { request_id: "request-4", claim_id: "claim-1", applied: 1, duplicate: 0, rejected: 0 },
    ],
    [
      "bundle.collection",
      {
        request_id: "request-5",
        available_from_ms: 1,
        available_before_ms: 2,
        row_count: 3,
        has_next: false,
      },
    ],
    ["bundle.orphan.invalid", { object_key: "bundles/object", reason: "invalid" }],
    [
      "bundle.ingest",
      {
        request_id: "request-6",
        bundle_id: "bundle-2",
        run_id: "run-1",
        bytes: 42,
        has_screenshot: true,
        outcome: "created",
      },
    ],
    [
      "worker.internal_error",
      { request_id: "request-7", route: "/bundles", error_name: "Error", reason: "failed" },
    ],
    [
      "ghost.discovery",
      { request_id: "request-8", account_hash: "hash", row_count: 1, limited: false },
    ],
  ];

  for (const [event, fields] of cases) {
    logEvent(event, fields);
  }

  expect(log.mock.calls.map(([line]) => line)).toEqual(
    cases.map(([event, fields]) => JSON.stringify({ event, ...fields })),
  );
});

test("exact forbidden fields are omitted and reported in encounter order", () => {
  const log = vi.spyOn(console, "log").mockImplementation(() => undefined);
  const forbiddenFields = [
    "account_id",
    "player_account_id",
    "uploader_account_id",
    "opponent_account_id",
    "authorization",
    "token",
    "secret",
    "password",
    "body",
    "download_url",
    "presigned_url",
    "url",
    "projection_json",
    "screenshot",
  ];

  logEvent("test.redaction", {
    safe: "retained",
    ...Object.fromEntries(forbiddenFields.map((key) => [key, `sensitive-${key}`])),
  });

  expect(JSON.parse(String(log.mock.calls[0]?.[0]))).toEqual({
    event: "test.redaction",
    safe: "retained",
    redacted_fields: forbiddenFields,
  });
});

test("forbidden suffixes are redacted without matching safe production names", () => {
  const log = vi.spyOn(console, "log").mockImplementation(() => undefined);

  logEvent("test.suffixes", {
    owner_account_id: "account",
    api_token: "token",
    signing_secret: "secret",
    object_url: "url",
    account_hash: "hash",
    object_key: "key",
    route: "/route",
    error_name: "Error",
    has_screenshot: true,
  });

  expect(JSON.parse(String(log.mock.calls[0]?.[0]))).toEqual({
    event: "test.suffixes",
    account_hash: "hash",
    object_key: "key",
    route: "/route",
    error_name: "Error",
    has_screenshot: true,
    redacted_fields: ["owner_account_id", "api_token", "signing_secret", "object_url"],
  });
});

test("string values containing X-Amz- are redacted", () => {
  const log = vi.spyOn(console, "log").mockImplementation(() => undefined);

  logEvent("test.presign", {
    before: "retained",
    capability: "https://example.com/object?X-Amz-Signature=secret",
    after: "retained-too",
  });

  expect(JSON.parse(String(log.mock.calls[0]?.[0]))).toEqual({
    event: "test.presign",
    before: "retained",
    after: "retained-too",
    redacted_fields: ["capability"],
  });
});

test("request_id remains optional", () => {
  const log = vi.spyOn(console, "log").mockImplementation(() => undefined);

  logEvent("test.no-request", { outcome: "complete" });

  expect(log).toHaveBeenCalledWith(
    JSON.stringify({ event: "test.no-request", outcome: "complete" }),
  );
});

test("caller-supplied redacted_fields cannot forge the audit list", () => {
  const log = vi.spyOn(console, "log").mockImplementation(() => undefined);

  logEvent("test.forged-redaction", {
    safe: true,
    redacted_fields: ["not-really-redacted"],
  });

  expect(JSON.parse(String(log.mock.calls[0]?.[0]))).toEqual({
    event: "test.forged-redaction",
    safe: true,
    redacted_fields: ["redacted_fields"],
  });
});

test("logging never throws for unserializable fields or failing sinks", () => {
  const circular: Record<string, unknown> = {};
  circular.self = circular;
  const log = vi.spyOn(console, "log").mockImplementation(() => undefined);

  expect(() => logEvent("test.circular", { circular })).not.toThrow();
  expect(JSON.parse(String(log.mock.calls[0]?.[0]))).toEqual({
    event: "test.circular",
    redacted_fields: ["circular"],
  });

  log.mockImplementation(() => {
    throw new Error("sink failed");
  });
  expect(() => logEvent("test.sink-failure", { outcome: "ignored" })).not.toThrow();
});

test("structured logs omit caller identities, secrets, bodies and presigned URLs", async () => {
  const log = vi.spyOn(console, "log").mockImplementation(() => undefined);
  const error = vi.spyOn(console, "error").mockImplementation(() => undefined);
  const account = "sensitive-account-must-not-be-logged";
  const fixture = await makeBundleFixture({
    bundleId: "01J00000000000000000000811",
    runId: "observability-run",
    uploaderAccountId: account,
    opponentAccountId: account,
  });
  expect((await worker.fetch(uploadRequest(fixture.body, fixture.headers), env)).status).toBe(201);
  expect(
    (
      await worker.fetch(
        new Request(
          `https://mod-api-v5.bazaarplusplus.com/ghost-battles?player_account_id=${account}`,
        ),
        env,
      )
    ).status,
  ).toBe(200);

  const output = JSON.stringify([...log.mock.calls, ...error.mock.calls]);
  expect(output).not.toContain(account);
  expect(output).not.toContain(env.BUNDLE_SYNC_TOKEN);
  expect(output).not.toContain(env.BAZAARDB_DELIVERY_TOKEN);
  expect(output).not.toContain(env.R2_PRESIGN_SECRET_ACCESS_KEY);
  expect(output).not.toContain("X-Amz-");
});

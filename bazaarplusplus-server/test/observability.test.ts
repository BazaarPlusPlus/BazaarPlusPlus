import { env } from "cloudflare:test";
import { afterEach, expect, test, vi } from "vitest";

import worker from "../src/index";
import { logEvent } from "../src/observability";
import { makeBundleFixture, uploadRequest } from "./fixtures/bundle";

afterEach(() => {
  vi.restoreAllMocks();
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

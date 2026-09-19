import { env } from "cloudflare:test";
import { describe, expect, test } from "vitest";

import worker from "../../src/index";
import { CLAIM_LEASE_MS, DELIVERY_RETRY_BACKOFF_MS, R2_RETENTION_MS } from "../../src/limits";
import { claimDeliveries, settleDeliveries } from "../../src/modules/bazaardb-delivery";
import { makeBundleFixture, uploadRequest } from "../fixtures/bundle";
import { FakeClock } from "../fixtures/clock";
import { createTestDeps } from "../fixtures/deps";
import {
  RecordingBundleDownloadSigner,
  RejectingBundleDownloadSigner,
} from "../fixtures/presigner";

const DELIVERY_TOKEN = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

async function uploadScreenshotBundle(index: number): Promise<string> {
  const suffix = String(index).padStart(3, "0");
  const bundleId = `01J00000000000000000003${suffix}`;
  const fixture = await makeBundleFixture({
    bundleId,
    runId: `delivery-run-${index}`,
    uploaderAccountId: `delivery-uploader-${index}`,
    screenshotBytes: new Uint8Array([0xff, 0xd8, index & 0xff, 0xff, 0xd9]),
    battles: [],
  });
  const response = await worker.fetch(uploadRequest(fixture.body, fixture.headers), env);
  expect(response.status).toBe(201);
  return bundleId;
}

function deliveryRequest(path: "claim" | "settle", body: unknown): Request {
  return new Request(`https://mod-api-v5.bazaarplusplus.com/bazaardb/deliveries/${path}`, {
    method: "POST",
    headers: {
      Authorization: `Bearer ${DELIVERY_TOKEN}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify(body),
  });
}

function rawDeliveryRequest(
  path: "claim" | "settle",
  headers: HeadersInit,
  body?: BodyInit,
): Request {
  return new Request(`https://mod-api-v5.bazaarplusplus.com/bazaardb/deliveries/${path}`, {
    method: "POST",
    headers: { Authorization: `Bearer ${DELIVERY_TOKEN}`, ...headers },
    body,
  });
}

function settleBundleId(index: number): string {
  return `01J0${String(index).padStart(22, "0")}`;
}

async function expectErrorBody(
  response: Response,
  code: string,
  retryable: boolean,
): Promise<void> {
  expect(response.headers.get("Content-Type")).toContain("application/json");
  const body = (await response.json()) as {
    error: { code: string; retryable: boolean; request_id: string };
  };
  expect(body.error).toMatchObject({ code, retryable });
  expect(body.error.request_id).toBeTruthy();
}

const VALID_CLAIM_ID = "clm_550e8400-e29b-41d4-a716-446655440000";

async function claim(limit = 1): Promise<{
  claim_id: string | null;
  expires_at_ms: number | null;
  items: Array<{ bundle_id: string; download_url: string; content_type: string; sha256: string }>;
}> {
  const response = await worker.fetch(deliveryRequest("claim", { limit }), env);
  expect(response.status).toBe(200);
  return response.json();
}

describe("BazaarDB delivery claim and settle", () => {
  test("uses the injected clock for claim leases and signed downloads", async () => {
    const bundleId = await uploadScreenshotBundle(11);
    const clock = new FakeClock(Date.now());
    const signer = new RecordingBundleDownloadSigner();
    const result = await claimDeliveries(
      deliveryRequest("claim", { limit: 1 }),
      env,
      "claim-injected-deps",
      createTestDeps({ signer, now: clock.now }),
    );
    const items = result.items as Array<{
      bundle_id: string;
      download_expires_at_ms: number;
    }>;

    expect(result.expires_at_ms).toBe(clock.ms + 600_000);
    expect(items).toMatchObject([
      { bundle_id: bundleId, download_expires_at_ms: clock.ms + 604_800_000 },
    ]);
    expect(signer.calls).toEqual([
      {
        objectKey: `bundles/2026-08-02/${bundleId}.bundle`,
        issuedAtMs: clock.ms,
      },
    ]);
    await env.DB.prepare(`DELETE FROM bundles WHERE bundle_id = ?1`).bind(bundleId).run();
  });

  test("compensates the claim transaction when injected signing fails", async () => {
    const bundleId = await uploadScreenshotBundle(12);
    const clock = new FakeClock(Date.now());
    const signer = new RejectingBundleDownloadSigner();

    await expect(
      claimDeliveries(
        deliveryRequest("claim", { limit: 1 }),
        env,
        "claim-rejection",
        createTestDeps({ signer, now: clock.now }),
      ),
    ).rejects.toMatchObject({
      status: 503,
      code: "storage_unavailable",
      message: "BazaarDB claim URL signing failed",
      retryable: true,
    });
    expect(
      await env.DB.prepare(
        `SELECT COUNT(*) AS count FROM bazaardb_delivery_attempts WHERE bundle_id = ?1`,
      )
        .bind(bundleId)
        .first(),
    ).toEqual({ count: 0 });
    expect(
      await env.DB.prepare(
        `SELECT delivery_state, active_claim_id, active_claim_order,
                claimable_at_ms, delivery_attempts
         FROM bazaardb_deliveries WHERE bundle_id = ?1`,
      )
        .bind(bundleId)
        .first(),
    ).toEqual({
      delivery_state: "pending",
      active_claim_id: null,
      active_claim_order: null,
      claimable_at_ms: clock.ms,
      delivery_attempts: 0,
    });
    expect(signer.calls).toHaveLength(1);
    await env.DB.prepare(`DELETE FROM bundles WHERE bundle_id = ?1`).bind(bundleId).run();
  });

  test("concurrent consumers claim non-overlapping Bundles", async () => {
    const expected = await Promise.all([uploadScreenshotBundle(1), uploadScreenshotBundle(2)]);
    const [left, right] = await Promise.all([claim(), claim()]);

    expect(left.claim_id).toMatch(/^clm_[0-9a-f-]{36}$/);
    expect(right.claim_id).toMatch(/^clm_[0-9a-f-]{36}$/);
    expect(left.items).toHaveLength(1);
    expect(right.items).toHaveLength(1);
    expect([left.items[0].bundle_id, right.items[0].bundle_id].sort()).toEqual(expected.sort());
    expect(left.items[0].content_type).toBe("application/x-bpp-bundle-v5");
    expect(left.items[0].sha256).toMatch(/^[0-9a-f]{64}$/);
    expect(new URL(left.items[0].download_url).searchParams.get("X-Amz-Expires")).toBe("604800");
  });

  test("retryable settle is idempotent across a later attempt", async () => {
    const bundleId = await uploadScreenshotBundle(3);
    const firstClaim = await claim();
    expect(firstClaim.items[0].bundle_id).toBe(bundleId);
    const result = { bundle_id: bundleId, outcome: "retryable_failure", reason: "timeout" };

    const first = await worker.fetch(
      deliveryRequest("settle", { claim_id: firstClaim.claim_id, results: [result] }),
      env,
    );
    expect(first.status).toBe(200);
    expect((await first.json()) as object).toMatchObject({
      items: [{ bundle_id: bundleId, status: "applied", state: "pending" }],
      summary: { applied: 1, duplicate: 0, rejected: 0 },
    });

    await env.DB.prepare(`UPDATE bazaardb_deliveries SET claimable_at_ms = ?1 WHERE bundle_id = ?2`)
      .bind(Date.now() - 1, bundleId)
      .run();
    const secondClaim = await claim();
    expect(secondClaim.items[0].bundle_id).toBe(bundleId);

    const replay = await worker.fetch(
      deliveryRequest("settle", { claim_id: firstClaim.claim_id, results: [result] }),
      env,
    );
    expect((await replay.json()) as object).toMatchObject({
      items: [{ bundle_id: bundleId, status: "duplicate", state: "pending" }],
      summary: { applied: 0, duplicate: 1, rejected: 0 },
    });
    const active = await env.DB.prepare(
      `SELECT active_claim_id, delivery_attempts FROM bazaardb_deliveries WHERE bundle_id = ?1`,
    )
      .bind(bundleId)
      .first<{ active_claim_id: string; delivery_attempts: number }>();
    expect(active).toEqual({ active_claim_id: secondClaim.claim_id, delivery_attempts: 2 });
  });

  test("accepted, permanent, conflict and unknown outcomes are complete", async () => {
    const acceptedId = await uploadScreenshotBundle(4);
    const acceptedClaim = await claim();
    expect(acceptedClaim.items[0].bundle_id).toBe(acceptedId);
    const accepted = await worker.fetch(
      deliveryRequest("settle", {
        claim_id: acceptedClaim.claim_id,
        results: [{ bundle_id: acceptedId, outcome: "accepted" }],
      }),
      env,
    );
    expect((await accepted.json()) as object).toMatchObject({
      items: [{ status: "applied", state: "done", next_claim_at_ms: null }],
    });

    const conflict = await worker.fetch(
      deliveryRequest("settle", {
        claim_id: acceptedClaim.claim_id,
        results: [
          { bundle_id: acceptedId, outcome: "permanent_failure", reason: "invalid_data" },
          {
            bundle_id: "01J00000000000000000003999",
            outcome: "permanent_failure",
            reason: "invalid_data",
          },
        ],
      }),
      env,
    );
    expect((await conflict.json()) as object).toMatchObject({
      items: [
        { status: "outcome_conflict", state: "done" },
        { status: "unknown_item", state: null },
      ],
      summary: { applied: 0, duplicate: 0, rejected: 2 },
    });

    const permanentId = await uploadScreenshotBundle(5);
    const permanentClaim = await claim();
    const permanent = await worker.fetch(
      deliveryRequest("settle", {
        claim_id: permanentClaim.claim_id,
        results: [{ bundle_id: permanentId, outcome: "permanent_failure", reason: "invalid_data" }],
      }),
      env,
    );
    expect((await permanent.json()) as object).toMatchObject({
      items: [{ status: "applied", state: "failed" }],
    });
  });

  test("an unreturned third lease becomes exhausted after expiry", async () => {
    const bundleId = await uploadScreenshotBundle(6);
    for (let attempt = 1; attempt <= 3; attempt += 1) {
      const leased = await claim();
      expect(leased.items[0].bundle_id).toBe(bundleId);
      await env.DB.prepare(
        `UPDATE bazaardb_deliveries SET claimable_at_ms = ?1 WHERE bundle_id = ?2`,
      )
        .bind(Date.now() - 1, bundleId)
        .run();
    }
    const empty = await claim();
    expect(empty).toEqual({ claim_id: null, expires_at_ms: null, items: [] });
    const row = await env.DB.prepare(
      `SELECT delivery_state, failure_reason FROM bazaardb_deliveries WHERE bundle_id = ?1`,
    )
      .bind(bundleId)
      .first();
    expect(row).toEqual({
      delivery_state: "failed",
      failure_reason: "delivery_attempts_exhausted",
    });
  });

  test("claim lazily fails a pending Bundle whose R2 retention elapsed", async () => {
    const bundleId = await uploadScreenshotBundle(10);
    await env.DB.prepare(`UPDATE bundles SET stored_at_ms = ?1 WHERE bundle_id = ?2`)
      .bind(Date.now() - R2_RETENTION_MS - 1, bundleId)
      .run();

    expect(await claim()).toEqual({ claim_id: null, expires_at_ms: null, items: [] });
    expect(
      await env.DB.prepare(
        `SELECT delivery_state, failure_reason
         FROM bazaardb_deliveries WHERE bundle_id = ?1`,
      )
        .bind(bundleId)
        .first(),
    ).toEqual({ delivery_state: "failed", failure_reason: "bundle_expired" });
  });

  test("returns a multi-item claim in pre-claim claimable order", async () => {
    const [first, second, third] = await Promise.all([
      uploadScreenshotBundle(7),
      uploadScreenshotBundle(8),
      uploadScreenshotBundle(9),
    ]);
    const now = Date.now();
    await env.DB.batch([
      env.DB.prepare(
        `UPDATE bazaardb_deliveries SET claimable_at_ms = ?1 WHERE bundle_id = ?2`,
      ).bind(now - 300, first),
      env.DB.prepare(
        `UPDATE bazaardb_deliveries SET claimable_at_ms = ?1 WHERE bundle_id = ?2`,
      ).bind(now - 100, second),
      env.DB.prepare(
        `UPDATE bazaardb_deliveries SET claimable_at_ms = ?1 WHERE bundle_id = ?2`,
      ).bind(now - 200, third),
    ]);

    const result = await claim(3);
    expect(result.items.map(({ bundle_id }) => bundle_id)).toEqual([first, third, second]);
  });

  test("settles valid, stale, and unknown items independently in one request", async () => {
    const [appliedId, staleId] = await Promise.all([
      uploadScreenshotBundle(13),
      uploadScreenshotBundle(14),
    ]);
    const leased = await claim(2);
    expect(leased.items.map(({ bundle_id }) => bundle_id)).toEqual([appliedId, staleId]);
    await env.DB.prepare(`UPDATE bazaardb_deliveries SET claimable_at_ms = ?1 WHERE bundle_id = ?2`)
      .bind(Date.now() - 1, staleId)
      .run();
    const response = await worker.fetch(
      deliveryRequest("settle", {
        claim_id: leased.claim_id,
        results: [
          { bundle_id: appliedId, outcome: "accepted" },
          { bundle_id: staleId, outcome: "accepted" },
          { bundle_id: "01J00000000000000000003998", outcome: "accepted" },
        ],
      }),
      env,
    );

    expect(response.status).toBe(200);
    expect(await response.json()).toMatchObject({
      items: [
        { bundle_id: appliedId, status: "applied", state: "done" },
        { bundle_id: staleId, status: "stale_claim", state: "pending" },
        { bundle_id: "01J00000000000000000003998", status: "unknown_item", state: null },
      ],
      summary: { applied: 1, duplicate: 0, rejected: 2 },
    });
    await env.DB.batch(
      [appliedId, staleId].map((bundleId) =>
        env.DB.prepare(`DELETE FROM bundles WHERE bundle_id = ?1`).bind(bundleId),
      ),
    );
  });

  test("treats a claim as stale at the injected lease boundary without changing rows", async () => {
    const bundleId = await uploadScreenshotBundle(15);
    const clock = new FakeClock(Date.now());
    const deps = createTestDeps({
      signer: new RecordingBundleDownloadSigner(),
      now: clock.now,
    });
    const leased = await claimDeliveries(
      deliveryRequest("claim", { limit: 1 }),
      env,
      "lease-boundary-claim",
      deps,
    );
    const claimId = leased.claim_id as string;
    const beforeDelivery = await env.DB.prepare(
      `SELECT * FROM bazaardb_deliveries WHERE bundle_id = ?1`,
    )
      .bind(bundleId)
      .first();
    const beforeAttempt = await env.DB.prepare(
      `SELECT * FROM bazaardb_delivery_attempts WHERE claim_id = ?1 AND bundle_id = ?2`,
    )
      .bind(claimId, bundleId)
      .first();

    clock.advance(CLAIM_LEASE_MS);
    const settled = await settleDeliveries(
      deliveryRequest("settle", {
        claim_id: claimId,
        results: [{ bundle_id: bundleId, outcome: "accepted" }],
      }),
      env,
      "lease-boundary-settle",
      deps,
    );

    expect(settled).toMatchObject({
      items: [{ bundle_id: bundleId, status: "stale_claim", state: "pending" }],
      summary: { applied: 0, duplicate: 0, rejected: 1 },
    });
    expect(
      await env.DB.prepare(`SELECT * FROM bazaardb_deliveries WHERE bundle_id = ?1`)
        .bind(bundleId)
        .first(),
    ).toEqual(beforeDelivery);
    expect(
      await env.DB.prepare(
        `SELECT * FROM bazaardb_delivery_attempts WHERE claim_id = ?1 AND bundle_id = ?2`,
      )
        .bind(claimId, bundleId)
        .first(),
    ).toEqual(beforeAttempt);
    await env.DB.prepare(`DELETE FROM bundles WHERE bundle_id = ?1`).bind(bundleId).run();
  });

  test("opens each retry exactly at the injected backoff boundary", async () => {
    const bundleId = await uploadScreenshotBundle(16);
    const clock = new FakeClock(Date.now());
    const deps = createTestDeps({
      signer: new RecordingBundleDownloadSigner(),
      now: clock.now,
    });
    const directClaim = async () =>
      (await claimDeliveries(
        deliveryRequest("claim", { limit: 1 }),
        env,
        "backoff-claim",
        deps,
      )) as {
        claim_id: string | null;
        items: Array<{ bundle_id: string }>;
      };
    const retry = async (claimId: string) =>
      settleDeliveries(
        deliveryRequest("settle", {
          claim_id: claimId,
          results: [{ bundle_id: bundleId, outcome: "retryable_failure", reason: "timeout" }],
        }),
        env,
        "backoff-settle",
        deps,
      );

    const first = await directClaim();
    expect(first.items).toMatchObject([{ bundle_id: bundleId }]);
    if (first.claim_id === null) throw new Error("expected the first claim to return a claim id");
    const firstSettle = await retry(first.claim_id);
    expect(firstSettle).toMatchObject({
      items: [
        {
          status: "applied",
          state: "pending",
          next_claim_at_ms: clock.ms + DELIVERY_RETRY_BACKOFF_MS[0],
        },
      ],
    });

    clock.advance(DELIVERY_RETRY_BACKOFF_MS[0] - 1);
    expect(await directClaim()).toMatchObject({ claim_id: null, items: [] });
    clock.advance(1);
    const second = await directClaim();
    expect(second.items).toMatchObject([{ bundle_id: bundleId }]);
    if (second.claim_id === null) throw new Error("expected the second claim to return a claim id");
    const secondSettle = await retry(second.claim_id);
    expect(secondSettle).toMatchObject({
      items: [
        {
          status: "applied",
          state: "pending",
          next_claim_at_ms: clock.ms + DELIVERY_RETRY_BACKOFF_MS[1],
        },
      ],
    });

    clock.advance(DELIVERY_RETRY_BACKOFF_MS[1] - 1);
    expect(await directClaim()).toMatchObject({ claim_id: null, items: [] });
    clock.advance(1);
    const third = await directClaim();
    expect(third.items).toMatchObject([{ bundle_id: bundleId }]);
    await env.DB.prepare(`DELETE FROM bundles WHERE bundle_id = ?1`).bind(bundleId).run();
  });
});

describe("claim invalid_limit", () => {
  test.each([
    ["a limit of 0", 0],
    ["a limit of 51", 51],
    ["a string limit", "10"],
    ["a fractional limit", 1.5],
  ])("rejects a claim request with %s", async (_label, limit) => {
    const response = await worker.fetch(deliveryRequest("claim", { limit }), env);
    expect(response.status).toBe(400);
    await expectErrorBody(response, "invalid_limit", false);
  });
});

describe("settle invalid_settle_request", () => {
  test("rejects a malformed claim_id", async () => {
    const response = await worker.fetch(
      deliveryRequest("settle", {
        claim_id: "not-a-valid-claim-id",
        results: [{ bundle_id: settleBundleId(1), outcome: "accepted" }],
      }),
      env,
    );
    expect(response.status).toBe(400);
    await expectErrorBody(response, "invalid_settle_request", false);
  });

  test("rejects settle requests with missing or empty results", async () => {
    const missing = await worker.fetch(
      deliveryRequest("settle", { claim_id: VALID_CLAIM_ID }),
      env,
    );
    expect(missing.status).toBe(400);
    await expectErrorBody(missing, "invalid_settle_request", false);

    const empty = await worker.fetch(
      deliveryRequest("settle", { claim_id: VALID_CLAIM_ID, results: [] }),
      env,
    );
    expect(empty.status).toBe(400);
    await expectErrorBody(empty, "invalid_settle_request", false);
  });

  test("rejects more than 50 results", async () => {
    const results = Array.from({ length: 51 }, (_, index) => ({
      bundle_id: settleBundleId(index),
      outcome: "accepted",
    }));
    const response = await worker.fetch(
      deliveryRequest("settle", { claim_id: VALID_CLAIM_ID, results }),
      env,
    );
    expect(response.status).toBe(400);
    await expectErrorBody(response, "invalid_settle_request", false);
  });

  test("rejects a settle result that is not an object", async () => {
    const response = await worker.fetch(
      deliveryRequest("settle", {
        claim_id: VALID_CLAIM_ID,
        results: ["not-an-object"],
      }),
      env,
    );
    expect(response.status).toBe(400);
    await expectErrorBody(response, "invalid_settle_request", false);
  });

  test("rejects an invalid bundle_id", async () => {
    const response = await worker.fetch(
      deliveryRequest("settle", {
        claim_id: VALID_CLAIM_ID,
        results: [{ bundle_id: "not-a-valid-bundle-id!", outcome: "accepted" }],
      }),
      env,
    );
    expect(response.status).toBe(400);
    await expectErrorBody(response, "invalid_settle_request", false);
  });

  test("rejects a duplicate bundle_id in one request", async () => {
    const bundleId = settleBundleId(2);
    const response = await worker.fetch(
      deliveryRequest("settle", {
        claim_id: VALID_CLAIM_ID,
        results: [
          { bundle_id: bundleId, outcome: "accepted" },
          { bundle_id: bundleId, outcome: "accepted" },
        ],
      }),
      env,
    );
    expect(response.status).toBe(400);
    await expectErrorBody(response, "invalid_settle_request", false);
  });

  test("rejects an invalid outcome value", async () => {
    const response = await worker.fetch(
      deliveryRequest("settle", {
        claim_id: VALID_CLAIM_ID,
        results: [{ bundle_id: settleBundleId(3), outcome: "unknown_outcome" }],
      }),
      env,
    );
    expect(response.status).toBe(400);
    await expectErrorBody(response, "invalid_settle_request", false);
  });

  test("rejects an accepted outcome that includes a reason", async () => {
    const response = await worker.fetch(
      deliveryRequest("settle", {
        claim_id: VALID_CLAIM_ID,
        results: [{ bundle_id: settleBundleId(4), outcome: "accepted", reason: "timeout" }],
      }),
      env,
    );
    expect(response.status).toBe(400);
    await expectErrorBody(response, "invalid_settle_request", false);
  });

  test.each([
    ["an uppercase reason", "TIMEOUT"],
    ["a reason longer than 64 characters", "a".repeat(65)],
    ["a missing reason", undefined],
  ])("rejects a failure outcome with %s", async (_label, reason) => {
    const response = await worker.fetch(
      deliveryRequest("settle", {
        claim_id: VALID_CLAIM_ID,
        results: [{ bundle_id: settleBundleId(5), outcome: "retryable_failure", reason }],
      }),
      env,
    );
    expect(response.status).toBe(400);
    await expectErrorBody(response, "invalid_settle_request", false);
  });
});

describe("readJsonObject invalid_json contract", () => {
  test("rejects the wrong Content-Type", async () => {
    const response = await worker.fetch(
      rawDeliveryRequest("claim", { "Content-Type": "text/plain" }, JSON.stringify({ limit: 1 })),
      env,
    );
    expect(response.status).toBe(400);
    await expectErrorBody(response, "invalid_json", false);
  });

  test("rejects a declared body larger than 64 KiB", async () => {
    const response = await worker.fetch(
      rawDeliveryRequest(
        "claim",
        { "Content-Type": "application/json", "Content-Length": "70000" },
        JSON.stringify({ limit: 1 }),
      ),
      env,
    );
    expect(response.status).toBe(400);
    await expectErrorBody(response, "invalid_json", false);
  });

  test("rejects malformed JSON text", async () => {
    const response = await worker.fetch(
      rawDeliveryRequest("claim", { "Content-Type": "application/json" }, "{not json"),
      env,
    );
    expect(response.status).toBe(400);
    await expectErrorBody(response, "invalid_json", false);
  });

  test("rejects a JSON array root", async () => {
    const response = await worker.fetch(
      rawDeliveryRequest("claim", { "Content-Type": "application/json" }, "[1,2,3]"),
      env,
    );
    expect(response.status).toBe(400);
    await expectErrorBody(response, "invalid_json", false);
  });

  test("rejects a missing body", async () => {
    const response = await worker.fetch(
      rawDeliveryRequest("claim", { "Content-Type": "application/json" }),
      env,
    );
    expect(response.status).toBe(400);
    await expectErrorBody(response, "invalid_json", false);
  });
});

test("retention convergence preserves the exact boundary and precedes attempt exhaustion", async () => {
  const expired = await uploadScreenshotBundle(70);
  const boundary = await uploadScreenshotBundle(71);
  const clock = new FakeClock(Date.now());
  await env.DB.batch([
    env.DB.prepare("UPDATE bundles SET stored_at_ms = ?1 WHERE bundle_id = ?2").bind(
      clock.ms - R2_RETENTION_MS - 1,
      expired,
    ),
    env.DB.prepare("UPDATE bundles SET stored_at_ms = ?1 WHERE bundle_id = ?2").bind(
      clock.ms - R2_RETENTION_MS,
      boundary,
    ),
    env.DB.prepare(`UPDATE bazaardb_deliveries
      SET delivery_attempts = 3, active_claim_id = ?1, active_claim_order = 0, claimable_at_ms = ?2
      WHERE bundle_id = ?3`).bind(VALID_CLAIM_ID, clock.ms - 1, expired),
    env.DB.prepare("UPDATE bazaardb_deliveries SET claimable_at_ms = ?1 WHERE bundle_id = ?2").bind(
      clock.ms + CLAIM_LEASE_MS,
      boundary,
    ),
  ]);
  const deps = createTestDeps({ now: clock.now });
  const runClaim = () =>
    claimDeliveries(deliveryRequest("claim", { limit: 1 }), env, "retention", deps);
  expect(await runClaim()).toEqual({ claim_id: null, expires_at_ms: null, items: [] });
  const state = (id: string) =>
    env.DB.prepare(
      "SELECT delivery_state, failure_reason FROM bazaardb_deliveries WHERE bundle_id = ?1",
    )
      .bind(id)
      .first();
  expect(await state(expired)).toEqual({
    delivery_state: "failed",
    failure_reason: "bundle_expired",
  });
  expect(await state(boundary)).toEqual({ delivery_state: "pending", failure_reason: null });
  clock.ms += 1;
  await runClaim();
  expect(await state(boundary)).toEqual({
    delivery_state: "failed",
    failure_reason: "bundle_expired",
  });
});

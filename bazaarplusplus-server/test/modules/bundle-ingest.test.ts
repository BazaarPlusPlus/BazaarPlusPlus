import { env } from "cloudflare:test";
import { describe, expect, test } from "vitest";

import worker from "../../src/index";
import { contentDigest, makeBundleFixture, uploadRequest } from "../fixtures/bundle";

describe("POST /bundles", () => {
  test("stores one Run-only Bundle and returns its receipt", async () => {
    const fixture = await makeBundleFixture();
    const response = await worker.fetch(uploadRequest(fixture.body, fixture.headers), env);

    expect(response.status).toBe(201);
    expect(await response.json()).toEqual({
      bundle_id: "01J00000000000000000000001",
      run_id: "run-001",
      outcome: "stored",
      bazaardb_delivery: "not_applicable",
    });

    const object = await env.BUNDLE_BUCKET.get(
      "bundles/2026-08-02/01J00000000000000000000001.bundle",
    );
    expect(object).not.toBeNull();
    expect(object?.size).toBe(fixture.body.byteLength);
  });

  test("returns duplicate for the same immutable Bundle", async () => {
    const fixture = await makeBundleFixture({
      bundleId: "01J00000000000000000000002",
      runId: "run-002",
    });
    const first = await worker.fetch(uploadRequest(fixture.body, fixture.headers), env);
    const second = await worker.fetch(uploadRequest(fixture.body, fixture.headers), env);

    expect(first.status).toBe(201);
    expect(second.status).toBe(200);
    expect(await second.json()).toEqual({
      bundle_id: "01J00000000000000000000002",
      run_id: "run-002",
      outcome: "duplicate",
      bazaardb_delivery: "not_applicable",
    });
  });

  test("rejects malformed transport headers before storage", async () => {
    const fixture = await makeBundleFixture();
    const headers = new Headers(fixture.headers);
    headers.delete("Content-Length");
    const response = await worker.fetch(uploadRequest(fixture.body, headers), env);

    expect(response.status).toBe(411);
    expect((await response.json()) as object).toMatchObject({
      error: { code: "content_length_required", retryable: false },
    });
  });

  test("rejects a segment mutation even when the whole-body digest is valid", async () => {
    const fixture = await makeBundleFixture({
      bundleId: "01J00000000000000000000003",
      runId: "run-003",
    });
    const body = fixture.body.slice();
    body[body.byteLength - 1] ^= 0xff;
    const headers = new Headers(fixture.headers);
    headers.set("Content-Digest", await contentDigest(body));
    const response = await worker.fetch(uploadRequest(body, headers), env);

    expect(response.status).toBe(422);
    expect((await response.json()) as object).toMatchObject({
      error: { code: "segment_digest_mismatch", retryable: false },
    });
    expect(
      await env.BUNDLE_BUCKET.head("bundles/2026-08-02/01J00000000000000000000003.bundle"),
    ).toBeNull();
  });

  test("does not use a whole-request buffering helper", async () => {
    const fixture = await makeBundleFixture({
      bundleId: "01J00000000000000000000004",
      runId: "run-004",
      runBytes: new Uint8Array(1_500_000).fill(7),
    });
    const request = uploadRequest(fixture.body, fixture.headers);
    Object.defineProperty(request, "arrayBuffer", {
      value: () => {
        throw new Error("whole-body buffering is forbidden");
      },
    });

    const response = await worker.fetch(request, env);
    expect(response.status).toBe(201);
  });

  test("two concurrent uploads produce one stored and one duplicate receipt", async () => {
    const fixture = await makeBundleFixture({
      bundleId: "01J00000000000000000000005",
      runId: "run-005",
    });
    const responses = await Promise.all([
      worker.fetch(uploadRequest(fixture.body, fixture.headers), env),
      worker.fetch(uploadRequest(fixture.body, fixture.headers), env),
    ]);

    expect(responses.map(({ status }) => status).sort()).toEqual([200, 201]);
  });

  test("reports created then existing for one Screenshot Bundle", async () => {
    const fixture = await makeBundleFixture({
      bundleId: "01J00000000000000000000006",
      runId: "run-006",
      screenshotBytes: new Uint8Array([0xff, 0xd8, 6, 0xff, 0xd9]),
      battles: [],
    });
    const first = await worker.fetch(uploadRequest(fixture.body, fixture.headers), env);
    const duplicate = await worker.fetch(uploadRequest(fixture.body, fixture.headers), env);

    expect(first.status).toBe(201);
    expect(await first.json()).toEqual({
      bundle_id: "01J00000000000000000000006",
      run_id: "run-006",
      outcome: "stored",
      bazaardb_delivery: "created",
    });
    expect(duplicate.status).toBe(200);
    expect(await duplicate.json()).toEqual({
      bundle_id: "01J00000000000000000000006",
      run_id: "run-006",
      outcome: "duplicate",
      bazaardb_delivery: "existing",
    });
  });
});

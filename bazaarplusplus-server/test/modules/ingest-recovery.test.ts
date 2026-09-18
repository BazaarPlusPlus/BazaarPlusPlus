import { env } from "cloudflare:test";
import { afterEach, describe, expect, test, vi } from "vitest";

import worker from "../../src/index";
import { contentDigest, makeBundleFixture, uploadRequest } from "../fixtures/bundle";

afterEach(() => vi.restoreAllMocks());

describe("Bundle ingest fault recovery", () => {
  test.each([
    ["index_failure", 503, "storage_unavailable", "01J00000000000000000000710"],
    ["bundle_conflict", 409, "bundle_id_conflict", "01J00000000000000000000711"],
    ["run_conflict", 409, "run_already_bundled", "01J00000000000000000000712"],
    ["duplicate", 200, null, "01J00000000000000000000713"],
  ] as const)("cancels the unread upload on %s", async (outcome, status, code, bundleId) => {
    const original = await makeBundleFixture({
      bundleId,
      runId: `recovery-unread-${outcome}`,
      uploaderAccountId: `recovery-unread-${outcome}`,
      battles: [],
    });
    if (outcome !== "index_failure") {
      expect((await worker.fetch(uploadRequest(original.body, original.headers), env)).status).toBe(
        201,
      );
    }
    const incoming =
      outcome === "bundle_conflict" || outcome === "run_conflict"
        ? await makeBundleFixture({
            bundleId: outcome === "bundle_conflict" ? bundleId : "01J00000000000000000000714",
            runId: `recovery-unread-${outcome}`,
            uploaderAccountId: `recovery-unread-${outcome}`,
            runBytes: new Uint8Array([1, 2, 3]),
            battles: [],
          })
        : original;
    const manifestEnd = 16 + new DataView(incoming.body.buffer).getUint32(12, false);
    const cancel = vi.fn();
    const body = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(incoming.body.subarray(0, manifestEnd + 1));
      },
      cancel,
    });
    const put = vi.spyOn(env.BUNDLE_BUCKET, "put");
    if (outcome === "index_failure") {
      vi.spyOn(env.DB, "prepare").mockImplementation(() => {
        throw new Error("injected D1 read failure");
      });
    }

    const response = await worker.fetch(
      new Request("https://mod-api-v5.bazaarplusplus.com/bundles", {
        method: "POST",
        headers: incoming.headers,
        body,
      }),
      env,
    );

    expect(response.status).toBe(status);
    expect(await response.json()).toMatchObject(
      code === null ? { outcome: "duplicate" } : { error: { code } },
    );
    expect(cancel).toHaveBeenCalledOnce();
    expect(put).not.toHaveBeenCalled();
  });

  test("keeps an R2-only object after D1 failure and recovers it on retry", async () => {
    const fixture = await makeBundleFixture({
      bundleId: "01J00000000000000000000701",
      runId: "recovery-orphan-run",
      battles: [],
    });
    const failingEnv = {
      ...env,
      DB: {
        prepare: env.DB.prepare.bind(env.DB),
        async batch() {
          throw new Error("injected D1 batch failure");
        },
      },
    } as unknown as Cloudflare.Env;

    const failed = await worker.fetch(uploadRequest(fixture.body, fixture.headers), failingEnv);
    expect(failed.status).toBe(503);
    const key = "bundles/2026-08-02/01J00000000000000000000701.bundle";
    const orphan = await env.BUNDLE_BUCKET.head(key);
    if (orphan === null) throw new Error("expected an R2-only orphan object");
    expect(
      await env.DB.prepare(
        `SELECT bundle_id FROM bundles WHERE bundle_id = '01J00000000000000000000701'`,
      ).first(),
    ).toBeNull();
    expect(
      await env.DB.prepare(
        `SELECT player_account_id FROM bundle_uploaders WHERE player_account_id = 'account-uploader'`,
      ).first(),
    ).toBeNull();

    const recoveredAt = orphan.uploaded.getTime() + 10 * 86_400_000;
    const now = vi.spyOn(Date, "now").mockReturnValue(recoveredAt);
    const recovered = await worker.fetch(uploadRequest(fixture.body, fixture.headers), env);
    now.mockRestore();
    expect(recovered.status).toBe(201);
    expect((await recovered.json()) as object).toMatchObject({ outcome: "stored" });
    expect(
      await env.DB.prepare(
        `SELECT stored_at_ms, available_at_ms FROM bundles
         WHERE bundle_id = '01J00000000000000000000701'`,
      ).first(),
    ).toEqual({ stored_at_ms: orphan.uploaded.getTime(), available_at_ms: recoveredAt });
  });

  test("a Run conflict never creates a second object", async () => {
    const first = await makeBundleFixture({
      bundleId: "01J00000000000000000000702",
      runId: "recovery-shared-run",
      battles: [],
    });
    const second = await makeBundleFixture({
      bundleId: "01J00000000000000000000703",
      runId: "recovery-shared-run",
      battles: [],
    });
    expect((await worker.fetch(uploadRequest(first.body, first.headers), env)).status).toBe(201);
    const conflict = await worker.fetch(uploadRequest(second.body, second.headers), env);
    expect(conflict.status).toBe(409);
    expect((await conflict.json()) as object).toMatchObject({
      error: { code: "run_already_bundled" },
    });
    expect(
      await env.BUNDLE_BUCKET.head("bundles/2026-08-02/01J00000000000000000000703.bundle"),
    ).toBeNull();
    expect(
      await env.BUNDLE_BUCKET.head("bundles/2026-08-02/01J00000000000000000000702.bundle"),
    ).not.toBeNull();
  });

  test("a Bundle ID conflict never overwrites the first object", async () => {
    const first = await makeBundleFixture({
      bundleId: "01J00000000000000000000704",
      runId: "recovery-first-run",
      battles: [],
    });
    expect((await worker.fetch(uploadRequest(first.body, first.headers), env)).status).toBe(201);
    const changed = first.body.slice();
    changed[changed.byteLength - 1] ^= 0xff;
    const headers = new Headers(first.headers);
    headers.set("Content-Digest", await contentDigest(changed));
    const conflict = await worker.fetch(uploadRequest(changed, headers), env);
    expect(conflict.status).toBe(409);
    expect((await conflict.json()) as object).toMatchObject({
      error: { code: "bundle_id_conflict" },
    });
    const object = await env.BUNDLE_BUCKET.get(
      "bundles/2026-08-02/01J00000000000000000000704.bundle",
    );
    if (object === null) throw new Error("expected the first Bundle object to survive");
    expect(new Uint8Array(await object.arrayBuffer())).toEqual(first.body);
  });

  test("an R2 PUT failure leaves no D1 logical commit", async () => {
    const fixture = await makeBundleFixture({
      bundleId: "01J00000000000000000000705",
      runId: "recovery-r2-failure-run",
      battles: [],
    });
    const failingEnv = {
      ...env,
      BUNDLE_BUCKET: {
        async put() {
          throw new Error("injected R2 failure");
        },
        async head() {
          return null;
        },
      },
    } as unknown as Cloudflare.Env;
    const response = await worker.fetch(uploadRequest(fixture.body, fixture.headers), failingEnv);
    expect(response.status).toBe(503);
    expect(
      await env.DB.prepare(
        `SELECT bundle_id FROM bundles WHERE bundle_id = '01J00000000000000000000705'`,
      ).first(),
    ).toBeNull();
  });

  test("an invalid pre-existing R2 object is preserved as evidence", async () => {
    const fixture = await makeBundleFixture({
      bundleId: "01J00000000000000000000706",
      runId: "recovery-invalid-object-run",
      battles: [],
    });
    const key = "bundles/2026-08-02/01J00000000000000000000706.bundle";
    const evidence = new Uint8Array([1, 2, 3, 4]);
    await env.BUNDLE_BUCKET.put(key, evidence);

    const response = await worker.fetch(uploadRequest(fixture.body, fixture.headers), env);
    expect(response.status).toBe(503);
    const object = await env.BUNDLE_BUCKET.get(key);
    if (object === null) throw new Error("expected the evidence object to survive");
    expect(new Uint8Array(await object.arrayBuffer())).toEqual(evidence);
    expect(
      await env.DB.prepare(
        `SELECT bundle_id FROM bundles WHERE bundle_id = '01J00000000000000000000706'`,
      ).first(),
    ).toBeNull();
  });

  test("a midstream disconnect is retryable and commits nothing", async () => {
    const fixture = await makeBundleFixture({
      bundleId: "01J00000000000000000000707",
      runId: "recovery-disconnect-run",
      battles: [],
    });
    let emitted = false;
    const stream = new ReadableStream<Uint8Array>({
      pull(controller) {
        if (!emitted) {
          emitted = true;
          controller.enqueue(fixture.body.subarray(0, fixture.body.byteLength - 2));
          return;
        }
        controller.error(new Error("injected connection loss"));
      },
    });
    const request = new Request("https://mod-api-v5.bazaarplusplus.com/bundles", {
      method: "POST",
      headers: fixture.headers,
      body: stream,
    });
    const consumingEnv = {
      ...env,
      BUNDLE_BUCKET: {
        async put(_key: string, value: ReadableStream<Uint8Array>) {
          const reader = value.getReader();
          while (true) {
            const result = await reader.read();
            if (result.done) break;
          }
          throw new Error("disconnected stream unexpectedly completed");
        },
        async head() {
          return null;
        },
      },
    } as unknown as Cloudflare.Env;

    const response = await worker.fetch(request, consumingEnv);
    expect(response.status).toBe(503);
    expect((await response.json()) as object).toMatchObject({
      error: { code: "storage_unavailable", retryable: true },
    });
    expect(
      await env.DB.prepare(
        `SELECT bundle_id FROM bundles WHERE bundle_id = '01J00000000000000000000707'`,
      ).first(),
    ).toBeNull();
  });
});

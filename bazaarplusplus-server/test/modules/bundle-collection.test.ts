import { env } from "cloudflare:test";
import { describe, expect, test } from "vitest";

import worker from "../../src/index";
import { R2_RETENTION_MS } from "../../src/limits";
import { collectBundles } from "../../src/modules/bundle-collection";
import { FakeClock } from "../fixtures/clock";
import { createTestDeps } from "../fixtures/deps";
import { RecordingBundleDownloadSigner } from "../fixtures/presigner";

const SYNC_TOKEN = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

async function insertBundle(bundleId: string, availableAtMs: number): Promise<void> {
  await env.DB.prepare(
    `INSERT INTO bundles (
      bundle_id, run_id, uploader_account_id, object_key, bundle_sha256,
      bundle_version, manifest_bytes, object_bytes, client_created_at_ms,
      stored_at_ms, available_at_ms, run_format_version, run_bytes, run_sha256,
      has_screenshot
    ) VALUES (?1, ?2, 'collection-uploader', ?3, ?4, 5, 10, 100, ?5, ?5, ?6, 5, 10, ?4, 0)`,
  )
    .bind(
      bundleId,
      `run-${bundleId}`,
      `bundles/2026-08-01/${bundleId}.bundle`,
      "a".repeat(64),
      availableAtMs - 1_000,
      availableAtMs,
    )
    .run();
}

function collectionRequest(query: string, token = SYNC_TOKEN): Request {
  return new Request(`https://mod-api-v5.bazaarplusplus.com/bundles?${query}`, {
    headers: { Authorization: `Bearer ${token}` },
  });
}

describe("GET /bundles", () => {
  test("uses the injected clock for one page of signed downloads", async () => {
    const clock = new FakeClock(Date.now());
    const signer = new RecordingBundleDownloadSigner();
    const bundleId = "01J00000000000000000000104";
    const availableAt = clock.ms - 120_000;
    await insertBundle(bundleId, availableAt);

    const result = await collectBundles(
      collectionRequest(
        `available_from_ms=${clock.ms - 180_000}&available_before_ms=${clock.ms - 60_000}`,
      ),
      env,
      "collection-injected-deps",
      createTestDeps({ signer, now: clock.now }),
    );
    const items = result.items as Array<{
      bundle_id: string;
      download_expires_at_ms: number;
    }>;

    expect(items).toEqual([
      {
        bundle_id: bundleId,
        available_at_ms: availableAt,
        download_url:
          "https://fake.invalid/bundles%2F2026-08-01%2F01J00000000000000000000104.bundle?method=GET&expires=604800",
        download_expires_at_ms: clock.ms + 604_800_000,
      },
    ]);
    expect(signer.calls).toEqual([
      {
        objectKey: `bundles/2026-08-01/${bundleId}.bundle`,
        issuedAtMs: clock.ms,
      },
    ]);
  });

  test("enumerates a fixed window with stable keyset pagination and 7-day URLs", async () => {
    const now = Date.now();
    const start = now - 7_200_000;
    const before = now - 3_600_000;
    const position = start + 1_000;
    const ids = [
      "01J00000000000000000000101",
      "01J00000000000000000000102",
      "01J00000000000000000000103",
    ];
    await Promise.all(ids.map((id) => insertBundle(id, position)));

    const first = await worker.fetch(
      collectionRequest(`available_from_ms=${start}&available_before_ms=${before}&limit=2`),
      env,
    );
    expect(first.status).toBe(200);
    const firstBody = (await first.json()) as {
      window: { available_from_ms: number; available_before_ms: number };
      items: Array<{
        bundle_id: string;
        available_at_ms: number;
        download_url: string;
        download_expires_at_ms: number;
      }>;
      next_after: { available_at_ms: number; bundle_id: string } | null;
    };
    expect(firstBody.window).toEqual({
      available_from_ms: start,
      available_before_ms: before,
    });
    expect(firstBody.items.map(({ bundle_id }) => bundle_id)).toEqual(ids.slice(0, 2));
    expect(firstBody.next_after).toEqual({
      available_at_ms: position,
      bundle_id: ids[1],
    });
    const signed = new URL(firstBody.items[0].download_url);
    expect(signed.hostname).toBe(
      "bazaarplusplus-bundle-v5.test-account-id.r2.cloudflarestorage.com",
    );
    expect(signed.searchParams.get("X-Amz-Expires")).toBe("604800");
    expect(firstBody.items[0]).not.toHaveProperty("object_key");
    expect(firstBody.items[0].download_expires_at_ms - Date.now()).toBeGreaterThan(604_799_000);

    const second = await worker.fetch(
      collectionRequest(
        `available_from_ms=${start}&available_before_ms=${before}&limit=2&after_available_at_ms=${position}&after_bundle_id=${ids[1]}`,
      ),
      env,
    );
    expect(second.status).toBe(200);
    const secondBody = (await second.json()) as {
      items: Array<{ bundle_id: string }>;
      next_after: null;
    };
    expect(secondBody.items.map(({ bundle_id }) => bundle_id)).toEqual([ids[2]]);
    expect(secondBody.next_after).toBeNull();
  });

  test("rejects an expired retention window", async () => {
    const response = await worker.fetch(
      collectionRequest(`available_from_ms=${Date.now() - R2_RETENTION_MS - 1}`),
      env,
    );
    expect(response.status).toBe(410);
    expect((await response.json()) as object).toMatchObject({
      error: { code: "window_expired", retryable: false },
    });
  });

  test("rejects duplicate and unknown query parameters", async () => {
    const from = Date.now() - 120_000;
    const response = await worker.fetch(
      collectionRequest(`available_from_ms=${from}&available_from_ms=${from}&cursor=nope`),
      env,
    );
    expect(response.status).toBe(400);
    expect((await response.json()) as object).toMatchObject({
      error: { code: "invalid_query", retryable: false },
    });
  });

  test("rejects a window end later than the settle point", async () => {
    const now = Date.now();
    const response = await worker.fetch(
      collectionRequest(`available_from_ms=${now - 120_000}&available_before_ms=${now - 100}`),
      env,
    );
    expect(response.status).toBe(400);
    const body = (await response.json()) as {
      error: { code: string; retryable: boolean; request_id: string };
    };
    expect(body.error).toMatchObject({ code: "window_not_settled", retryable: false });
    expect(body.error.request_id).toBeTruthy();
  });

  test("reports storage_unavailable when the D1 query fails", async () => {
    const failingEnv = {
      ...env,
      DB: {
        prepare() {
          throw new Error("injected D1 query failure");
        },
      },
    } as unknown as Cloudflare.Env;
    const response = await worker.fetch(
      collectionRequest(`available_from_ms=${Date.now() - 120_000}`),
      failingEnv,
    );
    expect(response.status).toBe(503);
    const body = (await response.json()) as {
      error: { code: string; retryable: boolean; request_id: string };
    };
    expect(body.error).toMatchObject({ code: "storage_unavailable", retryable: true });
    expect(body.error.request_id).toBeTruthy();
  });
});

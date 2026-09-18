import { env } from "cloudflare:test";
import { describe, expect, test, vi } from "vitest";

import { createBundleDownloadSigner, signDownloadPage } from "../src/presigner";
import { RecordingBundleDownloadSigner, RejectingBundleDownloadSigner } from "./fixtures/presigner";

describe("Bundle download presigner adapters", () => {
  test("the production adapter fixes the R2 endpoint, object, operation and 7-day expiry", async () => {
    const issuedAt = 1_785_628_800_000;
    const key = "bundles/2026-08-02/01J00000000000000000000801.bundle";
    const signed = await createBundleDownloadSigner(env).sign(key, issuedAt);
    const url = new URL(signed.url);

    expect(url.hostname).toBe("bazaarplusplus-bundle-v5.test-account-id.r2.cloudflarestorage.com");
    expect(url.pathname).toBe(`/${key}`);
    expect(url.searchParams.get("X-Amz-Expires")).toBe("604800");
    expect(url.searchParams.get("X-Amz-Credential")).toContain("/auto/s3/aws4_request");
    expect(signed.expiresAtMs).toBe(issuedAt + 604_800_000);
  });

  test("the test adapter records keys without network access", async () => {
    const signer = new RecordingBundleDownloadSigner();
    const signed = await signer.sign("bundles/2026-08-02/01J00000000000000000000802.bundle", 1000);
    expect(signer.calls).toEqual([
      {
        objectKey: "bundles/2026-08-02/01J00000000000000000000802.bundle",
        issuedAtMs: 1000,
      },
    ]);
    expect(signed).toEqual({
      url: "https://fake.invalid/bundles%2F2026-08-02%2F01J00000000000000000000802.bundle?method=GET&expires=604800",
      expiresAtMs: 604_801_000,
    });
  });

  test("signs each distinct page key once while preserving input alignment", async () => {
    const signer = new RecordingBundleDownloadSigner();
    const first = "bundles/2026-08-02/01J00000000000000000000803.bundle";
    const second = "bundles/2026-08-02/01J00000000000000000000804.bundle";
    const downloads = await signDownloadPage(signer, [first, second, first], 2_000, "failed");

    expect(signer.calls).toEqual([
      { objectKey: first, issuedAtMs: 2_000 },
      { objectKey: second, issuedAtMs: 2_000 },
    ]);
    expect(downloads).toHaveLength(3);
    expect(downloads[0]).toEqual(downloads[2]);
    expect(downloads[0].url).toContain(encodeURIComponent(first));
    expect(downloads[1].url).toContain(encodeURIComponent(second));
  });

  test("maps any page signing rejection to the caller-specific 503", async () => {
    const signer = new RejectingBundleDownloadSigner();
    await expect(
      signDownloadPage(
        signer,
        ["bundles/2026-08-02/01J00000000000000000000805.bundle"],
        3_000,
        "Caller-specific signing failed",
      ),
    ).rejects.toMatchObject({
      status: 503,
      code: "storage_unavailable",
      message: "Caller-specific signing failed",
      retryable: true,
    });
  });
});

test("a cold download page derives one signing key and preserves signatures across UTC dates", async () => {
  const signer = createBundleDownloadSigner(env);
  const keys = Array.from(
    { length: 50 },
    (_, index) => `bundles/2026-08-02/01J${String(index).padStart(23, "0")}.bundle`,
  );
  for (const issuedAt of [1_785_715_199_000, 1_785_715_200_000]) {
    const reference = createBundleDownloadSigner(env);
    const expected = [];
    for (const key of keys) expected.push(await reference.sign(key, issuedAt));
    const sign = vi.spyOn(crypto.subtle, "sign");
    try {
      const actual = await signDownloadPage(signer, keys, issuedAt, "failed");
      expect(actual).toEqual(expected);
      expect(sign).toHaveBeenCalledTimes(keys.length + 4);
    } finally {
      sign.mockRestore();
    }
  }
});

test("an empty download page performs no signing", async () => {
  const signer = new RecordingBundleDownloadSigner();
  expect(await signDownloadPage(signer, [], 1_000, "failed")).toEqual([]);
  expect(signer.calls).toEqual([]);
});

test("a later signing failure retains the page error contract", async () => {
  const signer = new RecordingBundleDownloadSigner();
  const sign = vi.spyOn(signer, "sign");
  sign.mockResolvedValueOnce({ url: "https://fake.invalid/first", expiresAtMs: 1_000 });
  sign.mockRejectedValueOnce(new Error("later signature failed"));
  await expect(
    signDownloadPage(signer, ["first", "second"], 1_000, "page failed"),
  ).rejects.toMatchObject({ status: 503, code: "storage_unavailable", message: "page failed" });
});

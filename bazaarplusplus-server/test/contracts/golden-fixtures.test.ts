import { env } from "cloudflare:test";
import { describe, expect, test } from "vitest";

import corruptMagicBase64 from "../../contracts/v5/fixtures/corrupt-magic.bundle.b64?raw";
import validBase64 from "../../contracts/v5/fixtures/run-only.bundle.b64?raw";
import segmentMismatchBase64 from "../../contracts/v5/fixtures/segment-digest-mismatch.bundle.b64?raw";
import worker from "../../src/index";
import { contentDigest, decodeBase64, uploadRequest } from "../fixtures/bundle";

async function headers(bytes: Uint8Array): Promise<Headers> {
  return new Headers({
    "Content-Type": "application/x-bpp-bundle-v5",
    "Content-Length": String(bytes.byteLength),
    "Content-Digest": await contentDigest(bytes),
  });
}

describe("checked-in Bundle V5 golden vectors", () => {
  test.each([
    [corruptMagicBase64, "invalid_bundle"],
    [segmentMismatchBase64, "segment_digest_mismatch"],
  ])("rejects a checked-in corrupt vector", async (encoded, code) => {
    const body = decodeBase64(encoded);
    const response = await worker.fetch(uploadRequest(body, await headers(body)), env);
    expect(response.status).toBe(422);
    expect((await response.json()) as object).toMatchObject({ error: { code } });
  });

  test("accepts the canonical Run-only vector", async () => {
    const body = decodeBase64(validBase64);
    expect(body.byteLength).toBe(399);
    const response = await worker.fetch(uploadRequest(body, await headers(body)), env);
    expect(response.status).toBe(201);
    expect((await response.json()) as object).toMatchObject({
      bundle_id: "01J00000000000000000000901",
      run_id: "golden-run-001",
      outcome: "stored",
    });
  });
});

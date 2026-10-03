import { env } from "cloudflare:test";
import { describe, expect, test } from "vitest";

import errorContract from "../../contracts/mod-api-errors.json";
import checksums from "../../contracts/v5/fixtures/checksums.json";
import corruptMagicBase64 from "../../contracts/v5/fixtures/corrupt-magic.bundle.b64?raw";
import runOnlyBase64 from "../../contracts/v5/fixtures/run-only.bundle.b64?raw";
import segmentMismatchBase64 from "../../contracts/v5/fixtures/segment-digest-mismatch.bundle.b64?raw";
import worker from "../../src/index";
import { contentDigest, decodeBase64, sha256Hex, uploadRequest } from "../fixtures/bundle";
import { expectGolden } from "../fixtures/golden";

// Every expectation comes from checksums.json; each listed vector is uploaded through the Worker.
const vectors: Record<string, string> = {
  "corrupt-magic.bundle.b64": corruptMagicBase64,
  "run-only.bundle.b64": runOnlyBase64,
  "segment-digest-mismatch.bundle.b64": segmentMismatchBase64,
};

type Expectation =
  | { expected: "valid"; sha256: string; decoded_bytes: number }
  | { expected_error: string; expected_reason?: string; sha256?: string };

// The vectors share one Bundle ID, so rejected vectors upload before the valid one is stored.
const expectations = Object.entries(checksums as Record<string, Expectation>).sort(
  ([, left], [, right]) => Number("expected_error" in right) - Number("expected_error" in left),
);

async function upload(bytes: Uint8Array): Promise<Response> {
  const headers = new Headers({
    "Content-Type": "application/x-bpp-bundle-v5",
    "Content-Length": String(bytes.byteLength),
    "Content-Digest": await contentDigest(bytes),
  });
  return worker.fetch(uploadRequest(bytes, headers), env);
}

describe("checked-in Bundle V5 golden vectors", () => {
  test.each(expectations)("%s uploads with its declared outcome", async (name, expectation) => {
    const body = decodeBase64(vectors[name]);
    if (expectation.sha256 !== undefined) {
      expect(await sha256Hex(body)).toBe(expectation.sha256);
    }
    const response = await upload(body);
    if ("expected_error" in expectation) {
      const declared = errorContract.codes.find(({ code }) => code === expectation.expected_error);
      expect(declared, expectation.expected_error).toBeDefined();
      expect(response.status).toBe(declared?.status);
      expect((await response.json()) as object).toMatchObject({
        error: {
          code: expectation.expected_error,
          ...(expectation.expected_reason === undefined
            ? {}
            : { details: { reason: expectation.expected_reason } }),
        },
      });
      return;
    }
    expect(body.byteLength).toBe(expectation.decoded_bytes);
    await expectGolden(response, `ingest-${name.replace(".bundle.b64", "")}`);
  });
});

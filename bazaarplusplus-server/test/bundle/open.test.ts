import { describe, expect, test, vi } from "vitest";

import checksumsJson from "../../contracts/v5/fixtures/checksums.json?raw";
import corruptMagicBase64 from "../../contracts/v5/fixtures/corrupt-magic.bundle.b64?raw";
import runOnlyBase64 from "../../contracts/v5/fixtures/run-only.bundle.b64?raw";
import segmentMismatchBase64 from "../../contracts/v5/fixtures/segment-digest-mismatch.bundle.b64?raw";
import { openBundle } from "../../src/bundle/open";
import { decodeBase64, stream } from "../fixtures/bundle";

async function rejection(promise: Promise<unknown>): Promise<unknown> {
  return promise.then(
    () => undefined,
    (error: unknown) => error,
  );
}

describe("openBundle", () => {
  test("cancels the source and rejects the digest with the caller's reason", async () => {
    const bytes = decodeBase64(runOnlyBase64);
    const manifestEnd = 16 + new DataView(bytes.buffer).getUint32(12, false);
    const cancel = vi.fn();
    const source = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(bytes.subarray(0, manifestEnd + 1));
      },
      cancel,
    });
    const opened = await openBundle(source, bytes.byteLength, null);
    const reason = new Error("upload no longer needed");

    await opened.body.cancel(reason);

    await expect(opened.digest).rejects.toBe(reason);
    expect(cancel).toHaveBeenCalledExactlyOnceWith(reason);
  });

  test("rejects body and digest with the original source failure", async () => {
    const bytes = decodeBase64(runOnlyBase64);
    const manifestEnd = 16 + new DataView(bytes.buffer).getUint32(12, false);
    const reason = new Error("upload disconnected");
    const source = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(bytes.subarray(0, manifestEnd + 1));
      },
      pull(controller) {
        controller.error(reason);
      },
    });
    const opened = await openBundle(source, bytes.byteLength, null);
    const [bodyError, digestError] = await Promise.all([
      rejection(opened.body.pipeTo(new WritableStream<Uint8Array>())),
      rejection(opened.digest),
    ]);

    expect(bodyError).toBe(reason);
    expect(digestError).toBe(reason);
  });

  test.each([
    [1_048_576, "undeclared_trailing_bytes"],
    [-1, "segment_out_of_bounds"],
  ])(
    "rejects a declared length difference of %i before reading payload bytes",
    async (difference, reason) => {
      const bytes = decodeBase64(runOnlyBase64);
      const manifestEnd = 16 + new DataView(bytes.buffer).getUint32(12, false);
      const pull = vi.fn(() => {
        throw new Error("payload must not be read");
      });
      const cancel = vi.fn();
      const source = new ReadableStream<Uint8Array>(
        {
          start(controller) {
            controller.enqueue(bytes.subarray(0, manifestEnd));
          },
          pull,
          cancel,
        },
        { highWaterMark: 0 },
      );
      await expect(openBundle(source, bytes.byteLength + difference, null)).rejects.toMatchObject({
        status: 422,
        details: { reason },
      });
      expect(pull).not.toHaveBeenCalled();
      expect(cancel).toHaveBeenCalledOnce();
    },
  );
  test("rejects corrupt magic before exposing a body", async () => {
    const bytes = decodeBase64(corruptMagicBase64);
    await expect(openBundle(stream(bytes), bytes.byteLength, null)).rejects.toMatchObject({
      status: 422,
      code: "invalid_bundle",
      message: "Bundle magic is invalid",
      retryable: false,
      details: { reason: "invalid_prefix" },
    });
  });

  test("rejects body and digest with the same segment mismatch error", async () => {
    const bytes = decodeBase64(segmentMismatchBase64);
    const opened = await openBundle(stream(bytes), bytes.byteLength, null);
    const [bodyError, digestError] = await Promise.all([
      rejection(opened.body.pipeTo(new WritableStream<Uint8Array>())),
      rejection(opened.digest),
    ]);

    expect(bodyError).toBe(digestError);
    expect(bodyError).toMatchObject({
      status: 422,
      code: "segment_digest_mismatch",
      message: "A Bundle segment digest does not match its manifest",
      retryable: false,
      details: undefined,
    });
  });

  test("opens and validates the canonical Run-only Bundle", async () => {
    const bytes = decodeBase64(runOnlyBase64);
    const checksums = JSON.parse(checksumsJson) as {
      "run-only.bundle.b64": { sha256: string };
    };
    const expectedDigest = checksums["run-only.bundle.b64"].sha256;
    const opened = await openBundle(stream(bytes), bytes.byteLength, expectedDigest);

    expect(opened.descriptor).toMatchObject({
      bundleId: "01J00000000000000000000901",
      runId: "golden-run-001",
      uploaderAccountId: "golden-account",
      createdAtMs: 1_785_628_800_000,
      manifestBytes: 374,
      objectBytes: 399,
      objectKey: "bundles/2026-08-02/01J00000000000000000000901.bundle",
      run: {
        offset: 0,
        length: 9,
        sha256: "4fcfdda3275cc407cb2f2eb487acb1a41eb2493851f2e34eb74bef6b4160a5e5",
      },
      screenshot: null,
      battles: [],
    });
    await expect(opened.body.pipeTo(new WritableStream<Uint8Array>())).resolves.toBeUndefined();
    await expect(opened.digest).resolves.toBe(expectedDigest);
  });

  test("maps an expected whole-Bundle digest mismatch without changing its text", async () => {
    const bytes = decodeBase64(runOnlyBase64);
    const opened = await openBundle(stream(bytes), bytes.byteLength, "0".repeat(64));
    const [bodyError, digestError] = await Promise.all([
      rejection(opened.body.pipeTo(new WritableStream<Uint8Array>())),
      rejection(opened.digest),
    ]);

    expect(bodyError).toBe(digestError);
    expect(bodyError).toMatchObject({
      status: 422,
      code: "bundle_digest_mismatch",
      message: "Bundle digest does not match Content-Digest",
      retryable: false,
      details: undefined,
    });
  });
});

import { describe, expect, test } from "vitest";

import checksumsJson from "../../contracts/v5/fixtures/checksums.json?raw";
import corruptMagicBase64 from "../../contracts/v5/fixtures/corrupt-magic.bundle.b64?raw";
import runOnlyBase64 from "../../contracts/v5/fixtures/run-only.bundle.b64?raw";
import segmentMismatchBase64 from "../../contracts/v5/fixtures/segment-digest-mismatch.bundle.b64?raw";
import { decodeBase64, sha256Hex } from "../fixtures/bundle";

interface FixtureChecksums {
  "run-only.bundle.b64": {
    decoded_bytes: number;
    manifest_bytes: number;
    sha256: string;
    expected: string;
  };
  "corrupt-magic.bundle.b64": {
    expected_error: string;
    expected_reason: string;
  };
  "segment-digest-mismatch.bundle.b64": {
    sha256: string;
    expected_error: string;
  };
}

describe("Bundle V5 golden checksum manifest", () => {
  test("matches the checked-in Bundle vectors", async () => {
    const checksums = JSON.parse(checksumsJson) as FixtureChecksums;
    const runOnly = decodeBase64(runOnlyBase64);
    const corruptMagic = decodeBase64(corruptMagicBase64);
    const segmentMismatch = decodeBase64(segmentMismatchBase64);

    expect(runOnly.byteLength).toBe(checksums["run-only.bundle.b64"].decoded_bytes);
    expect(new DataView(runOnly.buffer).getUint32(12, false)).toBe(
      checksums["run-only.bundle.b64"].manifest_bytes,
    );
    expect(await sha256Hex(runOnly)).toBe(checksums["run-only.bundle.b64"].sha256);
    expect(checksums["run-only.bundle.b64"].expected).toBe("valid");

    expect(await sha256Hex(segmentMismatch)).toBe(
      checksums["segment-digest-mismatch.bundle.b64"].sha256,
    );
    expect(checksums["segment-digest-mismatch.bundle.b64"].expected_error).toBe(
      "segment_digest_mismatch",
    );

    expect(new TextDecoder().decode(corruptMagic.subarray(0, 8))).not.toBe("BPPBNDL5");
    expect(checksums["corrupt-magic.bundle.b64"]).toMatchObject({
      expected_error: "invalid_bundle",
      expected_reason: "invalid_prefix",
    });
  });
});

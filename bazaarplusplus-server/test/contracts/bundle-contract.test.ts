import { env } from "cloudflare:test";
import { describe, expect, test } from "vitest";

import worker from "../../src/index";
import { contentDigest, makeBundleFixture, sealBundle, uploadRequest } from "../fixtures/bundle";

const DEFAULT_RUN = new Uint8Array([0x1f, 0x8b, 0x08, 0, 5, 4, 3, 2, 1]);

// Narrow view of the fixture manifest for tests that mutate nested fields.
interface MutableManifest extends Record<string, unknown> {
  run: { projection: { battles: Record<string, unknown>[] } };
  screenshot: { offset: number };
}

async function errorFor(
  body: Uint8Array,
  headers: Headers,
  workerEnv: Cloudflare.Env = env,
): Promise<{
  status: number;
  code: string;
  reason?: string;
}> {
  const response = await worker.fetch(uploadRequest(body, headers), workerEnv);
  const payload = (await response.json()) as {
    error: { code: string; details?: { reason?: string } };
  };
  return {
    status: response.status,
    code: payload.error.code,
    reason: payload.error.details?.reason,
  };
}

describe("Bundle V5 wire validation", () => {
  test("rejects corrupt magic and unknown prefix versions", async () => {
    const fixture = await makeBundleFixture({
      bundleId: "01J00000000000000000000601",
      runId: "contract-prefix-run",
    });
    const corrupt = fixture.body.slice();
    corrupt[0] ^= 0xff;
    const corruptHeaders = new Headers(fixture.headers);
    corruptHeaders.set("Content-Digest", await contentDigest(corrupt));
    expect(await errorFor(corrupt, corruptHeaders)).toEqual({
      status: 422,
      code: "invalid_bundle",
      reason: "invalid_prefix",
    });

    const unknown = fixture.body.slice();
    new DataView(unknown.buffer).setUint32(8, 6, false);
    const unknownHeaders = new Headers(fixture.headers);
    unknownHeaders.set("Content-Digest", await contentDigest(unknown));
    expect(await errorFor(unknown, unknownHeaders)).toMatchObject({
      status: 422,
      code: "unsupported_bundle_version",
    });
  });

  test("rejects 31 Battles and duplicate battle IDs within one Bundle", async () => {
    const base = await makeBundleFixture({
      bundleId: "01J00000000000000000000602",
      runId: "contract-battles-run",
    });
    const manifest = structuredClone(base.manifest) as MutableManifest;
    const battle = manifest.run.projection.battles[0];
    manifest.run.projection.battles = Array.from({ length: 31 }, (_, index) => ({
      ...battle,
      battle_id: `battle-limit-${index}`,
    }));
    const tooMany = await sealBundle(manifest, DEFAULT_RUN);
    expect(await errorFor(tooMany.body, tooMany.headers)).toEqual({
      status: 422,
      code: "invalid_bundle",
      reason: "too_many_battles",
    });

    manifest.run.projection.battles = [battle, { ...battle }];
    const duplicate = await sealBundle(manifest, DEFAULT_RUN);
    expect(await errorFor(duplicate.body, duplicate.headers)).toMatchObject({
      status: 422,
      code: "invalid_bundle",
    });
  });

  test("distinguishes short bodies, trailing bytes and whole Bundle digest mismatch", async () => {
    const fixture = await makeBundleFixture({
      bundleId: "01J00000000000000000000603",
      runId: "contract-length-run",
    });
    const shortHeaders = new Headers(fixture.headers);
    const shortBodyEnv = {
      ...env,
      BUNDLE_BUCKET: {
        async put(_key: string, value: ReadableStream<Uint8Array>) {
          const reader = value.getReader();
          while (true) {
            const result = await reader.read();
            if (result.done) break;
          }
          throw new Error("short stream unexpectedly completed");
        },
        async head() {
          return null;
        },
      },
    } as unknown as Cloudflare.Env;
    expect(await errorFor(fixture.body.subarray(0, -1), shortHeaders, shortBodyEnv)).toMatchObject({
      status: 400,
      code: "invalid_content_length",
    });

    const trailing = new Uint8Array(fixture.body.byteLength + 1);
    trailing.set(fixture.body);
    trailing[trailing.byteLength - 1] = 1;
    const trailingHeaders = new Headers(fixture.headers);
    trailingHeaders.set("Content-Length", String(trailing.byteLength));
    trailingHeaders.set("Content-Digest", await contentDigest(trailing));
    expect(await errorFor(trailing, trailingHeaders)).toEqual({
      status: 422,
      code: "invalid_bundle",
      reason: "undeclared_trailing_bytes",
    });

    const digestHeaders = new Headers(fixture.headers);
    digestHeaders.set("Content-Digest", `sha-256=:${"A".repeat(43)}=:`);
    expect(await errorFor(fixture.body, digestHeaders)).toMatchObject({
      status: 422,
      code: "bundle_digest_mismatch",
    });
  });

  test("rejects overlapping Screenshot layout", async () => {
    const screenshot = new Uint8Array([1, 2, 3, 4]);
    const fixture = await makeBundleFixture({
      bundleId: "01J00000000000000000000604",
      runId: "contract-overlap-run",
      screenshotBytes: screenshot,
    });
    const manifest = structuredClone(fixture.manifest) as MutableManifest;
    manifest.screenshot.offset = 1;
    const sealed = await sealBundle(manifest, DEFAULT_RUN, screenshot);
    expect(await errorFor(sealed.body, sealed.headers)).toEqual({
      status: 422,
      code: "invalid_bundle",
      reason: "segment_overlap",
    });
  });

  test("rejects the 8 MiB boundary and malformed digest before R2", async () => {
    const fixture = await makeBundleFixture({
      bundleId: "01J00000000000000000000605",
      runId: "contract-transport-run",
    });
    let r2Touched = false;
    const untouchedEnv = {
      ...env,
      BUNDLE_BUCKET: {
        async put() {
          r2Touched = true;
          throw new Error("R2 must not be touched");
        },
      },
    } as unknown as Cloudflare.Env;
    const oversized = new Headers(fixture.headers);
    oversized.set("Content-Length", "8388608");
    expect(await errorFor(fixture.body, oversized, untouchedEnv)).toMatchObject({
      status: 413,
      code: "bundle_too_large",
    });

    const malformed = new Headers(fixture.headers);
    malformed.set("Content-Digest", "sha-512=:AAAA:");
    expect(await errorFor(fixture.body, malformed, untouchedEnv)).toMatchObject({
      status: 400,
      code: "invalid_content_digest",
    });
    expect(r2Touched).toBe(false);
  });
});

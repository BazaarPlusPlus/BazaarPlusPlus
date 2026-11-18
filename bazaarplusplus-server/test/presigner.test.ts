import { env } from 'cloudflare:test';
import { describe, expect, test, vi } from 'vitest';

import { createBundleDownloadSigner, signDownloadPage } from '../src/presigner';

// An in-memory signer that records each signed key, for exercising page fan-out.
function recordingSigner() {
  const calls: Array<{ objectKey: string; issuedAtMs: number }> = [];
  return {
    calls,
    sign: vi.fn(async (objectKey: string, issuedAtMs: number) => {
      calls.push({ objectKey, issuedAtMs });
      return {
        url: `https://signed.test/${objectKey}`,
        expiresAtMs: issuedAtMs + 604_800_000
      };
    })
  };
}

describe('Bundle download presigner adapters', () => {
  test('signs each distinct page key once while preserving input alignment', async () => {
    const signer = recordingSigner();
    const first = 'bundles/2026-08-02/01J00000000000000000000803.bundle';
    const second = 'bundles/2026-08-02/01J00000000000000000000804.bundle';
    const downloads = await signDownloadPage(
      signer,
      [first, second, first],
      2_000,
      'failed'
    );

    expect(signer.calls).toEqual([
      { objectKey: first, issuedAtMs: 2_000 },
      { objectKey: second, issuedAtMs: 2_000 }
    ]);
    expect(downloads).toHaveLength(3);
    expect(downloads[0]).toEqual(downloads[2]);
    expect(downloads[0].url).toContain(first);
    expect(downloads[1].url).toContain(second);
  });
});

test('a cold download page derives one signing key and preserves signatures across UTC dates', async () => {
  const signer = createBundleDownloadSigner(env);
  const keys = Array.from(
    { length: 50 },
    (_, index) =>
      `bundles/2026-08-02/01J${String(index).padStart(23, '0')}.bundle`
  );
  for (const issuedAt of [1_785_715_199_000, 1_785_715_200_000]) {
    const reference = createBundleDownloadSigner(env);
    const expected = [];
    for (const key of keys) expected.push(await reference.sign(key, issuedAt));
    const sign = vi.spyOn(crypto.subtle, 'sign');
    try {
      const actual = await signDownloadPage(signer, keys, issuedAt, 'failed');
      expect(actual).toEqual(expected);
      expect(sign).toHaveBeenCalledTimes(keys.length + 4);
    } finally {
      sign.mockRestore();
    }
  }
});

test('an empty download page performs no signing', async () => {
  const signer = recordingSigner();
  expect(await signDownloadPage(signer, [], 1_000, 'failed')).toEqual([]);
  expect(signer.calls).toEqual([]);
});

test('a later signing failure retains the page error contract', async () => {
  const signer = recordingSigner();
  signer.sign.mockResolvedValueOnce({
    url: 'https://signed.test/first',
    expiresAtMs: 1_000
  });
  signer.sign.mockRejectedValueOnce(new Error('later signature failed'));
  await expect(
    signDownloadPage(signer, ['first', 'second'], 1_000, 'page failed')
  ).rejects.toMatchObject({
    status: 503,
    code: 'storage_unavailable',
    message: 'page failed'
  });
});

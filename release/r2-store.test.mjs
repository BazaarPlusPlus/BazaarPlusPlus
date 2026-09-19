import { expect, test, vi } from 'vitest';
import { createR2Store, r2StoreFromEnvironment } from './r2-store.mjs';

const credentials = {
  accountId: 'a'.repeat(32),
  accessKeyId: 'test-access',
  secretAccessKey: 'test-secret',
  now: () => new Date('2026-09-19T00:00:00.000Z')
};

test('R2 writes are signed and always carry the conditional precondition', async () => {
  const fetchImpl = vi
    .fn()
    .mockResolvedValue(new Response(null, { status: 200 }));
  const store = createR2Store({ ...credentials, fetchImpl });
  await store.put('latest.json', Buffer.from('{}'), {
    ifMatch: '"previous-etag"',
    contentType: 'application/json'
  });
  const [url, request] = fetchImpl.mock.calls[0];
  expect(url).toBe(
    `https://${credentials.accountId}.r2.cloudflarestorage.com/bppinstaller/latest.json`
  );
  expect(request.headers['if-match']).toBe('"previous-etag"');
  expect(request.headers['x-amz-date']).toBe('20260919T000000Z');
  expect(request.headers.authorization).toMatch(
    /Credential=test-access\/20260919\/auto\/s3\/aws4_request/
  );
  expect(request.headers.authorization).toContain('if-match');
  expect(request.headers.authorization).not.toContain(
    credentials.secretAccessKey
  );
  expect(request.redirect).toBe('error');
  await expect(store.put('latest.json', Buffer.from('{}'))).rejects.toThrow(
    /conditional/
  );
  await expect(
    store.put('latest.json', Buffer.from('{}'), {
      ifMatch: 'etag',
      ifNoneMatch: true
    })
  ).rejects.toThrow(/conditional/);
});

test('R2 GET preserves ETag and only 404 means absent', async () => {
  const fetchImpl = vi
    .fn()
    .mockResolvedValueOnce(
      new Response('{}', { headers: { etag: '"quoted-etag"' } })
    )
    .mockResolvedValueOnce(new Response('', { status: 404 }))
    .mockResolvedValueOnce(new Response('', { status: 403 }))
    .mockResolvedValueOnce(new Response('', { status: 503 }));
  const store = createR2Store({ ...credentials, fetchImpl });
  expect((await store.get('latest.json')).etag).toBe('"quoted-etag"');
  expect(await store.get('missing')).toBeNull();
  await expect(store.get('denied')).rejects.toThrow(/403/);
  await expect(store.get('unavailable')).rejects.toThrow(/503/);
});

test('conditional conflict returns false and never retries unconditionally', async () => {
  const fetchImpl = vi
    .fn()
    .mockResolvedValue(new Response('', { status: 412 }));
  const store = createR2Store({ ...credentials, fetchImpl });
  expect(
    await store.put('latest.json', Buffer.from('{}'), { ifNoneMatch: true })
  ).toBe(false);
  expect(fetchImpl).toHaveBeenCalledTimes(1);
  expect(fetchImpl.mock.calls[0][1].headers['if-none-match']).toBe('*');
});

test('missing credentials, missing ETag and oversized reads fail closed', async () => {
  expect(() => r2StoreFromEnvironment({})).toThrow(/BPP_R2/);
  const fetchImpl = vi
    .fn()
    .mockResolvedValueOnce(new Response('{}'))
    .mockResolvedValueOnce(
      new Response('too large', { headers: { etag: '"e"' } })
    );
  const store = createR2Store({ ...credentials, fetchImpl });
  await expect(store.get('no-etag')).rejects.toThrow(/ETag/);
  await expect(store.get('large', { maxBytes: 2 })).rejects.toThrow(
    /expected size/
  );
});

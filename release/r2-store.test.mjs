import crypto from 'node:crypto';
import { expect, test, vi } from 'vitest';
import {
  CLOUDFLARE_API,
  createR2Store,
  credentialsFromApiToken,
  r2StoreFromApiToken,
  r2StoreFromEnvironment
} from './r2-store.mjs';

// Credential derivation fails closed, before any R2 request, when:
//   the token is empty or whitespace          -> "CLOUDFLARE_API_TOKEN is empty"
//   the account id is not 32 hex characters   -> "CLOUDFLARE_ACCOUNT_ID must be"
//   the bucket name is not a valid R2 bucket  -> "Invalid R2 bucket"
// and on the first request when:
//   /user/tokens/verify is not 2xx or success -> "Cloudflare rejected ... HTTP"
//   the verify result has no 32-hex token id  -> "returned no token id"
//   the token is not active                   -> "not active"
// A verified id is reused for the process; a failed verification is not.

const credentials = {
  accountId: 'a'.repeat(32),
  accessKeyId: 'test-access',
  secretAccessKey: 'test-secret',
  now: () => new Date('2026-09-19T00:00:00.000Z')
};

const sha256 = (value) =>
  crypto.createHash('sha256').update(value).digest('hex');
const verifyResponse = (result, { status = 200, success = true } = {}) =>
  new Response(JSON.stringify({ success, errors: [], result }), { status });
const uniqueToken = () => `token-${crypto.randomUUID()}`;

test('the S3 pair is the verified token id and the SHA-256 of the token value', async () => {
  const token = uniqueToken();
  const id = 'b'.repeat(32);
  const fetchImpl = vi
    .fn()
    .mockResolvedValue(verifyResponse({ id, status: 'active' }));
  const derived = await credentialsFromApiToken({
    token: ` ${token} `,
    accountId: 'A'.repeat(32),
    fetchImpl
  });
  expect(derived).toEqual({
    accountId: 'a'.repeat(32),
    accessKeyId: id,
    secretAccessKey: sha256(token)
  });
  const [url, request] = fetchImpl.mock.calls[0];
  expect(url).toBe(`${CLOUDFLARE_API}/user/tokens/verify`);
  expect(request.headers.authorization).toBe(`Bearer ${token}`);
  expect(request.redirect).toBe('error');
  // The id is cached per process: a second derivation makes no request.
  await credentialsFromApiToken({
    token,
    accountId: 'a'.repeat(32),
    fetchImpl
  });
  expect(fetchImpl).toHaveBeenCalledTimes(1);
});

test('a token-backed store signs requests with the derived pair after one verify call', async () => {
  const token = uniqueToken();
  const id = 'c'.repeat(32);
  const fetchImpl = vi.fn(async (url) =>
    url.startsWith(CLOUDFLARE_API)
      ? verifyResponse({ id, status: 'active' })
      : new Response('', { status: 404 })
  );
  const store = r2StoreFromApiToken({
    token,
    accountId: credentials.accountId,
    bucket: 'bazaarplusplus-game-libs',
    fetchImpl,
    now: credentials.now
  });
  expect(fetchImpl).not.toHaveBeenCalled();
  expect(await store.head('game-libs/missing.json')).toBeNull();
  expect(await store.head('game-libs/other.json')).toBeNull();
  const requests = fetchImpl.mock.calls.filter(
    ([url]) => !url.startsWith(CLOUDFLARE_API)
  );
  expect(fetchImpl.mock.calls.length - requests.length).toBe(1);
  expect(requests[0][0]).toBe(
    `https://${credentials.accountId}.r2.cloudflarestorage.com/bazaarplusplus-game-libs/game-libs/missing.json`
  );
  expect(requests[0][1].headers.authorization).toMatch(
    new RegExp(`Credential=${id}/20260919/auto/s3/aws4_request`)
  );
  expect(requests[0][1].headers.authorization).not.toContain(token);
  expect(requests[0][1].headers.authorization).not.toContain(sha256(token));
});

test('derivation refuses an empty token, a malformed account id and a bad bucket before any request', async () => {
  const fetchImpl = vi.fn();
  for (const token of ['', '   ', undefined])
    await expect(
      credentialsFromApiToken({
        token,
        accountId: credentials.accountId,
        fetchImpl
      })
    ).rejects.toThrow(/CLOUDFLARE_API_TOKEN is empty/);
  for (const accountId of ['', 'abc', 'g'.repeat(32), 'a'.repeat(31)])
    await expect(
      credentialsFromApiToken({ token: 'token', accountId, fetchImpl })
    ).rejects.toThrow(/CLOUDFLARE_ACCOUNT_ID must be/);
  expect(() =>
    r2StoreFromApiToken({
      token: 'token',
      accountId: credentials.accountId,
      bucket: 'Bad_Bucket',
      fetchImpl
    })
  ).toThrow(/Invalid R2 bucket/);
  expect(() => r2StoreFromEnvironment({})).toThrow(
    /CLOUDFLARE_API_TOKEN and CLOUDFLARE_ACCOUNT_ID/
  );
  expect(() =>
    r2StoreFromEnvironment({
      CLOUDFLARE_API_TOKEN: 'token',
      CLOUDFLARE_ACCOUNT_ID: 'short'
    })
  ).toThrow(/CLOUDFLARE_ACCOUNT_ID must be/);
  expect(fetchImpl).not.toHaveBeenCalled();
});

test('a rejected, malformed or inactive verification fails the request and is retried next time', async () => {
  const accountId = credentials.accountId;
  const cases = [
    [
      verifyResponse(null, { status: 401, success: false }),
      /Cloudflare rejected CLOUDFLARE_API_TOKEN: HTTP 401/
    ],
    [new Response('not json', { status: 500 }), /HTTP 500/],
    [verifyResponse({ status: 'active' }), /returned no token id/],
    [
      verifyResponse({ id: 'd'.repeat(32), status: 'expired' }),
      /is expired, not active/
    ]
  ];
  for (const [response, pattern] of cases) {
    const token = uniqueToken();
    const fetchImpl = vi
      .fn()
      .mockResolvedValueOnce(response)
      .mockResolvedValueOnce(
        verifyResponse({ id: 'e'.repeat(32), status: 'active' })
      )
      .mockResolvedValue(new Response('', { status: 404 }));
    const store = r2StoreFromApiToken({ token, accountId, fetchImpl });
    await expect(store.head('latest.json')).rejects.toThrow(pattern);
    expect(await store.head('latest.json')).toBeNull();
    expect(
      fetchImpl.mock.calls.filter(([url]) => url.startsWith(CLOUDFLARE_API))
    ).toHaveLength(2);
  }
});

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

test('a missing pair, missing ETag and oversized reads fail closed', async () => {
  expect(() =>
    createR2Store({ accountId: credentials.accountId, accessKeyId: 'k' })
  ).toThrow(/access key id and secret/);
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

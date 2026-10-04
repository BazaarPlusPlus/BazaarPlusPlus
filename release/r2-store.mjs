import crypto from 'node:crypto';

const digest = (bytes) =>
  crypto.createHash('sha256').update(bytes).digest('hex');
const hmac = (key, value) =>
  crypto.createHmac('sha256', key).update(value).digest();
const encode = (value) =>
  encodeURIComponent(value).replace(
    /[!'()*]/g,
    (char) => `%${char.charCodeAt(0).toString(16).toUpperCase()}`
  );

export const CLOUDFLARE_API = 'https://api.cloudflare.com/client/v4';
const ACCOUNT_ID = /^[a-f0-9]{32}$/i;
const TOKEN_ID = /^[a-f0-9]{32}$/;

// R2's S3 credentials are a view of a Cloudflare API token: the Access Key ID
// is the token's id, which GET /user/tokens/verify reveals, and the Secret
// Access Key is the SHA-256 of the token value. Every store derives the pair
// from CLOUDFLARE_API_TOKEN and CLOUDFLARE_ACCOUNT_ID this way; nothing stores
// the pair. The verified id is cached per process under the token's digest.
const verifiedTokenIds = new Map();

export function assertAccountId(accountId) {
  if (!ACCOUNT_ID.test(accountId ?? ''))
    throw new Error(
      'CLOUDFLARE_ACCOUNT_ID must be the 32-character hexadecimal account id'
    );
  return accountId.toLowerCase();
}

function assertToken(token) {
  if (typeof token !== 'string' || !token.trim())
    throw new Error('CLOUDFLARE_API_TOKEN is empty');
  return token.trim();
}

async function verifyToken(token, fetchImpl) {
  const response = await fetchImpl(`${CLOUDFLARE_API}/user/tokens/verify`, {
    headers: { authorization: `Bearer ${token}` },
    redirect: 'error',
    signal: AbortSignal.timeout(30000)
  });
  const body = await response.json().catch(() => null);
  const codes = (body?.errors ?? [])
    .map((error) => error.code)
    .filter(Boolean)
    .join(', ');
  if (!response.ok || body?.success !== true)
    throw new Error(
      `Cloudflare rejected CLOUDFLARE_API_TOKEN: HTTP ${response.status}${codes ? ` (error ${codes})` : ''}`
    );
  const { id, status } = body.result ?? {};
  if (!TOKEN_ID.test(id ?? ''))
    throw new Error('Cloudflare token verification returned no token id');
  if (status !== 'active')
    throw new Error(
      `CLOUDFLARE_API_TOKEN is ${status ?? 'in an unknown state'}, not active`
    );
  return id;
}

export async function credentialsFromApiToken({
  token,
  accountId,
  fetchImpl = fetch
}) {
  const value = assertToken(token);
  const account = assertAccountId(accountId);
  const secretAccessKey = digest(value);
  let pending = verifiedTokenIds.get(secretAccessKey);
  if (!pending) {
    pending = verifyToken(value, fetchImpl).catch((error) => {
      verifiedTokenIds.delete(secretAccessKey);
      throw error;
    });
    verifiedTokenIds.set(secretAccessKey, pending);
  }
  return { accountId: account, accessKeyId: await pending, secretAccessKey };
}

export function createR2Store({
  accountId,
  accessKeyId,
  secretAccessKey,
  credentials,
  bucket = 'bppinstaller',
  fetchImpl = fetch,
  now = () => new Date()
}) {
  const account = assertAccountId(accountId);
  // A fixed pair (tests, callers that already hold one) or a resolver that
  // derives it on first use, so construction never touches the network.
  if (typeof credentials !== 'function') {
    if (!accessKeyId || !secretAccessKey)
      throw new Error('R2 access requires an access key id and secret');
    const pair = { accessKeyId, secretAccessKey };
    credentials = async () => pair;
  }
  if (!/^[a-z0-9][a-z0-9-]*[a-z0-9]$/.test(bucket))
    throw new Error('Invalid R2 bucket');
  const host = `${account}.r2.cloudflarestorage.com`;

  async function request(method, key, body, extraHeaders = {}) {
    if (
      typeof key !== 'string' ||
      key.split('/').some((part) => !part || part === '.' || part === '..')
    )
      throw new Error('Invalid R2 object key');
    const uri = `/${encode(bucket)}/${key.split('/').map(encode).join('/')}`;
    const { accessKeyId, secretAccessKey } = await credentials();
    const date = now()
      .toISOString()
      .replace(/[:-]|\.\d{3}/g, '');
    const day = date.slice(0, 8);
    const payloadHash = digest(body ?? Buffer.alloc(0));
    const headers = {
      host,
      'x-amz-content-sha256': payloadHash,
      'x-amz-date': date,
      ...extraHeaders
    };
    const names = Object.keys(headers).sort();
    const canonicalHeaders = names
      .map(
        (name) =>
          `${name}:${String(headers[name]).trim().replace(/\s+/g, ' ')}\n`
      )
      .join('');
    const signedHeaders = names.join(';');
    const canonical = [
      method,
      uri,
      '',
      canonicalHeaders,
      signedHeaders,
      payloadHash
    ].join('\n');
    const scope = `${day}/auto/s3/aws4_request`;
    const signingKey = hmac(
      hmac(hmac(hmac(`AWS4${secretAccessKey}`, day), 'auto'), 's3'),
      'aws4_request'
    );
    const signature = hmac(
      signingKey,
      `AWS4-HMAC-SHA256\n${date}\n${scope}\n${digest(canonical)}`
    ).toString('hex');
    headers.authorization = `AWS4-HMAC-SHA256 Credential=${accessKeyId}/${scope}, SignedHeaders=${signedHeaders}, Signature=${signature}`;
    const response = await fetchImpl(`https://${host}${uri}`, {
      method,
      headers,
      body,
      redirect: 'error',
      signal: AbortSignal.timeout(120000)
    });
    if (![200, 201, 204, 404, 412].includes(response.status)) {
      await response.body?.cancel();
      throw new Error(
        `R2 ${method} ${key} failed with HTTP ${response.status}`
      );
    }
    return response;
  }

  return {
    async get(key, { maxBytes = 1024 * 1024 } = {}) {
      const response = await request('GET', key);
      if (response.status === 404) {
        await response.body?.cancel();
        return null;
      }
      if (response.status !== 200) {
        await response.body?.cancel();
        throw new Error(`R2 GET ${key} failed with HTTP ${response.status}`);
      }
      const etag = response.headers.get('etag');
      if (!etag) {
        await response.body?.cancel();
        throw new Error(`R2 object has no ETag: ${key}`);
      }
      const chunks = [];
      let size = 0;
      for await (const chunk of response.body) {
        size += chunk.length;
        if (size > maxBytes)
          throw new Error(`R2 object exceeds expected size: ${key}`);
        chunks.push(Buffer.from(chunk));
      }
      return { bytes: Buffer.concat(chunks), etag };
    },
    async head(key) {
      const response = await request('HEAD', key);
      if (response.status === 404) return null;
      if (response.status !== 200)
        throw new Error(`R2 HEAD ${key} failed with HTTP ${response.status}`);
      return {
        size: Number(response.headers.get('content-length')),
        sha256: response.headers.get('x-amz-meta-sha256'),
        etag: response.headers.get('etag')
      };
    },
    async put(
      key,
      bytes,
      {
        ifMatch,
        ifNoneMatch = false,
        contentType = 'application/octet-stream'
      } = {}
    ) {
      if (Boolean(ifMatch) === Boolean(ifNoneMatch))
        throw new Error(
          'Every R2 write requires exactly one conditional precondition'
        );
      const response = await request('PUT', key, bytes, {
        'content-type': contentType,
        'x-amz-meta-sha256': digest(bytes),
        ...(ifMatch ? { 'if-match': ifMatch } : { 'if-none-match': '*' })
      });
      await response.body?.cancel();
      if (response.status === 412) return false;
      if (response.status === 404)
        throw new Error(`R2 PUT ${key} failed with HTTP 404`);
      return true;
    }
  };
}

// A store whose S3 pair is derived from an API token on its first request.
export function r2StoreFromApiToken({
  token,
  accountId,
  bucket,
  fetchImpl = fetch,
  now
}) {
  const value = assertToken(token);
  const account = assertAccountId(accountId);
  return createR2Store({
    accountId: account,
    credentials: () =>
      credentialsFromApiToken({ token: value, accountId: account, fetchImpl }),
    ...(bucket === undefined ? {} : { bucket }),
    fetchImpl,
    ...(now === undefined ? {} : { now })
  });
}

export function r2StoreFromEnvironment(env = process.env, options = {}) {
  if (!env.CLOUDFLARE_API_TOKEN?.trim() || !env.CLOUDFLARE_ACCOUNT_ID?.trim())
    throw new Error(
      'R2 access requires CLOUDFLARE_API_TOKEN and CLOUDFLARE_ACCOUNT_ID'
    );
  return r2StoreFromApiToken({
    token: env.CLOUDFLARE_API_TOKEN,
    accountId: env.CLOUDFLARE_ACCOUNT_ID,
    ...options
  });
}

export async function putImmutable(store, key, bytes, contentType) {
  let uncertain;
  try {
    if (await store.put(key, bytes, { ifNoneMatch: true, contentType })) return;
  } catch (error) {
    uncertain = error;
  }
  // A lost PUT response is indistinguishable from failure until we read back.
  const existing = await store.get(key, { maxBytes: bytes.length });
  if (existing && existing.bytes.equals(bytes)) return;
  if (uncertain) throw uncertain;
  throw new Error(
    `Immutable release object differs: ${key}; publish a new product version`
  );
}

// A replaceable object converges on `bytes` through ETag compare-and-swap:
// mirror records before their platform is promoted, snapshot manifests.
export async function putReplaceable(
  store,
  key,
  bytes,
  contentType,
  attempts = 4
) {
  for (let attempt = 0; attempt < attempts; attempt++) {
    const current = await store.get(key);
    if (current?.bytes.equals(bytes)) return;
    const written = current
      ? await store.put(key, bytes, { ifMatch: current.etag, contentType })
      : await store.put(key, bytes, { ifNoneMatch: true, contentType });
    if (written) return;
  }
  throw new Error(
    `Could not record ${key}; retry after the other writer finishes`
  );
}

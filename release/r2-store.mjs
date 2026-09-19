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

export function createR2Store({
  accountId,
  accessKeyId,
  secretAccessKey,
  bucket = 'bppinstaller',
  fetchImpl = fetch,
  now = () => new Date()
}) {
  if (
    !/^[a-f0-9]{32}$/i.test(accountId ?? '') ||
    !accessKeyId ||
    !secretAccessKey
  )
    throw new Error(
      'R2 publishing requires BPP_R2_ACCOUNT_ID, BPP_R2_ACCESS_KEY_ID and BPP_R2_SECRET_ACCESS_KEY'
    );
  if (!/^[a-z0-9][a-z0-9-]*[a-z0-9]$/.test(bucket))
    throw new Error('Invalid R2 bucket');
  const host = `${accountId}.r2.cloudflarestorage.com`;

  async function request(method, key, body, extraHeaders = {}) {
    if (
      typeof key !== 'string' ||
      key.split('/').some((part) => !part || part === '.' || part === '..')
    )
      throw new Error('Invalid R2 object key');
    const uri = `/${encode(bucket)}/${key.split('/').map(encode).join('/')}`;
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

export function r2StoreFromEnvironment(env = process.env) {
  return createR2Store({
    accountId: env.BPP_R2_ACCOUNT_ID,
    accessKeyId: env.BPP_R2_ACCESS_KEY_ID,
    secretAccessKey: env.BPP_R2_SECRET_ACCESS_KEY
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

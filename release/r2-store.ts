import crypto from 'node:crypto';
import fs from 'node:fs';
import { Readable } from 'node:stream';
import { pipeline } from 'node:stream/promises';

export interface StoredObject {
  bytes: Buffer;
  etag: string;
}

export interface StoredObjectHead {
  size: number;
  sha256: string | null;
  etag: string | null;
}

export interface GetOptions {
  /** The largest body the read accepts; 1 MiB by default. */
  maxBytes?: number;
}

/** A missing ETag from HEAD cannot satisfy a conditional replacement. */
export interface PutOptions {
  ifMatch?: string | null;
  ifNoneMatch?: boolean;
  contentType?: string;
}

export interface PutFileOptions extends PutOptions {
  sha256: string;
}

export interface GetFileResult {
  size: number;
}

/** Writes return false when their R2 precondition fails. */
export interface ObjectStore {
  get(key: string, options?: GetOptions): Promise<StoredObject | null>;
  head(key: string): Promise<StoredObjectHead | null>;
  put(key: string, bytes: Buffer, options?: PutOptions): Promise<boolean>;
  putFile(key: string, file: string, options: PutFileOptions): Promise<boolean>;
  getFile(key: string, file: string): Promise<GetFileResult | null>;
}

export interface FetchInit extends RequestInit {
  headers: Record<string, string>;
}

export type FetchImpl = (url: string, init: FetchInit) => Promise<Response>;

export interface R2KeyPair {
  accessKeyId: string;
  secretAccessKey: string;
}

export interface R2Credentials extends R2KeyPair {
  accountId: string;
}

export interface CredentialsFromApiTokenOptions {
  token: string | undefined;
  accountId: string | undefined;
  fetchImpl?: FetchImpl;
}

export interface CreateR2StoreOptions {
  accountId: string | undefined;
  accessKeyId?: string;
  secretAccessKey?: string;
  credentials?: () => Promise<R2KeyPair>;
  bucket?: string;
  prefix?: string;
  fetchImpl?: FetchImpl;
  now?: () => Date;
}

export interface R2StoreFromApiTokenOptions {
  token: string | undefined;
  accountId: string | undefined;
  bucket?: string;
  prefix?: string;
  fetchImpl?: FetchImpl;
  now?: () => Date;
}

export type R2StoreEnvironmentOptions = Omit<
  R2StoreFromApiTokenOptions,
  'token' | 'accountId'
>;

export interface PutImmutableFileOptions {
  sha256: string;
  size?: number;
  contentType?: string;
  attempts?: number;
  wait?: (ms: number) => Promise<void>;
}

export type PutImmutableFileOutcome = 'uploaded' | 'present';

type R2Method = 'GET' | 'HEAD' | 'PUT';

interface RequestOptions {
  payloadHash?: string;
  timeoutMs?: number;
}

interface TokenVerification {
  success?: boolean;
  errors?: { code?: unknown }[];
  result?: { id?: string; status?: unknown };
}

const digest = (bytes: string | NodeJS.ArrayBufferView): string =>
  crypto.createHash('sha256').update(bytes).digest('hex');
const hmac = (key: crypto.BinaryLike, value: string): Buffer =>
  crypto.createHmac('sha256', key).update(value).digest();
const encode = (value: string): string =>
  encodeURIComponent(value).replace(
    /[!'()*]/g,
    (char) => `%${char.charCodeAt(0).toString(16).toUpperCase()}`
  );

export const CLOUDFLARE_API = 'https://api.cloudflare.com/client/v4';
const ACCOUNT_ID = /^[a-f0-9]{32}$/i;
const REQUEST_TIMEOUT_MS = 120000;
// Multi-gigabyte transfers need more time than buffered requests.
const FILE_TIMEOUT_MS = 6 * 60 * 60 * 1000;
const TOKEN_ID = /^[a-f0-9]{32}$/;

// Node fetch puts transport diagnostics in nested causes. Callers redact any credentials in them.
const TRANSPORT_FIELDS = [
  'code',
  'errno',
  'syscall',
  'hostname',
  'address',
  'port'
] as const;

type TransportError = Error &
  Partial<Record<(typeof TRANSPORT_FIELDS)[number], unknown>> & {
    errors?: unknown;
  };

export function describeTransportError(error: unknown, depth = 0): string {
  if (depth > 5) return '...';
  if (!(error instanceof Error)) return String(error);
  const failure: TransportError = error;
  const fields = TRANSPORT_FIELDS.filter(
    (field) => failure[field] !== undefined
  )
    .map((field) => `${field}=${failure[field]}`)
    .join(' ');
  let text = `${error.name}: ${error.message}${fields ? ` (${fields})` : ''}`;
  if (Array.isArray(failure.errors) && failure.errors.length)
    text += ` [${failure.errors
      .map((inner) => describeTransportError(inner, depth + 1))
      .join('; ')}]`;
  if (error.cause !== undefined)
    text += ` <- ${describeTransportError(error.cause, depth + 1)}`;
  return text;
}

// Cloudflare's S3 Access Key ID comes from tokens/verify and its Secret Access Key is the token's SHA-256.
const verifiedTokenIds = new Map<string, Promise<string>>();

export function assertAccountId(accountId: string | undefined): string {
  if (accountId === undefined || !ACCOUNT_ID.test(accountId))
    throw new Error(
      'CLOUDFLARE_ACCOUNT_ID must be the 32-character hexadecimal account id'
    );
  return accountId.toLowerCase();
}

function assertToken(token: string | undefined): string {
  if (typeof token !== 'string' || !token.trim())
    throw new Error('CLOUDFLARE_API_TOKEN is empty');
  return token.trim();
}

async function verifyToken(
  token: string,
  account: string,
  fetchImpl: FetchImpl
): Promise<string> {
  let response: Response;
  try {
    response = await fetchImpl(
      `${CLOUDFLARE_API}/accounts/${account}/tokens/verify`,
      {
        headers: { authorization: `Bearer ${token}` },
        redirect: 'error',
        signal: AbortSignal.timeout(30000)
      }
    );
  } catch (error) {
    // undici's header validation quotes the raw header value in its message.
    const reason = describeTransportError(error).replaceAll(token, '<token>');
    throw new Error(`Cloudflare token verification failed: ${reason}`, {
      cause: error
    });
  }
  const body = (await response
    .json()
    .catch(() => null)) as TokenVerification | null;
  const codes = (body?.errors ?? [])
    .map((error) => error.code)
    .filter(Boolean)
    .join(', ');
  if (!response.ok || body?.success !== true)
    throw new Error(
      `Cloudflare rejected CLOUDFLARE_API_TOKEN: HTTP ${response.status}${codes ? ` (error ${codes})` : ''}; it must be an Account API Token of CLOUDFLARE_ACCOUNT_ID`
    );
  const { id, status } = body.result ?? {};
  if (!id || !TOKEN_ID.test(id))
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
}: CredentialsFromApiTokenOptions): Promise<R2Credentials> {
  const value = assertToken(token);
  const account = assertAccountId(accountId);
  const secretAccessKey = digest(value);
  const cacheKey = `${account}:${secretAccessKey}`;
  let pending = verifiedTokenIds.get(cacheKey);
  if (!pending) {
    pending = verifyToken(value, account, fetchImpl).catch((error) => {
      verifiedTokenIds.delete(cacheKey);
      throw error;
    });
    verifiedTokenIds.set(cacheKey, pending);
  }
  return { accountId: account, accessKeyId: await pending, secretAccessKey };
}

// Public release keys are relative to installer/, matching the path in RELEASE_BASE_URL.
export const RELEASE_BUCKET = 'bazaarplusplus-public';
export const RELEASE_PREFIX = 'installer';

export function createR2Store({
  accountId,
  accessKeyId,
  secretAccessKey,
  credentials,
  bucket = RELEASE_BUCKET,
  prefix = bucket === RELEASE_BUCKET ? RELEASE_PREFIX : '',
  fetchImpl = fetch,
  now = () => new Date()
}: CreateR2StoreOptions): ObjectStore {
  const account = assertAccountId(accountId);
  if (typeof credentials !== 'function') {
    if (!accessKeyId || !secretAccessKey)
      throw new Error('R2 access requires an access key id and secret');
    const pair = { accessKeyId, secretAccessKey };
    credentials = async () => pair;
  }
  const resolveCredentials = credentials;
  if (!/^[a-z0-9][a-z0-9-]*[a-z0-9]$/.test(bucket))
    throw new Error('Invalid R2 bucket');
  if (prefix && !/^[a-z0-9][a-z0-9-]*(\/[a-z0-9][a-z0-9-]*)*$/.test(prefix))
    throw new Error('Invalid R2 key prefix');
  const host = `${account}.r2.cloudflarestorage.com`;

  async function request(
    method: R2Method,
    key: string,
    body?: Buffer | ReadableStream<Uint8Array>,
    extraHeaders: Record<string, string> = {},
    { payloadHash, timeoutMs }: RequestOptions = {}
  ): Promise<Response> {
    if (payloadHash === undefined) {
      if (body instanceof ReadableStream)
        throw new Error('A streamed R2 request needs its payload hash');
      payloadHash = digest(body ?? Buffer.alloc(0));
    }
    if (
      typeof key !== 'string' ||
      key.split('/').some((part) => !part || part === '.' || part === '..')
    )
      throw new Error('Invalid R2 object key');
    const stored = prefix ? `${prefix}/${key}` : key;
    const uri = `/${encode(bucket)}/${stored.split('/').map(encode).join('/')}`;
    const { accessKeyId, secretAccessKey } = await resolveCredentials();
    const date = now()
      .toISOString()
      .replace(/[:-]|\.\d{3}/g, '');
    const day = date.slice(0, 8);
    const headers: Record<string, string> = {
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
    let response: Response;
    try {
      response = await fetchImpl(`https://${host}${uri}`, {
        method,
        // Compression can weaken ETags and change HEAD's content length.
        // Keep transport negotiation outside the signed object headers.
        headers: { ...headers, 'accept-encoding': 'identity' },
        body,
        ...(body instanceof ReadableStream ? { duplex: 'half' } : {}),
        redirect: 'error',
        signal: AbortSignal.timeout(timeoutMs ?? REQUEST_TIMEOUT_MS)
      });
    } catch (error) {
      // The host embeds the account id; keep it out of CI logs.
      const reason = describeTransportError(error).replaceAll(
        account,
        '<account>'
      );
      throw new Error(`R2 ${method} ${key} failed: ${reason}`, {
        cause: error
      });
    }
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
      if (!response.body) throw new Error(`R2 GET ${key} returned no body`);
      const chunks: Buffer[] = [];
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
      }: PutOptions = {}
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
    },
    async putFile(
      key,
      file,
      {
        sha256,
        ifMatch,
        ifNoneMatch = !ifMatch,
        contentType = 'application/octet-stream'
      }
    ) {
      if (Boolean(ifMatch) === Boolean(ifNoneMatch))
        throw new Error(
          'Every R2 write requires exactly one conditional precondition'
        );
      if (!/^[0-9a-f]{64}$/.test(sha256 ?? ''))
        throw new Error('A streamed R2 write needs the file sha256');
      const { size } = fs.statSync(file);
      const response = await request(
        'PUT',
        key,
        Readable.toWeb(fs.createReadStream(file)),
        {
          'content-length': String(size),
          'content-type': contentType,
          'x-amz-meta-sha256': sha256,
          ...(ifMatch ? { 'if-match': ifMatch } : { 'if-none-match': '*' })
        },
        { payloadHash: sha256, timeoutMs: FILE_TIMEOUT_MS }
      );
      await response.body?.cancel();
      if (response.status === 412) return false;
      if (response.status === 404)
        throw new Error(`R2 PUT ${key} failed with HTTP 404`);
      return true;
    },
    async getFile(key, file) {
      const response = await request(
        'GET',
        key,
        undefined,
        {},
        {
          timeoutMs: FILE_TIMEOUT_MS
        }
      );
      if (response.status !== 200) {
        await response.body?.cancel();
        if (response.status === 404) return null;
        throw new Error(`R2 GET ${key} failed with HTTP ${response.status}`);
      }
      if (!response.body) throw new Error(`R2 GET ${key} returned no body`);
      await pipeline(
        Readable.fromWeb(response.body),
        fs.createWriteStream(file)
      );
      return { size: fs.statSync(file).size };
    }
  };
}

export function r2StoreFromApiToken({
  token,
  accountId,
  bucket,
  prefix,
  fetchImpl = fetch,
  now
}: R2StoreFromApiTokenOptions): ObjectStore {
  const value = assertToken(token);
  const account = assertAccountId(accountId);
  return createR2Store({
    accountId: account,
    credentials: () =>
      credentialsFromApiToken({ token: value, accountId: account, fetchImpl }),
    ...(bucket === undefined ? {} : { bucket }),
    ...(prefix === undefined ? {} : { prefix }),
    fetchImpl,
    ...(now === undefined ? {} : { now })
  });
}

export function r2StoreFromEnvironment(
  env: NodeJS.ProcessEnv = process.env,
  options: R2StoreEnvironmentOptions = {}
): ObjectStore {
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

export async function putImmutable(
  store: Pick<ObjectStore, 'get' | 'put'>,
  key: string,
  bytes: Buffer,
  contentType?: string
): Promise<void> {
  let uncertain: unknown;
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

// R2 accepts at most 5 GiB in one PUT; an official game APK is about 2.8 GB.
export const R2_SINGLE_PUT_LIMIT = 5 * 1024 ** 3;

// Read metadata with HEAD after a transfer to settle lost PUT responses without downloading large files.
export async function putImmutableFile(
  store: Pick<ObjectStore, 'putFile' | 'head'>,
  key: string,
  file: string,
  {
    sha256,
    size = fs.statSync(file).size,
    contentType,
    attempts = 3,
    wait = (ms) => new Promise<void>((resolve) => setTimeout(resolve, ms))
  }: PutImmutableFileOptions
): Promise<PutImmutableFileOutcome> {
  if (size > R2_SINGLE_PUT_LIMIT)
    throw new Error(
      `${key} is ${size} bytes, above the ${R2_SINGLE_PUT_LIMIT}-byte single PUT limit`
    );
  let failure: unknown;
  for (let attempt = 1; attempt <= attempts; attempt++) {
    let created = false;
    try {
      created = await store.putFile(key, file, { sha256, contentType });
    } catch (error) {
      failure = error;
    }
    if (created) return 'uploaded';
    const existing = await store.head(key);
    if (existing) {
      if (existing.size === size && existing.sha256 === sha256)
        return 'present';
      throw new Error(
        `Immutable release object differs: ${key}; publish a new product version`
      );
    }
    failure ??= new Error(`R2 did not store ${key}`);
    if (attempt < attempts) await wait(attempt * 5000);
  }
  throw failure;
}

export async function putReplaceable(
  store: Pick<ObjectStore, 'get' | 'put'>,
  key: string,
  bytes: Buffer,
  contentType?: string,
  attempts = 4
): Promise<void> {
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

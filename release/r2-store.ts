import crypto from 'node:crypto';
import fs from 'node:fs';
import { Readable } from 'node:stream';
import { pipeline } from 'node:stream/promises';

/** An object read whole, with the ETag a conditional replacement names. */
export interface StoredObject {
  bytes: Buffer;
  etag: string;
}

/** What a HEAD of an object reports; R2 omits a header it does not hold. */
export interface StoredObjectHead {
  size: number;
  sha256: string | null;
  etag: string | null;
}

/** The options of ObjectStore.get. */
export interface GetOptions {
  /** The largest body the read accepts; 1 MiB by default. */
  maxBytes?: number;
}

/**
 * A conditional write: exactly one of `ifMatch` and `ifNoneMatch`. A null
 * `ifMatch`, as a HEAD without an ETag gives, names no ETag.
 */
export interface PutOptions {
  ifMatch?: string | null;
  ifNoneMatch?: boolean;
  contentType?: string;
}

/** A streamed conditional write of a file whose sha256 the caller holds. */
export interface PutFileOptions extends PutOptions {
  sha256: string;
}

/** What ObjectStore.getFile wrote. */
export interface GetFileResult {
  size: number;
}

/**
 * The release object store: R2 through createR2Store, and the in-memory
 * stand-in tests use. Every write is conditional; a lost precondition
 * returns false.
 */
export interface ObjectStore {
  get(key: string, options?: GetOptions): Promise<StoredObject | null>;
  head(key: string): Promise<StoredObjectHead | null>;
  put(key: string, bytes: Buffer, options?: PutOptions): Promise<boolean>;
  putFile(key: string, file: string, options: PutFileOptions): Promise<boolean>;
  getFile(key: string, file: string): Promise<GetFileResult | null>;
}

/** The request the store sends; its headers are always a plain record. */
export interface FetchInit extends RequestInit {
  headers: Record<string, string>;
}

/** The fetch the store calls: `fetch`, or a test double of it. */
export type FetchImpl = (url: string, init: FetchInit) => Promise<Response>;

/** An R2 S3 key pair. */
export interface R2KeyPair {
  accessKeyId: string;
  secretAccessKey: string;
}

/** The S3 key pair credentialsFromApiToken derives, with its account. */
export interface R2Credentials extends R2KeyPair {
  accountId: string;
}

/** The options of credentialsFromApiToken. */
export interface CredentialsFromApiTokenOptions {
  token: string | undefined;
  accountId: string | undefined;
  fetchImpl?: FetchImpl;
}

/** The options of createR2Store: a fixed key pair or a resolver of one. */
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

/** The options of r2StoreFromApiToken. */
export interface R2StoreFromApiTokenOptions {
  token: string | undefined;
  accountId: string | undefined;
  bucket?: string;
  prefix?: string;
  fetchImpl?: FetchImpl;
  now?: () => Date;
}

/** The options of r2StoreFromEnvironment; the credentials come from `env`. */
export type R2StoreEnvironmentOptions = Omit<
  R2StoreFromApiTokenOptions,
  'token' | 'accountId'
>;

/** The options of putImmutableFile. */
export interface PutImmutableFileOptions {
  sha256: string;
  size?: number;
  contentType?: string;
  attempts?: number;
  wait?: (ms: number) => Promise<void>;
}

/** How putImmutableFile settled: this upload, or an identical object. */
export type PutImmutableFileOutcome = 'uploaded' | 'present';

type R2Method = 'GET' | 'HEAD' | 'PUT';

interface RequestOptions {
  payloadHash?: string;
  timeoutMs?: number;
}

// The Cloudflare tokens/verify response, as it claims to be.
interface TokenVerification {
  success?: boolean;
  errors?: { code?: unknown }[];
  result?: { id?: string; status?: unknown };
}

// Content and signing digests; also the R2 Secret Access Key, which Cloudflare
// defines as the SHA-256 of the API token value (not password storage).
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
// A file transfer streams its body; a 2.8 GB official APK needs far longer
// than one buffered request.
const FILE_TIMEOUT_MS = 6 * 60 * 60 * 1000;
const TOKEN_ID = /^[a-f0-9]{32}$/;

// Node's fetch rejects with a bare "fetch failed" and keeps the reason (DNS,
// reset, TLS, timeout) on err.cause, often nested and sometimes an
// AggregateError. Flattens that chain into one line of messages and the
// system-error fields. It never reads request options; callers redact the
// token or account id that an error message itself may quote.
const TRANSPORT_FIELDS = [
  'code',
  'errno',
  'syscall',
  'hostname',
  'address',
  'port'
] as const;

// An Error with the system-error fields, or an AggregateError's `errors`.
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

// R2's S3 credentials are a view of a Cloudflare Account API Token: the Access
// Key ID is the token's id, which GET /accounts/{account}/tokens/verify
// reveals, and the Secret Access Key is the SHA-256 of the token value. Every
// store derives the pair from CLOUDFLARE_API_TOKEN and CLOUDFLARE_ACCOUNT_ID
// this way; nothing stores the pair. The verified id is cached per process
// under the account and the token's digest.
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

// Only account-owned tokens are supported; a user token is rejected here.
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

// Release artifacts live under installer/ in the public bucket, served at
// https://cdn.bazaarplusplus.com/installer (RELEASE_BASE_URL). Callers use keys
// relative to the prefix; another bucket has no prefix unless one is given.
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
  // A fixed pair (tests, callers that already hold one) or a resolver that
  // derives it on first use, so construction never touches the network.
  if (typeof credentials !== 'function') {
    if (!accessKeyId || !secretAccessKey)
      throw new Error('R2 access requires an access key id and secret');
    const pair = { accessKeyId, secretAccessKey };
    credentials = async () => pair;
  }
  // Narrowed here, so the request closure sees a resolver, never undefined.
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
    // A streamed body is never hashed here: its caller passes payloadHash.
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
    },
    // Streams a local file whose sha256 the caller already holds; R2 checks
    // the body against it through the signed payload hash.
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
    // Streams an object into a file; null when it does not exist.
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

// A store whose S3 pair is derived from an API token on its first request.
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

// Streams a local file to an immutable key. A large transfer can fail or lose
// its response after R2 stored the object, so every outcome is settled by a
// HEAD of the key, never by reading the object back: an object of the same
// size and sha256 is this upload, a different one fails, and no object means
// the transfer is retried.
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

// A replaceable object converges on `bytes` through ETag compare-and-swap:
// mirror records before their platform is promoted, snapshot manifests.
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

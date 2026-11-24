import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';
import {
  ZIP_FILES,
  ZIP_MANIFEST,
  ZIP_SIGNATURE,
  parseManagedPayloadManifest
} from './managed-payload.ts';
import type {
  ManagedPayloadManifest,
  ManagedPayloadSigner
} from './managed-payload.ts';
import { readZipEntries } from './payload-zip.ts';
import { putImmutable } from './r2-store.ts';
import type { ObjectStore } from './r2-store.ts';

// Format changes must land with every parser in the root AGENTS.md Contracts table.

export const CHUNK_INDEX_HEADER = 'bpp-managed-payload-chunks 1';
export const FASTCDC_ALGORITHM = 'fastcdc-gear32-v1';
export const CHUNK_COMPRESSION = 'deflate-raw';
export const MANAGED_CHUNKS_PREFIX = 'managed/chunks';

export const DEFAULT_CHUNKING: Readonly<FastCdcParams> = Object.freeze({
  minSize: 4096,
  avgSize: 16384,
  maxSize: 65536
});

export const MAX_CHUNK_INDEX_BYTES = 4 * 1024 * 1024;
export const MAX_MANIFEST_BYTES = 1024 * 1024;
export const MAX_MANIFEST_SIGNATURE_BYTES = 16 * 1024;
export const MAX_CHUNK_RAW_SIZE = 256 * 1024;
export const MAX_CHUNK_COMPRESSED_OVERHEAD = 1024;
export const MAX_CHUNK_COUNT = 65536;
export const MAX_FILE_COUNT = 4096;
export const MAX_CHUNK_REFERENCES = 131072;
export const FASTCDC_WARMUP_BYTES = 32;
export const FASTCDC_MIN_BITS = 10;
export const FASTCDC_MAX_BITS = 18;

export interface FastCdcParams {
  minSize: number;
  avgSize: number;
  maxSize: number;
}

export interface ManagedPayloadChunkRecord {
  rawSha256: string;
  rawSize: number;
  compressedSha256: string;
  compressedSize: number;
}

export interface ManagedPayloadChunkedFile {
  path: string;
  size: number;
  sha256: string;
  chunks: number[];
}

export interface ManagedPayloadChunkIndex {
  version: string;
  platformKey: string;
  chunking: FastCdcParams;
  manifestText: string;
  manifestSignature: string;
  manifest: ManagedPayloadManifest;
  chunks: ManagedPayloadChunkRecord[];
  files: ManagedPayloadChunkedFile[];
}

export interface BuiltChunkIndex {
  index: ManagedPayloadChunkIndex;
  text: string;
  compressedChunks: Map<string, Buffer>;
}

export interface WriteManagedPayloadChunkIndexOptions {
  zipPath: string;
  outputPath: string;
  sign: ManagedPayloadSigner;
  params?: FastCdcParams;
}

export interface WrittenManagedPayloadChunkIndex {
  path: string;
  size: number;
  sha256: string;
  signature: string;
  index: ManagedPayloadChunkIndex;
}

export interface UploadManagedChunksOptions {
  concurrency?: number;
  attempts?: number;
  wait?: (ms: number) => Promise<void>;
  log?: (line: string) => void;
}

export interface VerifyManagedChunksOptions {
  concurrency?: number;
  attempts?: number;
  wait?: (ms: number) => Promise<void>;
}

const SHA256 = /^[a-f0-9]{64}$/;
const STRICT_BASE64 =
  /^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/;
const VERSION = /^(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)$/;
const PLATFORM = /^[A-Za-z0-9_-]+$/;
const NON_NEGATIVE_INT = /^(?:0|[1-9]\d*)$/;

const sha256 = (bytes: string | NodeJS.ArrayBufferView): string =>
  crypto.createHash('sha256').update(bytes).digest('hex');

const ordinal = (left: string, right: string): number =>
  left < right ? -1 : left > right ? 1 : 0;

let cachedGearTable: Uint32Array | null = null;

// Deterministic 32-bit Gear table: little-endian uint32 of SHA-256([i])[0..4].
export function fastCdcGearTable(): Uint32Array {
  if (cachedGearTable) return cachedGearTable;
  const table = new Uint32Array(256);
  const byte = Buffer.alloc(1);
  for (let i = 0; i < 256; i++) {
    byte[0] = i;
    const digest = crypto.createHash('sha256').update(byte).digest();
    table[i] = digest.readUInt32LE(0);
  }
  cachedGearTable = table;
  return table;
}

export function validateFastCdcParams(params: FastCdcParams): number {
  const { minSize, avgSize, maxSize } = params;
  if (
    !Number.isSafeInteger(minSize) ||
    !Number.isSafeInteger(avgSize) ||
    !Number.isSafeInteger(maxSize)
  )
    throw new Error('FastCDC parameters must be safe integers');
  if (minSize < FASTCDC_WARMUP_BYTES)
    throw new Error(
      `FastCDC minSize (${minSize}) must be at least ${FASTCDC_WARMUP_BYTES}`
    );
  if (avgSize <= 0 || (avgSize & (avgSize - 1)) !== 0)
    throw new Error(`FastCDC avgSize (${avgSize}) must be a power of two`);
  const bits = Math.log2(avgSize);
  if (bits < FASTCDC_MIN_BITS || bits > FASTCDC_MAX_BITS)
    throw new Error(
      `FastCDC avgSize bits (${bits}) must be between ${FASTCDC_MIN_BITS} and ${FASTCDC_MAX_BITS}`
    );
  if (minSize >= avgSize || avgSize >= maxSize)
    throw new Error(
      `FastCDC sizes must satisfy minSize < avgSize < maxSize (${minSize}, ${avgSize}, ${maxSize})`
    );
  if (maxSize > MAX_CHUNK_RAW_SIZE)
    throw new Error(
      `FastCDC maxSize (${maxSize}) exceeds ${MAX_CHUNK_RAW_SIZE}`
    );
  return bits;
}

// FastCDC with normalized chunking (level 1) and 32-byte warmup so the first cut
// at pos + minSize depends on all 32 preceding bytes. Chunk lengths are strictly
// in [minSize, maxSize] except for the trailing chunk.
export function fastCdcBoundaries(
  data: Uint8Array,
  params: FastCdcParams = DEFAULT_CHUNKING
): number[] {
  const bits = validateFastCdcParams(params);
  const { minSize, avgSize, maxSize } = params;
  const n = data.length;
  if (n === 0) return [];
  const gear = fastCdcGearTable();
  const maskS = (~0 << (32 - (bits + 1))) >>> 0;
  const maskL = (~0 << (32 - (bits - 1))) >>> 0;
  const boundaries: number[] = [];
  let pos = 0;
  while (pos < n) {
    if (n - pos <= minSize) {
      boundaries.push(n);
      break;
    }
    const end = Math.min(pos + maxSize, n);
    const normal = Math.min(pos + avgSize, end);
    let fp = 0;
    const firstTest = pos + minSize - 1;
    for (let w = pos + minSize - FASTCDC_WARMUP_BYTES; w < firstTest; w++) {
      fp = ((fp << 1) + gear[data[w]]) >>> 0;
    }
    let cut = end;
    let i = firstTest;
    while (i < normal) {
      fp = ((fp << 1) + gear[data[i]]) >>> 0;
      if ((fp & maskS) === 0) {
        cut = i + 1;
        break;
      }
      i++;
    }
    if (cut === end) {
      while (i < end) {
        fp = ((fp << 1) + gear[data[i]]) >>> 0;
        if ((fp & maskL) === 0) {
          cut = i + 1;
          break;
        }
        i++;
      }
    }
    boundaries.push(cut);
    pos = cut;
  }
  return boundaries;
}

export function chunkBytes(
  data: Uint8Array,
  params: FastCdcParams = DEFAULT_CHUNKING
): Buffer[] {
  const boundaries = fastCdcBoundaries(data, params);
  const chunks: Buffer[] = [];
  let start = 0;
  for (const end of boundaries) {
    chunks.push(Buffer.from(data.subarray(start, end)));
    start = end;
  }
  return chunks;
}

export function compressChunk(raw: Uint8Array): Buffer {
  return zlib.deflateRawSync(raw, { level: 6 });
}

export function decompressChunk(
  compressed: Uint8Array,
  expectedRawSize: number
): Buffer {
  if (
    !Number.isSafeInteger(expectedRawSize) ||
    expectedRawSize <= 0 ||
    expectedRawSize > MAX_CHUNK_RAW_SIZE
  )
    throw new Error(`Invalid expected raw chunk size: ${expectedRawSize}`);
  const raw = zlib.inflateRawSync(compressed, {
    maxOutputLength: expectedRawSize
  });
  if (raw.length !== expectedRawSize)
    throw new Error(
      `Decompressed chunk size ${raw.length} differs from expected ${expectedRawSize}`
    );
  return raw;
}

export function managedChunkKey(compressedSha256: string): string {
  if (!SHA256.test(compressedSha256 ?? ''))
    throw new Error(`Invalid managed chunk digest: ${compressedSha256}`);
  return `${MANAGED_CHUNKS_PREFIX}/${compressedSha256.slice(0, 2)}/${compressedSha256}.deflate`;
}

function decodeStrictBase64(
  value: string,
  label: string,
  maxBytes: number
): Buffer {
  if (!value || value.length % 4 !== 0 || !STRICT_BASE64.test(value))
    throw new Error(`Invalid base64 in ${label}`);
  const decoded = Buffer.from(value, 'base64');
  if (decoded.length === 0 || decoded.length > maxBytes)
    throw new Error(
      `${label} decoded size (${decoded.length}) is out of bounds`
    );
  if (decoded.toString('base64') !== value)
    throw new Error(`Non-canonical base64 in ${label}`);
  return decoded;
}

function parseNonNegativeInt(value: string, label: string): number {
  if (!NON_NEGATIVE_INT.test(value))
    throw new Error(`Invalid integer for ${label}: ${value}`);
  const parsed = Number(value);
  if (!Number.isSafeInteger(parsed))
    throw new Error(`Integer overflow for ${label}: ${value}`);
  return parsed;
}

export function renderChunkIndex(index: ManagedPayloadChunkIndex): string {
  validateFastCdcParams(index.chunking);
  if (!VERSION.test(index.version))
    throw new Error(`Invalid chunk index version: ${index.version}`);
  if (!PLATFORM.test(index.platformKey))
    throw new Error(`Invalid chunk index platform: ${index.platformKey}`);
  const manifestBytes = Buffer.from(index.manifestText, 'utf8');
  const signatureBytes = Buffer.from(index.manifestSignature, 'utf8');
  if (manifestBytes.length === 0 || manifestBytes.length > MAX_MANIFEST_BYTES)
    throw new Error('Invalid embedded Managed Payload Manifest size');
  if (
    signatureBytes.length === 0 ||
    signatureBytes.length > MAX_MANIFEST_SIGNATURE_BYTES
  )
    throw new Error('Invalid embedded Managed Payload Manifest signature size');

  const lines = [
    CHUNK_INDEX_HEADER,
    `version ${index.version}`,
    `platform ${index.platformKey}`,
    `chunking ${FASTCDC_ALGORITHM} ${index.chunking.minSize} ${index.chunking.avgSize} ${index.chunking.maxSize}`,
    `compression ${CHUNK_COMPRESSION}`,
    `manifest-base64 ${manifestBytes.toString('base64')}`,
    `manifest-sig-base64 ${signatureBytes.toString('base64')}`,
    ...index.chunks.map(
      (chunk) =>
        `chunk ${chunk.rawSha256} ${chunk.rawSize} ${chunk.compressedSha256} ${chunk.compressedSize}`
    ),
    ...[...index.files]
      .sort((left, right) => ordinal(left.path, right.path))
      .map(
        (file) =>
          `file ${file.sha256} ${file.size} ${file.chunks.length === 0 ? '-' : file.chunks.join(',')} ${file.path}`
      )
  ];
  const text = `${lines.join('\n')}\n`;
  // Self-check every rendered index through the strict parser.
  parseChunkIndex(text);
  return text;
}

export function parseChunkIndex(text: string): ManagedPayloadChunkIndex {
  if (typeof text !== 'string' || !text.endsWith('\n') || text.includes('\r'))
    throw new Error(
      'A Managed Payload Chunk Index must be LF-terminated lines'
    );
  if (Buffer.byteLength(text, 'utf8') > MAX_CHUNK_INDEX_BYTES)
    throw new Error('Managed Payload Chunk Index exceeds maximum allowed size');
  const lines = text.slice(0, -1).split('\n');
  if (lines.length < 8 || lines[0] !== CHUNK_INDEX_HEADER)
    throw new Error('Not a Managed Payload Chunk Index');

  const field = (line: string | undefined, name: string): string => {
    const prefix = `${name} `;
    if (!line?.startsWith(prefix))
      throw new Error(`Managed Payload Chunk Index lacks ${name}`);
    return line.slice(prefix.length);
  };

  const version = field(lines[1], 'version');
  if (!VERSION.test(version))
    throw new Error(`Invalid chunk index version: ${version}`);
  const platformKey = field(lines[2], 'platform');
  if (!PLATFORM.test(platformKey))
    throw new Error(`Invalid chunk index platform: ${platformKey}`);

  const chunkingParts = field(lines[3], 'chunking').split(' ');
  if (chunkingParts.length !== 4 || chunkingParts[0] !== FASTCDC_ALGORITHM)
    throw new Error(`Unsupported chunking header: ${lines[3]}`);
  const chunking: FastCdcParams = {
    minSize: parseNonNegativeInt(chunkingParts[1], 'minSize'),
    avgSize: parseNonNegativeInt(chunkingParts[2], 'avgSize'),
    maxSize: parseNonNegativeInt(chunkingParts[3], 'maxSize')
  };
  validateFastCdcParams(chunking);

  const compression = field(lines[4], 'compression');
  if (compression !== CHUNK_COMPRESSION)
    throw new Error(`Unsupported chunk compression: ${compression}`);

  const manifestText = decodeStrictBase64(
    field(lines[5], 'manifest-base64'),
    'manifest-base64',
    MAX_MANIFEST_BYTES
  ).toString('utf8');
  const manifestSignature = decodeStrictBase64(
    field(lines[6], 'manifest-sig-base64'),
    'manifest-sig-base64',
    MAX_MANIFEST_SIGNATURE_BYTES
  ).toString('utf8');
  if (!manifestSignature.trim())
    throw new Error('Embedded Managed Payload Manifest signature is empty');

  const manifest = parseManagedPayloadManifest(manifestText);
  if (manifest.version !== version)
    throw new Error(
      `Chunk index version ${version} differs from manifest version ${manifest.version}`
    );
  if (manifest.platformKey !== platformKey)
    throw new Error(
      `Chunk index platform ${platformKey} differs from manifest platform ${manifest.platformKey}`
    );

  const chunks: ManagedPayloadChunkRecord[] = [];
  const seenRawSha = new Set<string>();
  const files: ManagedPayloadChunkedFile[] = [];
  let inFiles = false;
  let totalRefs = 0;

  for (let i = 7; i < lines.length; i++) {
    const line = lines[i];
    if (line.startsWith('chunk ')) {
      if (inFiles)
        throw new Error(
          `Chunk entry appears after file entries at line ${i + 1}`
        );
      if (chunks.length >= MAX_CHUNK_COUNT)
        throw new Error(`Chunk count exceeds limit ${MAX_CHUNK_COUNT}`);
      const parts = line.split(' ');
      if (parts.length !== 5) throw new Error(`Invalid chunk line ${i + 1}`);
      const rawSha256 = parts[1];
      const rawSize = parseNonNegativeInt(parts[2], 'rawSize');
      const compressedSha256 = parts[3];
      const compressedSize = parseNonNegativeInt(parts[4], 'compressedSize');
      if (
        !SHA256.test(rawSha256) ||
        !SHA256.test(compressedSha256) ||
        rawSize <= 0 ||
        rawSize > chunking.maxSize ||
        compressedSize <= 0 ||
        compressedSize > chunking.maxSize + MAX_CHUNK_COMPRESSED_OVERHEAD
      )
        throw new Error(`Invalid chunk metadata at line ${i + 1}`);
      if (seenRawSha.has(rawSha256))
        throw new Error(`Duplicate chunk rawSha256 at line ${i + 1}`);
      seenRawSha.add(rawSha256);
      chunks.push({ rawSha256, rawSize, compressedSha256, compressedSize });
      continue;
    }
    if (line.startsWith('file ')) {
      inFiles = true;
      if (files.length >= MAX_FILE_COUNT)
        throw new Error(`File count exceeds limit ${MAX_FILE_COUNT}`);
      const match = /^file ([a-f0-9]{64}) (\d+) ([^ ]+) (.+)$/.exec(line);
      if (!match) throw new Error(`Invalid file line ${i + 1}`);
      const fileSha256 = match[1];
      const size = parseNonNegativeInt(match[2], 'file size');
      const refsToken = match[3];
      const filePath = match[4];
      const fileChunks: number[] = [];
      if (refsToken === '-') {
        if (size !== 0)
          throw new Error(`Non-empty file has no chunks at line ${i + 1}`);
      } else {
        const tokens = refsToken.split(',');
        totalRefs += tokens.length;
        if (totalRefs > MAX_CHUNK_REFERENCES)
          throw new Error(
            `Chunk reference count exceeds limit ${MAX_CHUNK_REFERENCES}`
          );
        let sum = 0;
        for (const token of tokens) {
          const idx = parseNonNegativeInt(token, 'chunk index');
          if (idx >= chunks.length)
            throw new Error(`Chunk index ${idx} out of range at line ${i + 1}`);
          fileChunks.push(idx);
          sum += chunks[idx].rawSize;
        }
        if (sum !== size)
          throw new Error(
            `File chunk size sum ${sum} differs from file size ${size} at line ${i + 1}`
          );
      }
      files.push({
        path: filePath,
        size,
        sha256: fileSha256,
        chunks: fileChunks
      });
      continue;
    }
    throw new Error(`Unexpected chunk index line ${i + 1}`);
  }

  if (files.length !== manifest.files.length)
    throw new Error(
      `Chunk index lists ${files.length} files, but manifest lists ${manifest.files.length}`
    );
  for (let i = 0; i < files.length; i++) {
    const file = files[i];
    const expected = manifest.files[i];
    if (
      file.path !== expected.path ||
      file.size !== expected.size ||
      file.sha256 !== expected.sha256
    )
      throw new Error(
        `Chunk index file ${file.path} does not match manifest entry ${expected.path}`
      );
  }

  return {
    version,
    platformKey,
    chunking,
    manifestText,
    manifestSignature,
    manifest,
    chunks,
    files
  };
}

export function buildChunkIndexFromFiles({
  manifestText,
  manifestSignature,
  readFile,
  params = DEFAULT_CHUNKING
}: {
  manifestText: string;
  manifestSignature: string;
  readFile: (relativePath: string) => Buffer;
  params?: FastCdcParams;
}): BuiltChunkIndex {
  validateFastCdcParams(params);
  const manifest = parseManagedPayloadManifest(manifestText);
  const chunks: ManagedPayloadChunkRecord[] = [];
  const rawIndexBySha = new Map<string, number>();
  const compressedChunks = new Map<string, Buffer>();
  const files: ManagedPayloadChunkedFile[] = [];

  for (const file of manifest.files) {
    const bytes = readFile(file.path);
    if (bytes.length !== file.size || sha256(bytes) !== file.sha256)
      throw new Error(
        `Managed Payload file ${file.path} does not match manifest size or sha256`
      );
    const refs: number[] = [];
    for (const raw of chunkBytes(bytes, params)) {
      const rawSha256 = sha256(raw);
      let idx = rawIndexBySha.get(rawSha256);
      if (idx === undefined) {
        const compressed = compressChunk(raw);
        const compressedSha256 = sha256(compressed);
        idx = chunks.length;
        rawIndexBySha.set(rawSha256, idx);
        chunks.push({
          rawSha256,
          rawSize: raw.length,
          compressedSha256,
          compressedSize: compressed.length
        });
        compressedChunks.set(compressedSha256, compressed);
      }
      refs.push(idx);
    }
    files.push({
      path: file.path,
      size: file.size,
      sha256: file.sha256,
      chunks: refs
    });
  }

  const index: ManagedPayloadChunkIndex = {
    version: manifest.version,
    platformKey: manifest.platformKey,
    chunking: { ...params },
    manifestText,
    manifestSignature,
    manifest,
    chunks,
    files
  };
  const text = renderChunkIndex(index);
  return { index, text, compressedChunks };
}

export function buildChunkIndexFromZip(
  zipBytes: Buffer,
  params: FastCdcParams = DEFAULT_CHUNKING
): BuiltChunkIndex {
  const entries = new Map(
    readZipEntries(zipBytes).map((entry) => [entry.name, entry])
  );
  const manifestEntry = entries.get(ZIP_MANIFEST);
  const signatureEntry = entries.get(ZIP_SIGNATURE);
  if (!manifestEntry || !signatureEntry)
    throw new Error(
      'Managed Payload zip lacks manifest.txt or manifest.txt.minisig'
    );
  return buildChunkIndexFromFiles({
    manifestText: manifestEntry.data.toString('utf8'),
    manifestSignature: signatureEntry.data.toString('utf8'),
    readFile: (relativePath) => {
      const entry = entries.get(`${ZIP_FILES}/${relativePath}`);
      if (!entry)
        throw new Error(
          `Managed Payload zip lacks ${ZIP_FILES}/${relativePath}`
        );
      return entry.data;
    },
    params
  });
}

export function writeManagedPayloadChunkIndex({
  zipPath,
  outputPath,
  sign,
  params = DEFAULT_CHUNKING
}: WriteManagedPayloadChunkIndexOptions): WrittenManagedPayloadChunkIndex {
  const zipBytes = fs.readFileSync(zipPath);
  const built = buildChunkIndexFromZip(zipBytes, params);
  fs.mkdirSync(path.dirname(outputPath), { recursive: true });
  fs.writeFileSync(outputPath, built.text, 'utf8');
  const signature = sign({ file: outputPath }).trim();
  if (!signature)
    throw new Error('The Managed Payload Chunk Index signature is empty');
  const bytes = Buffer.from(built.text, 'utf8');
  return {
    path: outputPath,
    size: bytes.length,
    sha256: sha256(bytes),
    signature,
    index: built.index
  };
}

// Re-derives compressed chunks from the verified Managed Payload zip and proves
// every chunk matches the signed chunk index before uploading anything to R2.
export function verifyAndExtractChunksForUpload(
  zipBytes: Buffer,
  chunkIndexBytes: Buffer
): Map<string, { record: ManagedPayloadChunkRecord; bytes: Buffer }> {
  const index = parseChunkIndex(chunkIndexBytes.toString('utf8'));
  const rebuilt = buildChunkIndexFromZip(zipBytes, index.chunking);
  if (rebuilt.text !== chunkIndexBytes.toString('utf8'))
    throw new Error(
      'Managed Payload Chunk Index does not match the Managed Payload zip; rebuild the release'
    );
  const byKey = new Map<
    string,
    { record: ManagedPayloadChunkRecord; bytes: Buffer }
  >();
  for (const record of index.chunks) {
    const bytes = rebuilt.compressedChunks.get(record.compressedSha256);
    if (
      !bytes ||
      bytes.length !== record.compressedSize ||
      sha256(bytes) !== record.compressedSha256
    )
      throw new Error(
        `Derived chunk ${record.compressedSha256} does not match chunk index`
      );
    byKey.set(managedChunkKey(record.compressedSha256), { record, bytes });
  }
  return byKey;
}

async function runPool<T>(
  items: readonly T[],
  concurrency: number,
  worker: (item: T) => Promise<void>
): Promise<void> {
  if (items.length === 0) return;
  let next = 0;
  let firstError: unknown;
  const count = Math.max(1, Math.min(concurrency, items.length));
  await Promise.all(
    Array.from({ length: count }, async () => {
      while (firstError === undefined) {
        const index = next++;
        if (index >= items.length) break;
        try {
          await worker(items[index]);
        } catch (error) {
          firstError ??= error;
          break;
        }
      }
    })
  );
  if (firstError !== undefined) throw firstError;
}

async function withRetries<T>(
  operation: () => Promise<T>,
  attempts: number,
  wait: (ms: number) => Promise<void>
): Promise<T> {
  let lastError: unknown;
  for (let attempt = 1; attempt <= attempts; attempt++) {
    try {
      return await operation();
    } catch (error) {
      lastError = error;
      if (attempt < attempts) await wait(attempt * 500);
    }
  }
  throw lastError;
}

export async function uploadManagedChunks(
  store: Pick<ObjectStore, 'get' | 'head' | 'put'>,
  chunks: Map<string, { record: ManagedPayloadChunkRecord; bytes: Buffer }>,
  {
    concurrency = 16,
    attempts = 3,
    wait = (ms) => new Promise<void>((resolve) => setTimeout(resolve, ms)),
    log = () => {}
  }: UploadManagedChunksOptions = {}
): Promise<{ uploaded: number; reused: number }> {
  let uploaded = 0;
  let reused = 0;
  const entries = [...chunks.entries()].sort(([a], [b]) => ordinal(a, b));
  await runPool(entries, concurrency, async ([key, { record, bytes }]) => {
    const existing = await withRetries(() => store.head(key), attempts, wait);
    if (existing) {
      if (
        existing.size === record.compressedSize &&
        existing.sha256 === record.compressedSha256
      ) {
        reused++;
        return;
      }
      throw new Error(
        `Immutable managed chunk differs: ${key} (expected ${record.compressedSize}/${record.compressedSha256}, found ${existing.size}/${existing.sha256})`
      );
    }
    await withRetries(
      () => putImmutable(store, key, bytes, 'application/octet-stream'),
      attempts,
      wait
    );
    uploaded++;
  });
  log(
    `Managed chunks: uploaded ${uploaded}, reused ${reused} (${entries.length} total)`
  );
  return { uploaded, reused };
}

export async function verifyManagedChunksStored(
  store: Pick<ObjectStore, 'head'>,
  indexes: readonly ManagedPayloadChunkIndex[],
  {
    concurrency = 16,
    attempts = 3,
    wait = (ms) => new Promise<void>((resolve) => setTimeout(resolve, ms))
  }: VerifyManagedChunksOptions = {}
): Promise<void> {
  const unique = new Map<string, ManagedPayloadChunkRecord>();
  for (const index of indexes) {
    for (const chunk of index.chunks) {
      const key = managedChunkKey(chunk.compressedSha256);
      const existing = unique.get(key);
      if (
        existing &&
        (existing.compressedSize !== chunk.compressedSize ||
          existing.rawSha256 !== chunk.rawSha256)
      )
        throw new Error(`Conflicting chunk metadata for ${key}`);
      unique.set(key, chunk);
    }
  }
  const entries = [...unique.entries()].sort(([a], [b]) => ordinal(a, b));
  await runPool(entries, concurrency, async ([key, chunk]) => {
    const stored = await withRetries(() => store.head(key), attempts, wait);
    if (
      !stored ||
      stored.size !== chunk.compressedSize ||
      stored.sha256 !== chunk.compressedSha256
    )
      throw new Error(`Managed chunk is missing or differs: ${key}`);
  });
}

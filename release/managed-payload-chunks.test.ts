import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, expect, test } from 'vite-plus/test';
import {
  DEFAULT_CHUNKING,
  FASTCDC_ALGORITHM,
  buildChunkIndexFromFiles,
  buildChunkIndexFromZip,
  chunkBytes,
  compressChunk,
  decompressChunk,
  fastCdcBoundaries,
  fastCdcGearTable,
  managedChunkKey,
  parseChunkIndex,
  renderChunkIndex,
  uploadManagedChunks,
  verifyAndExtractChunksForUpload,
  verifyManagedChunksStored,
  writeManagedPayloadChunkIndex
} from './managed-payload-chunks.ts';
import { renderManagedPayloadManifest } from './managed-payload.ts';
import {
  buildPlatformFragment,
  managedChunksBaseUrl,
  managedPayloadChunkIndexKey,
  validatePlatformFragment
} from './manifest.ts';
import { createArtifactManifest } from './artifact-manifest.ts';
import { buildZipBuffer } from './payload-zip.ts';
import { RELEASE_BASE_URL } from './product.ts';
import { promotePlatform, uploadPlatform } from './publish.ts';
import { MemoryStore } from './test-support/memory-store.ts';
import { writeArtifacts } from './test-support/artifact-fixture.ts';
import { runFixtureGit } from '../scripts/test-support/git-fixture.ts';

const roots: string[] = [];
afterEach(() => {
  for (const root of roots.splice(0))
    fs.rmSync(root, { recursive: true, force: true });
});

const sha256 = (bytes: string | NodeJS.ArrayBufferView): string =>
  crypto.createHash('sha256').update(bytes).digest('hex');

// Deterministic LCG generator shared conceptually across TS, C#, and Java golden vector tests.
function prngBytes(seed: number, length: number): Buffer {
  const out = Buffer.alloc(length);
  let state = seed >>> 0;
  for (let i = 0; i < length; i++) {
    state = (Math.imul(state, 1664525) + 1013904223) >>> 0;
    out[i] = (state >>> 24) & 0xff;
  }
  return out;
}

test('the shared FastCDC golden vectors match the reference implementation', () => {
  const gear = fastCdcGearTable();
  const params = { minSize: 512, avgSize: 2048, maxSize: 8192 };
  const base16k = prngBytes(0x12345678, 16384);
  const shifted16k = Buffer.concat([
    Buffer.from('PREFIX-INSERTED-BYTES!'),
    base16k
  ]);
  const cases = [
    { name: 'empty', params, seed: 0, length: 0, prefixHex: '' },
    { name: 'below-min', params, seed: 1, length: 200, prefixHex: '' },
    { name: 'exact-min', params, seed: 2, length: 512, prefixHex: '' },
    {
      name: 'prng-16k',
      params,
      seed: 0x12345678,
      length: 16384,
      prefixHex: ''
    },
    {
      name: 'prng-16k-shifted',
      params,
      seed: 0x12345678,
      length: 16384,
      prefixHex: Buffer.from('PREFIX-INSERTED-BYTES!').toString('hex')
    },
    {
      name: 'default-params-96k',
      params: DEFAULT_CHUNKING,
      seed: 0xdeadbeef,
      length: 98304,
      prefixHex: ''
    }
  ];

  const vectors = {
    algorithm: FASTCDC_ALGORITHM,
    gearSamples: [0, 1, 42, 127, 128, 255].map((index) => ({
      index,
      value: gear[index]
    })),
    cases: cases.map((item) => {
      const prefix = item.prefixHex
        ? Buffer.from(item.prefixHex, 'hex')
        : Buffer.alloc(0);
      const data = Buffer.concat([prefix, prngBytes(item.seed, item.length)]);
      const boundaries = fastCdcBoundaries(data, item.params);
      const chunks = chunkBytes(data, item.params);
      // Verify boundaries reconstruct exact input and honor [minSize, maxSize] for non-tail chunks.
      expect(Buffer.concat(chunks).equals(data)).toBe(true);
      for (let i = 0; i < chunks.length - 1; i++) {
        expect(chunks[i].length).toBeGreaterThanOrEqual(item.params.minSize);
        expect(chunks[i].length).toBeLessThanOrEqual(item.params.maxSize);
      }
      return {
        ...item,
        dataSha256: sha256(data),
        boundaries,
        chunkSha256s: chunks.map((chunk) => sha256(chunk))
      };
    })
  };

  // Shifting the front resynchronizes chunk boundaries on the tail.
  const baseHashes = new Set(vectors.cases[3].chunkSha256s);
  const shiftedHits = vectors.cases[4].chunkSha256s.filter((hash) =>
    baseHashes.has(hash)
  );
  expect(shiftedHits.length).toBeGreaterThanOrEqual(3);

  const golden = path.join(
    import.meta.dirname,
    'fixtures/managed-payload/cdc-vectors.json'
  );
  const rendered = `${JSON.stringify(vectors, null, 2)}\n`;
  if (process.env.BPP_UPDATE_GOLDENS === '1') {
    fs.mkdirSync(path.dirname(golden), { recursive: true });
    fs.writeFileSync(golden, rendered);
  }
  expect(JSON.parse(fs.readFileSync(golden, 'utf8'))).toEqual(vectors);
  // Ensure shifted16k was not dead code
  expect(shifted16k.length).toBe(16384 + 'PREFIX-INSERTED-BYTES!'.length);
});

test('the shared chunk index golden parses cleanly and round-trips through deflate', () => {
  const fileA = prngBytes(101, 20000);
  const fileB = Buffer.alloc(0); // 0-byte file edge case
  const fileC = Buffer.concat([
    fileA.subarray(0, 10000),
    Buffer.from('tail-change')
  ]);
  const files = new Map<string, Buffer>([
    ['BepInEx/plugins/A.dll', fileA],
    ['BepInEx/plugins/Empty.txt', fileB],
    ['BepInEx/plugins/Sub File.dll', fileC]
  ]);
  const manifestText = renderManagedPayloadManifest({
    version: '6.1.0',
    platformKey: 'darwin-aarch64',
    historyDatabaseUserVersion: 1,
    nonManagedPayloadDigest: 'e'.repeat(64),
    files: [...files.entries()].map(([filePath, bytes]) => ({
      path: filePath,
      size: bytes.length,
      sha256: sha256(bytes)
    }))
  });
  const manifestSignature =
    'untrusted comment: signature from fixture\nRWQf6LRCGA9i53mlYecO4IzT51TGPpvWucNSCh1CBM0QTaLn73Y7GFO3\ntrusted comment: fixture\nAAAA\n';

  const built = buildChunkIndexFromFiles({
    manifestText,
    manifestSignature,
    readFile: (relativePath) => files.get(relativePath)!,
    params: { minSize: 512, avgSize: 2048, maxSize: 8192 }
  });

  const golden = path.join(
    import.meta.dirname,
    'fixtures/managed-payload/chunks.txt'
  );
  if (process.env.BPP_UPDATE_GOLDENS === '1') {
    fs.mkdirSync(path.dirname(golden), { recursive: true });
    fs.writeFileSync(golden, built.text);
  }
  expect(built.text).toBe(fs.readFileSync(golden, 'utf8'));

  const parsed = parseChunkIndex(built.text);
  expect(renderChunkIndex(parsed)).toBe(built.text);
  expect(
    parsed.files.find((f) => f.path === 'BepInEx/plugins/Empty.txt')?.chunks
  ).toEqual([]);
  expect(
    parsed.files.find((f) => f.path === 'BepInEx/plugins/Sub File.dll')
  ).toBeDefined();

  // Reassemble every file from compressed chunks and verify exact byte identity.
  for (const file of parsed.files) {
    const parts = file.chunks.map((idx) => {
      const record = parsed.chunks[idx];
      const compressed = built.compressedChunks.get(record.compressedSha256)!;
      const raw = decompressChunk(compressed, record.rawSize);
      expect(sha256(raw)).toBe(record.rawSha256);
      return raw;
    });
    expect(Buffer.concat(parts).equals(files.get(file.path)!)).toBe(true);
  }
});

test('parseChunkIndex rejects malformed headers, bounds, base64, and file mismatches', () => {
  const valid = fs.readFileSync(
    path.join(import.meta.dirname, 'fixtures/managed-payload/chunks.txt'),
    'utf8'
  );
  expect(() => parseChunkIndex(valid.replace(/\n/g, '\r\n'))).toThrow(
    /LF-terminated/
  );
  expect(() => parseChunkIndex(valid.slice(0, -1))).toThrow(/LF-terminated/);
  expect(() =>
    parseChunkIndex(
      valid.replace(
        'bpp-managed-payload-chunks 1',
        'bpp-managed-payload-chunks 2'
      )
    )
  ).toThrow(/Not a Managed Payload Chunk Index/);
  expect(() =>
    parseChunkIndex(
      valid.replace(
        'fastcdc-gear32-v1 512 2048 8192',
        'fastcdc-gear32-v1 16 2048 8192'
      )
    )
  ).toThrow(/minSize/);
  expect(() =>
    parseChunkIndex(
      valid.replace(
        'fastcdc-gear32-v1 512 2048 8192',
        'fastcdc-gear32-v1 512 3000 8192'
      )
    )
  ).toThrow(/power of two/);
  expect(() =>
    parseChunkIndex(
      valid.replace('compression deflate-raw', 'compression zstd')
    )
  ).toThrow(/compression/);
  expect(() =>
    parseChunkIndex(
      valid.replace(/^manifest-base64 .+$/m, 'manifest-base64 not valid!')
    )
  ).toThrow(/base64/);
  // Corrupt a file line's chunk index to point out of range
  expect(() =>
    parseChunkIndex(
      valid.replace(/^(file [a-f0-9]{64} \d+) [0-9,]+ /m, '$1 9999 ')
    )
  ).toThrow(/out of range/);
  // Raw deflate decompression bomb check
  const sample = compressChunk(Buffer.alloc(1024, 0x41));
  expect(() => decompressChunk(sample, 100)).toThrow();
});

test('desktop upload + promote uploads deduplicated chunks and promote rejects missing chunks', async () => {
  const workspaceRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-cdc-e2e-'));
  roots.push(workspaceRoot);
  const rootDir = path.join(workspaceRoot, 'bazaarplusplus-installer');
  fs.mkdirSync(rootDir, { recursive: true });
  runFixtureGit(['init', '-q'], { cwd: workspaceRoot });
  fs.writeFileSync(path.join(workspaceRoot, 'VERSION'), '6.5.0\n');
  const exclude = path.join(workspaceRoot, '.git/info/exclude');
  fs.mkdirSync(path.dirname(exclude), { recursive: true });
  fs.appendFileSync(
    exclude,
    'bazaarplusplus-installer/src-tauri/target/\nbazaarplusplus-installer/src-tauri/resources/\n'
  );
  runFixtureGit(['add', '-A'], { cwd: workspaceRoot });
  runFixtureGit(
    [
      '-c',
      'user.name=Test',
      '-c',
      'user.email=t@e.test',
      'commit',
      '-qm',
      'v6.5.0'
    ],
    { cwd: workspaceRoot }
  );

  const sharedDll = prngBytes(7, 40000);
  const modV1 = prngBytes(8, 30000);
  const manifestText = renderManagedPayloadManifest({
    version: '6.5.0',
    platformKey: 'darwin-aarch64',
    historyDatabaseUserVersion: 1,
    nonManagedPayloadDigest: 'e'.repeat(64),
    files: [
      {
        path: 'BepInEx/plugins/Mod.dll',
        size: modV1.length,
        sha256: sha256(modV1)
      },
      {
        path: 'BepInEx/plugins/Shared.dll',
        size: sharedDll.length,
        sha256: sha256(sharedDll)
      }
    ]
  });
  const manifestSig = 'sig-manifest\n';
  const zipBuf = buildZipBuffer([
    { name: 'manifest.txt', data: Buffer.from(manifestText), mode: 0o644 },
    {
      name: 'manifest.txt.minisig',
      data: Buffer.from(manifestSig),
      mode: 0o644
    },
    { name: 'files/BepInEx/plugins/Mod.dll', data: modV1, mode: 0o644 },
    { name: 'files/BepInEx/plugins/Shared.dll', data: sharedDll, mode: 0o644 }
  ]);

  const artifacts = writeArtifacts(rootDir, 'macos', {
    version: '6.5.0',
    managedPayload: 'placeholder'
  });
  fs.writeFileSync(artifacts.managedPayload, zipBuf);
  const chunkIndexPath = path.join(
    path.dirname(artifacts.managedPayload),
    path.posix.basename(managedPayloadChunkIndexKey('6.5.0', 'darwin-aarch64'))
  );
  const writtenIndex = writeManagedPayloadChunkIndex({
    zipPath: artifacts.managedPayload,
    outputPath: chunkIndexPath,
    sign: () => 'chunk-index-sig'
  });
  createArtifactManifest({
    rootDir,
    platform: 'macos',
    version: '6.5.0',
    managedPayload: {
      ...artifacts.managed,
      chunkIndex: { path: writtenIndex.path, signature: writtenIndex.signature }
    }
  });

  const store = new MemoryStore();
  const fragment = await uploadPlatform({
    workspaceRoot,
    platform: 'macos',
    baseUrl: RELEASE_BASE_URL,
    store
  });
  expect(fragment.managedPayload.chunkIndex).toEqual({
    url: `${RELEASE_BASE_URL}/${managedPayloadChunkIndexKey('6.5.0', 'darwin-aarch64')}`,
    size: writtenIndex.size,
    sha256: writtenIndex.sha256,
    signature: 'chunk-index-sig',
    chunksBaseUrl: managedChunksBaseUrl(RELEASE_BASE_URL)
  });
  expect(validatePlatformFragment(fragment, '6.5.0', 'darwin-aarch64')).toBe(
    fragment
  );
  expect(
    store.objects.has(
      `${managedPayloadChunkIndexKey('6.5.0', 'darwin-aarch64')}.sig`
    )
  ).toBe(true);

  // Verify verifyAndExtractChunksForUpload rejects tampered index
  expect(() =>
    verifyAndExtractChunksForUpload(
      zipBuf,
      Buffer.from(
        fs.readFileSync(chunkIndexPath, 'utf8').replace('6.5.0', '6.5.1')
      )
    )
  ).toThrow();

  // Promote succeeds when all chunks are stored
  await promotePlatform({
    version: '6.5.0',
    platform: 'macos',
    baseUrl: RELEASE_BASE_URL,
    store,
    withoutMainlandMirror: true
  });

  // Delete one chunk from R2 -> verifyManagedChunksStored / promote rejects
  const oneChunkKey = managedChunkKey(
    writtenIndex.index.chunks[0].compressedSha256
  );
  store.objects.delete(oneChunkKey);
  await expect(
    verifyManagedChunksStored(store, [writtenIndex.index])
  ).rejects.toThrow(/missing or differs/);

  // Re-upload restores only the single missing chunk
  const built = buildChunkIndexFromZip(zipBuf);
  const extracted = verifyAndExtractChunksForUpload(
    zipBuf,
    Buffer.from(built.text, 'utf8')
  );
  const res = await uploadManagedChunks(store, extracted);
  expect(res.uploaded).toBe(1);
  expect(res.reused).toBe(extracted.size - 1);
  await verifyManagedChunksStored(store, [writtenIndex.index]);

  // Verify fragment validation rejects cross-origin chunksBaseUrl
  expect(() =>
    buildPlatformFragment({
      manifest: {
        ...JSON.parse(
          fs.readFileSync(
            path.join(rootDir, 'src-tauri/target/release-artifacts/macos.json'),
            'utf8'
          )
        )
      },
      baseUrl: 'http://insecure.example/installer',
      selfUpdatable: false,
      game: { gameVersion: '1.0.0', managedSha256: 'a'.repeat(64) }
    })
  ).toThrow(/Unsafe release origin/);
});

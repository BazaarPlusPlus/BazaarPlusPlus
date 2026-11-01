import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { afterAll, afterEach, beforeAll, expect, test, vi } from 'vitest';
import { main } from '../release.mjs';
import {
  PRE_ADAPTATION_FILES,
  captureKind,
  cliMain,
  packPreAdaptation,
  publishPreAdaptation
} from './android-adaptation.mjs';
import { INTEROP_ASSET_ROOT, buildInteropPackage } from './android-app.mjs';
import {
  PRE_ADAPTATION_PURPOSE,
  adaptationRecordKey,
  certificateSha256,
  interopStatement,
  validateAdaptationRecord,
  verifyInteropStatement
} from './android-interop.mjs';
import {
  GAME_LIBS_LOCK_PATH,
  INTEROP_KEY,
  LOADER_MANAGED_DIRECTORY,
  LOADER_MARKER,
  interopDirectoryDigest,
  loaderDirectory,
  snapshotDirectory
} from './game-libs.mjs';
import { interopPackageKey } from './manifest.mjs';
import { buildZipBuffer } from './payload-zip.mjs';
import { RELEASE_BASE_URL, WORKSPACE_ROOT } from './product.mjs';
import { MemoryStore } from './test-support/memory-store.mjs';

// android-apk-watch.yml's decisions and its Pre-Adaptation, with the inputs
// simulated locally: a capture is a lock entry beside the previous one, the
// Interop Snapshot is a directory of stand-in assemblies, the release key is
// a throwaway RSA key with its own certificate (the committed
// test-certificate.pem has no private key), and R2 is a MemoryStore.

const sha256 = (bytes) =>
  crypto.createHash('sha256').update(bytes).digest('hex');
const LOCKED = Object.freeze(
  JSON.parse(
    fs.readFileSync(path.join(WORKSPACE_ROOT, GAME_LIBS_LOCK_PATH), 'utf8')
  ).entries[INTEROP_KEY]
);
const NOW = new Date('2026-11-02T03:04:05.000Z');
const SIGNING = Object.freeze({
  keystore: '/nonexistent/android-release.p12',
  passwordFile: '/nonexistent/android-keystore-password',
  alias: 'bpp'
});

const roots = [];
afterEach(() => {
  vi.unstubAllEnvs();
  for (const root of roots.splice(0))
    fs.rmSync(root, { recursive: true, force: true });
});
const tempDir = () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-adaptation-'));
  roots.push(root);
  return root;
};
const write = (file, bytes) => {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, bytes);
  return file;
};

let keyRoot;
let keys;
function throwawayKey(name) {
  const keyFile = path.join(keyRoot, `${name}.pem`);
  const certificateFile = path.join(keyRoot, `${name}.crt`);
  execFileSync(
    'openssl',
    [
      'req',
      '-x509',
      '-newkey',
      'rsa:2048',
      '-nodes',
      '-keyout',
      keyFile,
      '-out',
      certificateFile,
      '-subj',
      `/CN=BazaarPlusPlus ${name} test key`,
      '-days',
      '1'
    ],
    { stdio: 'ignore' }
  );
  return {
    keyFile,
    certificateFile,
    sha256: certificateSha256(fs.readFileSync(certificateFile))
  };
}
beforeAll(() => {
  keyRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-adaptation-keys-'));
  keys = { release: throwawayKey('release'), stranger: throwawayKey('other') };
});
afterAll(() => fs.rmSync(keyRoot, { recursive: true, force: true }));

// The stand-in for exportReleaseKey: copies the throwaway key into `into`
// and remembers the directory, which the signing must delete.
function releaseKeyFrom(key, exported) {
  return ({ into }) => {
    exported.push(into);
    fs.mkdirSync(into, { recursive: true });
    const keyFile = path.join(into, 'release-key.pem');
    const certificateFile = path.join(into, 'release-certificate.pem');
    fs.copyFileSync(key.keyFile, keyFile);
    fs.copyFileSync(key.certificateFile, certificateFile);
    return { keyFile, certificateFile };
  };
}

// A captured android-online entry: the locked one with the given changes.
const captured = (changes) => ({
  ...LOCKED,
  apkSha256: 'a'.repeat(64),
  capturedAt: NOW.toISOString(),
  ...changes
});

test('the same APK is unchanged', () => {
  expect(captureKind(LOCKED, { ...LOCKED })).toBe('unchanged');
});

test('a new APK with the locked game code is a content-only update', () => {
  expect(
    captureKind(
      LOCKED,
      captured({
        gameVersion: '1.0.12826-production-android-0badc0de',
        versionCode: 12826
      })
    )
  ).toBe('content');
});

test('a new libil2cpp.so or global-metadata.dat is a code change', () => {
  for (const changes of [
    { il2cppSha256: 'b'.repeat(64) },
    { metadataSha256: 'c'.repeat(64) },
    {
      il2cppSha256: 'b'.repeat(64),
      metadataSha256: 'c'.repeat(64),
      interopSha256: 'd'.repeat(64)
    }
  ])
    expect(captureKind(LOCKED, captured(changes))).toBe('code');
  expect(captureKind(null, captured({}))).toBe('code');
});

test('the locked game code with another Interop Snapshot is refused', () => {
  expect(() =>
    captureKind(LOCKED, captured({ interopSha256: 'd'.repeat(64) }))
  ).toThrow(
    `its Interop Snapshot hashes to ${'d'.repeat(64)}, not the locked ${LOCKED.interopSha256}`
  );
});

// A workspace whose lock holds `entry`, with its Interop Snapshot and Loader
// Release under game-libs/ as a capture leaves them.
function capturedWorkspace(entry = captured({})) {
  const workspaceRoot = tempDir();
  const modRoot = path.join(workspaceRoot, 'bazaarplusplus-mod');
  const interop = path.join(
    snapshotDirectory(modRoot, 'android', 'online', entry.gameVersion),
    'interop'
  );
  write(path.join(interop, 'Assembly-CSharp.dll'), 'interop assembly');
  write(path.join(interop, 'Il2Cppmscorlib.dll'), 'interop corlib');
  const loader = loaderDirectory(modRoot, entry.loader.tag);
  write(path.join(loader, LOADER_MANAGED_DIRECTORY, 'MelonLoader.dll'), 'ml');
  write(path.join(loader, LOADER_MARKER), `${entry.loader.sha256}\n`);
  const lock = JSON.parse(
    fs.readFileSync(path.join(WORKSPACE_ROOT, GAME_LIBS_LOCK_PATH), 'utf8')
  );
  lock.entries[INTEROP_KEY] = {
    ...entry,
    interopSha256: interopDirectoryDigest(interop)
  };
  write(
    path.join(workspaceRoot, GAME_LIBS_LOCK_PATH),
    `${JSON.stringify(lock, null, 2)}\n`
  );
  return { workspaceRoot, interop, lock, entry: lock.entries[INTEROP_KEY] };
}

test('kind compares the workspace lock with the previous lock file', async () => {
  const { workspaceRoot, lock } = capturedWorkspace();
  const previousFile = (entry) =>
    write(
      path.join(tempDir(), 'previous-lock.json'),
      JSON.stringify({
        ...lock,
        entries: { ...lock.entries, [INTEROP_KEY]: entry }
      })
    );
  const kind = (entry) =>
    cliMain(['kind', '--previous', previousFile(entry)], { workspaceRoot });
  const current = lock.entries[INTEROP_KEY];
  expect(await kind(current)).toBe('unchanged');
  expect(await kind({ ...current, apkSha256: 'e'.repeat(64) })).toBe('content');
  expect(await kind({ ...LOCKED, il2cppSha256: 'f'.repeat(64) })).toBe('code');
  await expect(cliMain(['kind'], { workspaceRoot })).rejects.toThrow('Usage');
});

test('pack writes the Interop Package of the locked snapshot and the lock entry', async () => {
  const { workspaceRoot, interop, entry } = capturedWorkspace();
  const out = path.join(tempDir(), 'pre-adaptation');
  const packed = await packPreAdaptation({
    workspaceRoot,
    out,
    storeFactory: () => {
      throw new Error('a capture leaves the snapshot under game-libs/');
    }
  });
  const zip = fs.readFileSync(path.join(out, PRE_ADAPTATION_FILES.zip));
  expect(zip.equals(buildInteropPackage(interop))).toBe(true);
  expect(packed).toEqual({
    size: zip.length,
    sha256: sha256(zip),
    gameVersion: entry.gameVersion
  });
  expect(
    JSON.parse(
      fs.readFileSync(path.join(out, PRE_ADAPTATION_FILES.entry), 'utf8')
    )
  ).toEqual(entry);
});

// What the capture job hands the signing job: the Interop Package of a
// stand-in snapshot (or `zip`) and the captured entry.
function handoff({
  zip,
  entry = captured({ il2cppSha256: 'b'.repeat(64) })
} = {}) {
  const from = tempDir();
  const interop = path.join(from, 'snapshot');
  write(path.join(interop, 'Assembly-CSharp.dll'), 'new interop assembly');
  write(path.join(interop, 'Il2Cppmscorlib.dll'), 'new interop corlib');
  const bytes = zip ?? buildInteropPackage(interop);
  write(path.join(from, PRE_ADAPTATION_FILES.zip), bytes);
  const recorded = { ...entry, interopSha256: interopDirectoryDigest(interop) };
  write(
    path.join(from, PRE_ADAPTATION_FILES.entry),
    `${JSON.stringify(recorded, null, 2)}\n`
  );
  return { from, zip: bytes, entry: recorded };
}

function publishOptions(from, store, exported, overrides = {}) {
  return {
    from,
    store,
    signing: SIGNING,
    baseUrl: RELEASE_BASE_URL,
    releaseKey: releaseKeyFrom(keys.release, exported),
    releaseSignerSha256: keys.release.sha256,
    now: NOW,
    ...overrides
  };
}

test('a Pre-Adaptation stores the zip, then a record whose signature verifies', async () => {
  const { from, zip, entry } = handoff();
  const store = new MemoryStore();
  const exported = [];
  const result = await publishPreAdaptation(
    publishOptions(from, store, exported)
  );
  const zipKey = interopPackageKey(sha256(zip));
  const recordKey = adaptationRecordKey({
    il2cppSha256: entry.il2cppSha256,
    metadataSha256: entry.metadataSha256,
    loaderTag: entry.loader.tag
  });
  expect(store.writes).toEqual([zipKey, recordKey]);
  expect(result).toMatchObject({ zipKey, recordKey, outcome: 'created' });
  expect(store.objects.get(zipKey).bytes.equals(zip)).toBe(true);

  const record = validateAdaptationRecord(
    JSON.parse(store.objects.get(recordKey).bytes.toString('utf8'))
  );
  expect(record).toEqual({
    schemaVersion: 1,
    kind: 'android-adaptation',
    game: {
      gameVersion: entry.gameVersion,
      versionCode: entry.versionCode,
      il2cppSha256: entry.il2cppSha256,
      metadataSha256: entry.metadataSha256
    },
    interop: {
      url: `${RELEASE_BASE_URL}/${zipKey}`,
      size: zip.length,
      sha256: sha256(zip),
      snapshotSha256: entry.interopSha256,
      signature: record.interop.signature
    },
    loader: { tag: entry.loader.tag, sha256: entry.loader.sha256 },
    il2cppInteropVersion: entry.il2cppInteropVersion,
    publishedAt: NOW.toISOString()
  });
  const statement = (purpose) =>
    interopStatement({
      zipSha256: record.interop.sha256,
      il2cppSha256: record.game.il2cppSha256,
      metadataSha256: record.game.metadataSha256,
      purpose
    });
  const certificate = fs.readFileSync(keys.release.certificateFile);
  const verifies = (purpose) =>
    verifyInteropStatement({
      statement: statement(purpose),
      signature: record.interop.signature,
      certificate
    });
  // The Android App's reader is tested against the shared golden; the
  // published record has its exact shape and field order.
  const shape = (value) =>
    value && typeof value === 'object'
      ? Object.entries(value).map(([key, field]) => [key, shape(field)])
      : typeof value;
  const golden = JSON.parse(
    fs.readFileSync(
      path.join(
        import.meta.dirname,
        'fixtures',
        'android-interop',
        'adaptation-record.json'
      ),
      'utf8'
    )
  );
  expect(shape(record)).toEqual(shape(golden));
  expect(verifies(PRE_ADAPTATION_PURPOSE)).toBe(true);
  // A Pre-Adaptation never passes for a release (normal mode).
  expect(verifies('release 6.0.0')).toBe(false);
  expect(exported.length).toBe(1);
  for (const directory of exported)
    expect(fs.existsSync(directory)).toBe(false);
});

test('a rerun writes nothing and keeps the first record', async () => {
  const { from } = handoff();
  const store = new MemoryStore();
  const first = await publishPreAdaptation(publishOptions(from, store, []));
  const writes = [...store.writes];
  const original = store.objects.get(first.recordKey).bytes;
  const again = await publishPreAdaptation(
    publishOptions(from, store, [], { now: new Date('2026-11-03T00:00:00Z') })
  );
  expect(again.outcome).toBe('present');
  expect(store.writes).toEqual(writes);
  expect(store.objects.get(first.recordKey).bytes.equals(original)).toBe(true);
});

test('an Interop Package a release already stored is not sent again', async () => {
  const { from, zip } = handoff();
  const store = new MemoryStore();
  const zipKey = interopPackageKey(sha256(zip));
  await store.put(zipKey, zip, { ifNoneMatch: true });
  const { recordKey } = await publishPreAdaptation(
    publishOptions(from, store, [])
  );
  expect(store.writes).toEqual([zipKey, recordKey]);
});

test('a record of another Pre-Adaptation stays unless force replaces it', async () => {
  const store = new MemoryStore();
  const first = handoff();
  const { recordKey } = await publishPreAdaptation(
    publishOptions(first.from, store, [])
  );
  // The same game code and Loader Release, another Interop Package.
  const second = handoff({
    zip: buildZipBuffer([
      {
        name: `${INTEROP_ASSET_ROOT}/Assembly-CSharp.dll`,
        data: Buffer.from('corrected interop'),
        mode: 0o644
      }
    ])
  });
  const before = store.objects.get(recordKey).bytes;
  await expect(
    publishPreAdaptation(publishOptions(second.from, store, []))
  ).rejects.toThrow(`${recordKey} already records another Pre-Adaptation`);
  expect(store.objects.get(recordKey).bytes.equals(before)).toBe(true);

  const replaced = await publishPreAdaptation(
    publishOptions(second.from, store, [], { force: true })
  );
  expect(replaced.outcome).toBe('replaced');
  const record = JSON.parse(
    store.objects.get(recordKey).bytes.toString('utf8')
  );
  expect(record.interop.sha256).toBe(sha256(second.zip));
});

test('force leaves a record of the same Pre-Adaptation as it is', async () => {
  const { from } = handoff();
  const store = new MemoryStore();
  await publishPreAdaptation(publishOptions(from, store, []));
  const writes = [...store.writes];
  const again = await publishPreAdaptation(
    publishOptions(from, store, [], { force: true, now: new Date() })
  );
  expect(again.outcome).toBe('present');
  expect(store.writes).toEqual(writes);
});

test('a keystore whose certificate is not the release one writes nothing', async () => {
  const { from } = handoff();
  const store = new MemoryStore();
  const exported = [];
  await expect(
    publishPreAdaptation(
      publishOptions(from, store, exported, {
        releaseKey: releaseKeyFrom(keys.stranger, exported)
      })
    )
  ).rejects.toThrow(
    `The keystore's certificate ${keys.stranger.sha256} is not the ${keys.release.sha256} that signed the Android App`
  );
  expect(store.writes).toEqual([]);
  expect(exported.length).toBe(1);
  expect(fs.existsSync(exported[0])).toBe(false);
});

test('a zip with entries outside the interop asset root is never signed', async () => {
  const exported = [];
  for (const name of [
    'assets/LemonLoader/runtime/loader/net6/MelonLoader.dll',
    `${INTEROP_ASSET_ROOT}/nested/Assembly-CSharp.dll`
  ]) {
    const { from } = handoff({
      zip: buildZipBuffer([
        {
          name: `${INTEROP_ASSET_ROOT}/Assembly-CSharp.dll`,
          data: Buffer.from('interop'),
          mode: 0o644
        },
        { name, data: Buffer.from('shadow'), mode: 0o644 }
      ])
    });
    const store = new MemoryStore();
    await expect(
      publishPreAdaptation(publishOptions(from, store, exported))
    ).rejects.toThrow(`must hold only files under ${INTEROP_ASSET_ROOT}/`);
    expect(store.writes).toEqual([]);
  }
  expect(exported).toEqual([]);
});

test('release.mjs pre-adapt reads the staged keystore and refuses one that is not the release key', async () => {
  const { from } = handoff();
  // The layout release-secrets.sh stage android writes, holding the
  // throwaway key: its certificate is not ANDROID_RELEASE_SIGNER_SHA256.
  const secrets = tempDir();
  const passwordFile = write(
    path.join(secrets, 'android-keystore-password'),
    'correct horse battery staple'
  );
  write(path.join(secrets, 'android-key-alias'), 'bpp-android-host');
  execFileSync(
    'openssl',
    [
      'pkcs12',
      '-export',
      '-inkey',
      keys.stranger.keyFile,
      '-in',
      keys.stranger.certificateFile,
      '-name',
      'bpp-android-host',
      '-passout',
      `file:${passwordFile}`,
      '-out',
      path.join(secrets, 'android-release.p12')
    ],
    { stdio: 'ignore' }
  );
  vi.stubEnv('BPP_SIGNING_SECRETS_DIR', secrets);
  const store = new MemoryStore();
  await expect(
    main(['pre-adapt', '--from', from], {
      createStore: () => store,
      log: () => {}
    })
  ).rejects.toThrow(
    `The keystore's certificate ${keys.stranger.sha256} is not the`
  );
  expect(store.writes).toEqual([]);
});

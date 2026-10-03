import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, expect, test } from 'vitest';
import {
  GAME_LIBS_LOCK_PATH,
  captureSnapshot,
  checkGameLibsLock,
  cliMain,
  fetchSnapshot,
  lockKeys,
  lockWarnings,
  lockedEntries,
  managedDirectoryDigest,
  managedDirectoryRecords,
  parseGameVersion,
  publishSnapshot,
  readGameLibsLock,
  readGameVersion,
  snapshotBlobKey,
  snapshotManifestKey,
  validateGameLibsLock
} from './game-libs.mjs';
import { computePayloadInputs } from './payload.mjs';
import {
  readInventory,
  synchronizePayloadProjection
} from './payload-inventory.mjs';
import { WORKSPACE_ROOT } from './product.mjs';

const roots = [];
afterEach(() => {
  for (const root of roots.splice(0))
    fs.rmSync(root, { recursive: true, force: true });
});

const STAGING_VERSION = '1.0.12575-staging-macos-arm64-fecb8f8e';
const ONLINE_VERSION = '1.0.12600-macos-arm64-0a1b2c3d';
const NOW = new Date('2026-10-03T12:00:00.000Z');

function write(file, bytes) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, bytes);
}

function emptyLock() {
  return {
    schemaVersion: 1,
    staleAfterDays: 30,
    entries: Object.fromEntries(lockKeys().map((key) => [key, null]))
  };
}

// A game install: Managed beside a globalgamemanagers that embeds the version
// in binary noise, as Unity writes it.
function gameInstall(root, gameVersion, { dlls = {} } = {}) {
  const managed = path.join(root, 'Data', 'Managed');
  write(
    path.join(root, 'Data', 'globalgamemanagers'),
    Buffer.concat([
      Buffer.from([0, 0, 0, 22, 0xff, 0xfe, 0x80]),
      Buffer.from(gameVersion, 'latin1'),
      Buffer.from([0, 0x80, 0xff, 7])
    ])
  );
  write(path.join(managed, 'Assembly-CSharp.dll'), 'game');
  write(path.join(managed, 'UnityEngine.dll'), 'unity');
  write(path.join(managed, 'Unity.Addressables.dll'), 'addressables');
  write(path.join(managed, 'notes.txt'), 'not a dll');
  for (const [name, bytes] of Object.entries(dlls))
    write(path.join(managed, name), bytes);
  return managed;
}

function workspace({ lock = emptyLock() } = {}) {
  const workspaceRoot = fs.mkdtempSync(
    path.join(os.tmpdir(), 'bpp-game-libs-')
  );
  roots.push(workspaceRoot);
  write(
    path.join(workspaceRoot, GAME_LIBS_LOCK_PATH),
    `${JSON.stringify(lock, null, 2)}\n`
  );
  return {
    workspaceRoot,
    modRoot: path.join(workspaceRoot, 'bazaarplusplus-mod'),
    lock: () => readGameLibsLock(workspaceRoot)
  };
}

function memoryStore() {
  const objects = new Map();
  return {
    objects,
    async head(key) {
      return objects.has(key) ? { size: objects.get(key).length } : null;
    },
    async get(key) {
      return objects.has(key)
        ? { bytes: objects.get(key), etag: `"${key}"` }
        : null;
    },
    async put(key, bytes, { ifNoneMatch, ifMatch } = {}) {
      if (ifNoneMatch && objects.has(key)) return false;
      if (ifMatch && !objects.has(key)) return false;
      objects.set(key, Buffer.from(bytes));
      return true;
    }
  };
}

test('the committed lock validates and names the six platform-channel keys', () => {
  const lock = readGameLibsLock(WORKSPACE_ROOT);
  expect(Object.keys(lock.entries)).toEqual([
    'macos-online',
    'macos-staging',
    'macos-ptr',
    'windows-online',
    'windows-staging',
    'windows-ptr'
  ]);
  expect(lock.staleAfterDays).toBe(30);
});

test('game versions carry their platform and channel', () => {
  expect(parseGameVersion(STAGING_VERSION)).toEqual({
    platform: 'macos',
    channel: 'staging'
  });
  expect(parseGameVersion('1.0.11358-ptr-windows-x64-947c079a')).toEqual({
    platform: 'windows',
    channel: 'ptr'
  });
  expect(parseGameVersion(ONLINE_VERSION).channel).toBe('online');
  expect(() => parseGameVersion('25661913')).toThrow(/Application.version/);
});

test('the game version is read from globalgamemanagers above Managed, exactly once', () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-game-'));
  roots.push(root);
  const managed = gameInstall(root, STAGING_VERSION);
  expect(readGameVersion(managed)).toBe(STAGING_VERSION);
  fs.rmSync(path.join(root, 'Data', 'globalgamemanagers'));
  expect(() => readGameVersion(managed)).toThrow(/globalgamemanagers/);
  write(
    path.join(root, 'Data', 'globalgamemanagers'),
    `${STAGING_VERSION} ${ONLINE_VERSION}`
  );
  expect(() => readGameVersion(managed)).toThrow(/found 2/);
});

test('the Managed digest hashes every DLL in ordinal order and feeds the payload inputs', () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-game-'));
  roots.push(root);
  const managed = gameInstall(root, STAGING_VERSION);
  const records = managedDirectoryRecords(managed);
  expect(records.map((record) => record.path)).toEqual([
    'managed/Assembly-CSharp.dll',
    'managed/Unity.Addressables.dll',
    'managed/UnityEngine.dll'
  ]);
  const digest = managedDirectoryDigest(managed);
  expect(digest).toMatch(/^[0-9a-f]{64}$/);
  write(path.join(managed, 'UnityEngine.dll'), 'patched');
  expect(managedDirectoryDigest(managed)).not.toBe(digest);
  fs.rmSync(path.join(managed, 'Assembly-CSharp.dll'));
  expect(() => managedDirectoryDigest(managed)).toThrow(/Assembly-CSharp/);

  const workspaceRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-inputs-'));
  roots.push(workspaceRoot);
  write(path.join(workspaceRoot, 'VERSION'), '5.5.0\n');
  write(
    path.join(workspaceRoot, 'release/payload.json'),
    JSON.stringify(readInventory())
  );
  synchronizePayloadProjection(workspaceRoot);
  write(
    path.join(workspaceRoot, 'bazaarplusplus-mod/src/Plugin.cs'),
    'class Plugin {}'
  );
  write(path.join(managed, 'Assembly-CSharp.dll'), 'game');
  const inputs = computePayloadInputs({ workspaceRoot, managedPath: managed });
  expect(
    inputs.files.filter((record) => record.path.startsWith('managed/'))
  ).toEqual(managedDirectoryRecords(managed));
});

test('lock validation rejects every malformed shape it will meet', () => {
  const entry = {
    gameVersion: STAGING_VERSION,
    sha256: 'a'.repeat(64),
    buildid: '25661913',
    steamBranch: 'staging',
    capturedAt: '2026-10-03T00:00:00.000Z'
  };
  const withEntry = (key, value) => {
    const lock = emptyLock();
    lock.entries[key] = value;
    return lock;
  };
  expect(validateGameLibsLock(withEntry('macos-staging', entry))).toBeTruthy();
  const cases = [
    [{ ...emptyLock(), schemaVersion: 2 }, /schemaVersion/],
    [{ ...emptyLock(), staleAfterDays: 0 }, /staleAfterDays/],
    [{ ...emptyLock(), extra: true }, /unknown top-level/],
    [
      { ...emptyLock(), entries: { 'macos-online': null } },
      /exactly the six keys/
    ],
    [withEntry('macos-staging', { ...entry, note: 'x' }), /exactly the fields/],
    [withEntry('macos-online', entry), /is a macos-staging build/],
    [withEntry('windows-staging', entry), /is a macos-staging build/],
    [withEntry('macos-staging', { ...entry, sha256: 'xyz' }), /sha256/],
    [withEntry('macos-staging', { ...entry, buildid: 25661913 }), /buildid/],
    [
      withEntry('macos-staging', { ...entry, steamBranch: 'public' }),
      /steamBranch must be staging/
    ],
    [
      withEntry('macos-staging', { ...entry, capturedAt: '2026-10-03' }),
      /capturedAt/
    ]
  ];
  for (const [lock, message] of cases)
    expect(() => validateGameLibsLock(lock), JSON.stringify(lock)).toThrow(
      message
    );
});

test('warnings name empty and stale entries without failing', () => {
  const lock = emptyLock();
  lock.entries['macos-staging'] = {
    gameVersion: STAGING_VERSION,
    sha256: 'a'.repeat(64),
    buildid: '25661913',
    steamBranch: 'staging',
    capturedAt: '2026-08-01T00:00:00.000Z'
  };
  lock.entries['windows-online'] = {
    gameVersion: '1.0.12600-windows-x64-0a1b2c3d',
    sha256: 'b'.repeat(64),
    buildid: '25661999',
    steamBranch: 'public',
    capturedAt: '2026-10-01T00:00:00.000Z'
  };
  const warnings = lockWarnings(lock, { now: NOW });
  expect(warnings).toHaveLength(5);
  expect(
    warnings.filter((w) => w.includes('no snapshot captured'))
  ).toHaveLength(4);
  expect(warnings.find((w) => w.startsWith('macos-staging'))).toMatch(
    /captured 63 days ago \(staleAfterDays 30\)/
  );
  expect(warnings.some((w) => w.startsWith('windows-online'))).toBe(false);
  expect(lockedEntries(lock).map(({ key }) => key)).toEqual([
    'macos-staging',
    'windows-online'
  ]);
});

test('snapshot captures the mounted game under its game version and records the lock entry', () => {
  const { workspaceRoot, modRoot, lock } = workspace();
  const managed = gameInstall(
    path.join(workspaceRoot, 'steam'),
    STAGING_VERSION
  );
  expect(() =>
    captureSnapshot({
      workspaceRoot,
      managedPath: managed,
      buildid: '25661913',
      steamBranch: 'public',
      now: NOW
    })
  ).toThrow(/Steam reports the public branch/);
  const result = captureSnapshot({
    workspaceRoot,
    managedPath: managed,
    buildid: 25661913,
    steamBranch: 'staging',
    now: NOW
  });
  expect(result.key).toBe('macos-staging');
  expect(result.directory).toBe(
    path.join(modRoot, 'game-libs', `macos-staging-${STAGING_VERSION}`)
  );
  expect(
    fs.existsSync(path.join(result.directory, 'Managed', 'UnityEngine.dll'))
  ).toBe(true);
  const entry = lock().entries['macos-staging'];
  expect(entry).toEqual({
    gameVersion: STAGING_VERSION,
    sha256: managedDirectoryDigest(managed),
    buildid: '25661913',
    steamBranch: 'staging',
    capturedAt: NOW.toISOString()
  });
  expect(
    JSON.parse(
      fs.readFileSync(path.join(result.directory, 'manifest.json'), 'utf8')
    )
  ).toEqual({
    schemaVersion: 1,
    platform: 'macos',
    channel: 'staging',
    ...entry
  });
  // Recapturing the same game is idempotent; a different game under the same
  // version is refused rather than silently replaced.
  captureSnapshot({
    workspaceRoot,
    managedPath: managed,
    buildid: '25661914',
    steamBranch: 'staging',
    now: NOW
  });
  expect(lock().entries['macos-staging'].buildid).toBe('25661914');
  write(path.join(managed, 'UnityEngine.dll'), 'patched');
  expect(() =>
    captureSnapshot({
      workspaceRoot,
      managedPath: managed,
      buildid: '25661915',
      steamBranch: 'staging',
      now: NOW
    })
  ).toThrow(/already exists with sha256/);
});

async function capturedWorkspace() {
  const ws = workspace();
  const managed = gameInstall(
    path.join(ws.workspaceRoot, 'steam'),
    STAGING_VERSION
  );
  const { directory, manifest } = captureSnapshot({
    workspaceRoot: ws.workspaceRoot,
    managedPath: managed,
    buildid: '25661913',
    steamBranch: 'staging',
    now: NOW
  });
  return { ...ws, managed, directory, manifest };
}

test('publish uploads the tarball by content and the manifest by identity, skipping stored blobs', async () => {
  const { workspaceRoot, manifest } = await capturedWorkspace();
  const store = memoryStore();
  const log = [];
  await expect(
    publishSnapshot({
      workspaceRoot,
      platform: 'macos',
      channel: 'online',
      store
    })
  ).rejects.toThrow(/macos-online is empty/);
  const keys = await publishSnapshot({
    workspaceRoot,
    platform: 'macos',
    channel: 'staging',
    store,
    log: (line) => log.push(line)
  });
  expect(keys).toEqual({
    blobKey: snapshotBlobKey(manifest.sha256),
    manifestKey: snapshotManifestKey('macos', 'staging', STAGING_VERSION)
  });
  expect(keys.blobKey).toBe(`game-libs/${manifest.sha256}.tar.gz`);
  expect(keys.manifestKey).toBe(
    `game-libs/macos-staging-${STAGING_VERSION}.json`
  );
  expect(JSON.parse(store.objects.get(keys.manifestKey).toString())).toEqual(
    manifest
  );
  expect(store.objects.get(keys.blobKey).length).toBeGreaterThan(0);
  const uploaded = store.objects.get(keys.blobKey);
  await publishSnapshot({
    workspaceRoot,
    platform: 'macos',
    channel: 'staging',
    store,
    tar: () => {
      throw new Error('must not re-tar a stored snapshot');
    },
    log: (line) => log.push(line)
  });
  expect(store.objects.get(keys.blobKey)).toBe(uploaded);
  expect(log.some((line) => /already stored/.test(line))).toBe(true);
});

test('fetch prefers a verified snapshot, then a matching Steam install, then the store', async () => {
  const { workspaceRoot, modRoot, managed, directory, manifest } =
    await capturedWorkspace();
  const store = memoryStore();
  await publishSnapshot({
    workspaceRoot,
    platform: 'macos',
    channel: 'staging',
    store
  });
  const fetchOptions = {
    workspaceRoot,
    platform: 'macos',
    channel: 'staging',
    storeFactory: () => {
      throw new Error('no credentials');
    }
  };

  const fromSnapshot = await fetchSnapshot(fetchOptions);
  expect(fromSnapshot).toEqual({
    managedPath: path.join(directory, 'Managed'),
    source: 'snapshot',
    gameVersion: STAGING_VERSION
  });

  fs.rmSync(directory, { recursive: true });
  const fromSteam = await fetchSnapshot({
    ...fetchOptions,
    steamManagedPath: managed
  });
  expect(fromSteam).toEqual({
    managedPath: managed,
    source: 'steam',
    gameVersion: STAGING_VERSION
  });

  // Same version string, different bytes: the sha256 half of the check.
  write(path.join(managed, 'UnityEngine.dll'), 'patched');
  await expect(
    fetchSnapshot({ ...fetchOptions, steamManagedPath: managed })
  ).rejects.toThrow(/hashes to [0-9a-f]{64} instead of/);

  // Another version mounted and no credentials: the two-version error.
  const other = gameInstall(path.join(workspaceRoot, 'other'), ONLINE_VERSION);
  await expect(
    fetchSnapshot({ ...fetchOptions, steamManagedPath: other })
  ).rejects.toThrow(
    new RegExp(
      `wants ${STAGING_VERSION}; the local Steam install is ${ONLINE_VERSION}.*no credentials`
    )
  );
  await expect(fetchSnapshot(fetchOptions)).rejects.toThrow(/is not found/);

  // With credentials the store serves it, and the download is verified.
  const log = [];
  const fromStore = await fetchSnapshot({
    ...fetchOptions,
    steamManagedPath: other,
    storeFactory: () => store,
    log: (line) => log.push(line)
  });
  expect(fromStore).toEqual({
    managedPath: path.join(directory, 'Managed'),
    source: 'store',
    gameVersion: STAGING_VERSION
  });
  expect(managedDirectoryDigest(fromStore.managedPath)).toBe(manifest.sha256);
  expect(
    JSON.parse(fs.readFileSync(path.join(directory, 'manifest.json'), 'utf8'))
  ).toEqual(manifest);
  expect(fs.readdirSync(path.join(modRoot, 'game-libs'))).toEqual([
    `macos-staging-${STAGING_VERSION}`
  ]);

  // A stored blob that does not hash to the lock entry never lands.
  fs.rmSync(directory, { recursive: true });
  const poisoned = memoryStore();
  poisoned.objects.set(
    snapshotBlobKey(manifest.sha256),
    store.objects.get(snapshotBlobKey('b'.repeat(64))) ?? Buffer.from('garbage')
  );
  await expect(
    fetchSnapshot({ ...fetchOptions, storeFactory: () => poisoned })
  ).rejects.toThrow();
  expect(fs.existsSync(directory)).toBe(false);
  expect(fs.readdirSync(path.join(modRoot, 'game-libs'))).toEqual([]);
});

test('fetch refuses an empty entry unless decompiling the mounted channel', async () => {
  const { workspaceRoot } = workspace();
  const managed = gameInstall(
    path.join(workspaceRoot, 'steam'),
    STAGING_VERSION
  );
  const base = { workspaceRoot, platform: 'macos', steamManagedPath: managed };
  await expect(fetchSnapshot({ ...base, channel: 'staging' })).rejects.toThrow(
    /macos-staging is empty and the mounted game is/
  );
  await expect(
    fetchSnapshot({ ...base, channel: 'online', allowUnlocked: true })
  ).rejects.toThrow(/macos-online is empty/);
  expect(
    await fetchSnapshot({ ...base, channel: 'staging', allowUnlocked: true })
  ).toEqual({
    managedPath: managed,
    source: 'steam-unlocked',
    gameVersion: STAGING_VERSION
  });
});

test('lock-check verifies the resolved Managed directory against the entry that names it', async () => {
  const { workspaceRoot, managed, directory, manifest } =
    await capturedWorkspace();
  const checked = checkGameLibsLock({
    workspaceRoot,
    managedPath: managed,
    now: NOW
  });
  expect(checked.verified).toEqual({
    gameVersion: STAGING_VERSION,
    keys: ['macos-staging']
  });
  expect(checked.warnings).toHaveLength(5);
  expect(
    checkGameLibsLock({
      workspaceRoot,
      managedPath: path.join(directory, 'Managed'),
      now: NOW
    }).verified.keys
  ).toEqual(['macos-staging']);
  const other = gameInstall(path.join(workspaceRoot, 'other'), ONLINE_VERSION);
  expect(
    checkGameLibsLock({ workspaceRoot, managedPath: other, now: NOW }).warnings
  ).toContainEqual(expect.stringMatching(/matches no lock entry/));
  write(path.join(managed, 'UnityEngine.dll'), 'patched');
  expect(() =>
    checkGameLibsLock({ workspaceRoot, managedPath: managed, now: NOW })
  ).toThrow(
    new RegExp(
      `not the ${manifest.sha256} that lock entry macos-staging records`
    )
  );
  write(
    path.join(workspaceRoot, GAME_LIBS_LOCK_PATH),
    JSON.stringify({ schemaVersion: 1 })
  );
  expect(() => checkGameLibsLock({ workspaceRoot, now: NOW })).toThrow(
    /staleAfterDays/
  );
});

test('the CLI prints only the result on stdout and reports failures on stderr', async () => {
  const { workspaceRoot } = workspace();
  const managed = gameInstall(
    path.join(workspaceRoot, 'steam'),
    STAGING_VERSION
  );
  const out = [];
  const err = [];
  const stdout = process.stdout.write.bind(process.stdout);
  const stderr = process.stderr.write.bind(process.stderr);
  process.stdout.write = (chunk) => (out.push(String(chunk)), true);
  process.stderr.write = (chunk) => (err.push(String(chunk)), true);
  try {
    expect(await cliMain(['game-version', managed], { workspaceRoot })).toBe(0);
    expect(await cliMain(['digest', managed], { workspaceRoot })).toBe(0);
    expect(await cliMain(['entries'], { workspaceRoot })).toBe(0);
    expect(
      await cliMain(
        [
          'snapshot',
          '--managed',
          managed,
          '--buildid',
          '25661913',
          '--steam-branch',
          'staging'
        ],
        { workspaceRoot }
      )
    ).toBe(0);
    expect(await cliMain(['entries'], { workspaceRoot })).toBe(0);
    expect(
      await cliMain(
        [
          'fetch',
          '--platform',
          'macos',
          '--channel',
          'staging',
          '--steam-managed',
          managed
        ],
        { workspaceRoot }
      )
    ).toBe(0);
    expect(
      await cliMain(['fetch', '--platform', 'macos', '--channel', 'ptr'], {
        workspaceRoot
      })
    ).toBe(1);
    expect(
      await cliMain(['lock-check', '--managed', managed], { workspaceRoot })
    ).toBe(0);
    expect(await cliMain(['snapshot'], { workspaceRoot })).toBe(1);
    expect(await cliMain(['bogus'], { workspaceRoot })).toBe(1);
  } finally {
    process.stdout.write = stdout;
    process.stderr.write = stderr;
  }
  expect(out).toEqual([
    `${STAGING_VERSION}\n`,
    `${managedDirectoryDigest(managed)}\n`,
    `${path.join(workspaceRoot, 'bazaarplusplus-mod', 'game-libs', `macos-staging-${STAGING_VERSION}`)}\n`,
    'macos staging\n',
    `${path.join(workspaceRoot, 'bazaarplusplus-mod', 'game-libs', `macos-staging-${STAGING_VERSION}`, 'Managed')}\n`
  ]);
  expect(err.join('')).toMatch(/macos-ptr is empty/);
  expect(err.join('')).toMatch(/snapshot requires --managed/);
  expect(err.join('')).toMatch(/Unknown game-libs verb/);
  expect(err.join('')).toMatch(/WARNING: windows-online/);
});

import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, expect, test, vi } from 'vite-plus/test';
import {
  GAME_LIBS_BUCKET,
  captureSnapshot,
  checkGameLibsLock,
  cliMain,
  fetchSnapshot,
  gameLibsStoreFromEnvironment,
  lockEntryForManaged,
  managedDirectoryDigest,
  managedDirectoryRecords,
  previewBehindOnline,
  publishSnapshot,
  readGameVersion,
  staleSteamPlatforms,
  steamCapture,
  steamChangeKind,
  steamStale
} from './game-libs.ts';
import type {
  CliOptions,
  FetchSnapshotOptions,
  SteamToolRunner
} from './game-libs.ts';
import {
  androidGameStatus,
  gameStatus,
  gameStatusStoreFromEnvironment,
  publishAndroidGameStatus,
  publishGameStatus
} from './game-status.ts';
import type {
  AndroidGameStatusDocument,
  GameStatusDocument
} from './game-status.ts';
import {
  GAME_LIBS_LOCK_PATH,
  lockKeys,
  readGameLibsLock,
  snapshotBlobKey,
  snapshotManifestKey,
  validateGameLibsLock
} from './snapshot-lock.ts';
import type {
  GameLibsLock,
  GameLibsLockEntries,
  InteropLockEntry,
  ManagedLockEntry,
  SnapshotChannel,
  SteamBranch
} from './snapshot-lock.ts';
import { computePayloadInputs } from './payload.ts';
import {
  readInventory,
  synchronizePayloadProjection
} from './payload-inventory.ts';
import { WORKSPACE_ROOT } from './product.ts';
import { CLOUDFLARE_API, createR2Store } from './r2-store.ts';
import type { FetchImpl, PutOptions } from './r2-store.ts';
import { MemoryStore } from './test-support/memory-store.ts';

const roots: string[] = [];
afterEach(() => {
  for (const root of roots.splice(0))
    fs.rmSync(root, { recursive: true, force: true });
});

const STAGING_VERSION = '1.0.12575-staging-macos-arm64-fecb8f8e';
const ONLINE_VERSION = '1.0.12600-macos-arm64-0a1b2c3d';
const NOW = new Date('2026-10-03T12:00:00.000Z');

function write(file: string, bytes: string | Buffer): void {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, bytes);
}

function emptyLock(): GameLibsLock {
  return {
    schemaVersion: 1,
    staleAfterDays: 30,
    entries: Object.fromEntries(
      lockKeys().map((key) => [key, null])
    ) as GameLibsLockEntries
  };
}

// A game install: Managed beside a globalgamemanagers that embeds the version
// in binary noise, as Unity writes it.
function gameInstall(
  root: string,
  gameVersion: string,
  { dlls = {} }: { dlls?: Record<string, string> } = {}
): string {
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

function workspace({ lock = emptyLock() }: { lock?: GameLibsLock } = {}) {
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
  const objects = new Map<string, Buffer>();
  return {
    objects,
    async head(key: string) {
      return objects.has(key)
        ? { size: objects.get(key)!.length, sha256: null, etag: null }
        : null;
    },
    async get(key: string) {
      return objects.has(key)
        ? { bytes: objects.get(key)!, etag: `"${key}"` }
        : null;
    },
    async put(
      key: string,
      bytes: Buffer,
      { ifNoneMatch, ifMatch }: PutOptions = {}
    ) {
      if (ifNoneMatch && objects.has(key)) return false;
      if (ifMatch && !objects.has(key)) return false;
      objects.set(key, Buffer.from(bytes));
      return true;
    }
  };
}

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
  expect(lock().entries['macos-staging']!.buildid).toBe('25661914');
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
  const log: string[] = [];
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
  expect(JSON.parse(store.objects.get(keys.manifestKey)!.toString())).toEqual(
    manifest
  );
  expect(store.objects.get(keys.blobKey)!.length).toBeGreaterThan(0);
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
  const fetchOptions: FetchSnapshotOptions = {
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
  const log: string[] = [];
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

test('a store fetch skips the AppleDouble members a macOS tar packed', async () => {
  const { workspaceRoot, directory, manifest } = await capturedWorkspace();
  // Pack the captured Managed with a `._<name>.dll` beside every DLL, as a
  // macOS tar without COPYFILE_DISABLE does.
  const packed = path.join(workspaceRoot, 'packed');
  fs.cpSync(path.join(directory, 'Managed'), path.join(packed, 'Managed'), {
    recursive: true
  });
  for (const name of fs.readdirSync(path.join(packed, 'Managed')))
    write(path.join(packed, 'Managed', `._${name}`), 'appledouble');
  execFileSync('tar', ['-czf', 'Managed.tar.gz', 'Managed'], { cwd: packed });
  const store = memoryStore();
  store.objects.set(
    snapshotBlobKey(manifest.sha256),
    fs.readFileSync(path.join(packed, 'Managed.tar.gz'))
  );
  fs.rmSync(directory, { recursive: true });

  const fetched = await fetchSnapshot({
    workspaceRoot,
    platform: 'macos',
    channel: 'staging',
    storeFactory: () => store
  });
  expect(fetched.source).toBe('store');
  expect(managedDirectoryDigest(fetched.managedPath)).toBe(manifest.sha256);
  expect(
    fs.readdirSync(fetched.managedPath).filter((name) => name.startsWith('._'))
  ).toEqual([]);
});

test('a store fetch that fails in transport reports the cause chain, not the signature', async () => {
  const { workspaceRoot } = await capturedWorkspace();
  fs.rmSync(path.join(workspaceRoot, 'bazaarplusplus-mod', 'game-libs'), {
    recursive: true
  });
  const accountId = 'c'.repeat(32);
  const hostname = `${accountId}.r2.cloudflarestorage.com`;
  const fetchImpl = async () => {
    throw new TypeError('fetch failed', {
      cause: Object.assign(new Error(`getaddrinfo ENOTFOUND ${hostname}`), {
        code: 'ENOTFOUND',
        errno: -3008,
        syscall: 'getaddrinfo',
        hostname
      })
    });
  };
  const failure = fetchSnapshot({
    workspaceRoot,
    platform: 'macos',
    channel: 'staging',
    storeFactory: () =>
      createR2Store({
        accountId,
        accessKeyId: 'test-access',
        secretAccessKey: 'test-secret',
        bucket: GAME_LIBS_BUCKET,
        fetchImpl
      })
  });
  await expect(failure).rejects.toThrow(
    /R2 GET game-libs\/[0-9a-f]{64}\.tar\.gz failed: TypeError: fetch failed <- Error: getaddrinfo ENOTFOUND <account>\.r2\.cloudflarestorage\.com \(code=ENOTFOUND errno=-3008 syscall=getaddrinfo hostname=<account>\.r2\.cloudflarestorage\.com\)/
  );
  const message = await failure.catch((error) => error.message);
  expect(message).not.toMatch(/Signature=|test-secret|test-access|c{32}/);
});

test('fetch refuses an empty entry unless decompiling the mounted channel', async () => {
  const { workspaceRoot } = workspace();
  const managed = gameInstall(
    path.join(workspaceRoot, 'steam'),
    STAGING_VERSION
  );
  const base = {
    workspaceRoot,
    platform: 'macos' as const,
    steamManagedPath: managed
  };
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
  expect(checked.warnings).toHaveLength(6);
  expect(
    checkGameLibsLock({
      workspaceRoot,
      managedPath: path.join(directory, 'Managed'),
      now: NOW
    }).verified!.keys
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
  const out: string[] = [];
  const err: string[] = [];
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

// lockEntryForManaged, the provenance the sealed build record carries. Failure
// modes: a Managed directory whose digest no entry records throws, naming its
// game version and the locked entries (an entry the build did not use is one
// such entry); an unknown build platform throws; identical bytes under several
// entries resolve to the build platform's entry, then online, staging, ptr.
test('the build record entry is the one the Managed digest matches, preferring the build platform', () => {
  const { workspaceRoot } = workspace();
  const managed = gameInstall(
    path.join(workspaceRoot, 'steam'),
    STAGING_VERSION
  );
  const digest = managedDirectoryDigest(managed);
  expect(() =>
    lockEntryForManaged({
      workspaceRoot,
      platform: 'macos',
      managedPath: managed
    })
  ).toThrow(
    new RegExp(
      `hashes to ${digest}, which no Snapshot Lock entry records \\(it is ${STAGING_VERSION}\\); locked entries: none`
    )
  );
  const entry = (
    gameVersion: string,
    steamBranch: SteamBranch,
    sha256 = digest
  ): ManagedLockEntry => ({
    gameVersion,
    sha256,
    buildid: '25661913',
    steamBranch,
    capturedAt: NOW.toISOString()
  });
  const lock = emptyLock();
  lock.entries['macos-online'] = entry(
    ONLINE_VERSION,
    'public',
    'c'.repeat(64)
  );
  lock.entries['macos-staging'] = entry(STAGING_VERSION, 'staging');
  lock.entries['windows-online'] = entry(
    '1.0.12600-windows-x64-0a1b2c3d',
    'public'
  );
  lock.entries['windows-ptr'] = entry(
    '1.0.12600-ptr-windows-x64-0a1b2c3d',
    'public_test_realm'
  );
  validateGameLibsLock(lock);
  expect(
    lockEntryForManaged({ lock, platform: 'macos', managedPath: managed })
  ).toEqual({
    key: 'macos-staging',
    platform: 'macos',
    channel: 'staging',
    gameVersion: STAGING_VERSION,
    sha256: digest,
    buildid: '25661913'
  });
  expect(
    lockEntryForManaged({ lock, platform: 'windows', managedPath: managed }).key
  ).toBe('windows-online');
  lock.entries['windows-online'] = null;
  expect(
    lockEntryForManaged({ lock, platform: 'windows', managedPath: managed }).key
  ).toBe('windows-ptr');
  expect(() =>
    lockEntryForManaged({ lock, platform: 'linux', managedPath: managed })
  ).toThrow(/Unknown snapshot platform/);
  // The game moved on after capture: the entries exist, but none was used.
  write(path.join(managed, 'UnityEngine.dll'), 'patched');
  expect(() =>
    lockEntryForManaged({ lock, platform: 'macos', managedPath: managed })
  ).toThrow(
    /locked entries: macos-online 1\.0\.12600-macos-arm64-0a1b2c3d, macos-staging 1\.0\.12575-staging-macos-arm64-fecb8f8e, windows-ptr/
  );
});

// Stands in for tools/SteamSnapshot: `download` lays out what the depot holds.
function steamTool({
  buildid = '26443971',
  gameVersion,
  calls = []
}: {
  buildid?: string;
  gameVersion?: string;
  calls?: string[][];
}): SteamToolRunner {
  return (args) => {
    calls.push(args);
    if (args[0] === 'state')
      return { buildid, updatedAt: 1793832079, manifests: {} };
    gameInstall(args[args.indexOf('--out') + 1], gameVersion!);
    return { buildid, manifest: '1' };
  };
}

const WINDOWS_ONLINE = '1.0.13056-prod-windows-x64-891f830a';
const onlineEntry = (
  gameVersion: string,
  buildid: string
): ManagedLockEntry => ({
  gameVersion,
  sha256: 'a'.repeat(64),
  buildid,
  steamBranch: 'public',
  capturedAt: NOW.toISOString()
});

test('a Steam capture records a lock entry from the depot on any host', () => {
  const { workspaceRoot, modRoot, lock } = workspace();
  const calls: string[][] = [];
  const result = steamCapture({
    workspaceRoot,
    modRoot,
    platform: 'windows',
    channel: 'online',
    runTool: steamTool({ gameVersion: WINDOWS_ONLINE, calls }),
    now: NOW
  });

  expect(result.key).toBe('windows-online');
  expect(lock().entries['windows-online']).toMatchObject({
    gameVersion: WINDOWS_ONLINE,
    buildid: '26443971',
    steamBranch: 'public'
  });
  expect(fs.existsSync(path.join(result.directory, 'Managed'))).toBe(true);
  expect(calls[0]).toEqual(
    expect.arrayContaining(['--branch', 'public', '--depot', '1617402'])
  );
  const download = calls[0][calls[0].indexOf('--out') + 1];
  expect(fs.existsSync(download)).toBe(false);
});

test('a Steam capture refuses a depot that holds another entry build', () => {
  const { workspaceRoot, modRoot, lock } = workspace();
  expect(() =>
    steamCapture({
      workspaceRoot,
      modRoot,
      platform: 'macos',
      channel: 'online',
      runTool: steamTool({ gameVersion: WINDOWS_ONLINE }),
      now: NOW
    })
  ).toThrow(/not a macos-online build/);
  expect(lock().entries['macos-online']).toBeNull();
});

test('a Steam capture reads a preview channel from its own branch', () => {
  const { workspaceRoot, modRoot, lock } = workspace();
  const calls: string[][] = [];
  steamCapture({
    workspaceRoot,
    modRoot,
    platform: 'macos',
    channel: 'staging',
    runTool: steamTool({ gameVersion: STAGING_VERSION, calls }),
    now: NOW
  });

  expect(calls[0]).toEqual(
    expect.arrayContaining(['--branch', 'staging', '--depot', '1617401'])
  );
  expect(lock().entries['macos-staging']!.steamBranch).toBe('staging');
});

test('a Steam capture needs a desktop entry', () => {
  const { workspaceRoot, modRoot } = workspace();
  expect(() =>
    steamCapture({
      workspaceRoot,
      modRoot,
      platform: 'android',
      channel: 'online',
      runTool: steamTool({ gameVersion: WINDOWS_ONLINE })
    })
  ).toThrow(/no Steam depot/);
  expect(() =>
    steamStale({
      workspaceRoot,
      modRoot,
      // Deliberately not a snapshot channel.
      channel: 'beta' as unknown as SnapshotChannel,
      runTool: steamTool({})
    })
  ).toThrow(/Unknown macos snapshot channel/);
});

test('a lock entry is stale when its buildid differs from Steam', () => {
  const lock = emptyLock();
  lock.entries['macos-online'] = onlineEntry(ONLINE_VERSION, '26443971');
  lock.entries['windows-online'] = onlineEntry(WINDOWS_ONLINE, '26428865');

  expect(staleSteamPlatforms(lock, 'online', '26443971')).toEqual(['windows']);
  expect(staleSteamPlatforms(lock, 'online', 26428865)).toEqual(['macos']);
  expect(staleSteamPlatforms(emptyLock(), 'online', '1')).toEqual([
    'macos',
    'windows'
  ]);
});

test('steam-stale reads both desktop depots of the branch', () => {
  const { workspaceRoot, modRoot } = workspace();
  const calls: string[][] = [];
  const state = steamStale({
    workspaceRoot,
    modRoot,
    channel: 'online',
    runTool: steamTool({ calls })
  });

  expect(state).toEqual({
    buildid: '26443971',
    updatedAt: 1793832079,
    platforms: ['macos', 'windows']
  });
  expect(calls[0]).toEqual(
    expect.arrayContaining(['--depots', '1617401,1617402'])
  );
});

test('a capture is code, content or none by what it changed', () => {
  const previous = emptyLock();
  previous.entries['macos-online'] = onlineEntry(ONLINE_VERSION, '1');
  previous.entries['windows-online'] = onlineEntry(WINDOWS_ONLINE, '1');
  const after = (changes: Partial<ManagedLockEntry>) => {
    const lock = structuredClone(previous);
    lock.entries['windows-online'] = {
      ...lock.entries['windows-online']!,
      buildid: '2',
      ...changes
    };
    return lock;
  };

  expect(steamChangeKind(previous, after({}), 'online')).toBe('none');
  expect(
    steamChangeKind(
      previous,
      after({ gameVersion: '1.0.13057-prod-windows-x64-891f830a' }),
      'online'
    )
  ).toBe('content');
  expect(
    steamChangeKind(previous, after({ sha256: 'b'.repeat(64) }), 'online')
  ).toBe('code');
  expect(steamChangeKind(emptyLock(), after({}), 'online')).toBe('code');
});

const STATUS_FIXTURE = JSON.parse(
  fs.readFileSync(
    path.join(WORKSPACE_ROOT, 'release/fixtures/game-status.json'),
    'utf8'
  )
) as GameStatusDocument;
const statusLock = () => {
  const lock = emptyLock();
  for (const platform of ['macos', 'windows'] as const) {
    const { gameVersion, managedSha256 } = STATUS_FIXTURE.platforms[platform]!;
    lock.entries[`${platform}-online`] = {
      ...onlineEntry(gameVersion, STATUS_FIXTURE.buildid),
      sha256: managedSha256
    };
  }
  return lock;
};
const STATUS_STATE = { buildid: '26443971', updatedAt: 1793832079 };

test('the Game Status of a recorded online build matches the shared fixture', () => {
  expect(gameStatus(statusLock(), STATUS_STATE)).toEqual(STATUS_FIXTURE);
});

test('the Game Status waits for the lock and for a real time', () => {
  const behind = statusLock();
  behind.entries['windows-online']!.buildid = '1';
  expect(gameStatus(behind, STATUS_STATE)).toBeNull();
  expect(
    gameStatus(statusLock(), { ...STATUS_STATE, updatedAt: 0 })
  ).toBeNull();
});

test('publishing the Game Status writes it once and leaves it alone when behind', async () => {
  const { workspaceRoot } = workspace({ lock: statusLock() });
  const store = memoryStore();
  expect(
    await publishGameStatus({ workspaceRoot, state: STATUS_STATE, store })
  ).toEqual(STATUS_FIXTURE);
  expect(JSON.parse(store.objects.get('latest.json')!.toString())).toEqual(
    STATUS_FIXTURE
  );
  const { workspaceRoot: behind } = workspace({ lock: emptyLock() });
  const untouched = memoryStore();
  expect(
    await publishGameStatus({
      workspaceRoot: behind,
      state: STATUS_STATE,
      store: untouched
    })
  ).toBeNull();
  expect(untouched.objects.size).toBe(0);
});

const ANDROID_STATUS_FIXTURE = JSON.parse(
  fs.readFileSync(
    path.join(WORKSPACE_ROOT, 'release/fixtures/android-game-status.json'),
    'utf8'
  )
) as AndroidGameStatusDocument;
const androidStatusLock = () => {
  const lock = emptyLock();
  const { schemaVersion: _, ...game } = ANDROID_STATUS_FIXTURE;
  lock.entries['android-online'] = {
    ...(readGameLibsLock(WORKSPACE_ROOT).entries[
      'android-online'
    ] as InteropLockEntry),
    ...game
  };
  return lock;
};

test('the Android Game Status of the android-online entry matches the shared fixture', async () => {
  expect(androidGameStatus(androidStatusLock())).toEqual(
    ANDROID_STATUS_FIXTURE
  );
  const { workspaceRoot } = workspace({ lock: androidStatusLock() });
  const store = memoryStore();
  expect(await publishAndroidGameStatus({ workspaceRoot, store })).toEqual(
    ANDROID_STATUS_FIXTURE
  );
  expect(JSON.parse(store.objects.get('android.json')!.toString())).toEqual(
    ANDROID_STATUS_FIXTURE
  );
  const { workspaceRoot: empty } = workspace({ lock: emptyLock() });
  const untouched = memoryStore();
  expect(
    await publishAndroidGameStatus({ workspaceRoot: empty, store: untouched })
  ).toBeNull();
  expect(untouched.objects.size).toBe(0);
});

// previewBehindOnline decides which preview channels the CompatCheck matrix
// skips. Failure modes: a string comparison of build ids ('9' > '10'), a
// preview at or above online read as behind, an empty entry read as behind.
test('a preview channel is behind online only when its Steam build id is numerically lower', () => {
  const lock = emptyLock();
  lock.entries['macos-online'] = onlineEntry(ONLINE_VERSION, '10');
  lock.entries['macos-staging'] = {
    gameVersion: STAGING_VERSION,
    sha256: 'b'.repeat(64),
    buildid: '9',
    steamBranch: 'staging',
    capturedAt: NOW.toISOString()
  };
  lock.entries['macos-ptr'] = {
    gameVersion: '1.0.12600-ptr-macos-arm64-0a1b2c3d',
    sha256: 'c'.repeat(64),
    buildid: '10',
    steamBranch: 'public_test_realm',
    capturedAt: NOW.toISOString()
  };
  expect(previewBehindOnline(lock, 'macos', 'staging')).toBe(
    lock.entries['macos-online']
  );
  expect(previewBehindOnline(lock, 'macos', 'ptr')).toBeNull();
  lock.entries['macos-ptr']!.buildid = '11';
  expect(previewBehindOnline(lock, 'macos', 'ptr')).toBeNull();
  expect(previewBehindOnline(lock, 'macos', 'online')).toBeNull();
  expect(previewBehindOnline(lock, 'windows', 'staging')).toBeNull();
  lock.entries['macos-online'] = null;
  expect(previewBehindOnline(lock, 'macos', 'staging')).toBeNull();
});

// ---------------------------------------------------------------------------
// The CLI as the workflows and bazaarplusplus-mod/scripts call it. Each test
// pins what the caller reads: the exact stdout (results only), the exit code
// (0 or 1), that logs and errors go to stderr, and what the verb wrote.

interface CliRun {
  code: number;
  out: string[];
  err: string;
}

async function runCli(argv: string[], options: CliOptions): Promise<CliRun> {
  const out: string[] = [];
  const err: string[] = [];
  const stdout = process.stdout.write.bind(process.stdout);
  const stderr = process.stderr.write.bind(process.stderr);
  process.stdout.write = (chunk) => (out.push(String(chunk)), true);
  process.stderr.write = (chunk) => (err.push(String(chunk)), true);
  try {
    const code = await cliMain(argv, options);
    return { code, out, err: err.join('') };
  } finally {
    process.stdout.write = stdout;
    process.stderr.write = stderr;
  }
}

test('--help and no verb print the usage on stdout, naming every verb', async () => {
  const verbs = [
    'game-version',
    'digest',
    'snapshot',
    'steam-stale',
    'steam-capture',
    'steam-change',
    'game-status',
    'android-game-status',
    'snapshot-android',
    'publish',
    'fetch',
    'lock-check',
    'entries',
    'behind-online',
    'official-apk',
    'record-apk-etag'
  ];
  for (const argv of [['--help'], []]) {
    const { code, out, err } = await runCli(argv, {
      workspaceRoot: workspace().workspaceRoot
    });
    expect(code).toBe(0);
    expect(err).toBe('');
    expect(out).toHaveLength(1);
    expect(out[0]).toMatch(
      /^Game Assembly and Interop Snapshot commands[^]*\n$/
    );
    for (const verb of verbs)
      expect(out[0]).toContain(`bun release/game-libs.ts ${verb}`);
  }
});

// The Game Status store is the one place the release bucket's default
// installer/ prefix must not apply: GAME_STATUS_KEY and the Release Manifest
// are both latest.json. The fake answers the Cloudflare token check and the S3
// requests, so a test sees the exact object URL the store writes. The
// expectations are literal strings on purpose; a change to a shared constant
// must fail here.
const R2_ACCOUNT = 'f'.repeat(32);
const R2_ENV = {
  CLOUDFLARE_API_TOKEN: 'game-status-test-token',
  CLOUDFLARE_ACCOUNT_ID: R2_ACCOUNT
};
const R2_OBJECTS = `https://${R2_ACCOUNT}.r2.cloudflarestorage.com`;

interface R2Request {
  method: string;
  url: string;
  body: Buffer | null;
}

function fakeR2() {
  const requests: R2Request[] = [];
  const objects = new Map<string, Buffer>();
  const fetchImpl: FetchImpl = async (url, init) => {
    if (url.startsWith(CLOUDFLARE_API))
      return new Response(
        JSON.stringify({
          success: true,
          errors: [],
          result: { id: 'e'.repeat(32), status: 'active' }
        })
      );
    const method = init.method ?? 'GET';
    const body = init.body ? Buffer.from(init.body as Uint8Array) : null;
    requests.push({ method, url, body });
    if (method === 'PUT') {
      objects.set(url, body!);
      return new Response(null, { status: 200 });
    }
    const stored = objects.get(url);
    return stored
      ? new Response(stored, { status: 200, headers: { etag: '"1"' } })
      : new Response(null, { status: 404 });
  };
  return { fetchImpl, requests, objects };
}

test('the Game Status store writes under game-status/ in the public bucket, never installer/', async () => {
  const r2 = fakeR2();
  const store = gameStatusStoreFromEnvironment(R2_ENV, {
    fetchImpl: r2.fetchImpl
  });
  expect(
    await store.put('latest.json', Buffer.from('{}'), { ifNoneMatch: true })
  ).toBe(true);
  expect(
    await store.put('android.json', Buffer.from('{}'), { ifNoneMatch: true })
  ).toBe(true);
  expect(r2.requests.map(({ method, url }) => `${method} ${url}`)).toEqual([
    `PUT ${R2_OBJECTS}/bazaarplusplus-public/game-status/latest.json`,
    `PUT ${R2_OBJECTS}/bazaarplusplus-public/game-status/android.json`
  ]);
  expect(r2.requests.some(({ url }) => url.includes('/installer/'))).toBe(
    false
  );
  expect(() => gameStatusStoreFromEnvironment({})).toThrow(
    /CLOUDFLARE_API_TOKEN and CLOUDFLARE_ACCOUNT_ID/
  );
});

// The private store holds the game's own files (ADR 0007), so it must never
// fall back to the public release bucket and its installer/ prefix.
test('the game-libs store reads and writes the private game-libs bucket, never the public one', async () => {
  const r2 = fakeR2();
  vi.stubGlobal('fetch', r2.fetchImpl);
  try {
    const store = gameLibsStoreFromEnvironment(R2_ENV);
    expect(
      await store.put('game-libs/manifest.json', Buffer.from('{}'), {
        ifNoneMatch: true
      })
    ).toBe(true);
    expect(await store.get('game-libs/manifest.json')).not.toBeNull();
  } finally {
    vi.unstubAllGlobals();
  }
  expect(r2.requests.map(({ method, url }) => `${method} ${url}`)).toEqual([
    `PUT ${R2_OBJECTS}/bazaarplusplus-game-libs/game-libs/manifest.json`,
    `GET ${R2_OBJECTS}/bazaarplusplus-game-libs/game-libs/manifest.json`
  ]);
  expect(() => gameLibsStoreFromEnvironment({})).toThrow(
    /CLOUDFLARE_API_TOKEN and CLOUDFLARE_ACCOUNT_ID/
  );
});

const STATUS_FIXTURE_BYTES = fs.readFileSync(
  path.join(WORKSPACE_ROOT, 'release/fixtures/game-status.json')
);

test('steam-stale prints one JSON line that game-status reads back as --state', async () => {
  const { workspaceRoot } = workspace({ lock: statusLock() });
  const calls: string[][] = [];
  const stale = await runCli(['steam-stale', '--channel', 'online'], {
    workspaceRoot,
    runTool: steamTool({ calls })
  });
  expect(stale).toEqual({
    code: 0,
    out: ['{"buildid":"26443971","updatedAt":1793832079,"platforms":[]}\n'],
    err: ''
  });
  expect(calls).toEqual([
    [
      'state',
      '--app',
      '1617400',
      '--branch',
      'public',
      '--depots',
      '1617401,1617402'
    ]
  ]);

  // The workflow keeps the last line (tail -n 1) as steam-state.json.
  const stateFile = path.join(workspaceRoot, 'steam-state.json');
  write(stateFile, stale.out.join('').trim().split('\n').at(-1)!);
  const r2 = fakeR2();
  const options = { workspaceRoot, env: R2_ENV, r2Fetch: r2.fetchImpl };
  expect(await runCli(['game-status', '--state', stateFile], options)).toEqual({
    code: 0,
    out: [],
    err: 'Game Status: online build 26443971\n'
  });
  const url = `${R2_OBJECTS}/bazaarplusplus-public/game-status/latest.json`;
  expect(r2.requests.map(({ method, url }) => `${method} ${url}`)).toEqual([
    `GET ${url}`,
    `PUT ${url}`
  ]);
  // The bytes are the shared fixture's, key order and trailing newline
  // included.
  expect(r2.requests[1].body).toEqual(STATUS_FIXTURE_BYTES);
  // The same status again reads and writes nothing more.
  expect(
    (await runCli(['game-status', '--state', stateFile], options)).code
  ).toBe(0);
  expect(r2.requests.map(({ method }) => method)).toEqual([
    'GET',
    'PUT',
    'GET'
  ]);
});

test('steam-stale names the platforms behind Steam and omits an updatedAt Steam did not give', async () => {
  const { workspaceRoot } = workspace();
  expect(
    await runCli(['steam-stale', '--channel', 'staging'], {
      workspaceRoot,
      runTool: steamTool({})
    })
  ).toEqual({
    code: 0,
    out: [
      '{"buildid":"26443971","updatedAt":1793832079,"platforms":["macos","windows"]}\n'
    ],
    err: ''
  });
  expect(
    await runCli(['steam-stale', '--channel', 'online'], {
      workspaceRoot,
      runTool: () => ({ buildid: '1' })
    })
  ).toEqual({
    code: 0,
    out: ['{"buildid":"1","platforms":["macos","windows"]}\n'],
    err: ''
  });
  for (const argv of [['steam-stale'], ['steam-stale', '--channel', 'beta']]) {
    const failed = await runCli(argv, {
      workspaceRoot,
      runTool: steamTool({})
    });
    expect(failed.code).toBe(1);
    expect(failed.out).toEqual([]);
  }
});

test('game-status leaves the store alone while the lock is behind, and fails without credentials or --state', async () => {
  const { workspaceRoot } = workspace();
  const stateFile = path.join(workspaceRoot, 'steam-state.json');
  write(stateFile, JSON.stringify(STATUS_STATE));
  const r2 = fakeR2();
  expect(
    await runCli(['game-status', '--state', stateFile], {
      workspaceRoot,
      env: R2_ENV,
      r2Fetch: r2.fetchImpl
    })
  ).toEqual({
    code: 0,
    out: [],
    err: 'The lock does not record this online build yet; the Game Status stays as it is\n'
  });
  expect(r2.requests).toEqual([]);
  const noState = await runCli(['game-status'], {
    workspaceRoot,
    env: R2_ENV,
    r2Fetch: r2.fetchImpl
  });
  expect(noState.code).toBe(1);
  expect(noState.out).toEqual([]);
  expect(noState.err).toMatch(/^game-status requires --state\n/);
  // The store is built before the lock is read, so even a lock that is behind
  // needs the credentials.
  const noCredentials = await runCli(['game-status', '--state', stateFile], {
    workspaceRoot,
    env: {},
    r2Fetch: r2.fetchImpl
  });
  expect(noCredentials).toEqual({
    code: 1,
    out: [],
    err: 'R2 access requires CLOUDFLARE_API_TOKEN and CLOUDFLARE_ACCOUNT_ID\n'
  });
  expect(r2.requests).toEqual([]);
});

test('android-game-status writes the android-online entry as game-status/android.json', async () => {
  const { workspaceRoot } = workspace({ lock: androidStatusLock() });
  const r2 = fakeR2();
  const options = { workspaceRoot, env: R2_ENV, r2Fetch: r2.fetchImpl };
  expect(await runCli(['android-game-status'], options)).toEqual({
    code: 0,
    out: [],
    err: 'Android Game Status: 1.0.12830-production-android-4b1e07c2\n'
  });
  const url = `${R2_OBJECTS}/bazaarplusplus-public/game-status/android.json`;
  expect(r2.requests.map(({ method, url }) => `${method} ${url}`)).toEqual([
    `GET ${url}`,
    `PUT ${url}`
  ]);
  expect(r2.requests[1].body).toEqual(
    fs.readFileSync(
      path.join(WORKSPACE_ROOT, 'release/fixtures/android-game-status.json')
    )
  );

  const { workspaceRoot: empty } = workspace();
  const untouched = fakeR2();
  expect(
    await runCli(['android-game-status'], {
      workspaceRoot: empty,
      env: R2_ENV,
      r2Fetch: untouched.fetchImpl
    })
  ).toEqual({
    code: 0,
    out: [],
    err: 'The lock has no android-online entry; the Android Game Status stays as it is\n'
  });
  expect(untouched.requests).toEqual([]);
  expect(
    await runCli(['android-game-status'], { workspaceRoot: empty, env: {} })
  ).toEqual({
    code: 1,
    out: [],
    err: 'R2 access requires CLOUDFLARE_API_TOKEN and CLOUDFLARE_ACCOUNT_ID\n'
  });
});

test('steam-capture prints the snapshot directory and writes the lock in its two-space format', async () => {
  const { workspaceRoot, modRoot, lock } = workspace();
  // A compact lock with its entries out of order: the writer, not this
  // test, gives the file its format.
  const seeded = emptyLock();
  write(
    path.join(workspaceRoot, GAME_LIBS_LOCK_PATH),
    JSON.stringify({
      ...seeded,
      entries: Object.fromEntries(Object.entries(seeded.entries).reverse())
    })
  );
  const calls: string[][] = [];
  const captured = await runCli(
    ['steam-capture', '--platform', 'windows', '--channel', 'online'],
    {
      workspaceRoot,
      runTool: steamTool({ gameVersion: WINDOWS_ONLINE, calls })
    }
  );
  const directory = path.join(
    modRoot,
    'game-libs',
    `windows-online-${WINDOWS_ONLINE}`
  );
  expect(captured.code).toBe(0);
  expect(captured.out).toEqual([`${directory}\n`]);
  expect(captured.err).toBe(
    `Captured windows-online ${WINDOWS_ONLINE} (buildid 26443971) into ${directory} and recorded it in bazaarplusplus-mod/build/game-libs.lock.json\n`
  );
  expect(calls[0].slice(0, 7)).toEqual([
    'download',
    '--app',
    '1617400',
    '--branch',
    'public',
    '--depot',
    '1617402'
  ]);
  expect(lock().entries['windows-online']).toMatchObject({
    gameVersion: WINDOWS_ONLINE,
    buildid: '26443971',
    steamBranch: 'public'
  });

  // build/ManagedPath.props reads an entry as the flat object after its key.
  const text = fs.readFileSync(
    path.join(workspaceRoot, GAME_LIBS_LOCK_PATH),
    'utf8'
  );
  const parsed = JSON.parse(text);
  expect(text).toBe(`${JSON.stringify(parsed, null, 2)}\n`);
  expect(Object.keys(parsed)).toEqual([
    'schemaVersion',
    'staleAfterDays',
    'entries'
  ]);
  expect(Object.keys(parsed.entries)).toEqual([
    'macos-online',
    'macos-staging',
    'macos-ptr',
    'windows-online',
    'windows-staging',
    'windows-ptr',
    'android-online'
  ]);
  expect(Object.keys(parsed.entries['windows-online'])).toEqual([
    'gameVersion',
    'sha256',
    'buildid',
    'steamBranch',
    'capturedAt'
  ]);

  const refused = await runCli(
    ['steam-capture', '--platform', 'android', '--channel', 'online'],
    { workspaceRoot, runTool: steamTool({ gameVersion: WINDOWS_ONLINE }) }
  );
  expect(refused).toEqual({
    code: 1,
    out: [],
    err: 'Lock entry android-online has no Steam depot\n'
  });
}, 30000);

test('steam-change prints code, content or none against the --previous lock', async () => {
  const previous = emptyLock();
  previous.entries['macos-online'] = onlineEntry(ONLINE_VERSION, '1');
  previous.entries['windows-online'] = onlineEntry(WINDOWS_ONLINE, '1');
  const { workspaceRoot } = workspace();
  const previousFile = path.join(workspaceRoot, 'previous-lock.json');
  write(previousFile, `${JSON.stringify(previous, null, 2)}\n`);
  const current = (changes: Partial<ManagedLockEntry>) => {
    const lock = structuredClone(previous);
    lock.entries['windows-online'] = {
      ...lock.entries['windows-online']!,
      buildid: '2',
      ...changes
    };
    write(
      path.join(workspaceRoot, GAME_LIBS_LOCK_PATH),
      `${JSON.stringify(lock, null, 2)}\n`
    );
  };
  const change = () =>
    runCli(
      ['steam-change', '--previous', previousFile, '--channel', 'online'],
      { workspaceRoot }
    );
  current({});
  expect(await change()).toEqual({ code: 0, out: ['none\n'], err: '' });
  current({ gameVersion: '1.0.13057-prod-windows-x64-891f830a' });
  expect(await change()).toEqual({ code: 0, out: ['content\n'], err: '' });
  current({ sha256: 'b'.repeat(64) });
  expect(await change()).toEqual({ code: 0, out: ['code\n'], err: '' });

  write(previousFile, '{}');
  const invalid = await change();
  expect(invalid.code).toBe(1);
  expect(invalid.out).toEqual([]);
  expect(invalid.err).toMatch(/schemaVersion must be 1/);
});

test('behind-online prints the online game version for a preview entry behind it, else nothing', async () => {
  const lock = emptyLock();
  lock.entries['macos-online'] = onlineEntry(ONLINE_VERSION, '10');
  lock.entries['macos-staging'] = {
    gameVersion: STAGING_VERSION,
    sha256: 'b'.repeat(64),
    buildid: '9',
    steamBranch: 'staging',
    capturedAt: NOW.toISOString()
  };
  const { workspaceRoot } = workspace({ lock });
  const behind = (platform: string, channel: string) =>
    runCli(['behind-online', '--platform', platform, '--channel', channel], {
      workspaceRoot
    });
  expect(await behind('macos', 'staging')).toEqual({
    code: 0,
    out: [`${ONLINE_VERSION}\n`],
    err: ''
  });
  for (const [platform, channel] of [
    ['macos', 'ptr'],
    ['macos', 'online'],
    ['windows', 'staging']
  ])
    expect(await behind(platform, channel)).toEqual({
      code: 0,
      out: [],
      err: ''
    });
  const failed = await runCli(['behind-online', '--platform', 'macos'], {
    workspaceRoot
  });
  expect(failed.code).toBe(1);
  expect(failed.out).toEqual([]);
  expect(failed.err).toMatch(/^behind-online requires --channel\n/);
});

test('publish of a desktop entry stores the tarball and the manifest, printing nothing', async () => {
  const { workspaceRoot, directory, manifest } = await capturedWorkspace();
  const store = new MemoryStore();
  const blobKey = `game-libs/${manifest.sha256}.tar.gz`;
  const manifestKey = `game-libs/macos-staging-${STAGING_VERSION}.json`;
  const published = await runCli(
    ['publish', '--platform', 'macos', '--channel', 'staging'],
    { workspaceRoot, gameLibsStore: () => store }
  );
  expect(published.code).toBe(0);
  expect(published.out).toEqual([]);
  expect(published.err).toMatch(
    new RegExp(
      `^Uploaded ${blobKey} \\(\\d+ bytes\\)\\nRecorded ${manifestKey}\\n$`
    )
  );
  expect(store.writes).toEqual([blobKey, manifestKey]);
  expect(store.objects.get(manifestKey)!.bytes).toEqual(
    fs.readFileSync(path.join(directory, 'manifest.json'))
  );

  const empty = await runCli(
    ['publish', '--platform', 'macos', '--channel', 'online'],
    { workspaceRoot, gameLibsStore: () => store }
  );
  expect(empty.code).toBe(1);
  expect(empty.out).toEqual([]);
  expect(empty.err).toMatch(/Lock entry macos-online is empty/);
  expect(store.writes).toHaveLength(2);
}, 30000);

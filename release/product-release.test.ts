import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, expect, test, vi } from 'vite-plus/test';
import {
  ANDROID_GAME_SIGNERS,
  ANDROID_RELEASE_SIGNER_SHA256,
  androidAppVersionCode,
  androidManagedPayloadKey,
  androidNonManagedPayloadDigest,
  buildAndroidFragment,
  buildPlatformFragment,
  buildLatestManifest,
  buildPlatformManifest,
  fragmentKey,
  interopPackageKey,
  mirrorFileName,
  mirrorRecordKey,
  modManifestPath,
  validatePlatformFragment,
  validatePlatformManifest
} from './manifest.ts';
import {
  uploadPlatform,
  recordMainlandMirror,
  promotePlatform,
  promoteRelease
} from './publish.ts';
import { MAINLAND_MIRROR, mainlandMirrorUrl } from './mirror.ts';
import { putImmutable } from './r2-store.ts';
import { ANDROID_SOURCES } from './snapshot-lock.ts';
import { createArtifactManifest } from './artifact-manifest.ts';
import { MemoryStore } from './test-support/memory-store.ts';
import { uploadAndroid } from './test-support/android-fixture.ts';
import { mirrorNetwork } from './test-support/mirror-network.ts';
import type { MirrorNetwork } from './test-support/mirror-network.ts';
import {
  artifactPaths,
  writeArtifacts
} from './test-support/artifact-fixture.ts';
import { runFixtureGit } from '../scripts/test-support/git-fixture.ts';
import type {
  AndroidPlatformFragment,
  BuildAndroidFragmentOptions,
  DesktopDownload,
  DesktopPlatformFragment,
  MirrorRecord,
  ReleaseManifest
} from './manifest.ts';
import type {
  BuildPlatform,
  ReleasePlatformKey,
  TauriBuildPlatform
} from './release-platforms.ts';
import type {
  PromotePlatformOptions,
  PromoteReleaseOptions,
  RecordMainlandMirrorOptions
} from './publish.ts';

interface ReleaseFixture {
  workspaceRoot: string;
  rootDir: string;
  version: string;
  commits: Record<string, string>;
}

const baseUrl = 'https://cdn.bazaarplusplus.com/installer';
const DESKTOP_GAME = {
  gameVersion: '1.0.13081-prod-macos-arm64-dae65d53',
  managedSha256: 'd'.repeat(64)
};
const roots: string[] = [];

afterEach(() => {
  for (const root of roots.splice(0))
    fs.rmSync(root, { recursive: true, force: true });
});

function fixture(version = '3.1.1'): ReleaseFixture {
  const workspaceRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-release-'));
  roots.push(workspaceRoot);
  const rootDir = path.join(workspaceRoot, 'bazaarplusplus-installer');
  fs.mkdirSync(rootDir);
  fs.writeFileSync(path.join(workspaceRoot, 'VERSION'), version);
  fs.writeFileSync(path.join(rootDir, 'package.json'), '{}\n');
  runFixtureGit(['init', '-q'], { cwd: workspaceRoot });
  const exclude = path.join(workspaceRoot, '.git/info/exclude');
  fs.mkdirSync(path.dirname(exclude), { recursive: true });
  fs.appendFileSync(
    exclude,
    'bazaarplusplus-installer/src-tauri/target/\nbazaarplusplus-installer/src-tauri/resources/\n'
  );
  runFixtureGit(['add', '-A'], { cwd: workspaceRoot });
  const data: ReleaseFixture = { workspaceRoot, rootDir, version, commits: {} };
  data.commits.a = commitFixture(data, 'a');
  return data;
}

function commitFixture(
  { workspaceRoot }: Pick<ReleaseFixture, 'workspaceRoot'>,
  message: string
) {
  runFixtureGit(
    [
      '-c',
      'user.name=Release Test',
      '-c',
      'user.email=release-test@example.test',
      'commit',
      '-q',
      '--allow-empty',
      '-m',
      message
    ],
    { cwd: workspaceRoot }
  );
  return runFixtureGit(['rev-parse', 'HEAD'], { cwd: workspaceRoot }).trim();
}

const installerName = (platform: string, version: string) =>
  platform === 'windows'
    ? `BazaarPlusPlus_${version}_x64-setup.exe`
    : `BazaarPlusPlus_${version}_aarch64.dmg`;

async function upload(
  data: ReleaseFixture,
  store: MemoryStore,
  platform: TauriBuildPlatform,
  commit = 'a'
) {
  data.commits[commit] ??= commitFixture(data, commit);
  runFixtureGit(['checkout', '-q', '--detach', data.commits[commit]], {
    cwd: data.workspaceRoot
  });
  const { managed } = writeArtifacts(data.rootDir, platform, {
    version: data.version,
    installer: `${platform} installer`,
    updater: 'macos updater',
    signature: `${platform}-signature`
  });
  createArtifactManifest({
    rootDir: data.rootDir,
    platform,
    version: data.version,
    managedPayload: managed
  });
  return uploadPlatform({
    workspaceRoot: data.workspaceRoot,
    platform,
    baseUrl,
    store
  });
}

const apkName = (version: string) =>
  `BazaarPlusPlus_${version}_android-aarch64.apk`;
const mirrorUrl = (platform: BuildPlatform, version: string) =>
  mainlandMirrorUrl(
    version,
    platform === 'android' ? apkName(version) : installerName(platform, version)
  );

// One mirror server per store, so pushes persist across records like on the real host.
const networks = new WeakMap<MemoryStore, MirrorNetwork>();
function networkOf(store: MemoryStore): MirrorNetwork {
  const existing = networks.get(store);
  if (existing) return existing;
  const network = mirrorNetwork(store, baseUrl);
  networks.set(store, network);
  return network;
}

function record(
  data: Pick<ReleaseFixture, 'version'>,
  store: MemoryStore,
  platform: BuildPlatform,
  extra: Partial<RecordMainlandMirrorOptions> = {}
) {
  const network = networkOf(store);
  return recordMainlandMirror({
    version: data.version,
    platform,
    store,
    sshKey: 'mirror-key\n',
    fetchImpl: network.fetchImpl,
    runRsync: network.runRsync,
    ...extra
  });
}

async function stage(data: ReleaseFixture, store: MemoryStore) {
  await upload(data, store, 'macos');
  await upload(data, store, 'windows');
  await record(data, store, 'macos');
  await record(data, store, 'windows');
}

const promote = (
  data: ReleaseFixture,
  store: MemoryStore,
  extra: Partial<PromoteReleaseOptions> = {}
) => promoteRelease({ ...data, baseUrl, store, ...extra });

const mirrorWrites = (store: MemoryStore) =>
  store.writes.filter((key) => key.includes('/mirror/'));

test('recording a mirror pushes the uploaded installer, proves its bytes and records the derived address without publishing', async () => {
  const data = fixture();
  const store = new MemoryStore();
  await upload(data, store, 'macos');
  const network = networkOf(store);
  const start = store.writes.length;
  const dmg = installerName('macos', data.version);
  expect(await record(data, store, 'macos')).toEqual({
    schemaVersion: 1,
    version: data.version,
    platform: 'darwin-aarch64',
    url: `https://1.14.150.142/bazaarplusplus/${data.version}/${dmg}`,
    fileName: dmg,
    verified: true
  });
  expect(network.pushes.map(({ files }) => Object.keys(files))).toEqual([
    [`${data.version}/${dmg}`]
  ]);
  expect(store.writes.slice(start)).toEqual([
    `${data.version}/darwin-aarch64/mirror/mainland.json`
  ]);
  // A re-run before promotion pushes again; the mirror keeps its copy and the record is unchanged.
  await record(data, store, 'macos');
  expect(network.pushes).toHaveLength(2);
  expect(store.writes).toHaveLength(start + 1);
  expect(await store.get('latest.json')).toBeNull();
  expect(await store.get('latest/darwin-aarch64.json')).toBeNull();
});

test.each(['commit', 'missing', 'hash'])(
  'dual promotion writes nothing when the second platform fails %s validation',
  async (failure) => {
    const data = fixture();
    const store = new MemoryStore();
    await stage(data, store);
    const fragmentKey = `${data.version}/darwin-aarch64/updater/platform-manifest.json`;
    const object = store.objects.get(fragmentKey)!;
    const mac = JSON.parse(object.bytes.toString());
    const installerKey = mac.installer.url.slice(baseUrl.length + 1);
    if (failure === 'commit') {
      mac.gitCommit = 'b'.repeat(40);
      store.objects.set(fragmentKey, {
        ...object,
        bytes: Buffer.from(JSON.stringify(mac))
      });
    } else if (failure === 'missing') {
      store.objects.delete(installerKey);
    } else {
      const installer = store.objects.get(installerKey)!;
      store.objects.set(installerKey, {
        ...installer,
        bytes: Buffer.alloc(installer.bytes.length)
      });
    }
    const start = store.writes.length;
    await expect(promote(data, store)).rejects.toThrow(
      failure === 'commit' ? /commits differ/ : /artifact is missing or differs/
    );
    expect(store.writes).toHaveLength(start);
  }
);

test('a failed mirror write can be retried before promotion', async () => {
  const data = fixture();
  const store = new MemoryStore();
  await upload(data, store, 'windows');
  await upload(data, store, 'macos');
  await record(data, store, 'windows');
  store.beforePut = async (key: string) => {
    if (key === `${data.version}/darwin-aarch64/mirror/mainland.json`)
      throw new Error('R2 unavailable');
  };
  await expect(record(data, store, 'macos')).rejects.toThrow('R2 unavailable');
  expect(await store.get('latest.json')).toBeNull();
  await expect(promote(data, store)).rejects.toThrow(
    /Missing darwin-aarch64 mainland mirror/
  );
  store.beforePut = undefined;
  await record(data, store, 'macos');
  expect(networkOf(store).served.size).toBe(2);
  expect((await promote(data, store)).version).toBe(data.version);
});

test('upload is platform-local; latest is promoted only after both verified platforms exist', async () => {
  const data = fixture();
  const store = new MemoryStore();
  await upload(data, store, 'macos');
  expect(await store.get('latest.json')).toBeNull();
  await expect(promote(data, store)).rejects.toThrow(/Missing windows/);
  expect(await store.get('latest.json')).toBeNull();
  await upload(data, store, 'windows');
  await record(data, store, 'macos');
  await record(data, store, 'windows');
  const latest = await promote(data, store);
  expect(Object.keys(latest.platforms)).toEqual([
    'windows-x86_64',
    'darwin-aarch64'
  ]);
  // Clients installing through latest.json need their platform manifest to exist first.
  const order = [
    'latest/windows-x86_64.json',
    'latest/mod/windows-x86_64.json',
    'latest/darwin-aarch64.json',
    'latest/mod/darwin-aarch64.json'
  ].map((key) => store.writes.indexOf(key));
  expect(Math.max(...order)).toBeLessThan(store.writes.indexOf('latest.json'));
  expect(
    JSON.parse(
      (await store.get('latest/darwin-aarch64.json'))!.bytes.toString()
    )
  ).toMatchObject({
    version: data.version,
    platforms: { 'darwin-aarch64': latest.platforms['darwin-aarch64'] },
    downloads: { 'darwin-aarch64': latest.downloads['darwin-aarch64'] }
  });
  expect(latest.downloads['darwin-aarch64']).toMatchObject({
    url: expect.stringContaining('/installer/BazaarPlusPlus_3.1.1_aarch64.dmg'),
    mainlandUrl: mirrorUrl('macos', data.version)
  });
  expect(latest.downloads['windows-x86_64'].mainlandUrl).toBe(
    mirrorUrl('windows', data.version)
  );
  for (const key of Object.keys(latest.platforms)) {
    expect((await store.get(modManifestPath(key)))!.bytes).toEqual(
      (await store.get(`latest/${key}.json`))!.bytes
    );
  }
  const writes = store.writes.length;
  await promote(data, store);
  expect(store.writes).toHaveLength(writes);
});

test.each(['all', 'windows'] as const)(
  'a failed mod endpoint write is completed by retrying %s promotion',
  async (mode) => {
    const data = fixture();
    const store = new MemoryStore();
    await stage(data, store);
    const run = () =>
      mode === 'all' ? promote(data, store) : promoteOne(data, store, mode);
    const modKey = modManifestPath('windows-x86_64');
    store.beforePut = (key: string) => {
      if (key === modKey) throw new Error('mod endpoint unavailable');
    };
    await expect(run()).rejects.toThrow('mod endpoint unavailable');
    const published = (await store.get('latest/windows-x86_64.json'))!.bytes;
    expect(await store.get(modKey)).toBeNull();
    expect(await store.get('latest.json')).toBeNull();
    store.beforePut = undefined;
    await run();
    expect((await store.get(modKey))!.bytes).toEqual(published);
    expect((await store.get('latest/windows-x86_64.json'))!.bytes).toEqual(
      published
    );
  }
);

test('confirming an already published release fills missing mod endpoints without changing installer manifests', async () => {
  const data = fixture();
  const store = new MemoryStore();
  await stage(data, store);
  await promote(data, store);
  const modKeys = ['windows-x86_64', 'darwin-aarch64'].map(modManifestPath);
  for (const key of modKeys) store.objects.delete(key);
  for (const key of store.objects.keys())
    if (key.includes('/mirror/')) store.objects.delete(key);
  const before = store.writes.length;
  await promote(data, store);
  expect(store.writes.slice(before)).toEqual(modKeys);
  for (const platform of ['windows-x86_64', 'darwin-aarch64'])
    expect((await store.get(modManifestPath(platform)))!.bytes).toEqual(
      (await store.get(`latest/${platform}.json`))!.bytes
    );
});

test.each(['all', 'windows'] as const)(
  'a changed mod manifest rejects %s confirmation before writing',
  async (mode) => {
    const data = fixture();
    const store = new MemoryStore();
    await stage(data, store);
    await promote(data, store);
    const key = modManifestPath('windows-x86_64');
    const changed = JSON.parse((await store.get(key))!.bytes.toString());
    changed.gitCommit = 'f'.repeat(40);
    store.objects.set(key, {
      bytes: Buffer.from(JSON.stringify(changed)),
      etag: '"changed"'
    });
    const before = store.writes.length;
    await expect(
      mode === 'all' ? promote(data, store) : promoteOne(data, store, mode)
    ).rejects.toThrow(/mod manifest differs/);
    expect(store.writes).toHaveLength(before);
  }
);

test.each(['installer', 'managedPayload'])(
  'a missing %s blocks both update endpoints',
  async (artifact) => {
    const data = fixture();
    const store = new MemoryStore();
    await stage(data, store);
    const fragment = JSON.parse(
      (await store.get(
        fragmentKey(data.version, 'windows-x86_64')
      ))!.bytes.toString()
    );
    store.objects.delete(
      decodeURIComponent(fragment[artifact].url.slice(baseUrl.length + 1))
    );
    const before = store.writes.length;
    await expect(promote(data, store)).rejects.toThrow(
      /Release artifact is missing/
    );
    expect(store.writes).toHaveLength(before);
    expect(await store.get(modManifestPath('windows-x86_64'))).toBeNull();
    expect(await store.get('latest/windows-x86_64.json')).toBeNull();
  }
);

test('platform fragments preserve source identity and actual artifact names', () => {
  const record = { path: 'some/renamed.dmg', size: 4, sha256: 'b'.repeat(64) };
  const fragment = buildPlatformFragment({
    baseUrl,
    manifest: {
      appVersion: '3.1.1',
      buildPlatform: 'macos',
      releasePlatformKey: 'darwin-aarch64',
      gitCommit: 'a'.repeat(40),
      dirty: false,
      installer: record,
      updater: { ...record, path: 'actual-updater.tgz' },
      signature: {
        ...record,
        path: 'actual-updater.tgz.sig',
        content: 'signature\n'
      },
      managedPayload: { ...record, signature: 'managed-signature\n' },
      historyDatabaseUserVersion: 1,
      nonManagedPayloadDigest: 'e'.repeat(64)
    },
    selfUpdatable: false,
    game: DESKTOP_GAME
  });
  expect(fragment.gitCommit).toBe('a'.repeat(40));
  expect(fragment.managedPayload).toEqual({
    url: `${baseUrl}/3.1.1/darwin-aarch64/managed/BazaarPlusPlus_3.1.1_darwin-aarch64-managed.zip`,
    size: 4,
    sha256: 'b'.repeat(64),
    signature: 'managed-signature'
  });
  expect(fragment.installer.url).toContain('/installer/renamed.dmg');
  expect(fragment.updater.signature).toBe('signature');
});

test('upload freezes all local files before any remote write', async () => {
  const data = fixture();
  const store = new MemoryStore();
  const { updater, signature } = artifactPaths(
    data.rootDir,
    'macos',
    data.version
  );
  store.beforePut = async () => {
    fs.writeFileSync(updater, 'replacement build');
    fs.writeFileSync(signature, 'replacement signature');
  };
  const fragment = await upload(data, store, 'macos');
  const key = fragment.updater.url.slice(baseUrl.length + 1);
  expect((await store.get(key))!.bytes.toString()).toBe('macos updater');
  expect((await store.get(`${key}.sig`))!.bytes.toString()).toBe(
    'macos-signature'
  );
});

test('a product version cannot mix platform commits', async () => {
  const data = fixture();
  const store = new MemoryStore();
  const windows = await upload(data, store, 'windows');
  await expect(upload(data, store, 'macos', 'b')).rejects.toThrow(
    `was uploaded from commit ${data.commits.a} on windows-x86_64`
  );
  expect(store.writes.some((key) => key.includes('darwin'))).toBe(false);
  const macos = await upload(data, store, 'macos');
  expect(() =>
    buildLatestManifest({
      version: data.version,
      fragments: [windows, { ...macos, gitCommit: 'b'.repeat(40) }],
      existingLatest: null
    })
  ).toThrow(/commits differ/);
  expect(await store.get('latest.json')).toBeNull();
});

test('immutable uploads are repeatable but cannot replace artifact bytes', async () => {
  const store = new MemoryStore();
  await putImmutable(store, '3.1.1/asset', Buffer.from('original'));
  await putImmutable(store, '3.1.1/asset', Buffer.from('original'));
  await expect(
    putImmutable(store, '3.1.1/asset', Buffer.from('changed'))
  ).rejects.toThrow(/Immutable/);
  expect((await store.get('3.1.1/asset'))!.bytes.toString()).toBe('original');
});

test('missing assets, read errors and malformed fragments do not advance latest', async () => {
  const data = fixture();
  const store = new MemoryStore();
  await stage(data, store);
  const assetKey = store.writes.find((key) => key.includes('/installer/'))!;
  const asset = store.objects.get(assetKey)!;
  store.objects.delete(assetKey);
  await expect(promote(data, store)).rejects.toThrow(/artifact is missing/);
  store.objects.set(assetKey, asset);
  const key = '3.1.1/windows-x86_64/updater/platform-manifest.json';
  store.objects.get(key)!.bytes = Buffer.from('{broken');
  await expect(promote(data, store)).rejects.toThrow(/Invalid JSON/);
  const get = store.get.bind(store);
  store.get = async (name: string) => {
    if (name === key) throw new Error('HTTP 503');
    return get(name);
  };
  await expect(promote(data, store)).rejects.toThrow(/503/);
  expect(await store.get('latest.json')).toBeNull();
});

test('CAS prevents an older concurrent publisher from overwriting a newer release', async () => {
  const old = fixture('3.1.1');
  const newer = fixture('3.2.0');
  const store = new MemoryStore();
  await stage(old, store);
  const fragments = [
    await upload(newer, store, 'macos'),
    await upload(newer, store, 'windows')
  ];
  const newerLatest = buildLatestManifest({
    version: newer.version,
    fragments,
    existingLatest: null
  });
  store.beforePut = async (key: string) => {
    if (key !== 'latest.json') return;
    store.beforePut = undefined;
    store.objects.set(key, {
      bytes: Buffer.from(JSON.stringify(newerLatest)),
      etag: '"other-writer"'
    });
  };
  await expect(promote(old, store)).rejects.toThrow(/rollback/);
  expect(
    JSON.parse((await store.get('latest.json'))!.bytes.toString()).version
  ).toBe('3.2.0');
});

test('a lost successful conditional PUT response is recognized by read-back', async () => {
  const data = fixture();
  const store = new MemoryStore();
  await stage(data, store);
  const put = store.put.bind(store);
  store.put = async (...args: Parameters<MemoryStore['put']>) => {
    const result = await put(...args);
    if (args[0] === 'latest.json') throw new Error('connection lost');
    return result;
  };
  expect((await promote(data, store)).version).toBe(data.version);
});

test('same-version publication refuses changed release facts', async () => {
  const data = fixture();
  const store = new MemoryStore();
  const fragments = [
    await upload(data, store, 'macos'),
    await upload(data, store, 'windows')
  ];
  const latest = buildLatestManifest({
    version: data.version,
    fragments,
    existingLatest: null
  });
  const changed = structuredClone(fragments);
  changed[0].installer.sha256 = 'c'.repeat(64);
  expect(() =>
    buildLatestManifest({
      version: data.version,
      fragments: changed,
      existingLatest: latest
    })
  ).toThrow(/differs/);
});

test('recording refuses a mirror that does not serve the release bytes; only an explicit override records an unverified one', async () => {
  const data = fixture();
  const store = new MemoryStore();
  await upload(data, store, 'macos');
  const network = networkOf(store);
  await expect(record(data, store, 'windows')).rejects.toThrow(
    /Missing windows-x86_64 platform fragment/
  );
  // rrsync never overwrites: a wrong file already on the mirror survives the push.
  const file = `${data.version}/${installerName('macos', data.version)}`;
  network.served.set(file, Buffer.from('another build'));
  await expect(record(data, store, 'macos')).rejects.toThrow(
    /missing-or-different .* \(serves 13 bytes, expected \d+\)/
  );
  expect(network.pushes).toHaveLength(1);
  expect(mirrorWrites(store)).toEqual([]);
  network.served.delete(file);
  const log = vi.fn();
  const unverified = await record(data, store, 'macos', {
    fetchImpl: async (url, init) =>
      url.startsWith(`${MAINLAND_MIRROR.baseUrl}/`)
        ? new Response('', { status: 503 })
        : network.fetchImpl(url, init),
    allowUnverified: true,
    log
  });
  expect(unverified).toMatchObject({
    platform: 'darwin-aarch64',
    url: mirrorUrl('macos', data.version),
    fileName: 'BazaarPlusPlus_3.1.1_aarch64.dmg',
    verified: false
  });
  expect(log).toHaveBeenCalledWith(expect.stringMatching(/WARNING.*HTTP 503/));
  expect(
    JSON.parse(
      (await store.get(
        '3.1.1/darwin-aarch64/mirror/mainland.json'
      ))!.bytes.toString()
    )
  ).toEqual(unverified);
});

test('recording stops before the push when the release artifact differs or the version is already published', async () => {
  const data = fixture();
  const store = new MemoryStore();
  const mac = await upload(data, store, 'macos');
  const runRsync = vi.fn(networkOf(store).runRsync);
  const installerKey = mac.installer.url.slice(baseUrl.length + 1);
  const installer = store.objects.get(installerKey)!;
  store.objects.set(installerKey, {
    ...installer,
    bytes: Buffer.alloc(installer.bytes.length)
  });
  await expect(record(data, store, 'macos', { runRsync })).rejects.toThrow(
    /the fragment records/
  );
  store.objects.set(installerKey, installer);
  await upload(data, store, 'windows');
  await record(data, store, 'macos');
  await record(data, store, 'windows');
  await promote(data, store);
  await expect(record(data, store, 'macos', { runRsync })).rejects.toThrow(
    /already published[\s\S]*publish a new product version/
  );
  const older = fixture('3.0.0');
  await upload(older, store, 'macos');
  await expect(record(older, store, 'macos', { runRsync })).rejects.toThrow(
    /already published/
  );
  expect(runRsync).not.toHaveBeenCalled();
});

test('promotion requires every platform mirror unless explicitly waived, and a waived mirror cannot be added later', async () => {
  const data = fixture();
  const store = new MemoryStore();
  await upload(data, store, 'macos');
  await upload(data, store, 'windows');
  await record(data, store, 'windows');
  await expect(promote(data, store)).rejects.toThrow(
    /Missing darwin-aarch64 mainland mirror[\s\S]*--without-mainland-mirror/
  );
  expect(await store.get('latest.json')).toBeNull();
  const log = vi.fn();
  const latest = await promote(data, store, {
    withoutMainlandMirror: true,
    log
  });
  expect(latest.downloads['windows-x86_64'].mainlandUrl).toBe(
    mirrorUrl('windows', data.version)
  );
  expect(latest.downloads['darwin-aarch64']).not.toHaveProperty('mainlandUrl');
  expect(log).toHaveBeenCalledWith(
    expect.stringMatching(/WARNING.*darwin-aarch64 without a mainland mirror/)
  );
  const writes = store.writes.length;
  expect((await promote(data, store)).version).toBe(data.version);
  expect(store.writes).toHaveLength(writes);
  await expect(record(data, store, 'macos')).rejects.toThrow(
    /already published/
  );
});

test('the manifest writer refuses a mainland mirror recorded for another file name', () => {
  const expected = JSON.parse(
    fs.readFileSync(
      path.resolve(import.meta.dirname, './fixtures/latest.json'),
      'utf8'
    )
  ) as ReleaseManifest;
  const platformKeys = Object.keys(expected.platforms) as ReleasePlatformKey[];
  const fragments = platformKeys.map((platform): DesktopPlatformFragment => {
    const { mainlandUrl, ...release } = expected.downloads[platform];
    const { installer, selfUpdate } = splitDesktopRelease(release);
    return {
      schemaVersion: 1,
      version: expected.version,
      platform,
      gitCommit: expected.gitCommit,
      installer,
      ...selfUpdate,
      updater: {
        ...expected.platforms[platform],
        size: 100,
        sha256: 'b'.repeat(64)
      },
      signatureFile: {
        url: `${expected.platforms[platform].url}.sig`,
        size: 10,
        sha256: 'c'.repeat(64)
      }
    };
  });
  const mirrors = Object.fromEntries(
    fragments.map((fragment): [string, MirrorRecord] => [
      fragment.platform,
      {
        schemaVersion: 1,
        version: expected.version,
        platform: fragment.platform,
        url: expected.downloads[fragment.platform].mainlandUrl!,
        fileName: decodeURIComponent(
          path.posix.basename(new URL(fragment.installer.url).pathname)
        ),
        verified: true
      }
    ])
  );
  expect(() =>
    buildLatestManifest({
      version: expected.version,
      fragments,
      mirrors: {
        ...mirrors,
        'windows-x86_64': {
          ...mirrors['windows-x86_64'],
          fileName: 'other.exe'
        }
      },
      existingLatest: null
    })
  ).toThrow(/recorded for other\.exe/);
});

function splitDesktopRelease({
  game,
  managedPayload,
  selfUpdatable,
  historyDatabaseUserVersion,
  nonManagedPayloadDigest,
  ...installer
}: Omit<DesktopDownload, 'mainlandUrl'>) {
  return {
    installer,
    selfUpdate: {
      game,
      managedPayload,
      selfUpdatable,
      historyDatabaseUserVersion,
      nonManagedPayloadDigest
    }
  };
}

const promoteOne = (
  data: ReleaseFixture,
  store: MemoryStore,
  platform: BuildPlatform,
  extra: Partial<PromotePlatformOptions> = {}
) =>
  promotePlatform({
    ...data,
    platform,
    baseUrl,
    store,
    ...extra
  });

test('one platform can run ahead once a lockstep release carries the per-platform endpoint', async () => {
  const data = fixture();
  const store = new MemoryStore();
  await stage(data, store);
  await promote(data, store);
  const next = fixture('3.1.2');
  await upload(next, store, 'windows');
  await record(next, store, 'windows');
  const log = vi.fn();
  const ahead = await promoteOne(next, store, 'windows', { log });
  expect(ahead.platform.version).toBe('3.1.2');
  expect(ahead.latest).toBeNull();
  const read = async (key: string) =>
    JSON.parse((await store.get(key))!.bytes.toString());
  expect((await read('latest/windows-x86_64.json')).downloads).toEqual({
    'windows-x86_64': {
      ...ahead.platform.downloads['windows-x86_64'],
      mainlandUrl: mirrorUrl('windows', '3.1.2')
    }
  });
  expect((await read('latest/darwin-aarch64.json')).version).toBe('3.1.1');
  expect((await read('latest.json')).version).toBe('3.1.1');
  expect(log).toHaveBeenCalledWith(
    expect.stringMatching(
      /latest\.json unchanged: platforms are at 3\.1\.2 and 3\.1\.1/
    )
  );
  const modAhead = (await store.get(modManifestPath('windows-x86_64')))!.bytes;
  await promote(data, store);
  expect((await store.get(modManifestPath('windows-x86_64')))!.bytes).toEqual(
    modAhead
  );
  await expect(record(next, store, 'windows')).rejects.toThrow(
    /already published/
  );
  await expect(promoteOne(data, store, 'windows')).rejects.toThrow(/rollback/);
  const writes = store.writes.length;
  expect((await promoteOne(next, store, 'windows')).platform.version).toBe(
    '3.1.2'
  );
  expect(store.writes).toHaveLength(writes);
  await expect(upload(next, store, 'macos', 'b')).rejects.toThrow(
    /was uploaded from commit/
  );
  await upload(next, store, 'macos');
  await record(next, store, 'macos');
  const joined = await promoteOne(next, store, 'macos');
  expect(joined.latest?.version).toBe('3.1.2');
  expect(await read('latest.json')).toMatchObject({
    version: '3.1.2',
    gitCommit: next.commits.a,
    downloads: {
      'windows-x86_64': { mainlandUrl: mirrorUrl('windows', '3.1.2') },
      'darwin-aarch64': { mainlandUrl: mirrorUrl('macos', '3.1.2') }
    }
  });
});

test('a promotion that lands during the mirror check refuses the record and keeps the published address', async () => {
  const data = fixture();
  const store = new MemoryStore();
  await stage(data, store);
  await promote(data, store);
  const next = fixture('3.1.2');
  await upload(next, store, 'windows');
  await record(next, store, 'windows');
  const network = networkOf(store);
  const writes = mirrorWrites(store).length;
  await expect(
    record(next, store, 'windows', {
      fetchImpl: async (url, init) => {
        if (url === mirrorUrl('windows', next.version))
          await promoteOne(next, store, 'windows');
        return network.fetchImpl(url, init);
      }
    })
  ).rejects.toThrow(/has already published 3\.1\.2/);
  expect(mirrorWrites(store)).toHaveLength(writes);
  const published = JSON.parse(
    (await store.get('latest/windows-x86_64.json'))!.bytes.toString()
  );
  expect(published.downloads['windows-x86_64'].mainlandUrl).toBe(
    mirrorUrl('windows', next.version)
  );
  // Once promoted, the published address wins over a record that changed after it.
  store.objects.set('3.1.2/windows-x86_64/mirror/mainland.json', {
    bytes: Buffer.from(
      JSON.stringify({
        schemaVersion: 1,
        version: '3.1.2',
        platform: 'windows-x86_64',
        url: 'https://mirror.example/windows-3.1.2-elsewhere',
        fileName: installerName('windows', next.version),
        verified: true
      })
    ),
    etag: '"stale"'
  });
  const before = store.writes.length;
  expect((await promoteOne(next, store, 'windows')).platform.downloads).toEqual(
    published.downloads
  );
  expect(store.writes).toHaveLength(before);
});

test('re-running a platform promotion after both platforms aligned reports no advance', async () => {
  const data = fixture();
  const store = new MemoryStore();
  await stage(data, store);
  await promote(data, store);
  const next = fixture('3.1.2');
  await upload(next, store, 'windows');
  await record(next, store, 'windows');
  await upload(next, store, 'macos');
  await record(next, store, 'macos');
  expect((await promoteOne(next, store, 'windows')).advanced).toBe(false);
  const joined = await promoteOne(next, store, 'macos');
  expect(joined.advanced).toBe(true);
  expect(joined.latest?.version).toBe('3.1.2');
  const again = await promoteOne(next, store, 'macos');
  expect(again.advanced).toBe(false);
  expect(again.latest?.version).toBe('3.1.2');
});

test('a Platform Release Manifest without the self-update facts is rejected', () => {
  const release = `${baseUrl}/5.7.0/windows-x86_64`;
  expect(() =>
    validatePlatformManifest(
      {
        version: '5.7.0',
        gitCommit: '5'.repeat(40),
        notes: 'Release 5.7.0',
        pub_date: '2026-10-08T04:48:26.752Z',
        platforms: {
          'windows-x86_64': {
            url: `${release}/updater/windows-x86_64-updater`,
            signature: 'windows-x86_64-signature-5.7.0'
          }
        },
        downloads: {
          // Deliberately without the self-update facts the type requires.
          'windows-x86_64': {
            url: `${release}/installer/windows-x86_64-installer`,
            size: 8872188,
            sha256: '9'.repeat(64)
          } as DesktopDownload
        }
      },
      'windows-x86_64'
    )
  ).toThrow(/self-update facts for 5\.7\.0/);
});

test('a platform manifest promotes a fragment without game and refuses an invalid game', () => {
  for (const key of ['windows-x86_64', 'darwin-aarch64'] as const) {
    const expected = JSON.parse(
      fs.readFileSync(
        path.resolve(import.meta.dirname, `./fixtures/latest/${key}.json`),
        'utf8'
      )
    ) as ReleaseManifest;
    const { mainlandUrl, ...release } = expected.downloads[key];
    const { installer, selfUpdate } = splitDesktopRelease(release);
    const fragment: DesktopPlatformFragment = {
      schemaVersion: 1,
      version: expected.version,
      platform: key,
      gitCommit: expected.gitCommit,
      installer,
      ...selfUpdate,
      updater: {
        ...expected.platforms[key],
        size: 100,
        sha256: 'b'.repeat(64)
      },
      signatureFile: {
        url: `${expected.platforms[key].url}.sig`,
        size: 10,
        sha256: 'c'.repeat(64)
      }
    };
    const mirror: MirrorRecord = {
      schemaVersion: 1,
      version: expected.version,
      platform: key,
      url: mainlandUrl!,
      fileName: decodeURIComponent(
        path.posix.basename(new URL(installer.url).pathname)
      ),
      verified: true
    };
    // The fixture simulates a fragment uploaded before desktop game compatibility was tracked.
    const { game: _game, ...withoutGame } = expected.downloads[key];
    const older = { ...expected, downloads: { [key]: withoutGame } };
    expect(
      buildPlatformManifest({
        version: expected.version,
        fragment: { ...fragment, game: undefined },
        mirror,
        existing: older,
        now: new Date(expected.pub_date)
      })
    ).toEqual(older);
    expect(() =>
      validatePlatformManifest(
        {
          ...expected,
          downloads: {
            [key]: {
              ...expected.downloads[key],
              game: { gameVersion: '', managedSha256: 'd'.repeat(64) }
            }
          }
        },
        key
      )
    ).toThrow(`Invalid ${key} ${expected.version} game`);
  }
});

const ANDROID = 'android-aarch64';
const promoteAndroid = (
  data: Pick<ReleaseFixture, 'version'>,
  store: MemoryStore,
  extra: Partial<PromotePlatformOptions> = {}
) =>
  promotePlatform({
    version: data.version,
    platform: 'android',
    baseUrl,
    store,
    withoutMainlandMirror: true,
    ...extra
  });

test('Android promotion checks the three artifacts and publishes only its own manifest', async () => {
  const data = fixture();
  const store = new MemoryStore();
  const { fragment, appKey } = await uploadAndroid(store, {
    version: data.version,
    gitCommit: data.commits.a,
    baseUrl
  });
  for (const key of [
    appKey,
    ...ANDROID_SOURCES.flatMap((source) => [
      interopPackageKey(fragment.adaptations[source].interop.sha256),
      androidManagedPayloadKey(data.version, source)
    ])
  ]) {
    const object = store.objects.get(key)!;
    store.objects.delete(key);
    await expect(promoteAndroid(data, store)).rejects.toThrow(
      `Release artifact is missing or differs: ${key}`
    );
    store.objects.set(key, object);
  }
  const before = new Set(store.objects.keys());
  const now = new Date('2026-09-21T00:00:00.000Z');
  const result = await promoteAndroid(data, store, { now });
  expect(result).toMatchObject({
    platform: { version: data.version, gitCommit: data.commits.a },
    latest: null,
    advanced: false
  });
  expect([...store.objects.keys()].filter((key) => !before.has(key))).toEqual([
    `latest/${ANDROID}.json`,
    modManifestPath(ANDROID)
  ]);
  expect((await store.get(modManifestPath(ANDROID)))!.bytes).toEqual(
    (await store.get(`latest/${ANDROID}.json`))!.bytes
  );
  expect(store.writes.some((key) => key.includes('mirror'))).toBe(false);
  const published = store.objects.get(`latest/${ANDROID}.json`)!.bytes;
  await promoteAndroid(data, store);
  expect(store.objects.get(`latest/${ANDROID}.json`)!.bytes).toEqual(published);
});

test('an Android mirror serves the App APK, stays recordable after latest.json moves, and is published with the release', async () => {
  const data = fixture();
  const store = new MemoryStore();
  await stage(data, store);
  await promote(data, store);
  const { fragment } = await uploadAndroid(store, {
    version: data.version,
    gitCommit: data.commits.a,
    baseUrl
  });
  const apk = mirrorFileName(fragment);
  expect(apk).toBe(`BazaarPlusPlus_${data.version}_${ANDROID}.apk`);
  await expect(
    promoteAndroid(data, store, { withoutMainlandMirror: false })
  ).rejects.toThrow(
    `Missing ${ANDROID} mainland mirror for ${data.version}; run mirror for it or pass --without-mainland-mirror`
  );
  const url = mirrorUrl('android', data.version);
  const network = networkOf(store);
  const file = `${data.version}/${apk}`;
  network.served.set(file, Buffer.from('an older APK'));
  await expect(record(data, store, 'android')).rejects.toThrow(
    /missing-or-different/
  );
  expect(await store.get(mirrorRecordKey(data.version, ANDROID))).toBeNull();
  network.served.delete(file);
  // latest.json already names this version; it never freezes Android's mirror.
  expect(await record(data, store, 'android')).toMatchObject({
    platform: ANDROID,
    url,
    fileName: apk,
    verified: true
  });
  expect(network.served.get(file)).toEqual(
    store.objects.get(fragment.app.url.slice(baseUrl.length + 1))!.bytes
  );
  await promoteAndroid(data, store, { withoutMainlandMirror: false });
  const published = store.objects.get(`latest/${ANDROID}.json`)!.bytes;
  expect(JSON.parse(published.toString()).downloads[ANDROID].mainlandUrl).toBe(
    url
  );
  expect((await store.get(modManifestPath(ANDROID)))!.bytes).toEqual(published);
  await expect(record(data, store, 'android')).rejects.toThrow(
    `latest/${ANDROID}.json has already published ${data.version}`
  );
  await promoteAndroid(data, store, { withoutMainlandMirror: false });
  expect(store.objects.get(`latest/${ANDROID}.json`)!.bytes).toEqual(published);
});

test('an Android fragment pins the version to one commit for every platform', async () => {
  const data = fixture();
  const store = new MemoryStore();
  await uploadAndroid(store, {
    version: data.version,
    gitCommit: 'b'.repeat(40),
    baseUrl
  });
  await expect(upload(data, store, 'windows')).rejects.toThrow(
    `was uploaded from commit ${'b'.repeat(40)} on ${ANDROID}`
  );
  expect(store.writes.some((key) => key.includes('windows'))).toBe(false);

  const other = new MemoryStore();
  await upload(data, other, 'windows');
  await uploadAndroid(other, {
    version: data.version,
    gitCommit: 'b'.repeat(40),
    baseUrl
  });
  await expect(promoteAndroid(data, other)).rejects.toThrow(
    `was uploaded from commit ${data.commits.a} on windows-x86_64`
  );
  expect(await other.get(`latest/${ANDROID}.json`)).toBeNull();
});

test('an Android release refuses rollback and changed facts at a published version', async () => {
  const data = fixture('3.1.2');
  const store = new MemoryStore();
  await uploadAndroid(store, {
    version: '3.1.2',
    gitCommit: data.commits.a,
    baseUrl
  });
  await promoteAndroid(data, store);
  await uploadAndroid(store, {
    version: '3.1.1',
    gitCommit: data.commits.a,
    baseUrl
  });
  await expect(
    promoteAndroid({ ...data, version: '3.1.1' }, store)
  ).rejects.toThrow(/rollback from 3.1.2 to 3.1.1/);
  const key = fragmentKey('3.1.2', ANDROID);
  const fragment = JSON.parse(store.objects.get(key)!.bytes.toString());
  fragment.selfUpdatable = false;
  store.objects.get(key)!.bytes = Buffer.from(JSON.stringify(fragment));
  await expect(promoteAndroid(data, store)).rejects.toThrow(
    /Published 3.1.2 android-aarch64 downloads differs/
  );
});

test('upload and promote refuse an Android App another key signed, in both layers', async () => {
  const data = fixture('3.1.2');
  const store = new MemoryStore();
  const other = 'b'.repeat(64);
  await uploadAndroid(store, {
    version: '3.1.2',
    gitCommit: data.commits.a,
    baseUrl,
    signerSha256: other
  });
  await expect(promoteAndroid(data, store)).rejects.toThrow(
    `signed by ${other}, not the release key ${ANDROID_RELEASE_SIGNER_SHA256}`
  );
  expect(await store.get(`latest/${ANDROID}.json`)).toBeNull();
  // A new pinned certificate still has to match the already-promoted App.
  await promoteAndroid(data, store, { releaseSignerSha256: other });
  await uploadAndroid(store, {
    version: '3.1.3',
    gitCommit: data.commits.a,
    baseUrl
  });
  await expect(
    promoteAndroid({ ...data, version: '3.1.3' }, store)
  ).rejects.toThrow(
    `signed by ${ANDROID_RELEASE_SIGNER_SHA256}, not the release key ${other} the promoted Android App carries`
  );
});

const androidFacts = () => ({
  version: '3.1.2',
  gitCommit: 'a'.repeat(40),
  baseUrl,
  app: { size: 1, sha256: 'b'.repeat(64), signerSha256: 'c'.repeat(64) },
  adaptations: {
    publisher: {
      game: {
        gameVersion: '1.0.12825-production-android-d9601ded',
        versionCode: 12825,
        il2cppSha256: 'e'.repeat(64),
        metadataSha256: 'f'.repeat(64)
      },
      interop: {
        size: 2,
        sha256: '1'.repeat(64),
        snapshotSha256: `0123abcd${'0'.repeat(56)}`,
        signature: 'c2lnbmF0dXJl'
      },
      managedPayload: { size: 1, sha256: 'b'.repeat(64), signature: 'sig' }
    },
    play: {
      game: {
        gameVersion: '1.0.6',
        versionCode: 13168,
        il2cppSha256: '2'.repeat(64),
        metadataSha256: '3'.repeat(64)
      },
      interop: {
        size: 3,
        sha256: '4'.repeat(64),
        snapshotSha256: `4567cdef${'0'.repeat(56)}`,
        signature: 'c2lnbmF0dXJl'
      },
      managedPayload: { size: 2, sha256: '5'.repeat(64), signature: 'sig' }
    }
  },
  loader: { tag: 'v0.7.4-android.4', sha256: 'f'.repeat(64) },
  selfUpdatable: true,
  historyDatabaseUserVersion: 1,
  nonManagedPayloadDigest: 'd'.repeat(64)
});

test("an Android fragment names its own version's App, the content-addressed interop and its Mods", () => {
  const facts = androidFacts();
  const fragment = buildAndroidFragment(facts);
  expect(Object.keys(fragment)).toEqual([
    'schemaVersion',
    'kind',
    'version',
    'platform',
    'gitCommit',
    'app',
    'game',
    'interop',
    'managedPayload',
    'loader',
    'selfUpdatable',
    'historyDatabaseUserVersion',
    'nonManagedPayloadDigest',
    'adaptations'
  ]);
  expect(fragment).toMatchObject({
    schemaVersion: 1,
    kind: 'android-app',
    platform: ANDROID,
    app: {
      url: `${baseUrl}/3.1.2/${ANDROID}/app/BazaarPlusPlus_3.1.2_${ANDROID}.apk`,
      versionCode: 3_001_002
    },
    adaptations: {
      publisher: {
        signerSha256: ANDROID_GAME_SIGNERS.publisher,
        interop: { url: `${baseUrl}/android/interop/${'1'.repeat(64)}.zip` },
        managedPayload: {
          url: `${baseUrl}/3.1.2/${ANDROID}/managed/publisher/BazaarPlusPlus_3.1.2_${ANDROID}-publisher-managed.zip`
        }
      },
      play: {
        signerSha256: ANDROID_GAME_SIGNERS.play,
        game: { gameVersion: '1.0.6', versionCode: 13168 },
        interop: { url: `${baseUrl}/android/interop/${'4'.repeat(64)}.zip` },
        managedPayload: {
          url: `${baseUrl}/3.1.2/${ANDROID}/managed/play/BazaarPlusPlus_3.1.2_${ANDROID}-play-managed.zip`
        }
      }
    }
  });
  // The top level is the publisher's adaptation, which App 6.2.5 reads.
  const { publisher, play } = fragment.adaptations;
  expect(fragment.game).toEqual(publisher.game);
  expect(fragment.interop).toEqual(publisher.interop);
  expect(fragment.managedPayload).toEqual(publisher.managedPayload);
  const invalid = (change: object) => () =>
    validatePlatformFragment({ ...fragment, ...change }, '3.1.2', ANDROID);
  expect(invalid({ app: { ...fragment.app, versionCode: 12825 } })).toThrow(
    /Invalid android-aarch64 Android App/
  );
  expect(
    invalid({
      app: { ...fragment.app, url: `${baseUrl}/android/apk/BazaarPlusPlus.apk` }
    })
  ).toThrow(/Unexpected Android App URL/);
  expect(
    invalid({
      interop: {
        ...fragment.interop,
        url: `${baseUrl}/3.1.2/${ANDROID}/interop/x.zip`
      }
    })
  ).toThrow(/Unexpected Interop Package URL/);
  expect(
    invalid({ interop: { ...fragment.interop, signature: 'not base64!' } })
  ).toThrow(/Invalid android-aarch64 3.1.2 Interop Package/);
  expect(
    invalid({ game: { ...fragment.game, il2cppSha256: undefined } })
  ).toThrow(/Invalid android-aarch64 3.1.2 game/);
  expect(invalid({ adaptations: { publisher } })).toThrow(
    /android-aarch64 3.1.2 adaptations must be exactly publisher, play/
  );
  expect(
    invalid({
      adaptations: {
        publisher,
        play: { ...play, signerSha256: ANDROID_GAME_SIGNERS.publisher }
      }
    })
  ).toThrow(/Invalid android-aarch64 3.1.2 play game signer/);
  expect(invalid({ game: play.game })).toThrow(
    /must be the publisher adaptation's/
  );
  const { app, interop, ...rest } = fragment;
  expect(() =>
    validatePlatformFragment(
      // Deliberately the ADR 0010 shape, which no fragment type admits.
      {
        ...rest,
        kind: 'android',
        apk: app
      } as unknown as AndroidPlatformFragment,
      '3.1.2',
      ANDROID
    )
  ).toThrow(/is of kind "android", not android-app/);
  expect(() =>
    buildAndroidFragment({
      ...facts,
      selfUpdatable: undefined
    } as unknown as BuildAndroidFragmentOptions)
  ).toThrow(/Invalid android-aarch64 release facts/);
});

test.each([
  ['1.0.0', 1_000_000],
  ['6.0.2', 6_000_002],
  ['12.345.678', 12_345_678],
  ['2100.0.0', 2_100_000_000]
])('the Android App versionCode of %s is %i', (version, code) => {
  expect(androidAppVersionCode(version)).toBe(code);
});

test.each(['1.1000.0', '1.0.1000', '0.0.0', '2100.0.1', 'v1.0.0'])(
  '%s has no Android App versionCode',
  (version) => {
    expect(() => androidAppVersionCode(version)).toThrow();
  }
);

test('the Android non-managed payload digest covers the loader, native libraries and Updater, not the interop', () => {
  const runtime = {
    loader: { tag: 'v0.7.4-android.4', sha256: 'a'.repeat(64) },
    nativeLibs: { 'libe_sqlite3.so': { sha256: 'b'.repeat(64) } },
    updaterSha256: 'c'.repeat(64)
  };
  const digest = androidNonManagedPayloadDigest(runtime);
  const withInterop = { ...runtime, interopSha256: 'd'.repeat(64) };
  expect(androidNonManagedPayloadDigest(withInterop)).toBe(digest);
  for (const change of [
    { loader: { ...runtime.loader, tag: 'v0.7.5-android.1' } },
    { nativeLibs: { 'libe_sqlite3.so': { sha256: 'e'.repeat(64) } } },
    { updaterSha256: 'f'.repeat(64) }
  ])
    expect(androidNonManagedPayloadDigest({ ...runtime, ...change })).not.toBe(
      digest
    );
});

test('a desktop fragment carries its signed Managed Payload and the self-update facts', () => {
  const record = { size: 1, sha256: 'b'.repeat(64) };
  const fragment = buildPlatformFragment({
    manifest: {
      appVersion: '3.1.1',
      buildPlatform: 'windows',
      releasePlatformKey: 'windows-x86_64',
      gitCommit: 'a'.repeat(40),
      dirty: false,
      installer: { path: 'setup.exe', ...record },
      updater: { path: 'setup.exe', ...record },
      signature: { path: 'setup.exe.sig', ...record, content: 'sig' },
      managedPayload: { path: 'managed.zip', ...record, signature: 'msig' },
      historyDatabaseUserVersion: 2,
      nonManagedPayloadDigest: 'e'.repeat(64)
    },
    baseUrl,
    selfUpdatable: true,
    game: DESKTOP_GAME
  });
  expect(fragment).toMatchObject({
    game: DESKTOP_GAME,
    managedPayload: {
      url: `${baseUrl}/3.1.1/windows-x86_64/managed/BazaarPlusPlus_3.1.1_windows-x86_64-managed.zip`,
      signature: 'msig'
    },
    selfUpdatable: true,
    historyDatabaseUserVersion: 2,
    nonManagedPayloadDigest: 'e'.repeat(64)
  });
  const invalid = (change: object) => () =>
    validatePlatformFragment(
      { ...fragment, ...change },
      '3.1.1',
      'windows-x86_64'
    );
  expect(
    invalid({
      managedPayload: {
        ...fragment.managedPayload,
        url: `${baseUrl}/3.1.0/windows-x86_64/managed/x.zip`
      }
    })
  ).toThrow(/outside its release/);
  expect(invalid({ managedPayload: undefined })).toThrow(
    /Invalid windows-x86_64 Managed Payload release artifact/
  );
  expect(invalid({ selfUpdatable: 'yes' })).toThrow(
    /Invalid windows-x86_64 self-update facts/
  );
  expect(invalid({ historyDatabaseUserVersion: 0 })).toThrow(
    /Invalid windows-x86_64 self-update facts/
  );
  expect(invalid({ nonManagedPayloadDigest: 'native' })).toThrow(
    /Invalid windows-x86_64 self-update facts/
  );
});

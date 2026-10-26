import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, expect, test, vi } from 'vitest';
import {
  buildAndroidFragment,
  buildPlatformFragment,
  buildLatestManifest,
  buildPlatformManifest,
  fragmentKey,
  managedPayloadKey,
  validatePlatformFragment,
  validatePlatformManifest
} from './manifest.mjs';
import { LEGACY_RELEASE_BASE_URL } from './downloads.ts';
import {
  uploadPlatform,
  recordMainlandMirror,
  recordMainlandMirrors,
  promotePlatform,
  promoteRelease
} from './publish.mjs';
import { putImmutable } from './r2-store.mjs';
import { createArtifactManifest } from './artifact-manifest.mjs';
import { MemoryStore } from './test-support/memory-store.mjs';
import { uploadAndroid } from './test-support/android-fixture.mjs';
import {
  artifactPaths,
  writeArtifacts
} from './test-support/artifact-fixture.mjs';
import { runFixtureGit } from '../scripts/test-support/git-fixture.mjs';

const baseUrl = 'https://cdn.bazaarplusplus.com/installer';
const roots = [];

afterEach(() => {
  for (const root of roots.splice(0))
    fs.rmSync(root, { recursive: true, force: true });
});

// A release checkout: a git repository whose installer project holds the
// native build output. `commit` labels name its commits; `a` is the initial
// one and any other label is created on first use.
function fixture(version = '3.1.1') {
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
  const data = { workspaceRoot, rootDir, version, commits: {} };
  data.commits.a = commitFixture(data, 'a');
  return data;
}

function commitFixture({ workspaceRoot }, message) {
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

const platformKey = (platform) =>
  platform === 'windows' ? 'windows-x86_64' : 'darwin-aarch64';
const installerName = (platform, version) =>
  platform === 'windows'
    ? `BazaarPlusPlus_${version}_x64-setup.exe`
    : `BazaarPlusPlus_${version}_aarch64.dmg`;

// Builds `platform` from the commit labelled `commit` with the real artifact
// manifest, then uploads it.
async function upload(data, store, platform, commit = 'a') {
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

const sharePage = (fileName) => ({
  status: 200,
  html: `<html><head><title>${fileName} - 蓝奏云</title></head><body></body></html>`
});
const missingShare = {
  status: 200,
  html: '<html><head><title></title></head><body>文件取消分享</body></html>'
};
const mirrorUrl = (platform, version) =>
  `https://mirror.example/${platform}-${version}`;

// A share host serving the correctly named installer at each platform's
// mirror address for `version`; `overrides` replaces individual pages.
function mirrorProbe(version, overrides = {}) {
  const pages = {
    [mirrorUrl('windows', version)]: sharePage(
      installerName('windows', version)
    ),
    [mirrorUrl('macos', version)]: sharePage(installerName('macos', version)),
    ...overrides
  };
  return vi.fn(async (url) => pages[url] ?? { status: 404, html: '' });
}

function record(data, store, platform, extra = {}) {
  return recordMainlandMirror({
    version: data.version,
    platform,
    url: mirrorUrl(platform, data.version),
    store,
    probeMirror: mirrorProbe(data.version),
    ...extra
  });
}

async function stage(data, store) {
  await upload(data, store, 'macos');
  await upload(data, store, 'windows');
  await record(data, store, 'macos');
  await record(data, store, 'windows');
}

const promote = (data, store, extra = {}) =>
  promoteRelease({ ...data, baseUrl, store, ...extra });

const recordAll = (data, store, extra = {}) =>
  recordMainlandMirrors({
    version: data.version,
    urls: {
      windows: mirrorUrl('windows', data.version),
      macos: mirrorUrl('macos', data.version)
    },
    baseUrl,
    store,
    probeMirror: mirrorProbe(data.version),
    ...extra
  });

test('the mirror stage requires both uploads and records both shares without publishing', async () => {
  const data = fixture();
  const store = new MemoryStore();
  await upload(data, store, 'windows');
  const probeMirror = mirrorProbe(data.version);
  await expect(recordAll(data, store, { probeMirror })).rejects.toThrow(
    /Missing darwin-aarch64/
  );
  expect(probeMirror).not.toHaveBeenCalled();
  await upload(data, store, 'macos');
  const start = store.writes.length;
  const records = await recordAll(data, store);
  expect(records.map((record) => record.verified)).toEqual([true, true]);
  expect(store.writes.slice(start)).toEqual([
    `${data.version}/windows-x86_64/mirror/mainland.json`,
    `${data.version}/darwin-aarch64/mirror/mainland.json`
  ]);
  await recordAll(data, store);
  expect(store.writes).toHaveLength(start + 2);
  expect(await store.get('latest.json')).toBeNull();
  const latest = await promote(data, store);
  for (const platform of ['windows', 'macos'])
    expect(latest.downloads[platformKey(platform)].mainlandUrl).toBe(
      mirrorUrl(platform, data.version)
    );
  await expect(recordAll(data, store)).rejects.toThrow(/already published/);
});

test.each(['commit', 'missing', 'hash', 'share'])(
  'the mirror stage writes nothing when the second platform fails %s validation',
  async (failure) => {
    const data = fixture();
    const store = new MemoryStore();
    await upload(data, store, 'windows');
    const mac = await upload(data, store, 'macos');
    const fragmentKey = `${data.version}/darwin-aarch64/updater/platform-manifest.json`;
    const installerKey = mac.installer.url.slice(baseUrl.length + 1);
    if (failure === 'commit') {
      const object = store.objects.get(fragmentKey);
      const fragment = JSON.parse(object.bytes);
      fragment.gitCommit = 'b'.repeat(40);
      store.objects.set(fragmentKey, {
        ...object,
        bytes: Buffer.from(JSON.stringify(fragment))
      });
    } else if (failure === 'missing') {
      store.objects.delete(installerKey);
    } else if (failure === 'hash') {
      const object = store.objects.get(installerKey);
      store.objects.set(installerKey, {
        ...object,
        bytes: Buffer.alloc(object.bytes.length)
      });
    }
    const probeMirror = mirrorProbe(
      data.version,
      failure === 'share'
        ? { [mirrorUrl('macos', data.version)]: missingShare }
        : {}
    );
    const start = store.writes.length;
    await expect(recordAll(data, store, { probeMirror })).rejects.toThrow(
      failure === 'commit'
        ? /commits differ/
        : failure === 'share'
          ? /share is missing/
          : /artifact is missing or differs/
    );
    expect(store.writes).toHaveLength(start);
    if (failure !== 'share') expect(probeMirror).not.toHaveBeenCalled();
  }
);

test('promotion during the second mirror probe prevents either record from changing', async () => {
  const data = fixture();
  const store = new MemoryStore();
  await stage(data, store);
  const start = store.writes.filter((key) => key.includes('/mirror/')).length;
  await expect(
    recordAll(data, store, {
      probeMirror: async (url) => {
        if (url === mirrorUrl('macos', data.version))
          await promote(data, store);
        return mirrorProbe(data.version)(url);
      }
    })
  ).rejects.toThrow(/already published/);
  expect(store.writes.filter((key) => key.includes('/mirror/'))).toHaveLength(
    start
  );
});

test('a failed second mirror write can be retried before promotion', async () => {
  const data = fixture();
  const store = new MemoryStore();
  await upload(data, store, 'windows');
  await upload(data, store, 'macos');
  store.beforePut = async (key) => {
    if (key === `${data.version}/darwin-aarch64/mirror/mainland.json`)
      throw new Error('R2 unavailable');
  };
  await expect(recordAll(data, store)).rejects.toThrow('R2 unavailable');
  expect(await store.get('latest.json')).toBeNull();
  await expect(promote(data, store)).rejects.toThrow(
    /Missing darwin-aarch64 mainland mirror/
  );
  store.beforePut = undefined;
  await recordAll(data, store);
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
  // Platform manifests are written before the lockstep manifest so a client
  // that installs through latest.json finds its platform file afterwards.
  const order = [
    'latest/windows-x86_64.json',
    'latest/darwin-aarch64.json'
  ].map((key) => store.writes.indexOf(key));
  expect(Math.max(...order)).toBeLessThan(store.writes.indexOf('latest.json'));
  expect(
    JSON.parse((await store.get('latest/darwin-aarch64.json')).bytes)
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
  const writes = store.writes.length;
  await promote(data, store);
  expect(store.writes).toHaveLength(writes);
});

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
    selfUpdatable: false
  });
  expect(fragment.gitCommit).toBe('a'.repeat(40));
  expect(fragment.managedPayload).toEqual({
    url: `${baseUrl}/${managedPayloadKey('3.1.1', 'darwin-aarch64')}`,
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
  expect((await store.get(key)).bytes.toString()).toBe('macos updater');
  expect((await store.get(`${key}.sig`)).bytes.toString()).toBe(
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
  expect((await store.get('3.1.1/asset')).bytes.toString()).toBe('original');
});

test('missing assets, read errors and malformed fragments do not advance latest', async () => {
  const data = fixture();
  const store = new MemoryStore();
  await stage(data, store);
  const assetKey = store.writes.find((key) => key.includes('/installer/'));
  const asset = store.objects.get(assetKey);
  store.objects.delete(assetKey);
  await expect(promote(data, store)).rejects.toThrow(/artifact is missing/);
  store.objects.set(assetKey, asset);
  const key = '3.1.1/windows-x86_64/updater/platform-manifest.json';
  store.objects.get(key).bytes = Buffer.from('{broken');
  await expect(promote(data, store)).rejects.toThrow(/Invalid JSON/);
  const get = store.get.bind(store);
  store.get = async (name) => {
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
  store.beforePut = async (key) => {
    if (key !== 'latest.json') return;
    store.beforePut = null;
    store.objects.set(key, {
      bytes: Buffer.from(JSON.stringify(newerLatest)),
      etag: '"other-writer"'
    });
  };
  await expect(promote(old, store)).rejects.toThrow(/rollback/);
  expect(JSON.parse((await store.get('latest.json')).bytes).version).toBe(
    '3.2.0'
  );
});

test('a lost successful conditional PUT response is recognized by read-back', async () => {
  const data = fixture();
  const store = new MemoryStore();
  await stage(data, store);
  const put = store.put.bind(store);
  store.put = async (...args) => {
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

test('recording a mirror proves the share serves the uploaded installer; only an explicit override records an unverified one', async () => {
  const data = fixture();
  const store = new MemoryStore();
  await upload(data, store, 'macos');
  await expect(record(data, store, 'windows')).rejects.toThrow(
    /Missing windows-x86_64 platform fragment/
  );
  await expect(
    record(data, store, 'macos', {
      probeMirror: mirrorProbe(data.version, {
        [mirrorUrl('macos', data.version)]: missingShare
      })
    })
  ).rejects.toThrow(/share is missing or cancelled/);
  await expect(
    record(data, store, 'macos', {
      probeMirror: mirrorProbe(data.version, {
        [mirrorUrl('macos', data.version)]: sharePage(
          'BazaarPlusPlus_3.0.0_aarch64.dmg'
        )
      })
    })
  ).rejects.toThrow(
    /serves BazaarPlusPlus_3\.0\.0_aarch64\.dmg, expected BazaarPlusPlus_3\.1\.1_aarch64\.dmg/
  );
  await expect(
    record(data, store, 'macos', { url: 'http://mirror.example/insecure' })
  ).rejects.toThrow(/Unsafe mainland mirror URL/);
  expect(store.writes.filter((key) => key.includes('/mirror/'))).toEqual([]);
  const log = vi.fn();
  const unverified = await record(data, store, 'macos', {
    probeMirror: mirrorProbe(data.version, {
      [mirrorUrl('macos', data.version)]: { status: 503, html: '' }
    }),
    allowUnverified: true,
    log
  });
  expect(unverified).toMatchObject({
    platform: 'darwin-aarch64',
    fileName: 'BazaarPlusPlus_3.1.1_aarch64.dmg',
    verified: false
  });
  expect(log).toHaveBeenCalledWith(expect.stringMatching(/WARNING.*HTTP 503/));
  expect(
    JSON.parse(
      (await store.get('3.1.1/darwin-aarch64/mirror/mainland.json')).bytes
    )
  ).toEqual(unverified);
});

test('a mirror record can be replaced until its version is published, never afterwards', async () => {
  const data = fixture();
  const store = new MemoryStore();
  await upload(data, store, 'macos');
  await upload(data, store, 'windows');
  await record(data, store, 'macos');
  const replaced = await record(data, store, 'macos', {
    url: 'https://mirror.example/mac-reshared',
    probeMirror: mirrorProbe(data.version, {
      'https://mirror.example/mac-reshared': sharePage(
        installerName('macos', data.version)
      )
    })
  });
  expect(replaced.url).toBe('https://mirror.example/mac-reshared');
  await record(data, store, 'windows');
  const latest = await promote(data, store);
  expect(latest.downloads['darwin-aarch64'].mainlandUrl).toBe(
    'https://mirror.example/mac-reshared'
  );
  await expect(record(data, store, 'macos')).rejects.toThrow(
    /already published[\s\S]*publish a new product version/
  );
  const older = fixture('3.0.0');
  await upload(older, store, 'macos');
  await expect(record(older, store, 'macos')).rejects.toThrow(
    /already published/
  );
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
  // Confirming the published release needs no waiver and writes nothing.
  const writes = store.writes.length;
  expect((await promote(data, store)).version).toBe(data.version);
  expect(store.writes).toHaveLength(writes);
  await expect(record(data, store, 'macos')).rejects.toThrow(
    /already published/
  );
});

test('manifest writer and website share the same complete release fixture', () => {
  const expected = JSON.parse(
    fs.readFileSync(
      path.resolve(import.meta.dirname, './fixtures/latest.json'),
      'utf8'
    )
  );
  const fragments = Object.keys(expected.platforms).map((platform) => {
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
    fragments.map((fragment) => [
      fragment.platform,
      {
        schemaVersion: 1,
        version: expected.version,
        platform: fragment.platform,
        url: expected.downloads[fragment.platform].mainlandUrl,
        fileName: decodeURIComponent(
          path.posix.basename(new URL(fragment.installer.url).pathname)
        ),
        verified: true
      }
    ])
  );
  expect(
    buildLatestManifest({
      version: expected.version,
      fragments,
      mirrors,
      existingLatest: null,
      now: new Date(expected.pub_date)
    })
  ).toEqual(expected);
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

// A desktop `downloads[platform]` record is the installer plus the
// self-update facts its fragment carries beside it.
function splitDesktopRelease({
  managedPayload,
  selfUpdatable,
  historyDatabaseUserVersion,
  nonManagedPayloadDigest,
  ...installer
}) {
  return {
    installer,
    selfUpdate: {
      managedPayload,
      selfUpdatable,
      historyDatabaseUserVersion,
      nonManagedPayloadDigest
    }
  };
}

const promoteOne = (data, store, platform, extra = {}) =>
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
  const read = async (key) => JSON.parse((await store.get(key)).bytes);
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
  // The published platform manifest freezes its mirror and refuses rollback.
  await expect(record(next, store, 'windows')).rejects.toThrow(
    /already published/
  );
  await expect(promoteOne(data, store, 'windows')).rejects.toThrow(/rollback/);
  // Re-running is a confirmation that writes nothing.
  const writes = store.writes.length;
  expect((await promoteOne(next, store, 'windows')).platform.version).toBe(
    '3.1.2'
  );
  expect(store.writes).toHaveLength(writes);
  // The other platform must ship the same commit, and then latest.json advances.
  await expect(upload(next, store, 'macos', 'b')).rejects.toThrow(
    /was uploaded from commit/
  );
  await upload(next, store, 'macos');
  await record(next, store, 'macos');
  const joined = await promoteOne(next, store, 'macos');
  expect(joined.latest.version).toBe('3.1.2');
  expect(await read('latest.json')).toMatchObject({
    version: '3.1.2',
    gitCommit: next.commits.a,
    downloads: {
      'windows-x86_64': { mainlandUrl: mirrorUrl('windows', '3.1.2') },
      'darwin-aarch64': { mainlandUrl: mirrorUrl('macos', '3.1.2') }
    }
  });
});

test('a mirror record cannot silently diverge from an address promoted during its probe', async () => {
  const data = fixture();
  const store = new MemoryStore();
  await stage(data, store);
  await promote(data, store);
  const next = fixture('3.1.2');
  await upload(next, store, 'windows');
  await record(next, store, 'windows');
  const reshared = 'https://mirror.example/windows-3.1.2-reshared';
  await expect(
    record(next, store, 'windows', {
      url: reshared,
      probeMirror: async (url) => {
        if (url === reshared) await promoteOne(next, store, 'windows');
        return sharePage(installerName('windows', next.version));
      }
    })
  ).rejects.toThrow(/has already published 3\.1\.2/);
  const published = JSON.parse(
    (await store.get('latest/windows-x86_64.json')).bytes
  );
  expect(published.downloads['windows-x86_64'].mainlandUrl).toBe(
    mirrorUrl('windows', next.version)
  );
  // Confirming the platform again reads the published address, not a record.
  store.objects.set('3.1.2/windows-x86_64/mirror/mainland.json', {
    bytes: Buffer.from(
      JSON.stringify({
        schemaVersion: 1,
        version: '3.1.2',
        platform: 'windows-x86_64',
        url: reshared,
        fileName: installerName('windows', next.version),
        verified: true
      })
    ),
    etag: '"stale"'
  });
  const writes = store.writes.length;
  expect((await promoteOne(next, store, 'windows')).platform.downloads).toEqual(
    published.downloads
  );
  expect(store.writes).toHaveLength(writes);
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
  expect(joined.latest.version).toBe('3.1.2');
  const again = await promoteOne(next, store, 'macos');
  expect(again.advanced).toBe(false);
  expect(again.latest.version).toBe('3.1.2');
});

// The Platform Release Manifest the last 5.x release left under the legacy
// origin, shaped like the live 5.7.0 one: no self-update facts.
function legacyPlatformManifest(key) {
  const release = `${LEGACY_RELEASE_BASE_URL}/5.7.0/${key}`;
  return {
    version: '5.7.0',
    gitCommit: '5'.repeat(40),
    notes: 'Release 5.7.0',
    pub_date: '2026-10-08T04:48:26.752Z',
    platforms: {
      [key]: {
        url: `${release}/updater/${key}-updater`,
        signature: `${key}-signature-5.7.0`
      }
    },
    downloads: {
      [key]: {
        url: `${release}/installer/${key}-installer`,
        size: 8872188,
        sha256: '9'.repeat(64),
        mainlandUrl: `https://mirror.example/${key}-5.7.0`
      }
    }
  };
}

test('the first 6.0.0 release promotes over the 5.x manifests the bucket migration copies', async () => {
  const store = new MemoryStore();
  const legacy = ['windows-x86_64', 'darwin-aarch64'].map(
    legacyPlatformManifest
  );
  const seed = (key, value) =>
    store.objects.set(key, {
      bytes: Buffer.from(JSON.stringify(value)),
      etag: `"${key}"`
    });
  for (const manifest of legacy)
    seed(`latest/${Object.keys(manifest.platforms)[0]}.json`, manifest);
  seed('latest.json', {
    ...legacy[0],
    platforms: { ...legacy[0].platforms, ...legacy[1].platforms },
    downloads: { ...legacy[0].downloads, ...legacy[1].downloads }
  });
  const data = fixture('6.0.0');
  expect((await upload(data, store, 'windows')).selfUpdatable).toBe(false);
  expect((await upload(data, store, 'macos')).selfUpdatable).toBe(false);
  await recordAll(data, store);
  const log = vi.fn();
  expect((await promoteOne(data, store, 'windows', { log })).advanced).toBe(
    false
  );
  expect(log).toHaveBeenCalledWith(
    expect.stringMatching(/platforms are at 6\.0\.0 and 5\.7\.0/)
  );
  const latest = await promote(data, store);
  expect(latest).toMatchObject({
    version: '6.0.0',
    downloads: {
      'windows-x86_64': {
        url: expect.stringMatching(`^${baseUrl}/6.0.0/`),
        mainlandUrl: mirrorUrl('windows', '6.0.0'),
        selfUpdatable: false
      },
      'darwin-aarch64': {
        url: expect.stringMatching(`^${baseUrl}/6.0.0/`),
        mainlandUrl: mirrorUrl('macos', '6.0.0'),
        selfUpdatable: false
      }
    }
  });
  // Only a pre-6.0.0 manifest may lack the self-update facts.
  expect(() =>
    validatePlatformManifest(
      { ...legacy[0], version: '6.0.0' },
      'windows-x86_64'
    )
  ).toThrow(/self-update facts for 6\.0\.0/);
});

test('the writer reproduces every shared platform fixture', () => {
  for (const key of ['windows-x86_64', 'darwin-aarch64']) {
    const expected = JSON.parse(
      fs.readFileSync(
        path.resolve(import.meta.dirname, `./fixtures/latest/${key}.json`),
        'utf8'
      )
    );
    const { mainlandUrl, ...release } = expected.downloads[key];
    const { installer, selfUpdate } = splitDesktopRelease(release);
    const fragment = {
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
    const mirror = {
      schemaVersion: 1,
      version: expected.version,
      platform: key,
      url: mainlandUrl,
      fileName: decodeURIComponent(
        path.posix.basename(new URL(installer.url).pathname)
      ),
      verified: true
    };
    expect(
      buildPlatformManifest({
        version: expected.version,
        fragment,
        mirror,
        now: new Date(expected.pub_date)
      })
    ).toEqual(expected);
  }
});

// Android (ADR 0010): its own Platform Release Manifest, outside latest.json,
// without a mainland mirror, under the same one-commit-per-version rule.
const ANDROID = 'android-aarch64';
const promoteAndroid = (data, store, extra = {}) =>
  promotePlatform({
    version: data.version,
    platform: 'android',
    baseUrl,
    store,
    ...extra
  });

test('Android promotion checks both artifacts and publishes only its own manifest', async () => {
  const data = fixture();
  const store = new MemoryStore();
  const { fragment, apkKey } = await uploadAndroid(store, {
    version: data.version,
    gitCommit: data.commits.a,
    baseUrl
  });
  for (const key of [apkKey, managedPayloadKey(data.version, ANDROID)]) {
    const object = store.objects.get(key);
    store.objects.delete(key);
    await expect(promoteAndroid(data, store)).rejects.toThrow(
      `Release artifact is missing or differs: ${key}`
    );
    store.objects.set(key, object);
  }
  const before = new Set(store.objects.keys());
  const now = new Date('2026-09-21T00:00:00.000Z');
  const result = await promoteAndroid(data, store, { now });
  expect(result).toEqual({
    platform: buildPlatformManifest({ version: data.version, fragment, now }),
    latest: null,
    advanced: false
  });
  expect([...store.objects.keys()].filter((key) => !before.has(key))).toEqual([
    `latest/${ANDROID}.json`
  ]);
  // No mirror record is read, and none can be waived.
  expect(store.writes.some((key) => key.includes('mirror'))).toBe(false);
  await expect(
    promoteAndroid(data, store, { withoutMainlandMirror: true })
  ).rejects.toThrow(/no mainland mirror/);
  // Repeating the promotion leaves the manifest unchanged.
  const published = store.objects.get(`latest/${ANDROID}.json`).bytes;
  await promoteAndroid(data, store);
  expect(store.objects.get(`latest/${ANDROID}.json`).bytes).toEqual(published);
});

test('Android promotion never moves latest.json, even after a lockstep release', async () => {
  const data = fixture();
  const store = new MemoryStore();
  await stage(data, store);
  await promote(data, store);
  const latest = store.objects.get('latest.json').bytes;
  const next = { ...data, version: '3.1.2' };
  await uploadAndroid(store, {
    version: next.version,
    gitCommit: data.commits.a,
    baseUrl
  });
  const result = await promoteAndroid(next, store);
  expect(result.latest).toBeNull();
  expect(store.objects.get('latest.json').bytes).toEqual(latest);
  expect(
    JSON.parse(store.objects.get(`latest/${ANDROID}.json`).bytes).version
  ).toBe('3.1.2');
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
  // Both versions point at the one APK that carries the 3.1.1 Mods.
  await uploadAndroid(store, {
    version: '3.1.2',
    gitCommit: data.commits.a,
    baseUrl,
    apkVersion: '3.1.1'
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
  const fragment = JSON.parse(store.objects.get(key).bytes);
  fragment.selfUpdatable = false;
  store.objects.get(key).bytes = Buffer.from(JSON.stringify(fragment));
  await expect(promoteAndroid(data, store)).rejects.toThrow(
    /Published 3.1.2 android-aarch64 downloads differs/
  );
});

test('an Android fragment names its APK by game, loader and non-managed payload digest, and the Mods it carries', () => {
  const facts = {
    version: '3.1.2',
    gitCommit: 'a'.repeat(40),
    baseUrl,
    apk: {
      size: 1,
      sha256: 'b'.repeat(64),
      versionCode: 12825,
      signerSha256: 'c'.repeat(64),
      builtForProductVersion: '3.1.1'
    },
    managedPayload: { size: 1, sha256: 'b'.repeat(64), signature: 'sig' },
    selfUpdatable: true,
    historyDatabaseUserVersion: 1,
    nonManagedPayloadDigest: 'd'.repeat(64),
    game: {
      gameVersion: '1.0.12825-production-android-d9601ded',
      versionCode: 12825,
      officialApkSha256: 'e'.repeat(64)
    },
    loader: { tag: 'v0.7.4-android.4', sha256: 'f'.repeat(64) },
    interopSha256: `0123abcd${'0'.repeat(56)}`
  };
  // A pure Mods update points at the APK an earlier version baked.
  const fragment = buildAndroidFragment(facts);
  expect(fragment.apk.url).toBe(
    `${baseUrl}/android/apk/BazaarPlusPlus_1.0.12825_lemon-v0.7.4-android.4_dddddddd.apk`
  );
  expect(fragment.managedPayload.url).toBe(
    `${baseUrl}/3.1.2/${ANDROID}/managed/BazaarPlusPlus_3.1.2_${ANDROID}-managed.zip`
  );
  expect(() =>
    buildAndroidFragment({
      ...facts,
      apk: { ...facts.apk, builtForProductVersion: '3.1.3' }
    })
  ).toThrow(/Invalid android-aarch64 APK/);
  expect(() =>
    validatePlatformFragment(
      {
        ...fragment,
        apk: {
          ...fragment.apk,
          url: `${baseUrl}/3.1.2/${ANDROID}/apk/BazaarPlusPlus.apk`
        }
      },
      '3.1.2',
      ANDROID
    )
  ).toThrow(/does not name its game, loader and non-managed payload digest/);
  expect(() =>
    validatePlatformFragment({ ...fragment, kind: 'tauri' }, '3.1.2', ANDROID)
  ).toThrow(/Invalid android-aarch64 platform fragment/);
  expect(() =>
    buildAndroidFragment({ ...facts, selfUpdatable: undefined })
  ).toThrow(/Invalid android-aarch64 release facts/);
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
    selfUpdatable: true
  });
  expect(fragment).toMatchObject({
    managedPayload: {
      url: `${baseUrl}/${managedPayloadKey('3.1.1', 'windows-x86_64')}`,
      signature: 'msig'
    },
    selfUpdatable: true,
    historyDatabaseUserVersion: 2,
    nonManagedPayloadDigest: 'e'.repeat(64)
  });
  const invalid = (change) => () =>
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

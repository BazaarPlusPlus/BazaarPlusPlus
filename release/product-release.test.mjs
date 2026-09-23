import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import crypto from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { afterEach, expect, test, vi } from 'vitest';
import {
  buildPlatformFragment,
  buildLatestManifest,
  buildPlatformManifest
} from './manifest.mjs';
import {
  uploadPlatform,
  recordMainlandMirror,
  promotePlatform,
  promoteRelease
} from './publish.mjs';
import { putImmutable } from './r2-store.mjs';

const baseUrl = 'https://bppinstaller.bazaarplusplus.com';
const sha256 = (bytes) =>
  crypto.createHash('sha256').update(bytes).digest('hex');
const roots = [];

test.each([
  ['artifact-manifest.mjs', 'generate', '--platform', 'macos'],
  ['native-recorder-input.mjs', 'ensure', '--platform', 'macos'],
  ['payload-zip.mjs', '--platform', 'macos']
])('legacy writer %s cannot bypass product orchestration', (...args) => {
  const [file, ...flags] = args;
  const result = spawnSync(
    process.execPath,
    [path.join(import.meta.dirname, file), ...flags],
    { encoding: 'utf8' }
  );
  expect(result.status).toBe(1);
  expect(result.stderr).toContain('release.mjs');
});
afterEach(() => {
  for (const root of roots.splice(0))
    fs.rmSync(root, { recursive: true, force: true });
});

class MemoryStore {
  objects = new Map();
  writes = [];
  generation = 0;
  beforePut;
  async get(key) {
    return this.objects.get(key) ?? null;
  }
  async head(key) {
    const object = this.objects.get(key);
    return object
      ? {
          size: object.bytes.length,
          sha256: sha256(object.bytes),
          etag: object.etag
        }
      : null;
  }
  async put(key, bytes, { ifMatch, ifNoneMatch } = {}) {
    if (this.beforePut) await this.beforePut(key, bytes);
    const current = this.objects.get(key);
    if ((ifNoneMatch && current) || (ifMatch && current?.etag !== ifMatch))
      return false;
    if (!ifNoneMatch && !ifMatch) throw new Error('unconditional write');
    this.objects.set(key, {
      bytes: Buffer.from(bytes),
      etag: `"${++this.generation}"`
    });
    this.writes.push(key);
    return true;
  }
}

function fixture(version = '3.1.1') {
  const workspaceRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-release-'));
  roots.push(workspaceRoot);
  fs.writeFileSync(path.join(workspaceRoot, 'VERSION'), version);
  return { workspaceRoot, version };
}

const platformKey = (platform) =>
  platform === 'windows' ? 'windows-x86_64' : 'darwin-aarch64';
const installerName = (platform, version) =>
  platform === 'windows'
    ? `BazaarPlusPlus_${version}_x64-setup.exe`
    : `BazaarPlusPlus_${version}_aarch64.dmg`;

async function upload(data, store, platform, commit = 'a'.repeat(40)) {
  const installer = path.join(
    data.workspaceRoot,
    installerName(platform, data.version)
  );
  const updater =
    platform === 'windows'
      ? installer
      : path.join(data.workspaceRoot, 'BazaarPlusPlus.app.tar.gz');
  const signature = `${updater}.sig`;
  fs.writeFileSync(installer, `${platform} installer`);
  if (updater !== installer) fs.writeFileSync(updater, 'macos updater');
  fs.writeFileSync(signature, `${platform}-signature`);
  const record = (file) => ({
    path: file,
    size: fs.statSync(file).size,
    sha256: sha256(fs.readFileSync(file))
  });
  const manifest = {
    appVersion: data.version,
    releasePlatformKey: platformKey(platform),
    buildPlatform: platform,
    gitCommit: commit,
    dirty: false,
    installer: record(installer),
    updater: record(updater),
    signature: { ...record(signature), content: `${platform}-signature` }
  };
  return uploadPlatform({
    ...data,
    platform,
    baseUrl,
    store,
    validateManifest: () => ({ manifest, installer, updater, signature })
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
      }
    }
  });
  expect(fragment.gitCommit).toBe('a'.repeat(40));
  expect(fragment.installer.url).toContain('/installer/renamed.dmg');
  expect(fragment.updater.signature).toBe('signature');
});

test('upload freezes all local files before any remote write', async () => {
  const data = fixture();
  const store = new MemoryStore();
  store.beforePut = async () => {
    fs.writeFileSync(
      path.join(data.workspaceRoot, 'BazaarPlusPlus.app.tar.gz'),
      'replacement build'
    );
    fs.writeFileSync(
      path.join(data.workspaceRoot, 'BazaarPlusPlus.app.tar.gz.sig'),
      'replacement signature'
    );
  };
  const fragment = await upload(data, store, 'macos');
  const key = new URL(fragment.updater.url).pathname.slice(1);
  expect((await store.get(key)).bytes.toString()).toBe('macos updater');
  expect((await store.get(`${key}.sig`)).bytes.toString()).toBe(
    'macos-signature'
  );
});

test('a product version cannot mix platform commits', async () => {
  const data = fixture();
  const store = new MemoryStore();
  const windows = await upload(data, store, 'windows');
  await expect(upload(data, store, 'macos', 'b'.repeat(40))).rejects.toThrow(
    /was uploaded from commit a{40} on windows-x86_64/
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
    const { mainlandUrl, ...installer } = expected.downloads[platform];
    return {
      schemaVersion: 1,
      version: expected.version,
      platform,
      gitCommit: expected.gitCommit,
      installer,
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

const promoteOne = (data, store, platform, extra = {}) =>
  promotePlatform({
    ...data,
    platform,
    baseUrl,
    store,
    platformManifestSince: '3.1.1',
    ...extra
  });

test('one platform can run ahead once a lockstep release carries the per-platform endpoint', async () => {
  const data = fixture();
  const store = new MemoryStore();
  await stage(data, store);
  const next = fixture('3.1.2');
  await upload(next, store, 'windows');
  await record(next, store, 'windows');
  await expect(promoteOne(next, store, 'windows')).rejects.toThrow(
    /requires a Release Manifest at 3\.1\.1 or newer/
  );
  expect(await store.get('latest/windows-x86_64.json')).toBeNull();
  await promote(data, store);
  await expect(
    promoteOne(next, store, 'windows', { platformManifestSince: '3.2.0' })
  ).rejects.toThrow(/requires a Release Manifest at 3\.2\.0 or newer/);
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
  await expect(upload(next, store, 'macos', 'b'.repeat(40))).rejects.toThrow(
    /was uploaded from commit/
  );
  await upload(next, store, 'macos');
  await record(next, store, 'macos');
  const joined = await promoteOne(next, store, 'macos');
  expect(joined.latest.version).toBe('3.1.2');
  expect(await read('latest.json')).toMatchObject({
    version: '3.1.2',
    gitCommit: 'a'.repeat(40),
    downloads: {
      'windows-x86_64': { mainlandUrl: mirrorUrl('windows', '3.1.2') },
      'darwin-aarch64': { mainlandUrl: mirrorUrl('macos', '3.1.2') }
    }
  });
});

test('the first release through the per-platform writer advances a lockstep manifest from the old flow', async () => {
  const store = new MemoryStore();
  store.objects.set('latest.json', {
    bytes: Buffer.from(
      JSON.stringify({
        version: '5.9.0',
        notes: 'Release 5.9.0',
        pub_date: '2026-09-12T20:33:58.483Z',
        platforms: {
          'windows-x86_64': {
            url: `${baseUrl}/5.9.0/windows-x86_64/updater/x.exe`,
            signature: 's'
          },
          'darwin-aarch64': {
            url: `${baseUrl}/5.9.0/darwin-aarch64/updater/x.tar.gz`,
            signature: 's'
          }
        }
      })
    ),
    etag: '"legacy"'
  });
  const data = fixture('6.0.0');
  await stage(data, store);
  const latest = await promote(data, store);
  expect(latest.version).toBe('6.0.0');
  expect(latest.gitCommit).toBe('a'.repeat(40));
  expect(
    JSON.parse((await store.get('latest/windows-x86_64.json')).bytes).version
  ).toBe('6.0.0');
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

test('the writer reproduces every shared platform fixture', () => {
  for (const key of ['windows-x86_64', 'darwin-aarch64']) {
    const expected = JSON.parse(
      fs.readFileSync(
        path.resolve(import.meta.dirname, `./fixtures/latest/${key}.json`),
        'utf8'
      )
    );
    const { mainlandUrl, ...installer } = expected.downloads[key];
    const fragment = {
      schemaVersion: 1,
      version: expected.version,
      platform: key,
      gitCommit: expected.gitCommit,
      installer,
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

test('confirming a published release restores a missing platform manifest from it', async () => {
  const data = fixture();
  const store = new MemoryStore();
  await stage(data, store);
  const latest = await promote(data, store);
  const removed = store.objects.get('latest/darwin-aarch64.json');
  store.objects.delete('latest/darwin-aarch64.json');
  const log = vi.fn();
  expect((await promote(data, store, { log })).version).toBe(data.version);
  expect(
    JSON.parse((await store.get('latest/darwin-aarch64.json')).bytes)
  ).toEqual(JSON.parse(removed.bytes));
  expect(log).toHaveBeenCalledWith(
    expect.stringMatching(
      /Restored latest\/darwin-aarch64\.json from latest\.json/
    )
  );
  expect(store.writes.at(-1)).toBe('latest/darwin-aarch64.json');
  expect(JSON.parse((await store.get('latest.json')).bytes)).toEqual(latest);
});

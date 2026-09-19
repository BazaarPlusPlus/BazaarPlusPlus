import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import crypto from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { afterEach, expect, test } from 'vitest';
import { buildPlatformFragment, buildLatestManifest } from './manifest.mjs';
import { uploadPlatform, promoteRelease } from './publish.mjs';
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

async function upload(data, store, platform, commit = 'a'.repeat(40)) {
  const platformKey =
    platform === 'windows' ? 'windows-x86_64' : 'darwin-aarch64';
  const installerName =
    platform === 'windows'
      ? `BazaarPlusPlus_${data.version}_x64-setup.exe`
      : `BazaarPlusPlus_${data.version}_aarch64.dmg`;
  const installer = path.join(data.workspaceRoot, installerName);
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
    releasePlatformKey: platformKey,
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

test('upload is platform-local; latest is promoted only after both verified platforms exist', async () => {
  const data = fixture();
  const store = new MemoryStore();
  await upload(data, store, 'macos');
  expect(await store.get('latest.json')).toBeNull();
  await expect(promoteRelease({ ...data, baseUrl, store })).rejects.toThrow(
    /Missing windows/
  );
  expect(await store.get('latest.json')).toBeNull();
  await upload(data, store, 'windows');
  const latest = await promoteRelease({ ...data, baseUrl, store });
  expect(Object.keys(latest.platforms)).toEqual([
    'windows-x86_64',
    'darwin-aarch64'
  ]);
  expect(latest.downloads['darwin-aarch64'].url).toContain(
    '/installer/BazaarPlusPlus_3.1.1_aarch64.dmg'
  );
  const writes = store.writes.length;
  await promoteRelease({ ...data, baseUrl, store });
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
  await upload(data, store, 'windows');
  await upload(data, store, 'macos', 'b'.repeat(40));
  await expect(promoteRelease({ ...data, baseUrl, store })).rejects.toThrow(
    /commits differ/
  );
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
  await upload(data, store, 'macos');
  await upload(data, store, 'windows');
  const assetKey = store.writes.find((key) => key.includes('/installer/'));
  const asset = store.objects.get(assetKey);
  store.objects.delete(assetKey);
  await expect(promoteRelease({ ...data, baseUrl, store })).rejects.toThrow(
    /artifact is missing/
  );
  store.objects.set(assetKey, asset);
  const key = '3.1.1/windows-x86_64/updater/platform-manifest.json';
  store.objects.get(key).bytes = Buffer.from('{broken');
  await expect(promoteRelease({ ...data, baseUrl, store })).rejects.toThrow(
    /Invalid JSON/
  );
  const get = store.get.bind(store);
  store.get = async (name) => {
    if (name === key) throw new Error('HTTP 503');
    return get(name);
  };
  await expect(promoteRelease({ ...data, baseUrl, store })).rejects.toThrow(
    /503/
  );
  expect(await store.get('latest.json')).toBeNull();
});

test('CAS prevents an older concurrent publisher from overwriting a newer release', async () => {
  const old = fixture('3.1.1');
  const newer = fixture('3.2.0');
  const store = new MemoryStore();
  await upload(old, store, 'macos');
  await upload(old, store, 'windows');
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
  await expect(promoteRelease({ ...old, baseUrl, store })).rejects.toThrow(
    /rollback/
  );
  expect(JSON.parse((await store.get('latest.json')).bytes).version).toBe(
    '3.2.0'
  );
});

test('a lost successful conditional PUT response is recognized by read-back', async () => {
  const data = fixture();
  const store = new MemoryStore();
  await upload(data, store, 'macos');
  await upload(data, store, 'windows');
  const put = store.put.bind(store);
  store.put = async (...args) => {
    const result = await put(...args);
    if (args[0] === 'latest.json') throw new Error('connection lost');
    return result;
  };
  expect((await promoteRelease({ ...data, baseUrl, store })).version).toBe(
    data.version
  );
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

test('manifest writer and website share the same complete release fixture', () => {
  const expected = JSON.parse(
    fs.readFileSync(
      path.resolve(import.meta.dirname, './fixtures/latest.json'),
      'utf8'
    )
  );
  const fragments = Object.keys(expected.platforms).map((platform) => ({
    schemaVersion: 1,
    version: expected.version,
    platform,
    gitCommit: expected.gitCommit,
    installer: expected.downloads[platform],
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
  }));
  expect(
    buildLatestManifest({
      version: expected.version,
      fragments,
      existingLatest: null,
      now: new Date(expected.pub_date)
    })
  ).toEqual(expected);
});

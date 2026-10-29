import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { afterEach, expect, test } from 'vitest';
import {
  parseAndroidManifest,
  parseUnityBundleVersion
} from './android-apk.mjs';
import {
  GAME_LIBS_LOCK_PATH,
  GAME_VERSION_PATTERN,
  INTEROP_KEY,
  LOADER_MANAGED_DIRECTORY,
  androidInputKey,
  assertPortableInteropArchive,
  captureInteropSnapshot,
  checkGameLibsLock,
  cliMain,
  fetchInteropSnapshot,
  fetchLockedAndroidInput,
  interopBlobKey,
  interopDirectoryDigest,
  officialApkChanged,
  lockKey,
  lockKeys,
  managedEntries,
  parseGameVersion,
  publishInteropSnapshot,
  readGameLibsLock,
  snapshotManifestKey,
  tarballEntries,
  validateGameLibsLock
} from './game-libs.mjs';
import { buildZipBuffer } from './payload-zip.mjs';
import { WORKSPACE_ROOT } from './product.mjs';

// The Android half of the Snapshot Lock (ADR 0008). Failure modes: an APK
// whose versionName is not an android production build, a first capture that
// names no toolchain, interop generated for another Unity version, a capture
// on a host that cannot run the Patcher without --interop, a stored input
// whose bytes differ from the lock, a local interop directory that drifted
// from its entry, and a stored interop archive that a Linux fetch would
// extract to other files than the lock records.

const ANDROID_VERSION = '1.0.12825-production-android-d9601ded';
const UNITY_VERSION = '6000.3.11f1';
const NOW = new Date('2026-10-22T12:00:00.000Z');
const sha = (bytes) => crypto.createHash('sha256').update(bytes).digest('hex');

const roots = [];
afterEach(() => {
  for (const root of roots.splice(0))
    fs.rmSync(root, { recursive: true, force: true });
});

function tempRoot(prefix) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), prefix));
  roots.push(root);
  return root;
}

function write(file, bytes) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, bytes);
}

// A compiled AndroidManifest.xml: XML chunk, UTF-16 string pool, and one
// <manifest> start element carrying versionCode (int) and versionName.
function binaryManifest({ versionName, versionCode }) {
  const strings = ['versionCode', 'versionName', 'manifest', versionName];
  const encoded = strings.map((value) => {
    const bytes = Buffer.alloc(2 + value.length * 2 + 2);
    bytes.writeUInt16LE(value.length, 0);
    bytes.write(value, 2, 'utf16le');
    return bytes;
  });
  const headerSize = 28;
  const header = Buffer.alloc(headerSize + strings.length * 4);
  let offset = 0;
  encoded.forEach((bytes, index) => {
    header.writeUInt32LE(offset, headerSize + index * 4);
    offset += bytes.length;
  });
  let data = Buffer.concat(encoded);
  data = Buffer.concat([data, Buffer.alloc((4 - (data.length % 4)) % 4)]);
  header.writeUInt16LE(0x0001, 0);
  header.writeUInt16LE(headerSize, 2);
  header.writeUInt32LE(header.length + data.length, 4);
  header.writeUInt32LE(strings.length, 8);
  header.writeUInt32LE(header.length, 20);
  const pool = Buffer.concat([header, data]);
  const element = Buffer.alloc(36 + 2 * 20);
  element.writeUInt16LE(0x0102, 0);
  element.writeUInt16LE(16, 2);
  element.writeUInt32LE(element.length, 4);
  element.writeUInt32LE(0xffffffff, 12);
  element.writeUInt32LE(0xffffffff, 16);
  element.writeUInt32LE(2, 20);
  element.writeUInt16LE(20, 24);
  element.writeUInt16LE(20, 26);
  element.writeUInt16LE(2, 28);
  const attribute = (index, name, raw, dataType, value) => {
    const at = 36 + index * 20;
    element.writeUInt32LE(0xffffffff, at);
    element.writeUInt32LE(name, at + 4);
    element.writeUInt32LE(raw, at + 8);
    element.writeUInt16LE(8, at + 12);
    element[at + 15] = dataType;
    element.writeUInt32LE(value, at + 16);
  };
  attribute(0, 0, 0xffffffff, 0x10, versionCode);
  attribute(1, 1, 3, 0x03, 3);
  const xml = Buffer.alloc(8);
  xml.writeUInt16LE(0x0003, 0);
  xml.writeUInt16LE(8, 2);
  xml.writeUInt32LE(8 + pool.length + element.length, 4);
  return Buffer.concat([xml, pool, element]);
}

function unityBundle(unityVersion) {
  const format = Buffer.alloc(4);
  format.writeUInt32BE(8);
  return Buffer.concat([
    Buffer.from('UnityFS\0', 'latin1'),
    format,
    Buffer.from(`5.x.x\0${unityVersion}\0`, 'latin1'),
    Buffer.alloc(64, 0x80)
  ]);
}

function fakeApk(
  file,
  { versionName = ANDROID_VERSION, il2cpp = 'il2cpp' } = {}
) {
  write(
    file,
    buildZipBuffer([
      {
        name: 'AndroidManifest.xml',
        data: binaryManifest({ versionName, versionCode: 12825 })
      },
      {
        name: 'assets/bin/Data/data.unity3d',
        data: unityBundle(UNITY_VERSION)
      },
      {
        name: 'assets/bin/Data/Managed/Metadata/global-metadata.dat',
        data: Buffer.from('metadata')
      },
      { name: 'lib/arm64-v8a/libil2cpp.so', data: Buffer.from(il2cpp) }
    ])
  );
  return file;
}

function interopDirectory(root, { unityVersion = UNITY_VERSION } = {}) {
  write(path.join(root, 'Assembly-CSharp.dll'), 'proxy');
  write(path.join(root, 'Il2CppBazaarGameShared.dll'), 'shared proxy');
  write(path.join(root, 'MethodAddressToToken.db'), 'db');
  write(
    path.join(root, 'interop-manifest.json'),
    JSON.stringify({
      unityVersion,
      tools: {
        cpp2IlVersion: '2022.1.0-pre-release.21',
        il2CppInteropVersion: '1.5.3.0'
      }
    })
  );
  return root;
}

function toolchain(root) {
  const loader = path.join(root, 'LemonLoader-Android-arm64.zip');
  write(
    loader,
    buildZipBuffer([
      {
        name: 'lemonloader-release.json',
        data: Buffer.from(JSON.stringify({ managedRuntimeVersion: '11.0.0' }))
      },
      {
        name: 'assets/LemonLoader/runtime/loader/net6/MelonLoader.dll',
        data: Buffer.from('melon')
      }
    ])
  );
  const patcherSource = path.join(root, 'patcher-source');
  write(path.join(patcherSource, 'LemonLoader.Patcher.CLI'), 'patcher');
  const patcher = path.join(root, 'LemonLoader.Patcher.CLI-linux-x64.tar.gz');
  execFileSync('tar', [
    '-czf',
    patcher,
    '-C',
    patcherSource,
    'LemonLoader.Patcher.CLI'
  ]);
  const cpp2il = path.join(root, 'Cpp2IL-2022.1.0-pre-release.21-Linux');
  write(cpp2il, 'cpp2il');
  return { loader, patcher, cpp2il };
}

function nugetPackage(root) {
  write(
    path.join(
      root,
      'sourcegear.sqlite3',
      '3.53.4',
      'runtimes',
      'android-arm64',
      'native',
      'libe_sqlite3.so'
    ),
    'sqlite'
  );
  return root;
}

function workspace() {
  const workspaceRoot = tempRoot('bpp-interop-ws-');
  write(
    path.join(workspaceRoot, GAME_LIBS_LOCK_PATH),
    `${JSON.stringify(
      {
        schemaVersion: 1,
        staleAfterDays: 30,
        entries: Object.fromEntries(lockKeys().map((key) => [key, null]))
      },
      null,
      2
    )}\n`
  );
  return {
    workspaceRoot,
    modRoot: path.join(workspaceRoot, 'bazaarplusplus-mod'),
    entry: () => readGameLibsLock(workspaceRoot).entries[INTEROP_KEY]
  };
}

function memoryStore() {
  const objects = new Map();
  return {
    objects,
    async head(key) {
      return objects.has(key)
        ? { size: objects.get(key).length, etag: `"${key}"` }
        : null;
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
    },
    async putFile(key, file, { sha256, ifMatch }) {
      const bytes = fs.readFileSync(file);
      if (sha(bytes) !== sha256) throw new Error('payload hash mismatch');
      if (ifMatch ? ifMatch !== `"${key}"` : objects.has(key)) return false;
      objects.set(key, bytes);
      return true;
    },
    async getFile(key, file) {
      if (!objects.has(key)) return null;
      fs.writeFileSync(file, objects.get(key));
      return { size: objects.get(key).length };
    }
  };
}

const noGeneration = () => {
  throw new Error('generation must not run');
};

function firstCapture(ws, root, overrides = {}) {
  const tools = toolchain(root);
  return captureInteropSnapshot({
    workspaceRoot: ws.workspaceRoot,
    modRoot: ws.modRoot,
    apk: fakeApk(path.join(root, 'TheBazaar.apk')),
    interop: interopDirectory(path.join(root, 'interop')),
    loader: tools.loader,
    loaderTag: 'v0.7.4-android.4',
    patcher: tools.patcher,
    patcherTag: 'v2.1.0',
    cpp2il: tools.cpp2il,
    nativePackages: ['SourceGear.sqlite3@3.53.4'],
    nugetRoot: nugetPackage(path.join(root, 'nuget')),
    storeFactory: () => {
      throw new Error('a first capture with every file needs no store');
    },
    generate: noGeneration,
    now: NOW,
    ...overrides
  }).then((result) => ({ ...result, tools }));
}

test('android versions parse to android-online and match the MSBuild pattern', () => {
  expect(parseGameVersion(ANDROID_VERSION)).toEqual({
    platform: 'android',
    channel: 'online'
  });
  expect(parseGameVersion('1.0.12651-prod-macos-arm64-d5cc730f')).toEqual({
    platform: 'macos',
    channel: 'online'
  });
  expect(() => parseGameVersion('1.0.12825-production-ios-d9601ded')).toThrow(
    /Application.version/
  );
  // Desktop keeps exactly one architecture token; Android has none.
  expect(() => parseGameVersion('1.0.12651-prod-macos-d5cc730f')).toThrow(
    /Application.version/
  );
  expect(() =>
    parseGameVersion('1.0.12825-production-android-arm64-d9601ded')
  ).toThrow(/Application.version/);
  expect(lockKeys().at(-1)).toBe('android-online');
  expect(() => lockKey('android', 'staging')).toThrow(
    /Unknown android snapshot channel 'staging'; expected online/
  );
  // build/ManagedPath.props mirrors the pattern; the two must stay equal.
  expect(
    execFileSync(
      'dotnet',
      [
        'msbuild',
        path.join(WORKSPACE_ROOT, 'bazaarplusplus-mod/build/ManagedPath.props'),
        '-nologo',
        '-getProperty:BppGameVersionPattern'
      ],
      { encoding: 'utf8' }
    ).trim()
  ).toBe(GAME_VERSION_PATTERN.source);
});

test('the APK readers take versionName, versionCode and the Unity version from their binary formats', () => {
  expect(
    parseAndroidManifest(
      binaryManifest({ versionName: ANDROID_VERSION, versionCode: 12825 })
    )
  ).toEqual({ versionName: ANDROID_VERSION, versionCode: 12825 });
  expect(() => parseAndroidManifest(Buffer.from('<manifest/>'))).toThrow(
    /not a compiled binary XML/
  );
  expect(parseUnityBundleVersion(unityBundle(UNITY_VERSION))).toBe(
    UNITY_VERSION
  );
  expect(() => parseUnityBundleVersion(Buffer.from('UnityWeb\0'))).toThrow(
    /not a UnityFS bundle/
  );
});

test('a first capture records the APK, the interop digest and the named toolchain', async () => {
  const ws = workspace();
  const root = tempRoot('bpp-interop-in-');
  const result = await firstCapture(ws, root);
  const interop = path.join(result.directory, 'interop');
  expect(result.directory).toBe(
    path.join(ws.modRoot, 'game-libs', `android-online-${ANDROID_VERSION}`)
  );
  expect(ws.entry()).toEqual({
    gameVersion: ANDROID_VERSION,
    versionCode: 12825,
    apkSha256: sha(fs.readFileSync(path.join(root, 'TheBazaar.apk'))),
    il2cppSha256: sha('il2cpp'),
    metadataSha256: sha('metadata'),
    unityVersion: UNITY_VERSION,
    interopSha256: interopDirectoryDigest(interop),
    loader: {
      tag: 'v0.7.4-android.4',
      sha256: sha(fs.readFileSync(result.tools.loader)),
      managedRuntimeVersion: '11.0.0'
    },
    patcher: {
      tag: 'v2.1.0',
      sha256: sha(fs.readFileSync(result.tools.patcher))
    },
    cpp2ilVersion: '2022.1.0-pre-release.21',
    cpp2ilSha256: sha('cpp2il'),
    il2cppInteropVersion: '1.5.3.0',
    nativeLibs: {
      'libe_sqlite3.so': {
        package: 'SourceGear.sqlite3',
        version: '3.53.4',
        sha256: sha('sqlite')
      }
    },
    capturedAt: NOW.toISOString()
  });
  // The digest covers the generator's databases and manifest, not only DLLs.
  expect(fs.readdirSync(interop).sort()).toEqual([
    'Assembly-CSharp.dll',
    'Il2CppBazaarGameShared.dll',
    'MethodAddressToToken.db',
    'interop-manifest.json'
  ]);
  expect(
    fs.existsSync(
      path.join(
        result.loaderPath,
        'assets/LemonLoader/runtime/loader/net6/MelonLoader.dll'
      )
    )
  ).toBe(true);
  // A desktop build never compiles against the Android entry.
  expect(managedEntries(readGameLibsLock(ws.workspaceRoot))).toEqual([]);
  expect(
    checkGameLibsLock({ workspaceRoot: ws.workspaceRoot, now: NOW }).interop
  ).toEqual({ gameVersion: ANDROID_VERSION });
  write(path.join(interop, 'Assembly-CSharp.dll'), 'tampered');
  expect(() =>
    checkGameLibsLock({ workspaceRoot: ws.workspaceRoot, now: NOW })
  ).toThrow(/hashes to .*, but the lock entry records/);
});

test('a capture refuses a desktop APK, a missing toolchain, foreign interop and generation off Linux x64', async () => {
  const ws = workspace();
  const root = tempRoot('bpp-interop-in-');
  const tools = toolchain(root);
  const base = {
    workspaceRoot: ws.workspaceRoot,
    modRoot: ws.modRoot,
    apk: fakeApk(path.join(root, 'TheBazaar.apk')),
    interop: interopDirectory(path.join(root, 'interop')),
    storeFactory: () => {
      throw new Error('no credentials');
    },
    generate: noGeneration
  };
  await expect(
    captureInteropSnapshot({
      ...base,
      apk: fakeApk(path.join(root, 'desktop.apk'), {
        versionName: '1.0.12651-prod-macos-arm64-d5cc730f'
      })
    })
  ).rejects.toThrow(/a macos-online build, not an android-online APK/);
  await expect(captureInteropSnapshot(base)).rejects.toThrow(
    /lock entry is empty, so the first capture names the toolchain: pass --loader/
  );
  const named = {
    ...base,
    loader: tools.loader,
    loaderTag: 'v0.7.4-android.4',
    patcher: tools.patcher,
    patcherTag: 'v2.1.0',
    cpp2il: tools.cpp2il
  };
  await expect(
    captureInteropSnapshot({ ...named, loaderTag: '' })
  ).rejects.toThrow(/--loader needs --loader-tag/);
  await expect(
    captureInteropSnapshot({
      ...named,
      interop: interopDirectory(path.join(root, 'old-unity'), {
        unityVersion: '2022.3.40f1'
      })
    })
  ).rejects.toThrow(/generated for Unity 2022\.3\.40f1/);
  if (process.platform !== 'linux' || process.arch !== 'x64')
    await expect(
      captureInteropSnapshot({ ...named, interop: '', generate: undefined })
    ).rejects.toThrow(/pass --interop <dir>/);
  expect(ws.entry()).toBeNull();
});

test('publish stores every pinned input by content; fetch restores interop and loader from the store', async () => {
  const ws = workspace();
  const root = tempRoot('bpp-interop-in-');
  const { tools } = await firstCapture(ws, root);
  const entry = ws.entry();
  const store = memoryStore();
  const logs = [];
  const published = await publishInteropSnapshot({
    workspaceRoot: ws.workspaceRoot,
    modRoot: ws.modRoot,
    store,
    log: (line) => logs.push(line)
  });
  const keys = [
    interopBlobKey(entry.interopSha256),
    androidInputKey('apk', entry.apkSha256),
    androidInputKey('loader', entry.loader.sha256),
    androidInputKey('patcher', entry.patcher.sha256),
    androidInputKey('cpp2il', entry.cpp2ilSha256),
    snapshotManifestKey('android', 'online', ANDROID_VERSION)
  ];
  expect([...store.objects.keys()].sort()).toEqual([...keys].sort());
  expect(published.manifestKey).toBe(
    `game-libs/android-online-${ANDROID_VERSION}.json`
  );
  expect(androidInputKey('apk', entry.apkSha256)).toBe(
    `game-libs/apk/${entry.apkSha256}.apk`
  );
  expect(
    store.objects.get(androidInputKey('loader', entry.loader.sha256))
  ).toEqual(fs.readFileSync(tools.loader));
  // Republishing reads nothing it already stored.
  logs.length = 0;
  await publishInteropSnapshot({
    workspaceRoot: ws.workspaceRoot,
    modRoot: ws.modRoot,
    store,
    log: (line) => logs.push(line)
  });
  expect(logs.filter((line) => line.startsWith('Already stored'))).toHaveLength(
    5
  );
  expect(logs).toContain(
    `Already stored and verified: ${interopBlobKey(entry.interopSha256)}`
  );

  // A fresh clone: nothing under game-libs/ but the lock.
  fs.rmSync(path.join(ws.modRoot, 'game-libs'), { recursive: true });
  const fetched = await fetchInteropSnapshot({
    workspaceRoot: ws.workspaceRoot,
    modRoot: ws.modRoot,
    storeFactory: () => store
  });
  expect(fetched.source).toBe('store');
  expect(interopDirectoryDigest(fetched.interopPath)).toBe(entry.interopSha256);
  expect(fetched.loaderPath).toBe(
    path.join(ws.modRoot, 'game-libs', 'loader-v0.7.4-android.4')
  );
  // Satisfied locally, a second fetch needs no store.
  expect(
    (
      await fetchInteropSnapshot({
        workspaceRoot: ws.workspaceRoot,
        modRoot: ws.modRoot,
        storeFactory: () => {
          throw new Error('no credentials');
        }
      })
    ).source
  ).toBe('snapshot');
  const apk = await fetchLockedAndroidInput({
    workspaceRoot: ws.workspaceRoot,
    modRoot: ws.modRoot,
    kind: 'apk',
    storeFactory: () => store
  });
  expect(sha(fs.readFileSync(apk))).toBe(entry.apkSha256);

  // A stored input whose bytes differ from the lock is refused.
  store.objects.set(
    androidInputKey('cpp2il', entry.cpp2ilSha256),
    Buffer.from('swapped')
  );
  await expect(
    fetchLockedAndroidInput({
      workspaceRoot: ws.workspaceRoot,
      modRoot: ws.modRoot,
      kind: 'cpp2il',
      storeFactory: () => store
    })
  ).rejects.toThrow(/hashes to .*, not the .* the lock records/);
});

test('publish replaces a stored interop archive that would extract to other files on Linux; fetch refuses one', async () => {
  const ws = workspace();
  const root = tempRoot('bpp-interop-in-');
  await firstCapture(ws, root);
  const entry = ws.entry();
  const key = interopBlobKey(entry.interopSha256);
  // What macOS tar packs without COPYFILE_DISABLE: an AppleDouble sidecar
  // per file, which macOS tar hides on listing and extraction but GNU tar
  // extracts beside the proxies. A regular file by that name stands in.
  const stale = tempRoot('bpp-interop-stale-');
  const copy = path.join(stale, 'interop');
  fs.cpSync(path.join(root, 'interop'), copy, { recursive: true });
  write(path.join(copy, '._Assembly-CSharp.dll'), 'AppleDouble');
  const archive = path.join(stale, 'interop.tar.gz');
  execFileSync('tar', ['-czf', archive, '-C', stale, 'interop'], {
    env: { ...process.env, COPYFILE_DISABLE: '1' }
  });
  expect(tarballEntries(archive)).toContain('interop/._Assembly-CSharp.dll');
  expect(() => assertPortableInteropArchive(archive, 'Stale')).toThrow(
    /Stale carries the member interop\/\._Assembly-CSharp\.dll/
  );

  const store = memoryStore();
  store.objects.set(key, fs.readFileSync(archive));
  const logs = [];
  await publishInteropSnapshot({
    workspaceRoot: ws.workspaceRoot,
    modRoot: ws.modRoot,
    store,
    log: (line) => logs.push(line)
  });
  expect(logs.find((line) => line.startsWith(`Replacing ${key}: `))).toMatch(
    /carries the member interop\/\._Assembly-CSharp\.dll/
  );
  expect(logs).toContain(`Replaced ${key}`);
  const replaced = path.join(stale, 'replaced.tar.gz');
  fs.writeFileSync(replaced, store.objects.get(key));
  expect(tarballEntries(replaced).sort()).toEqual([
    'interop/',
    'interop/Assembly-CSharp.dll',
    'interop/Il2CppBazaarGameShared.dll',
    'interop/MethodAddressToToken.db',
    'interop/interop-manifest.json'
  ]);

  // A second publish reads the archive back and keeps it.
  logs.length = 0;
  await publishInteropSnapshot({
    workspaceRoot: ws.workspaceRoot,
    modRoot: ws.modRoot,
    store,
    log: (line) => logs.push(line)
  });
  expect(logs).toContain(`Already stored and verified: ${key}`);
  expect(fs.readFileSync(replaced).equals(store.objects.get(key))).toBe(true);

  // A fetch never extracts such an archive, whatever the host's tar does.
  store.objects.set(key, fs.readFileSync(archive));
  fs.rmSync(path.join(ws.modRoot, 'game-libs'), { recursive: true });
  await expect(
    fetchInteropSnapshot({
      workspaceRoot: ws.workspaceRoot,
      modRoot: ws.modRoot,
      storeFactory: () => store
    })
  ).rejects.toThrow(
    /Stored snapshot .* carries the member interop\/\._Assembly-CSharp\.dll/
  );
});

test('a recapture reuses the pinned toolchain from the store and generates interop with it', async () => {
  const ws = workspace();
  const root = tempRoot('bpp-interop-in-');
  await firstCapture(ws, root);
  const first = ws.entry();
  const store = memoryStore();
  await publishInteropSnapshot({
    workspaceRoot: ws.workspaceRoot,
    modRoot: ws.modRoot,
    store
  });
  fs.rmSync(path.join(ws.modRoot, 'game-libs'), { recursive: true });
  const nextVersion = '1.0.12900-production-android-0badc0de';
  const calls = [];
  await captureInteropSnapshot({
    workspaceRoot: ws.workspaceRoot,
    modRoot: ws.modRoot,
    apk: fakeApk(path.join(root, 'next.apk'), {
      versionName: nextVersion,
      il2cpp: 'il2cpp next'
    }),
    storeFactory: () => store,
    generate: (options) => {
      calls.push(options);
      interopDirectory(options.output);
      write(path.join(options.output, 'Il2CppNext.dll'), 'next proxy');
    },
    now: NOW
  });
  expect(calls).toHaveLength(1);
  expect(sha(fs.readFileSync(calls[0].patcher))).toBe(first.patcher.sha256);
  expect(sha(fs.readFileSync(calls[0].cpp2il))).toBe(first.cpp2ilSha256);
  expect(calls[0].unityVersion).toBe(UNITY_VERSION);
  const next = ws.entry();
  expect(next.gameVersion).toBe(nextVersion);
  expect(next.interopSha256).not.toBe(first.interopSha256);
  expect(next.loader).toEqual(first.loader);
  expect(next.patcher).toEqual(first.patcher);
  expect(next.nativeLibs).toEqual(first.nativeLibs);
});

test('lock validation holds the android-online entry to its schema and field order', () => {
  const ws = workspace();
  const entry = {
    gameVersion: ANDROID_VERSION,
    versionCode: 12825,
    apkSha256: 'a'.repeat(64),
    il2cppSha256: 'b'.repeat(64),
    metadataSha256: 'c'.repeat(64),
    unityVersion: UNITY_VERSION,
    interopSha256: 'd'.repeat(64),
    loader: {
      tag: 'v0.7.4-android.4',
      sha256: 'e'.repeat(64),
      managedRuntimeVersion: '11.0.0'
    },
    patcher: { tag: 'v2.1.0', sha256: 'f'.repeat(64) },
    cpp2ilVersion: '2022.1.0-pre-release.21',
    cpp2ilSha256: '1'.repeat(64),
    il2cppInteropVersion: '1.5.3.0',
    nativeLibs: {},
    capturedAt: NOW.toISOString()
  };
  const lock = (value) => {
    const parsed = readGameLibsLock(ws.workspaceRoot);
    parsed.entries[INTEROP_KEY] = value;
    return parsed;
  };
  expect(validateGameLibsLock(lock(entry))).toBeTruthy();
  const { gameVersion, ...rest } = entry;
  const cases = [
    [{ ...rest, gameVersion }, /in the order gameVersion, versionCode/],
    [{ ...entry, sha256: 'a'.repeat(64) }, /exactly the fields/],
    [
      { ...entry, gameVersion: '1.0.12651-prod-macos-arm64-d5cc730f' },
      /is a macos-online build/
    ],
    [{ ...entry, versionCode: '12825' }, /versionCode/],
    [{ ...entry, apkSha256: 'xyz' }, /invalid apkSha256/],
    [{ ...entry, unityVersion: '6000.3' }, /unityVersion/],
    [
      { ...entry, loader: { tag: 'v0.7.4', sha256: 'e'.repeat(64) } },
      /loader must have exactly the fields tag, sha256, managedRuntimeVersion/
    ],
    [
      { ...entry, patcher: { tag: '../x', sha256: 'f'.repeat(64) } },
      /patcher\.tag must be a version or tag token/
    ],
    [
      { ...entry, nativeLibs: { 'sqlite.dll': {} } },
      /nativeLibs key "sqlite\.dll"/
    ],
    [{ ...entry, capturedAt: '2026-10-22' }, /capturedAt/]
  ];
  for (const [value, message] of cases)
    expect(
      () => validateGameLibsLock(lock(value)),
      JSON.stringify(value)
    ).toThrow(message);
});

test('InteropPath.props resolves the fetched interop and loader directories from the lock', async () => {
  const ws = workspace();
  const root = tempRoot('bpp-interop-in-');
  const props = path.join(ws.modRoot, 'build', 'InteropPath.props');
  fs.copyFileSync(
    path.join(WORKSPACE_ROOT, 'bazaarplusplus-mod/build/InteropPath.props'),
    props
  );
  const evaluate = () =>
    JSON.parse(
      execFileSync(
        'dotnet',
        [
          'msbuild',
          props,
          '-nologo',
          '-getProperty:InteropPath',
          '-getProperty:LemonLoaderPath',
          '-getProperty:LemonLoaderManagedPath',
          '-getProperty:BppInteropGameVersion'
        ],
        { encoding: 'utf8' }
      )
    ).Properties;
  expect(evaluate()).toMatchObject({
    InteropPath: '',
    LemonLoaderPath: '',
    BppInteropGameVersion: ''
  });
  const { directory, loaderPath } = await firstCapture(ws, root);
  expect(evaluate()).toEqual({
    InteropPath: `${path.join(directory, 'interop')}/`,
    LemonLoaderPath: `${loaderPath}/`,
    LemonLoaderManagedPath: `${loaderPath}/${LOADER_MANAGED_DIRECTORY}/`,
    BppInteropGameVersion: ANDROID_VERSION
  });
}, 60000);

test('the CLI fetches android online by printing the interop directory', async () => {
  const ws = workspace();
  const root = tempRoot('bpp-interop-in-');
  const { directory } = await firstCapture(ws, root);
  const out = [];
  const err = [];
  const stdout = process.stdout.write.bind(process.stdout);
  const stderr = process.stderr.write.bind(process.stderr);
  process.stdout.write = (chunk) => (out.push(String(chunk)), true);
  process.stderr.write = (chunk) => (err.push(String(chunk)), true);
  try {
    expect(
      await cliMain(['fetch', '--platform', 'android', '--channel', 'online'], {
        workspaceRoot: ws.workspaceRoot
      })
    ).toBe(0);
    expect(
      await cliMain(['fetch', '--platform', 'android', '--channel', 'ptr'], {
        workspaceRoot: ws.workspaceRoot
      })
    ).toBe(1);
    expect(
      await cliMain(['entries'], { workspaceRoot: ws.workspaceRoot })
    ).toBe(0);
  } finally {
    process.stdout.write = stdout;
    process.stderr.write = stderr;
  }
  expect(out).toEqual([`${path.join(directory, 'interop')}\n`]);
  expect(err.join('')).toMatch(/Loader Release .*loader-v0\.7\.4-android\.4/);
  expect(err.join('')).toMatch(/Unknown android snapshot channel 'ptr'/);
});

test('the official APK reads as changed only when its length differs from the locked one', async () => {
  const root = tempRoot('bpp-official-apk-');
  const ws = workspace();
  await firstCapture(ws, root);
  const store = memoryStore();
  const apkKey = androidInputKey('apk', ws.entry().apkSha256);
  const head =
    (length, ok = true) =>
    async (url, init) => {
      expect(init.method).toBe('HEAD');
      return {
        ok,
        status: ok ? 200 : 503,
        headers: new Headers(
          length === null ? {} : { 'content-length': `${length}` }
        )
      };
    };
  const check = (fetchImpl) =>
    officialApkChanged({
      workspaceRoot: ws.workspaceRoot,
      url: 'https://cdn.example/TheBazaar.apk',
      storeFactory: () => store,
      fetchImpl
    });

  await expect(check(head(10))).rejects.toThrow(
    `The locked APK ${apkKey} is not in the store`
  );
  store.objects.set(apkKey, Buffer.alloc(10));
  expect(await check(head(10))).toMatchObject({
    changed: false,
    officialSize: 10,
    lockedSize: 10
  });
  expect((await check(head(11))).changed).toBe(true);
  // A publisher that cannot answer never reads as unchanged.
  await expect(check(head(10, false))).rejects.toThrow(/answered 503/);
  await expect(check(head(null))).rejects.toThrow(/no Content-Length/);
});

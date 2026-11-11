import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { afterEach, expect, test } from 'vite-plus/test';
import {
  parseAndroidManifest,
  parseUnityBundleVersion
} from './android-apk.ts';
import type { AndroidManifestVersion } from './android-apk.ts';
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
  recordApkEtag,
  lockKey,
  lockKeys,
  managedEntries,
  parseGameVersion,
  publishInteropSnapshot,
  readGameLibsLock,
  snapshotManifestKey,
  tarballEntries,
  validateGameLibsLock
} from './game-libs.ts';
import type {
  ApkHeadFetch,
  CaptureInteropSnapshotOptions,
  CliOptions,
  GenerateInteropOptions
} from './game-libs.ts';
import { buildZipBuffer } from './payload-zip.ts';
import { WORKSPACE_ROOT } from './product.ts';
import type { PutFileOptions, PutOptions } from './r2-store.ts';
import { MemoryStore } from './test-support/memory-store.ts';

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
const sha = (bytes: string | NodeJS.ArrayBufferView) =>
  crypto.createHash('sha256').update(bytes).digest('hex');

const roots: string[] = [];
afterEach(() => {
  for (const root of roots.splice(0))
    fs.rmSync(root, { recursive: true, force: true });
});

function tempRoot(prefix: string): string {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), prefix));
  roots.push(root);
  return root;
}

function write(file: string, bytes: string | Buffer): void {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, bytes);
}

// A compiled AndroidManifest.xml: XML chunk, UTF-16 string pool, and one
// <manifest> start element carrying versionCode (int) and versionName.
function binaryManifest({ versionName, versionCode }: AndroidManifestVersion) {
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
  const attribute = (
    index: number,
    name: number,
    raw: number,
    dataType: number,
    value: number
  ) => {
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

function unityBundle(unityVersion: string) {
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
  file: string,
  {
    versionName = ANDROID_VERSION,
    il2cpp = 'il2cpp'
  }: { versionName?: string; il2cpp?: string } = {}
): string {
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

function interopDirectory(
  root: string,
  { unityVersion = UNITY_VERSION }: { unityVersion?: string } = {}
): string {
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

function toolchain(root: string) {
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

function nugetPackage(root: string): string {
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
  const objects = new Map<string, Buffer>();
  return {
    objects,
    async head(key: string) {
      return objects.has(key)
        ? { size: objects.get(key)!.length, sha256: null, etag: `"${key}"` }
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
    },
    async putFile(
      key: string,
      file: string,
      { sha256, ifMatch }: PutFileOptions
    ) {
      const bytes = fs.readFileSync(file);
      if (sha(bytes) !== sha256) throw new Error('payload hash mismatch');
      if (ifMatch ? ifMatch !== `"${key}"` : objects.has(key)) return false;
      objects.set(key, bytes);
      return true;
    },
    async getFile(key: string, file: string) {
      if (!objects.has(key)) return null;
      fs.writeFileSync(file, objects.get(key)!);
      return { size: objects.get(key)!.length };
    }
  };
}

const noGeneration = () => {
  throw new Error('generation must not run');
};

function firstCapture(
  ws: ReturnType<typeof workspace>,
  root: string,
  overrides: Partial<CaptureInteropSnapshotOptions> = {}
) {
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

test('android versions parse to android-online', () => {
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
});

test('build/ManagedPath.props mirrors the game version pattern', () => {
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
}, 30000);

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
    apkEtag: null,
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
  const entry = ws.entry()!;
  const store = memoryStore();
  const logs: string[] = [];
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
  const entry = ws.entry()!;
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
  const logs: string[] = [];
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
  fs.writeFileSync(replaced, store.objects.get(key)!);
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
  expect(fs.readFileSync(replaced).equals(store.objects.get(key)!)).toBe(true);

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
  const first = ws.entry()!;
  const store = memoryStore();
  await publishInteropSnapshot({
    workspaceRoot: ws.workspaceRoot,
    modRoot: ws.modRoot,
    store
  });
  fs.rmSync(path.join(ws.modRoot, 'game-libs'), { recursive: true });
  const nextVersion = '1.0.12900-production-android-0badc0de';
  const calls: GenerateInteropOptions[] = [];
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
  const next = ws.entry()!;
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
    apkEtag: '72439fcc9794ac65abd8649b18aa59ac',
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
  const lock = (value: object) => {
    const parsed = readGameLibsLock(ws.workspaceRoot);
    (parsed.entries as Record<string, unknown>)[INTEROP_KEY] = value;
    return parsed;
  };
  expect(validateGameLibsLock(lock(entry))).toBeTruthy();
  const { gameVersion, ...rest } = entry;
  const cases: [object, RegExp][] = [
    [{ ...rest, gameVersion }, /in the order gameVersion, versionCode/],
    [{ ...entry, sha256: 'a'.repeat(64) }, /exactly the fields/],
    [
      { ...entry, gameVersion: '1.0.12651-prod-macos-arm64-d5cc730f' },
      /is a macos-online build/
    ],
    [{ ...entry, versionCode: '12825' }, /versionCode/],
    [{ ...entry, apkSha256: 'xyz' }, /invalid apkSha256/],
    [
      { ...entry, apkEtag: '"quoted"' },
      /apkEtag must be null or a strong ETag/
    ],
    [{ ...entry, apkEtag: '' }, /apkEtag must be null or a strong ETag/],
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
  const out: string[] = [];
  const err: string[] = [];
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

test('the official APK reads as changed when its length or its recorded ETag differs', async () => {
  const root = tempRoot('bpp-official-apk-');
  const ws = workspace();
  const { apk } = await firstCapture(ws, root).then((result) => ({
    ...result,
    apk: path.join(root, 'TheBazaar.apk')
  }));
  const store = memoryStore();
  const apkKey = androidInputKey('apk', ws.entry()!.apkSha256);
  const head =
    (
      length: number | null,
      etag: string | null = '"v1"',
      ok = true
    ): ApkHeadFetch =>
    async (_url, init) => {
      expect(init.method).toBe('HEAD');
      const headers = new Headers();
      if (length !== null) headers.set('content-length', `${length}`);
      if (etag !== null) headers.set('etag', etag);
      return { ok, status: ok ? 200 : 503, headers };
    };
  const check = (fetchImpl: ApkHeadFetch) =>
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
  // A manual capture recorded no ETag: sizes alone decide.
  expect(await check(head(10, '"v2"'))).toMatchObject({
    changed: false,
    officialSize: 10,
    officialEtag: 'v2',
    lockedEtag: null
  });
  expect((await check(head(11))).changed).toBe(true);

  // Recording needs the locked bytes, so another file never takes its place.
  await expect(
    recordApkEtag({
      workspaceRoot: ws.workspaceRoot,
      apk: toolchain(root).loader,
      etag: 'v1'
    })
  ).rejects.toThrow(/not the locked APK/);
  await recordApkEtag({ workspaceRoot: ws.workspaceRoot, apk, etag: 'v1' });
  expect(ws.entry()!.apkEtag).toBe('v1');
  expect((await check(head(10, '"v1"'))).changed).toBe(false);
  expect((await check(head(10, '"v2"'))).changed).toBe(true);
  // A weak or missing ETag names no bytes, so it never matches a recorded one.
  expect((await check(head(10, 'W/"v1"'))).changed).toBe(true);
  expect((await check(head(10, null))).changed).toBe(true);

  // A publisher that cannot answer never reads as unchanged.
  await expect(check(head(10, '"v1"', false))).rejects.toThrow(/answered 503/);
  await expect(check(head(null))).rejects.toThrow(/no Content-Length/);
});

// ---------------------------------------------------------------------------
// The CLI as bazaarplusplus-mod/scripts/game.sh and android-apk-watch.yml call
// it: the exact stdout (results only), the exit code (0 or 1), logs and errors
// on stderr, and what the verb wrote to the lock and the private store.

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

const noStore = () => {
  throw new Error('this step must not need the private store');
};

// A first capture through the CLI, every toolchain file named, from a compact
// lock so that the writer, not the test, gives the file its format.
async function cliFirstCapture(ws: ReturnType<typeof workspace>, root: string) {
  write(
    path.join(ws.workspaceRoot, GAME_LIBS_LOCK_PATH),
    JSON.stringify(readGameLibsLock(ws.workspaceRoot))
  );
  const tools = toolchain(root);
  const apk = fakeApk(path.join(root, 'TheBazaar.apk'));
  const run = await runCli(
    [
      'snapshot-android',
      '--apk',
      apk,
      '--apk-etag',
      'v1',
      '--interop',
      interopDirectory(path.join(root, 'interop')),
      '--loader',
      tools.loader,
      '--loader-tag',
      'v0.7.4-android.4',
      '--patcher',
      tools.patcher,
      '--patcher-tag',
      'v2.1.0',
      '--cpp2il',
      tools.cpp2il
    ],
    {
      workspaceRoot: ws.workspaceRoot,
      gameLibsStore: noStore,
      generate: noGeneration
    }
  );
  return { run, tools, apk };
}

test('snapshot-android prints the snapshot directory and writes the android entry in the order InteropPath.props reads', async () => {
  const ws = workspace();
  const root = tempRoot('bpp-interop-cli-');
  const { run, apk } = await cliFirstCapture(ws, root);
  const directory = path.join(
    ws.modRoot,
    'game-libs',
    `android-online-${ANDROID_VERSION}`
  );
  expect(run.code).toBe(0);
  expect(run.out).toEqual([`${directory}\n`]);
  expect(run.err).toMatch(new RegExp(`^Reading ${apk}\\n`));
  expect(run.err).toMatch(
    new RegExp(
      `Captured android-online ${ANDROID_VERSION} into ${directory} \\(loader .*loader-v0\\.7\\.4-android\\.4\\) and recorded it in bazaarplusplus-mod/build/game-libs\\.lock\\.json\\n$`
    )
  );
  expect(ws.entry()).toMatchObject({
    gameVersion: ANDROID_VERSION,
    apkSha256: sha(fs.readFileSync(apk)),
    apkEtag: 'v1',
    nativeLibs: {}
  });

  // build/InteropPath.props finds gameVersion and loader.tag by position.
  const text = fs.readFileSync(
    path.join(ws.workspaceRoot, GAME_LIBS_LOCK_PATH),
    'utf8'
  );
  const parsed = JSON.parse(text);
  expect(text).toBe(`${JSON.stringify(parsed, null, 2)}\n`);
  expect(Object.keys(parsed)).toEqual([
    'schemaVersion',
    'staleAfterDays',
    'entries'
  ]);
  expect(Object.keys(parsed.entries).at(-1)).toBe('android-online');
  const entry = parsed.entries['android-online'];
  expect(Object.keys(entry)).toEqual([
    'gameVersion',
    'versionCode',
    'apkSha256',
    'apkEtag',
    'il2cppSha256',
    'metadataSha256',
    'unityVersion',
    'interopSha256',
    'loader',
    'patcher',
    'cpp2ilVersion',
    'cpp2ilSha256',
    'il2cppInteropVersion',
    'nativeLibs',
    'capturedAt'
  ]);
  expect(Object.keys(entry.loader)).toEqual([
    'tag',
    'sha256',
    'managedRuntimeVersion'
  ]);
  expect(Object.keys(entry.patcher)).toEqual(['tag', 'sha256']);

  const refused = await runCli(['snapshot-android'], {
    workspaceRoot: ws.workspaceRoot,
    gameLibsStore: noStore,
    generate: noGeneration
  });
  expect(refused.code).toBe(1);
  expect(refused.out).toEqual([]);
  expect(refused.err).toMatch(/^snapshot-android requires --apk\n/);
}, 60000);

test('publish android online stores the interop archive, every pinned input and the manifest, printing nothing', async () => {
  const ws = workspace();
  const root = tempRoot('bpp-interop-cli-');
  await cliFirstCapture(ws, root);
  const entry = ws.entry()!;
  const store = new MemoryStore();
  const published = await runCli(
    ['publish', '--platform', 'android', '--channel', 'online'],
    { workspaceRoot: ws.workspaceRoot, gameLibsStore: () => store }
  );
  expect(published.code).toBe(0);
  expect(published.out).toEqual([]);
  expect(published.err).toMatch(
    new RegExp(
      `Recorded game-libs/android-online-${ANDROID_VERSION}\\.json\\n$`
    )
  );
  expect(store.writes).toEqual([
    `game-libs/interop/${entry.interopSha256}.tar.gz`,
    `game-libs/apk/${entry.apkSha256}.apk`,
    `game-libs/loader/${entry.loader.sha256}.zip`,
    `game-libs/patcher/${entry.patcher.sha256}.tar.gz`,
    `game-libs/cpp2il/${entry.cpp2ilSha256}`,
    `game-libs/android-online-${ANDROID_VERSION}.json`
  ]);

  const refused = await runCli(
    ['publish', '--platform', 'android', '--channel', 'ptr'],
    { workspaceRoot: ws.workspaceRoot, gameLibsStore: () => store }
  );
  expect(refused).toEqual({
    code: 1,
    out: [],
    err: "Unknown android snapshot channel 'ptr'; expected online\n"
  });
  expect(store.writes).toHaveLength(6);
}, 60000);

test('snapshot-android as the APK watch runs it: only the APK and its ETag, the toolchain from the store', async () => {
  const ws = workspace();
  const root = tempRoot('bpp-interop-cli-');
  await cliFirstCapture(ws, root);
  const first = ws.entry()!;
  const store = new MemoryStore();
  expect(
    (
      await runCli(
        ['publish', '--platform', 'android', '--channel', 'online'],
        {
          workspaceRoot: ws.workspaceRoot,
          gameLibsStore: () => store
        }
      )
    ).code
  ).toBe(0);
  // The runner starts with nothing under game-libs/.
  fs.rmSync(path.join(ws.modRoot, 'game-libs'), { recursive: true });
  const nextVersion = '1.0.12900-production-android-0badc0de';
  const apk = fakeApk(path.join(root, 'next.apk'), {
    versionName: nextVersion,
    il2cpp: 'il2cpp next'
  });
  const calls: GenerateInteropOptions[] = [];
  const run = await runCli(
    ['snapshot-android', '--apk', apk, '--apk-etag', 'v2'],
    {
      workspaceRoot: ws.workspaceRoot,
      gameLibsStore: () => store,
      generate: (options) => {
        calls.push(options);
        interopDirectory(options.output);
      }
    }
  );
  expect(run.code).toBe(0);
  expect(run.out).toEqual([
    `${path.join(ws.modRoot, 'game-libs', `android-online-${nextVersion}`)}\n`
  ]);
  expect(calls).toHaveLength(1);
  expect(sha(fs.readFileSync(calls[0].patcher))).toBe(first.patcher.sha256);
  expect(sha(fs.readFileSync(calls[0].cpp2il))).toBe(first.cpp2ilSha256);
  expect(ws.entry()).toMatchObject({
    gameVersion: nextVersion,
    apkSha256: sha(fs.readFileSync(apk)),
    apkEtag: 'v2',
    loader: first.loader,
    patcher: first.patcher,
    cpp2ilSha256: first.cpp2ilSha256
  });
}, 60000);

test('official-apk prints changed or unchanged and fails when the publisher cannot answer', async () => {
  const ws = workspace();
  const root = tempRoot('bpp-interop-cli-');
  await cliFirstCapture(ws, root);
  const entry = ws.entry()!;
  const store = new MemoryStore();
  await store.put(`game-libs/apk/${entry.apkSha256}.apk`, Buffer.alloc(10), {
    ifNoneMatch: true
  });
  const urls: string[] = [];
  const head =
    (length: number, etag: string, ok = true): ApkHeadFetch =>
    async (url, init) => {
      urls.push(`${init.method} ${url}`);
      return {
        ok,
        status: ok ? 200 : 503,
        headers: new Headers({ 'content-length': `${length}`, etag })
      };
    };
  const official = (apkFetch: ApkHeadFetch) =>
    runCli(['official-apk'], {
      workspaceRoot: ws.workspaceRoot,
      gameLibsStore: () => store,
      apkFetch
    });
  expect(await official(head(10, '"v1"'))).toEqual({
    code: 0,
    out: ['unchanged\n'],
    err: `Official APK 10 bytes, ETag v1; locked ${ANDROID_VERSION} 10 bytes, ETag v1\n`
  });
  expect(urls).toEqual([
    'HEAD https://data.playthebazaar.com/game/android/TheBazaar.apk'
  ]);
  expect((await official(head(11, '"v1"'))).out).toEqual(['changed\n']);
  expect((await official(head(10, '"v2"'))).out).toEqual(['changed\n']);
  expect(await official(head(10, '"v1"', false))).toEqual({
    code: 1,
    out: [],
    err: 'HEAD https://data.playthebazaar.com/game/android/TheBazaar.apk answered 503\n'
  });
}, 60000);

test('record-apk-etag records the ETag of the locked APK and refuses another file', async () => {
  const ws = workspace();
  const root = tempRoot('bpp-interop-cli-');
  const { apk, tools } = await cliFirstCapture(ws, root);
  const lockFile = path.join(ws.workspaceRoot, GAME_LIBS_LOCK_PATH);
  const record = (argv: string[]) =>
    runCli(['record-apk-etag', ...argv], {
      workspaceRoot: ws.workspaceRoot,
      gameLibsStore: noStore
    });
  expect(await record(['--apk', apk, '--apk-etag', 'v2'])).toEqual({
    code: 0,
    out: [],
    err: `Recorded the ETag of ${ANDROID_VERSION} in bazaarplusplus-mod/build/game-libs.lock.json\n`
  });
  expect(ws.entry()!.apkEtag).toBe('v2');

  const before = fs.readFileSync(lockFile);
  for (const [argv, message] of [
    [['--apk', tools.loader, '--apk-etag', 'v3'], /not the locked APK/],
    [['--apk', apk, '--apk-etag', '"v3"'], /^Not a strong ETag value: "v3"\n$/],
    [['--apk', apk], /^record-apk-etag requires --apk-etag\n/]
  ] as const) {
    const failed = await record([...argv]);
    expect(failed.code).toBe(1);
    expect(failed.out).toEqual([]);
    expect(failed.err).toMatch(message);
  }
  expect(fs.readFileSync(lockFile)).toEqual(before);
}, 60000);

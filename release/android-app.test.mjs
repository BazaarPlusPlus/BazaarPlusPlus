import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { expect, test } from 'vitest';

import {
  RUNTIME_FACTS,
  RUNTIME_ZIP,
  UPDATER_ASSET,
  buildInteropPackage,
  buildRuntimeZip,
  stageAndroidAppRuntime
} from './android-app.mjs';
import { platformInventory } from './payload-inventory.mjs';
import { readZipEntries } from './payload-zip.mjs';

const sha256 = (bytes) =>
  crypto.createHash('sha256').update(bytes).digest('hex');
const tempDir = (prefix) =>
  fs.mkdtempSync(path.join(os.tmpdir(), `bpp-android-app-${prefix}-`));
const write = (root, relative, content) => {
  const file = path.join(root, relative);
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, content);
  return file;
};
const names = (zip) => readZipEntries(zip).map(({ name }) => name);

function interopFixture() {
  const root = tempDir('interop');
  write(root, 'Assembly-CSharp.dll', 'game proxies');
  write(root, 'Il2Cppmscorlib.dll', 'corlib proxies');
  write(root, 'nested/ignored.dll', 'not a top-level file');
  return root;
}

function loaderFixture() {
  const root = tempDir('loader');
  write(root, 'assets/LemonLoader/payload.json', '{}');
  write(root, 'assets/LemonLoader/runtime/loader/MelonLoader.dll', 'loader');
  write(root, 'assets/LemonLoader/runtime/dotnet/System.Runtime.dll', 'bcl');
  write(root, 'lib/arm64-v8a/libmain.so', 'bootstrap');
  return root;
}

test('the Interop Package holds only top-level interop files under LemonLoader’s asset root', () => {
  const interop = interopFixture();
  const zip = buildInteropPackage(interop);
  expect(names(zip)).toEqual([
    'assets/LemonLoader/runtime/interop/Assembly-CSharp.dll',
    'assets/LemonLoader/runtime/interop/Il2Cppmscorlib.dll'
  ]);
  expect(sha256(buildInteropPackage(interop))).toBe(sha256(zip));
});

test('the Interop Package refuses a directory without the game proxies', () => {
  const root = tempDir('empty-interop');
  write(root, 'Il2Cppmscorlib.dll', 'corlib proxies');
  expect(() => buildInteropPackage(root)).toThrow(/Assembly-CSharp\.dll/);
});

test('the runtime zip carries the Loader Release assets and the Updater as the packaged deployment', () => {
  const loader = loaderFixture();
  const updater = write(tempDir('updater'), 'BazaarPlusPlus.Updater.dll', 'u');
  const zip = buildRuntimeZip({ loaderPath: loader, updaterDll: updater });
  expect(names(zip).sort()).toEqual(
    [
      'assets/LemonLoader/payload.json',
      'assets/LemonLoader/runtime/dotnet/System.Runtime.dll',
      'assets/LemonLoader/runtime/loader/MelonLoader.dll',
      UPDATER_ASSET
    ].sort()
  );
  expect(
    sha256(buildRuntimeZip({ loaderPath: loader, updaterDll: updater }))
  ).toBe(sha256(zip));
  expect(() =>
    buildRuntimeZip({ loaderPath: tempDir('bare'), updaterDll: updater })
  ).toThrow(/not an unpacked Loader Release/);
});

test('staging writes the runtime zip, its facts and the native libraries the App packages', () => {
  const loader = loaderFixture();
  const updater = write(tempDir('updater'), 'BazaarPlusPlus.Updater.dll', 'u');
  const sqlite = write(tempDir('native'), 'libe_sqlite3.so', 'sqlite');
  const outDir = path.join(tempDir('out'), 'runtime');
  write(outDir, 'stale.txt', 'left from an earlier stage');

  const facts = stageAndroidAppRuntime({
    loaderPath: loader,
    loader: { tag: 'v0.0.0-test', sha256: 'a'.repeat(64) },
    updaterDll: updater,
    nativeLibs: { 'libe_sqlite3.so': sqlite },
    outDir
  });

  expect(fs.existsSync(path.join(outDir, 'stale.txt'))).toBe(false);
  expect(fs.readdirSync(path.join(outDir, 'jniLibs/arm64-v8a')).sort()).toEqual(
    ['libe_sqlite3.so', 'libmain.so']
  );
  const zip = fs.readFileSync(path.join(outDir, 'assets', RUNTIME_ZIP));
  expect(facts).toEqual({
    schemaVersion: 1,
    loader: { tag: 'v0.0.0-test', sha256: 'a'.repeat(64) },
    updaterSha256: sha256('u'),
    runtimeZipSha256: sha256(zip),
    nonManagedPayloadDigest: null
  });
  expect(
    JSON.parse(fs.readFileSync(path.join(outDir, 'assets', RUNTIME_FACTS)))
  ).toEqual(facts);
});

test('the Payload Inventory names the APK paths of what staging writes', () => {
  const outDir = path.join(tempDir('inventory'), 'runtime');
  stageAndroidAppRuntime({
    loaderPath: loaderFixture(),
    loader: { tag: 'v0.0.0-test', sha256: 'a'.repeat(64) },
    updaterDll: write(tempDir('updater'), 'BazaarPlusPlus.Updater.dll', 'u'),
    nativeLibs: {
      'libe_sqlite3.so': write(tempDir('native'), 'libe_sqlite3.so', 's')
    },
    outDir
  });
  // Gradle packages assets/ as is and jniLibs/<abi>/ as lib/<abi>/.
  const staged = [
    ...fs.readdirSync(path.join(outDir, 'assets')).map((n) => `assets/${n}`),
    ...fs
      .readdirSync(path.join(outDir, 'jniLibs/arm64-v8a'))
      .map((n) => `lib/arm64-v8a/${n}`)
  ];
  const inApk = platformInventory('android')
    .filter(
      (entry) =>
        entry.producer === 'android-app' && /^(lib|assets)\//.test(entry.path)
    )
    .map((entry) => entry.path);
  expect(inApk.length).toBeGreaterThan(0);
  expect(staged).toEqual(expect.arrayContaining(inApk));
});

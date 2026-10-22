import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { afterEach, expect, test, vi } from 'vitest';
import {
  androidApkKeyForEntry,
  androidApkRecordKey,
  androidNonManagedPayloadDigest,
  androidPaths,
  buildAndroidRelease,
  injectAndroidApk,
  androidSigningKey,
  prepareAndroidPayload,
  resolveBuildTools,
  resolveNativeLib,
  verifyAndroidPayload
} from './android-release.mjs';
import {
  GAME_LIBS_LOCK_PATH,
  INTEROP_KEY,
  interopDirectoryDigest
} from './game-libs.mjs';
import {
  fragmentKey,
  managedPayloadKey,
  platformManifestPath,
  validatePlatformFragment
} from './manifest.mjs';
import { platformInventory } from './payload-inventory.mjs';
import { buildZipBuffer } from './payload-zip.mjs';
import { RELEASE_BASE_URL, WORKSPACE_ROOT } from './product.mjs';
import { promotePlatform, uploadAndroidPlatform } from './publish.mjs';
import { MemoryStore } from './test-support/memory-store.mjs';

// The Android release job (#358) end to end, without the Android SDK or R2:
// prepare stages the Android Mod Payload from a stand-in host build, build
// runs the real injection pipeline with a fake LemonLoader Patcher, zipalign
// and apksigner, and upload and promote write to an in-memory store. Failure
// modes: a host build that misses a required inventory file, an interop
// directory that is not the locked one, a Payload changed after preparation,
// and a mod-only release that must reuse the published APK instead of
// injecting or uploading 2.8 GB again.

const KEY = 'android-aarch64';
const COMMIT = 'c'.repeat(40);
const SIGNER = 'a'.repeat(64);
const SIGNATURE = 'untrusted comment: signature from tauri secret key\nRUQ=';
const sha = (bytes) => crypto.createHash('sha256').update(bytes).digest('hex');

const roots = [];
afterEach(() => {
  for (const root of roots.splice(0))
    fs.rmSync(root, { recursive: true, force: true });
});

function write(file, bytes, mode) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, bytes);
  if (mode) fs.chmodSync(file, mode);
  return file;
}

function script(file, body) {
  return write(
    file,
    `#!/usr/bin/env bash\nset -euo pipefail\n${body}\n`,
    0o755
  );
}

// The Patcher's `inject` as far as the release depends on it: the output APK
// is the input plus libmain.so and the deployment under assets/LemonLoader/.
const FAKE_PATCHER = `
[[ "$1" == inject ]] || { echo "unexpected verb $1" >&2; exit 2; }
input="$2"; shift 2
while [[ $# -gt 0 ]]; do
  case "$1" in
    --output) output="$2" ;;
    --interop) interop="$2" ;;
    --release) release="$2" ;;
    --deployment) deployment="$2" ;;
    *) echo "unexpected option $1" >&2; exit 2 ;;
  esac
  shift 2
done
[[ -f "$interop/Assembly-CSharp.dll" && -f "$release" ]]
tree="$(mktemp -d)"
mkdir -p "$tree/lib/arm64-v8a" "$tree/assets/LemonLoader/deployment"
printf 'libmain' > "$tree/lib/arm64-v8a/libmain.so"
cp -R "$deployment"/. "$tree/assets/LemonLoader/deployment/"
cp "$input" "$output"
(cd "$tree" && zip -qr -X "$output" lib assets)
echo "inject" >> "$(dirname "$0")/../../../calls.log"`;

const FAKE_ZIPALIGN = `
if [[ "$1" == -c ]]; then exit 0; fi
cp "\${@: -2:1}" "\${@: -1}"`;

const FAKE_APKSIGNER = `
case "$1" in
  sign)
    shift
    while [[ $# -gt 1 ]]; do
      case "$1" in
        --out) out="$2" ;;
        --ks-pass) [[ "$2" == file:* && -s "\${2#file:}" ]] || exit 3 ;;
      esac
      shift 2
    done
    cp "$1" "$out" ;;
  verify)
    echo "Signer #1 certificate DN: CN=BazaarPlusPlus"
    echo "Signer #1 certificate SHA-256 digest: ${SIGNER}" ;;
esac`;

function fixture() {
  const workspaceRoot = fs.mkdtempSync(
    path.join(os.tmpdir(), 'bpp-android-release-')
  );
  roots.push(workspaceRoot);
  for (const file of [
    'VERSION',
    'release/payload.json',
    'release/generated/Payload.targets',
    'bazaarplusplus-mod/src/BazaarPlusPlus.Storage/BazaarPlusPlus.history-database.json'
  ])
    write(
      path.join(workspaceRoot, file),
      fs.readFileSync(path.join(WORKSPACE_ROOT, file))
    );
  const tools = path.join(workspaceRoot, 'tools');
  const interopPath = path.join(tools, 'interop');
  write(path.join(interopPath, 'Assembly-CSharp.dll'), 'interop assembly');
  write(path.join(interopPath, 'Il2Cppmscorlib.dll'), 'interop corlib');
  const loaderPath = path.join(tools, 'loader');
  write(path.join(loaderPath, 'MelonLoader.dll'), 'loader');
  const nativeLib = write(path.join(tools, 'libe_sqlite3.so'), 'sqlite elf');
  const lock = JSON.parse(
    fs.readFileSync(path.join(WORKSPACE_ROOT, GAME_LIBS_LOCK_PATH), 'utf8')
  );
  lock.entries[INTEROP_KEY] = {
    ...lock.entries[INTEROP_KEY],
    interopSha256: interopDirectoryDigest(interopPath),
    nativeLibs: {
      'libe_sqlite3.so': {
        package: 'SourceGear.sqlite3',
        version: '3.53.4',
        sha256: sha(fs.readFileSync(nativeLib))
      }
    }
  };
  write(
    path.join(workspaceRoot, GAME_LIBS_LOCK_PATH),
    `${JSON.stringify(lock, null, 2)}\n`
  );
  const officialApk = write(
    path.join(tools, 'TheBazaar.apk'),
    buildZipBuffer([
      {
        name: 'AndroidManifest.xml',
        data: Buffer.from('manifest'),
        mode: 0o644
      },
      {
        name: 'lib/arm64-v8a/libil2cpp.so',
        data: Buffer.from('il2cpp'),
        mode: 0o644
      }
    ])
  );
  const loaderZip = write(path.join(tools, 'loader.zip'), 'loader release');
  const patcherTree = path.join(tools, 'patcher-tree');
  script(path.join(patcherTree, 'LemonLoader.Patcher.CLI'), FAKE_PATCHER);
  const patcherArchive = path.join(tools, 'patcher.tar.gz');
  execFileSync('tar', ['-czf', patcherArchive, '-C', patcherTree, '.']);
  const keys = path.join(tools, 'keys');
  write(path.join(keys, 'android-release.p12'), 'keystore');
  write(path.join(keys, 'android-keystore-password'), 'password');
  const calls = path.join(workspaceRoot, 'target', 'calls.log');
  return {
    workspaceRoot,
    interopPath,
    entry: lock.entries[INTEROP_KEY],
    calls: () =>
      fs.existsSync(calls) ? fs.readFileSync(calls, 'utf8').split('\n') : [],
    options: {
      workspaceRoot,
      resolveSnapshot: async () => ({ interopPath, loaderPath }),
      produce: ({ payloadRoot }) => stageHostBuild(workspaceRoot, payloadRoot),
      resolveInputs: async () => ({
        officialApk,
        loaderZip,
        patcherArchive,
        nativeLibs: { 'libe_sqlite3.so': nativeLib }
      }),
      // The work directory sits under target/, so the fake Patcher's call
      // log lands in target/calls.log.
      inject: (args) =>
        injectAndroidApk({ ...args, hostPlatform: 'linux', hostArch: 'x64' }),
      sign: ({ file }) => {
        write(`${file}.sig`, SIGNATURE);
        return SIGNATURE;
      },
      signingKey: () => ({
        keystore: path.join(keys, 'android-release.p12'),
        passwordFile: path.join(keys, 'android-keystore-password'),
        alias: 'bpp'
      }),
      buildTools: () => ({
        zipalign: script(path.join(tools, 'bin', 'zipalign'), FAKE_ZIPALIGN),
        apksigner: script(path.join(tools, 'bin', 'apksigner'), FAKE_APKSIGNER)
      }),
      captureIdentity: () => ({ commit: COMMIT, dirty: false, digest: 'd' }),
      gameLibsStore: () => {
        throw new Error('this test must not reach the private store');
      }
    }
  };
}

const version = (workspaceRoot) =>
  fs.readFileSync(path.join(workspaceRoot, 'VERSION'), 'utf8').trim();

// What the Android host's CopyToPayloadStaging target writes: every
// mod-built MelonLoader entry of the inventory, minus `omit`.
function stageHostBuild(workspaceRoot, payloadRoot, omit = []) {
  for (const entry of platformInventory('android')) {
    if (!['managed', 'history-contract'].includes(entry.producer)) continue;
    if (omit.includes(entry.path)) continue;
    const bytes = entry.path.endsWith('.version')
      ? `${version(workspaceRoot)}.prod\n`
      : entry.producer === 'history-contract'
        ? fs.readFileSync(
            path.join(
              workspaceRoot,
              'bazaarplusplus-mod/src/BazaarPlusPlus.Storage/BazaarPlusPlus.history-database.json'
            )
          )
        : `bytes of ${entry.path}`;
    write(path.join(payloadRoot, entry.path), bytes);
  }
}

function apkEntries(apk) {
  return execFileSync('unzip', ['-Z1', apk], { encoding: 'utf8' })
    .split('\n')
    .filter(Boolean);
}

test('prepare stages exactly the mod-built MelonLoader entries and seals them', async () => {
  const f = fixture();
  const record = await prepareAndroidPayload(f.options);
  const expected = platformInventory('android')
    .filter((entry) => ['managed', 'history-contract'].includes(entry.producer))
    .map((entry) => entry.path)
    .sort((a, b) => a.localeCompare(b));
  expect(record.output.map((file) => file.path)).toEqual(expected);
  expect(record.lockEntry).toMatchObject({
    key: INTEROP_KEY,
    gameVersion: f.entry.gameVersion,
    interopSha256: f.entry.interopSha256
  });
  expect(verifyAndroidPayload(f.workspaceRoot).inputDigest).toBe(
    record.inputDigest
  );
  write(
    path.join(
      androidPaths(f.workspaceRoot).payload,
      'MelonLoader/Mods/BazaarPlusPlus.Android.dll'
    ),
    'tampered'
  );
  expect(() => verifyAndroidPayload(f.workspaceRoot)).toThrow(
    /output changed since preparation/
  );
});

test('prepare names every required inventory file the host build did not produce', async () => {
  const f = fixture();
  await expect(
    prepareAndroidPayload({
      ...f.options,
      produce: ({ payloadRoot }) =>
        stageHostBuild(f.workspaceRoot, payloadRoot, [
          'MelonLoader/Mods/BazaarPlusPlus.Core.dll',
          'MelonLoader/UserLibs/MessagePack.dll'
        ])
    })
  ).rejects.toThrow(
    'Android Mod Payload lacks MelonLoader/Mods/BazaarPlusPlus.Core.dll, MelonLoader/UserLibs/MessagePack.dll'
  );
  expect(fs.existsSync(androidPaths(f.workspaceRoot).buildRecord)).toBe(false);
});

test('prepare refuses an interop directory that is not the locked snapshot and undeclared files', async () => {
  const f = fixture();
  const other = path.join(f.workspaceRoot, 'tools', 'other-interop');
  write(path.join(other, 'Assembly-CSharp.dll'), 'another game build');
  await expect(
    prepareAndroidPayload({
      ...f.options,
      resolveSnapshot: async () => ({
        interopPath: other,
        loaderPath: other
      })
    })
  ).rejects.toThrow(/not the .* lock entry android-online records/);
  await expect(
    prepareAndroidPayload({
      ...f.options,
      produce: ({ payloadRoot }) => {
        stageHostBuild(f.workspaceRoot, payloadRoot);
        write(path.join(payloadRoot, 'MelonLoader/Mods/Extra.dll'), 'x');
      }
    })
  ).rejects.toThrow(
    /Android Mod Payload holds MelonLoader\/Mods\/Extra.dll, which release\/payload.json does not give to the mod build/
  );
  await expect(
    prepareAndroidPayload({
      ...f.options,
      msbuildArgs: ['-p:ManagedPath=/Managed']
    })
  ).rejects.toThrow(/Unsupported release build override/);
});

test('a new APK identity is injected, aligned and signed, then streamed with its sidecar and promoted', async () => {
  const f = fixture();
  const store = new MemoryStore();
  const manifest = await buildAndroidRelease({
    ...f.options,
    releaseStore: () => store
  });
  const paths = androidPaths(f.workspaceRoot);
  const apkKey = androidApkKeyForEntry(f.entry);
  expect(f.calls().filter(Boolean)).toEqual(['inject']);
  expect(manifest).toMatchObject({
    platform: KEY,
    appVersion: version(f.workspaceRoot),
    gitCommit: COMMIT,
    dirty: false,
    apk: {
      key: apkKey,
      published: false,
      versionCode: f.entry.versionCode,
      signerSha256: SIGNER,
      builtForProductVersion: version(f.workspaceRoot)
    },
    managedPayload: { signature: SIGNATURE },
    nonManagedPayloadDigest: androidNonManagedPayloadDigest(f.entry),
    interopSha256: f.entry.interopSha256
  });
  const apk = path.join(paths.root, manifest.apk.path);
  const entries = apkEntries(apk);
  expect(entries).toEqual(
    expect.arrayContaining([
      'AndroidManifest.xml',
      'lib/arm64-v8a/libil2cpp.so',
      'lib/arm64-v8a/libmain.so',
      'lib/arm64-v8a/libe_sqlite3.so',
      'assets/LemonLoader/deployment/Mods/BazaarPlusPlus.Android.dll',
      'assets/LemonLoader/deployment/UserLibs/Newtonsoft.Json.dll'
    ])
  );
  // The native library is stored, so the platform can map it in place.
  expect(
    execFileSync('unzip', ['-v', apk], { encoding: 'utf8' })
      .split('\n')
      .find((line) => line.endsWith('lib/arm64-v8a/libe_sqlite3.so'))
  ).toMatch(/\bStored\b/);
  expect(fs.existsSync(paths.work)).toBe(false);
  const zipEntries = apkEntries(
    path.join(paths.root, manifest.managedPayload.path)
  );
  expect(zipEntries.every((name) => name.startsWith('MelonLoader/'))).toBe(
    true
  );

  const log = vi.fn();
  const fragment = await uploadAndroidPlatform({
    workspaceRoot: f.workspaceRoot,
    baseUrl: RELEASE_BASE_URL,
    store,
    gitState: () => ({ commit: COMMIT, dirty: false }),
    log
  });
  const current = version(f.workspaceRoot);
  expect(validatePlatformFragment(fragment, current, KEY)).toBe(fragment);
  expect(fragment.selfUpdatable).toBe(false);
  expect(fragment.apk.url).toBe(`${RELEASE_BASE_URL}/${apkKey}`);
  expect(store.objects.get(apkKey).bytes).toEqual(fs.readFileSync(apk));
  expect(
    JSON.parse(store.objects.get(androidApkRecordKey(apkKey)).bytes)
  ).toEqual({
    size: manifest.apk.size,
    sha256: manifest.apk.sha256,
    versionCode: f.entry.versionCode,
    signerSha256: SIGNER,
    builtForProductVersion: current
  });
  expect(
    store.objects.get(`${managedPayloadKey(current, KEY)}.sig`).bytes.toString()
  ).toBe(`${SIGNATURE}\n`);
  expect(store.writes.at(-1)).toBe(fragmentKey(current, KEY));

  const promoted = await promotePlatform({
    version: current,
    platform: 'android',
    baseUrl: RELEASE_BASE_URL,
    store
  });
  expect(promoted.advanced).toBe(false);
  expect(
    JSON.parse(store.objects.get(platformManifestPath(KEY)).bytes).version
  ).toBe(current);
  // A second upload of the same build is a no-op on every immutable key.
  const writes = store.writes.length;
  await uploadAndroidPlatform({
    workspaceRoot: f.workspaceRoot,
    baseUrl: RELEASE_BASE_URL,
    store,
    gitState: () => ({ commit: COMMIT, dirty: false })
  });
  expect(store.writes.length).toBe(writes);
});

test('a mod-only release reuses the published APK and uploads only the Managed Payload', async () => {
  const f = fixture();
  const store = new MemoryStore();
  const gitState = () => ({ commit: COMMIT, dirty: false });
  const first = version(f.workspaceRoot);
  await buildAndroidRelease({ ...f.options, releaseStore: () => store });
  await uploadAndroidPlatform({
    workspaceRoot: f.workspaceRoot,
    baseUrl: RELEASE_BASE_URL,
    store,
    gitState
  });
  const [major, minor, patch] = first.split('.').map(Number);
  const next = `${major}.${minor}.${patch + 1}`;
  write(path.join(f.workspaceRoot, 'VERSION'), `${next}\n`);
  const apkKey = androidApkKeyForEntry(f.entry);

  const manifest = await buildAndroidRelease({
    ...f.options,
    releaseStore: () => store,
    signingKey: () => {
      throw new Error('a reused APK needs no signing key');
    }
  });
  expect(f.calls().filter(Boolean)).toEqual(['inject']);
  expect(manifest.apk).toMatchObject({
    path: null,
    published: true,
    builtForProductVersion: first
  });
  const before = store.writes.length;
  const fragment = await uploadAndroidPlatform({
    workspaceRoot: f.workspaceRoot,
    baseUrl: RELEASE_BASE_URL,
    store,
    gitState
  });
  expect(store.writes.slice(before)).toEqual([
    managedPayloadKey(next, KEY),
    `${managedPayloadKey(next, KEY)}.sig`,
    fragmentKey(next, KEY)
  ]);
  expect(fragment.apk.builtForProductVersion).toBe(first);
  expect(fragment.selfUpdatable).toBe(true);

  // --rebuild-apk (build_only) injects anyway; upload keeps the published APK.
  write(
    path.join(f.workspaceRoot, 'VERSION'),
    `${major}.${minor}.${patch + 2}\n`
  );
  const rebuilt = await buildAndroidRelease({
    ...f.options,
    releaseStore: () => store,
    rebuildApk: true
  });
  expect(f.calls().filter(Boolean)).toEqual(['inject', 'inject']);
  expect(rebuilt.apk.published).toBe(true);
  expect(rebuilt.apk.path).toMatch(/\.apk$/);
  const log = vi.fn();
  const kept = await uploadAndroidPlatform({
    workspaceRoot: f.workspaceRoot,
    baseUrl: RELEASE_BASE_URL,
    store,
    gitState,
    log
  });
  expect(kept.apk.builtForProductVersion).toBe(first);
  expect(log).toHaveBeenCalledWith(
    expect.stringContaining(`${apkKey} is already published`)
  );
});

test('upload refuses a build from another commit or a dirty checkout', async () => {
  const f = fixture();
  const store = new MemoryStore();
  await buildAndroidRelease({ ...f.options, releaseStore: () => store });
  const upload = (gitState) =>
    uploadAndroidPlatform({
      workspaceRoot: f.workspaceRoot,
      baseUrl: RELEASE_BASE_URL,
      store,
      gitState
    });
  await expect(
    upload(() => ({ commit: 'd'.repeat(40), dirty: false }))
  ).rejects.toThrow(/commit mismatch/);
  await expect(upload(() => ({ commit: COMMIT, dirty: true }))).rejects.toThrow(
    /dirty/
  );
  expect(store.writes).toEqual([]);
});

test('injection runs only where the Patcher CLI runs', () => {
  expect(() =>
    injectAndroidApk({ hostPlatform: 'darwin', hostArch: 'arm64' })
  ).toThrow(/Linux x64 CLI, and this is darwin-arm64/);
});

test('a locked native library comes from the NuGet cache or nuget.org and must hash to its pin', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-android-native-'));
  roots.push(root);
  const bytes = Buffer.from('sqlite for android');
  const pin = {
    package: 'SourceGear.sqlite3',
    version: '3.53.4',
    sha256: sha(bytes)
  };
  const nugetRoot = path.join(root, 'nuget');
  const fetchImpl = vi.fn(
    async () =>
      new Response(
        buildZipBuffer([
          {
            name: 'runtimes/android-arm64/native/libe_sqlite3.so',
            data: bytes,
            mode: 0o644
          }
        ])
      )
  );
  const downloaded = await resolveNativeLib({
    name: 'libe_sqlite3.so',
    pin,
    into: path.join(root, 'a'),
    nugetRoot,
    fetchImpl
  });
  expect(fs.readFileSync(downloaded)).toEqual(bytes);
  expect(fetchImpl).toHaveBeenCalledWith(
    'https://api.nuget.org/v3-flatcontainer/sourcegear.sqlite3/3.53.4/sourcegear.sqlite3.3.53.4.nupkg',
    expect.anything()
  );
  write(
    path.join(
      nugetRoot,
      'sourcegear.sqlite3/3.53.4/runtimes/android-arm64/native/libe_sqlite3.so'
    ),
    'a different build'
  );
  await expect(
    resolveNativeLib({
      name: 'libe_sqlite3.so',
      pin,
      into: path.join(root, 'b'),
      nugetRoot,
      fetchImpl
    })
  ).rejects.toThrow(
    /hashes to .*, not the .* lock entry android-online records/
  );
  expect(fetchImpl).toHaveBeenCalledTimes(1);
  await expect(
    resolveNativeLib({ name: '../escape.so', pin, into: root, nugetRoot })
  ).rejects.toThrow(/Invalid native library name/);
});

test('the signing key and the build-tools resolve from the staged secrets and the newest SDK build-tools', () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-android-tools-'));
  roots.push(root);
  const secrets = path.join(root, 'secrets');
  write(path.join(secrets, 'android-release.p12'), 'keystore');
  write(path.join(secrets, 'android-keystore-password'), 'password');
  expect(() => androidSigningKey({ BPP_SIGNING_SECRETS_DIR: secrets })).toThrow(
    /needs android-key-alias in BPP_SIGNING_SECRETS_DIR/
  );
  expect(() => androidSigningKey({})).toThrow(/android-release\.p12/);
  write(path.join(secrets, 'android-key-alias'), 'bpp\n');
  expect(androidSigningKey({ BPP_SIGNING_SECRETS_DIR: secrets })).toEqual({
    keystore: path.join(secrets, 'android-release.p12'),
    passwordFile: path.join(secrets, 'android-keystore-password'),
    alias: 'bpp'
  });

  const sdk = path.join(root, 'sdk');
  for (const version of ['34.0.0', '35.0.1', '37.0.0'])
    for (const tool of ['zipalign', 'apksigner'])
      script(path.join(sdk, 'build-tools', version, tool), 'exit 0');
  expect(resolveBuildTools({ PATH: '', ANDROID_HOME: sdk })).toEqual({
    zipalign: path.join(sdk, 'build-tools/37.0.0/zipalign'),
    apksigner: path.join(sdk, 'build-tools/37.0.0/apksigner')
  });
  fs.rmSync(path.join(sdk, 'build-tools/37.0.0'), { recursive: true });
  fs.rmSync(path.join(sdk, 'build-tools/35.0.1'), { recursive: true });
  expect(() => resolveBuildTools({ PATH: '', ANDROID_HOME: sdk })).toThrow(
    /zipalign not found on PATH or in ANDROID_HOME\/build-tools\/<35\+>/
  );
});

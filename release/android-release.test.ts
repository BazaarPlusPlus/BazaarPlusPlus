import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import {
  afterAll,
  afterEach,
  beforeAll,
  expect,
  test,
  vi
} from 'vite-plus/test';
import {
  adaptationPaths,
  androidPaths,
  buildAndroidRelease,
  androidSigningKey,
  prepareAndroidPayload,
  resolveBuildTools,
  resolveNativeLib,
  signerCertificateSha256,
  verifyAndroidPayload
} from './android-release.ts';
import type { AssembleOptions, ProduceOptions } from './android-release.ts';
import type { ReleaseKeyExporter } from './android-interop.ts';
import { INTEROP_ASSET_ROOT, RUNTIME_FACTS } from './android-app.ts';
import {
  certificateSha256,
  interopStatement,
  verifyInteropStatement
} from './android-interop.ts';
import {
  LOADER_MANAGED_DIRECTORY,
  LOADER_MARKER,
  interopDirectoryDigest
} from './interop-snapshot.ts';
import {
  ANDROID_SOURCES,
  GAME_LIBS_LOCK_PATH,
  PUBLISHER_KEY,
  interopKey
} from './snapshot-lock.ts';
import type { AndroidSource } from './snapshot-lock.ts';
import {
  ANDROID_GAME_SIGNERS,
  androidAppKey,
  androidCarriedRelease,
  androidManagedPayloadKey,
  androidAppVersionCode,
  androidNonManagedPayloadDigest,
  fragmentKey,
  interopPackageKey,
  platformManifestPath,
  validatePlatformFragment
} from './manifest.ts';
import { platformInventory } from './payload-inventory.ts';
import { buildZipBuffer, readZipEntries } from './payload-zip.ts';
import { RELEASE_BASE_URL, WORKSPACE_ROOT } from './product.ts';
import { promotePlatform, uploadAndroidPlatform } from './publish.ts';
import type {
  PromotePlatformOptions,
  UploadAndroidPlatformOptions
} from './publish.ts';
import { MemoryStore } from './test-support/memory-store.ts';

// The Android release job end to end, without the Android SDK, Gradle or R2:
// prepare stages the Android Mod Payload from a stand-in host build; build
// stages the Android App Runtime, runs a stand-in Gradle build, zipalign and
// apksigner, packs the Interop Package and signs its statement with a
// throwaway RSA key; upload and promote write to an in-memory store. Failure
// modes: a host build that misses a required inventory file, an interop
// directory that is not the locked one, a Payload changed after preparation,
// an App signed by another key or packaging another runtime, a keystore
// whose certificate did not sign the App, and an Interop Package key that
// holds other bytes.

const KEY = 'android-aarch64';
const COMMIT = 'c'.repeat(40);
const SIGNATURE = 'untrusted comment: signature from tauri secret key\nRUQ=';
const sha = (bytes: string | NodeJS.ArrayBufferView): string =>
  crypto.createHash('sha256').update(bytes).digest('hex');
// The stand-in host build's Updater plugin, part of the App's runtime.
const UPDATER_SHA256 = sha(
  'bytes of MelonLoader/Plugins/BazaarPlusPlus.Updater.dll'
);

// Each pipeline test runs zipalign, apksigner and unzip as processes; on a
// loaded machine that exceeds the 5 s default.
vi.setConfig({ testTimeout: 30_000 });

const roots: string[] = [];
afterEach(() => {
  for (const root of roots.splice(0))
    fs.rmSync(root, { recursive: true, force: true });
});

interface ThrowawayKey {
  keyFile: string;
  certificateFile: string;
  sha256: string;
}

// Two throwaway release keys: the one that signs, and a stranger.
let keys: { release: ThrowawayKey; stranger: ThrowawayKey };
let keyRoot: string;
function throwawayKey(name: string): ThrowawayKey {
  const keyFile = path.join(keyRoot, `${name}.pem`);
  const certificateFile = path.join(keyRoot, `${name}.crt`);
  execFileSync(
    'openssl',
    [
      'req',
      '-x509',
      '-newkey',
      'rsa:2048',
      '-nodes',
      '-keyout',
      keyFile,
      '-out',
      certificateFile,
      '-subj',
      `/CN=${name}`,
      '-days',
      '1'
    ],
    { stdio: 'ignore' }
  );
  return {
    keyFile,
    certificateFile,
    sha256: certificateSha256(fs.readFileSync(certificateFile))
  };
}
beforeAll(() => {
  keyRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-android-keys-'));
  keys = { release: throwawayKey('release'), stranger: throwawayKey('other') };
});
afterAll(() => fs.rmSync(keyRoot, { recursive: true, force: true }));

function write(file: string, bytes: string | Buffer, mode?: number): string {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, bytes);
  if (mode) fs.chmodSync(file, mode);
  return file;
}

function script(file: string, body: string): string {
  return write(
    file,
    `#!/usr/bin/env bash\nset -euo pipefail\n${body}\n`,
    0o755
  );
}

const FAKE_ZIPALIGN = `
if [[ "$1" == -c ]]; then exit 0; fi
cp "\${@: -2:1}" "\${@: -1}"`;

// build-tools 37 signs a minSdk 24+ APK with v3 only and labels the signer
// "V3.0 Signer:"; older output and v1/v2 signing say "Signer #1".
const fakeApksigner = (signer: string, label: string = 'V3.0 Signer:') => `
case "$1" in
  sign)
    shift
    while [[ $# -gt 1 ]]; do
      case "$1" in
        --out) out="$2" ;;
        --ks-pass) [[ "$2" == file:* && -s "\${2#file:}" ]] || exit 3; ks_pass="$2" ;;
        # apksigner reads a file named twice line by line; the one-line file has no second.
        --key-pass) [[ "$2" != "$ks_pass" ]] || exit 4 ;;
      esac
      shift 2
    done
    cp "$1" "$out" ;;
  verify)
    echo "${label} certificate DN: CN=BazaarPlusPlus"
    echo "${label} certificate SHA-256 digest: ${signer}" ;;
esac`;

// What Gradle's assembleRelease packages from -Pbpp.runtimeDir: assets/ as
// is, jniLibs/<abi>/ as lib/<abi>/, beside the App's own code. `edit`
// changes the staged files on their way into the APK.
interface GradleCall {
  version: string;
  versionCode: number;
  runtimeDir: string;
}

interface ApkEntry {
  name: string;
  data: Buffer;
}

function fakeGradle(
  calls: GradleCall[],
  edit: (entries: ApkEntry[]) => ApkEntry[] = (entries) => entries
): (options: AssembleOptions) => string {
  return ({ workspaceRoot, version, versionCode, runtimeDir }) => {
    calls.push({ version, versionCode, runtimeDir });
    const entries: ApkEntry[] = [
      { name: 'AndroidManifest.xml', data: Buffer.from('manifest') },
      { name: 'classes.dex', data: Buffer.from(`dex ${version}`) }
    ];
    const walk = (directory: string, prefix: string): void => {
      for (const name of fs.readdirSync(directory).sort()) {
        const file = path.join(directory, name);
        if (fs.statSync(file).isDirectory()) walk(file, `${prefix}/${name}`);
        else
          entries.push({
            name: `${prefix}/${name}`,
            data: fs.readFileSync(file)
          });
      }
    };
    walk(path.join(runtimeDir, 'assets'), 'assets');
    walk(path.join(runtimeDir, 'jniLibs', 'arm64-v8a'), 'lib/arm64-v8a');
    return write(
      path.join(workspaceRoot, 'target', 'gradle', 'app-release-unsigned.apk'),
      buildZipBuffer(edit(entries).map((entry) => ({ ...entry, mode: 0o644 })))
    );
  };
}

// The stand-in for exportReleaseKey: the throwaway key and certificate
// copied into `into`, which the build must remove.
function releaseKeyFrom(
  key: ThrowawayKey,
  exported: string[]
): ReleaseKeyExporter {
  return ({ into }) => {
    exported.push(into);
    fs.mkdirSync(into, { recursive: true });
    const keyFile = path.join(into, 'release-key.pem');
    const certificateFile = path.join(into, 'release-certificate.pem');
    fs.copyFileSync(key.keyFile, keyFile);
    fs.copyFileSync(key.certificateFile, certificateFile);
    return { keyFile, certificateFile };
  };
}

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
  // One Interop Snapshot per Android source (ADR 0019).
  const interopPaths = {
    publisher: path.join(tools, 'interop'),
    play: path.join(tools, 'interop-play')
  };
  const interopPath = interopPaths.publisher;
  for (const source of ANDROID_SOURCES) {
    write(
      path.join(interopPaths[source], 'Assembly-CSharp.dll'),
      `interop assembly ${source}`
    );
    write(
      path.join(interopPaths[source], 'Il2Cppmscorlib.dll'),
      'interop corlib'
    );
  }
  const loaderPath = path.join(tools, 'loader');
  write(
    path.join(loaderPath, LOADER_MANAGED_DIRECTORY, 'MelonLoader.dll'),
    'loader'
  );
  write(path.join(loaderPath, 'assets/LemonLoader/payload.json'), '{}');
  write(path.join(loaderPath, 'lib/arm64-v8a/libmain.so'), 'bootstrap');
  const nativeLib = write(path.join(tools, 'libe_sqlite3.so'), 'sqlite elf');
  const lock = JSON.parse(
    fs.readFileSync(path.join(WORKSPACE_ROOT, GAME_LIBS_LOCK_PATH), 'utf8')
  );
  for (const source of ANDROID_SOURCES)
    lock.entries[interopKey(source)] = {
      ...lock.entries[interopKey(source)],
      interopSha256: interopDirectoryDigest(interopPaths[source]),
      nativeLibs: {
        'libe_sqlite3.so': {
          package: 'SourceGear.sqlite3',
          version: '3.53.4',
          sha256: sha(fs.readFileSync(nativeLib))
        }
      }
    };
  const writeLock = () =>
    write(
      path.join(workspaceRoot, GAME_LIBS_LOCK_PATH),
      `${JSON.stringify(lock, null, 2)}\n`
    );
  writeLock();
  write(
    path.join(loaderPath, LOADER_MARKER),
    lock.entries[PUBLISHER_KEY].loader.sha256
  );
  const secrets = path.join(tools, 'secrets');
  write(path.join(secrets, 'android-release.p12'), 'keystore');
  write(path.join(secrets, 'android-keystore-password'), 'password');
  const gradleCalls: GradleCall[] = [];
  const exportedKeys: string[] = [];
  const buildTools =
    (signer = keys.release.sha256) =>
    () => ({
      zipalign: script(path.join(tools, 'bin', 'zipalign'), FAKE_ZIPALIGN),
      apksigner: script(
        path.join(tools, 'bin', `apksigner-${signer.slice(0, 8)}`),
        fakeApksigner(signer)
      )
    });
  return {
    workspaceRoot,
    interopPath,
    tools,
    gradleCalls,
    exportedKeys,
    buildTools,
    interopPaths,
    entry: lock.entries[PUBLISHER_KEY],
    playEntry: lock.entries[interopKey('play')],
    writeLock,
    lock,
    // Re-collecting the Snapshot Lock with another SourceGear.sqlite3: only
    // the native library pin changes, not the game, loader or interop.
    upgradeNativeLib: (bytes: string) => {
      write(nativeLib, bytes);
      for (const source of ANDROID_SOURCES)
        lock.entries[interopKey(source)].nativeLibs['libe_sqlite3.so'] = {
          package: 'SourceGear.sqlite3',
          version: '3.53.5',
          sha256: sha(Buffer.from(bytes))
        };
      writeLock();
      return lock.entries[PUBLISHER_KEY];
    },
    options: {
      workspaceRoot,
      resolveSnapshot: async ({ source }: { source: AndroidSource }) => ({
        interopPath: interopPaths[source],
        loaderPath
      }),
      produce: ({ payloadRoot }: ProduceOptions) =>
        stageHostBuild(workspaceRoot, payloadRoot),
      resolveNativeLibs: async () => ({ 'libe_sqlite3.so': nativeLib }),
      assemble: fakeGradle(gradleCalls),
      sign: ({ file }: { file: string }) => {
        write(`${file}.sig`, SIGNATURE);
        return SIGNATURE;
      },
      signingKey: () => ({
        keystore: path.join(secrets, 'android-release.p12'),
        passwordFile: path.join(secrets, 'android-keystore-password'),
        alias: 'bpp-android-host'
      }),
      buildTools: buildTools(),
      releaseKey: releaseKeyFrom(keys.release, exportedKeys),
      releaseSignerSha256: keys.release.sha256,
      captureIdentity: () => ({ commit: COMMIT, dirty: false, digest: 'd' }),
      gameLibsStore: () => {
        throw new Error('this test must not reach the private store');
      }
    }
  };
}

type Fixture = ReturnType<typeof fixture>;

const version = (workspaceRoot: string): string =>
  fs.readFileSync(path.join(workspaceRoot, 'VERSION'), 'utf8').trim();

// What the Android host's CopyToPayloadStaging target writes: every
// MSBuild-produced MelonLoader entry of the inventory (the Managed Payload and
// the Updater plugin), minus `omit`.
function stageHostBuild(
  workspaceRoot: string,
  payloadRoot: string,
  omit: string[] = []
): void {
  for (const entry of platformInventory('android')) {
    if (!['managed', 'history-contract', 'updater'].includes(entry.producer))
      continue;
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

function zipNames(file: string): string[] {
  return readZipEntries(fs.readFileSync(file)).map(({ name }) => name);
}

const upload = (
  f: Fixture,
  store: MemoryStore,
  extra: Partial<UploadAndroidPlatformOptions> = {}
) =>
  uploadAndroidPlatform({
    workspaceRoot: f.workspaceRoot,
    baseUrl: RELEASE_BASE_URL,
    store,
    gitState: () => ({ commit: COMMIT, dirty: false }),
    releaseSignerSha256: keys.release.sha256,
    ...extra
  });
const promote = (
  f: Fixture,
  store: MemoryStore,
  extra: Partial<PromotePlatformOptions> = {}
) =>
  promotePlatform({
    version: version(f.workspaceRoot),
    platform: 'android',
    baseUrl: RELEASE_BASE_URL,
    store,
    releaseSignerSha256: keys.release.sha256,
    ...extra
  });

test('prepare stages exactly the mod-built MelonLoader entries and seals them', async () => {
  const f = fixture();
  const records = await prepareAndroidPayload(f.options);
  const record = records.publisher;
  const expected = platformInventory('android')
    .filter((entry) =>
      ['managed', 'history-contract', 'updater', 'managed-manifest'].includes(
        entry.producer
      )
    )
    .map((entry) => entry.path)
    .sort((a, b) => a.localeCompare(b));
  expect(record.output.map((file) => file.path)).toEqual(expected);
  expect(record.lockEntry).toMatchObject({
    key: PUBLISHER_KEY,
    gameVersion: f.entry.gameVersion,
    il2cppSha256: f.entry.il2cppSha256,
    metadataSha256: f.entry.metadataSha256,
    interopSha256: f.entry.interopSha256
  });
  // The Play adaptation compiles against its own entry and interop.
  expect(records.play.lockEntry).toMatchObject({
    key: 'android-play',
    gameVersion: f.playEntry.gameVersion,
    interopSha256: f.playEntry.interopSha256
  });
  expect(records.play.interopPath).toBe(fs.realpathSync(f.interopPaths.play));
  expect(verifyAndroidPayload(f.workspaceRoot).publisher.inputDigest).toBe(
    record.inputDigest
  );
  write(
    path.join(
      adaptationPaths(f.workspaceRoot, 'play').payload,
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
  expect(
    fs.existsSync(adaptationPaths(f.workspaceRoot, 'publisher').buildRecord)
  ).toBe(false);
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
  ).rejects.toThrow(/not the .* lock entry android-publisher records/);
  // A -p:LemonLoaderPath override must still be the locked Loader Release.
  await expect(
    prepareAndroidPayload({
      ...f.options,
      resolveSnapshot: async () => ({
        interopPath: f.interopPath,
        loaderPath: other
      })
    })
  ).rejects.toThrow(
    /is not the Loader Release v0\.7\.4-android\.4 .* lock entry android-publisher records/
  );
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

test('the carried release is the Platform Release Manifest without the App', () => {
  const fixtures = path.join(WORKSPACE_ROOT, 'release', 'fixtures');
  const published = JSON.parse(
    fs.readFileSync(path.join(fixtures, 'latest', `${KEY}.json`), 'utf8')
  );
  const record = published.downloads[KEY];
  const produced = androidCarriedRelease({
    version: published.version,
    gitCommit: published.gitCommit,
    baseUrl: RELEASE_BASE_URL,
    adaptations: record.adaptations,
    loader: record.loader,
    historyDatabaseUserVersion: record.historyDatabaseUserVersion,
    nonManagedPayloadDigest: record.nonManagedPayloadDigest
  });
  const golden = path.join(fixtures, 'android-carried-release.json');
  const text = `${JSON.stringify(produced, null, 2)}\n`;
  if (process.env.BPP_UPDATE_GOLDENS === '1') fs.writeFileSync(golden, text);
  expect(text).toBe(fs.readFileSync(golden, 'utf8'));
  const { app: _app, selfUpdatable: _self, ...rest } = record;
  expect(produced.downloads[KEY]).toMatchObject(rest);
  expect(produced.downloads[KEY]).not.toHaveProperty('app');
});

test('a release builds and signs its Android App and Interop Package, then streams them and promotes', async () => {
  const f = fixture();
  const store = new MemoryStore();
  const manifest = await buildAndroidRelease(f.options);
  const { publisher, play } = manifest.adaptations;
  const paths = androidPaths(f.workspaceRoot);
  const current = version(f.workspaceRoot);
  const digest = androidNonManagedPayloadDigest({
    ...f.entry,
    updaterSha256: UPDATER_SHA256
  });
  expect(f.gradleCalls).toEqual([
    {
      version: current,
      versionCode: androidAppVersionCode(current),
      runtimeDir: paths.runtime
    }
  ]);
  expect(manifest).toMatchObject({
    platform: KEY,
    appVersion: current,
    gitCommit: COMMIT,
    dirty: false,
    app: {
      key: androidAppKey(current),
      versionCode: androidAppVersionCode(current),
      signerSha256: keys.release.sha256
    },
    loader: { tag: f.entry.loader.tag, sha256: f.entry.loader.sha256 },
    nonManagedPayloadDigest: digest
  });
  for (const [adaptation, entry] of [
    [publisher, f.entry],
    [play, f.playEntry]
  ] as const)
    expect(adaptation).toMatchObject({
      interop: { snapshotSha256: entry.interopSha256 },
      managedPayload: { signature: SIGNATURE },
      game: {
        gameVersion: entry.gameVersion,
        versionCode: entry.versionCode,
        il2cppSha256: entry.il2cppSha256,
        metadataSha256: entry.metadataSha256
      }
    });
  expect(play.interop.sha256).not.toBe(publisher.interop.sha256);

  // The App carries the runtime with the fragment's digest.
  const apk = path.join(paths.root, manifest.app.path);
  const entries = readZipEntries(fs.readFileSync(apk));
  expect(entries.map(({ name }) => name)).toEqual(
    expect.arrayContaining([
      'lib/arm64-v8a/libmain.so',
      'lib/arm64-v8a/libe_sqlite3.so',
      'assets/lemon-runtime.zip',
      `assets/${RUNTIME_FACTS}`
    ])
  );
  const facts = JSON.parse(
    String(entries.find(({ name }) => name === `assets/${RUNTIME_FACTS}`)!.data)
  );
  expect(facts).toMatchObject({
    nonManagedPayloadDigest: digest,
    updaterSha256: UPDATER_SHA256
  });

  // The App runs its own release with no download: both zips and the document
  // that names them, with no `app`, because an APK cannot describe itself.
  const appPath = path.join(paths.root, manifest.app.path);
  const carried = JSON.parse(
    String(
      execFileSync('unzip', ['-p', appPath, 'assets/bpp-release/release.json'])
    )
  );
  const carriedRecord = carried.downloads[KEY];
  expect(carried.version).toBe(current);
  expect(carried.gitCommit).toBe(COMMIT);
  expect(carriedRecord.app).toBeUndefined();
  expect(carriedRecord.interop.sha256).toBe(publisher.interop.sha256);
  expect(carriedRecord.interop.signature).toBe(publisher.interop.signature);
  expect(carriedRecord.managedPayload.sha256).toBe(
    publisher.managedPayload.sha256
  );
  expect(carriedRecord.adaptations.play.interop.sha256).toBe(
    play.interop.sha256
  );
  for (const [digest, file] of [publisher, play].flatMap(
    ({ interop, managedPayload }) => [
      [interop.sha256, interop.path],
      [managedPayload.sha256, managedPayload.path]
    ]
  ))
    expect(
      execFileSync(
        'unzip',
        ['-p', appPath, `assets/bpp-release/${digest}.zip`],
        { maxBuffer: 1 << 28 }
      ).equals(fs.readFileSync(path.join(paths.root, file)))
    ).toBe(true);

  // The Interop Package is the locked snapshot at its content address, with
  // a `release <version>` statement the release certificate verifies.
  const interopZip = fs.readFileSync(
    path.join(paths.root, publisher.interop.path)
  );
  expect(publisher.interop.sha256).toBe(sha(interopZip));
  expect(publisher.interop.key).toBe(interopPackageKey(sha(interopZip)));
  expect(zipNames(path.join(paths.root, publisher.interop.path))).toEqual([
    `${INTEROP_ASSET_ROOT}/Assembly-CSharp.dll`,
    `${INTEROP_ASSET_ROOT}/Il2Cppmscorlib.dll`
  ]);
  // Each statement binds its own zip to its own game.
  for (const [adaptation, entry] of [
    [publisher, f.entry],
    [play, f.playEntry]
  ] as const) {
    const statement = interopStatement({
      zipSha256: adaptation.interop.sha256,
      il2cppSha256: entry.il2cppSha256,
      metadataSha256: entry.metadataSha256,
      purpose: `release ${current}`
    });
    expect(
      fs.readFileSync(path.join(paths.root, adaptation.interop.statementPath))
    ).toEqual(statement);
    expect(
      fs
        .readFileSync(
          path.join(paths.root, `${adaptation.interop.statementPath}.sig`),
          'utf8'
        )
        .trim()
    ).toBe(adaptation.interop.signature);
    expect(
      verifyInteropStatement({
        statement,
        signature: adaptation.interop.signature,
        certificate: fs.readFileSync(keys.release.certificateFile)
      })
    ).toBe(true);
  }
  // Key material sat outside the workspace and is gone.
  expect(f.exportedKeys).toHaveLength(2);
  expect(f.exportedKeys[0].startsWith(f.workspaceRoot)).toBe(false);
  expect(fs.existsSync(path.dirname(f.exportedKeys[0]))).toBe(false);
  expect(fs.existsSync(paths.work)).toBe(false);

  // The Managed Payload: the signed manifest and the Mods and UserLibs it
  // names; the Updater plugin ships only inside the App.
  const zipEntries = zipNames(
    path.join(paths.root, publisher.managedPayload.path)
  );
  expect(zipEntries).toEqual(
    expect.arrayContaining(['manifest.txt', 'manifest.txt.minisig'])
  );
  expect(
    zipEntries
      .filter((name) => !name.startsWith('manifest.txt'))
      .every((name) => /^files\/MelonLoader\/(Mods|UserLibs)\//.test(name))
  ).toBe(true);

  const fragment = await upload(f, store);
  expect(validatePlatformFragment(fragment, current, KEY)).toBe(fragment);
  expect(fragment.selfUpdatable).toBe(false);
  expect(fragment.app.url).toBe(
    `${RELEASE_BASE_URL}/${androidAppKey(current)}`
  );
  expect(store.writes).toEqual([
    androidAppKey(current),
    publisher.interop.key,
    androidManagedPayloadKey(current, 'publisher'),
    play.interop.key,
    androidManagedPayloadKey(current, 'play'),
    `${androidManagedPayloadKey(current, 'publisher')}.sig`,
    `${androidManagedPayloadKey(current, 'play')}.sig`,
    fragmentKey(current, KEY)
  ]);
  expect(store.objects.get(androidAppKey(current))!.bytes).toEqual(
    fs.readFileSync(apk)
  );
  expect(store.objects.get(publisher.interop.key)!.bytes).toEqual(interopZip);
  expect(fragment.adaptations!.play.signerSha256).toBe(
    ANDROID_GAME_SIGNERS.play
  );

  const promoted = await promote(f, store);
  expect(promoted.advanced).toBe(false);
  const published = JSON.parse(
    String(store.objects.get(platformManifestPath(KEY))!.bytes)
  );
  expect(Object.keys(published.downloads[KEY])).toEqual([
    'app',
    'managedPayload',
    'interop',
    'game',
    'loader',
    'selfUpdatable',
    'historyDatabaseUserVersion',
    'nonManagedPayloadDigest',
    'adaptations'
  ]);
  // A second upload of the same build is a no-op on every immutable key.
  const writes = store.writes.length;
  await upload(f, store);
  expect(store.writes.length).toBe(writes);
});

const nextVersion = (workspaceRoot: string, step: number = 1): string => {
  const [major, minor, patch] = version(workspaceRoot).split('.').map(Number);
  const next = `${major}.${minor}.${patch + step}`;
  write(path.join(workspaceRoot, 'VERSION'), `${next}\n`);
  return next;
};

test('the next release ships its own App, keeps the stored Interop Package, and stays self-updatable', async () => {
  const f = fixture();
  const store = new MemoryStore();
  await buildAndroidRelease(f.options);
  const promotedFragment = await upload(f, store);
  await promote(f, store);

  const next = nextVersion(f.workspaceRoot);
  const manifest = await buildAndroidRelease(f.options);
  expect(f.gradleCalls.map((call) => call.versionCode)).toEqual([
    androidAppVersionCode(version(f.workspaceRoot)) - 1,
    androidAppVersionCode(next)
  ]);
  expect(manifest.adaptations.publisher.interop.key).toBe(
    objectKeyOf(promotedFragment.interop.url)
  );
  const log = vi.fn();
  const before = store.writes.length;
  const streamed: string[] = [];
  const putFile = store.putFile.bind(store);
  store.putFile = async (key, ...rest) => {
    streamed.push(key);
    return putFile(key, ...rest);
  };
  const fragment = await upload(f, store, { log });
  // The same snapshot is the same object: nothing to upload for it, not even
  // a refused transfer.
  const payloads = ANDROID_SOURCES.map((source) =>
    androidManagedPayloadKey(next, source)
  );
  expect(store.writes.slice(before)).toEqual([
    androidAppKey(next),
    ...payloads,
    ...payloads.map((key) => `${key}.sig`),
    fragmentKey(next, KEY)
  ]);
  expect(streamed).toEqual([androidAppKey(next), ...payloads]);
  expect(log).toHaveBeenCalledWith(
    `Already stored: ${manifest.adaptations.publisher.interop.key}`
  );
  expect(fragment.nonManagedPayloadDigest).toBe(
    promotedFragment.nonManagedPayloadDigest
  );
  // Installed Apps take it as a Managed Payload; a new install gets its App.
  expect(fragment.selfUpdatable).toBe(true);
});

const objectKeyOf = (url: string): string =>
  url.slice(`${RELEASE_BASE_URL}/`.length);

test('upload refuses a build from another commit or a dirty checkout', async () => {
  const f = fixture();
  const store = new MemoryStore();
  await buildAndroidRelease(f.options);
  await expect(
    upload(f, store, {
      gitState: () => ({ commit: 'd'.repeat(40), dirty: false })
    })
  ).rejects.toThrow(/commit mismatch/);
  await expect(
    upload(f, store, { gitState: () => ({ commit: COMMIT, dirty: true }) })
  ).rejects.toThrow(/dirty/);
  expect(store.writes).toEqual([]);
});

test('a native library change alone is a new non-managed payload, so installed Apps need the new App', async () => {
  const f = fixture();
  const store = new MemoryStore();
  await buildAndroidRelease(f.options);
  const first = await upload(f, store);
  await promote(f, store);

  nextVersion(f.workspaceRoot);
  const entry = f.upgradeNativeLib('sqlite elf 3.53.5');
  const manifest = await buildAndroidRelease(f.options);
  const fragment = await upload(f, store);
  expect(fragment.nonManagedPayloadDigest).toBe(
    androidNonManagedPayloadDigest({ ...entry, updaterSha256: UPDATER_SHA256 })
  );
  expect(fragment.nonManagedPayloadDigest).not.toBe(
    first.nonManagedPayloadDigest
  );
  // The new native library needs the new App: no self-update onto it.
  expect(fragment.selfUpdatable).toBe(false);
  const apk = path.join(androidPaths(f.workspaceRoot).root, manifest.app.path);
  expect(
    execFileSync('unzip', ['-p', apk, 'lib/arm64-v8a/libe_sqlite3.so'], {
      encoding: 'utf8'
    })
  ).toBe('sqlite elf 3.53.5');
});

test('an upload that stopped before its fragment completes by uploading the same build again', async () => {
  const f = fixture();
  const store = new MemoryStore();
  const manifest = await buildAndroidRelease(f.options);
  const current = version(f.workspaceRoot);
  store.beforePut = async (key) => {
    if (key === fragmentKey(current, KEY)) throw new Error('the job timed out');
  };
  await expect(upload(f, store)).rejects.toThrow(/timed out/);
  expect(store.objects.has(manifest.app.key)).toBe(true);

  store.beforePut = undefined;
  await upload(f, store);
  expect(store.writes.at(-1)).toBe(fragmentKey(current, KEY));

  // A rebuilt App has other bytes; the stored one is never replaced.
  const other = new MemoryStore();
  await other.put(manifest.app.key, Buffer.from('another APK'), {
    ifNoneMatch: true
  });
  await expect(upload(f, other)).rejects.toThrow(
    `Immutable release object differs: ${manifest.app.key}`
  );
  // Nor are other bytes at the Interop Package's content address.
  const poisoned = new MemoryStore();
  await poisoned.put(
    manifest.adaptations.publisher.interop.key,
    Buffer.from('not the snapshot'),
    {
      ifNoneMatch: true
    }
  );
  await expect(upload(f, poisoned)).rejects.toThrow(
    `Immutable release object differs: ${manifest.adaptations.publisher.interop.key}`
  );
  expect(poisoned.writes).not.toContain(fragmentKey(current, KEY));
});

test('build refuses an App signed by another key than the pinned release key', async () => {
  const f = fixture();
  await expect(
    buildAndroidRelease({
      ...f.options,
      buildTools: f.buildTools(keys.stranger.sha256)
    })
  ).rejects.toThrow(
    `The Android App is signed by ${keys.stranger.sha256}, not the release key ${keys.release.sha256}`
  );
  expect(fs.existsSync(androidPaths(f.workspaceRoot).artifactManifest)).toBe(
    false
  );
});

test('upload refuses a build receipt that names another signer than the pinned release key', async () => {
  const f = fixture();
  const store = new MemoryStore();
  await buildAndroidRelease(f.options);
  const receipt = androidPaths(f.workspaceRoot).artifactManifest;
  const manifest = JSON.parse(fs.readFileSync(receipt, 'utf8'));
  manifest.app.signerSha256 = keys.stranger.sha256;
  fs.writeFileSync(receipt, JSON.stringify(manifest));
  await expect(upload(f, store)).rejects.toThrow(
    `signed by ${keys.stranger.sha256}, not the release key ${keys.release.sha256}`
  );
  expect(store.writes).toEqual([]);
});

test('build refuses an App whose packaged runtime facts differ from the staged ones', async () => {
  const f = fixture();
  await expect(
    buildAndroidRelease({
      ...f.options,
      assemble: fakeGradle([], (entries) =>
        entries.map((entry) =>
          entry.name === `assets/${RUNTIME_FACTS}`
            ? {
                ...entry,
                data: Buffer.from(
                  JSON.stringify({
                    ...JSON.parse(String(entry.data)),
                    nonManagedPayloadDigest: 'f'.repeat(64)
                  })
                )
              }
            : entry
        )
      )
    })
  ).rejects.toThrow(
    `names nonManagedPayloadDigest ${'f'.repeat(64)}, not the staged`
  );
  await expect(
    buildAndroidRelease({
      ...f.options,
      assemble: fakeGradle([], (entries) =>
        entries.filter((entry) => entry.name !== 'lib/arm64-v8a/libmain.so')
      )
    })
  ).rejects.toThrow(
    'The Android App lacks lib/arm64-v8a/libmain.so that release/payload.json gives it'
  );
});

test('build refuses to sign the interop statement with a keystore whose certificate did not sign the App', async () => {
  const f = fixture();
  const exported: string[] = [];
  await expect(
    buildAndroidRelease({
      ...f.options,
      releaseKey: releaseKeyFrom(keys.stranger, exported)
    })
  ).rejects.toThrow(
    `The keystore's certificate ${keys.stranger.sha256} is not the ${keys.release.sha256} that signed the Android App`
  );
  expect(fs.existsSync(path.dirname(exported[0]))).toBe(false);
  expect(fs.existsSync(androidPaths(f.workspaceRoot).artifactManifest)).toBe(
    false
  );
});

test('the signer digest is the one certificate apksigner reports under either label', () => {
  const a = 'a'.repeat(64);
  const b = 'b'.repeat(64);
  const line = (label: string, digest: string): string =>
    `${label} certificate SHA-256 digest: ${digest}`;
  expect(signerCertificateSha256(line('Signer #1', a), 'x.apk')).toBe(a);
  expect(
    signerCertificateSha256(
      [line('V3.0 Signer:', a), line('V3.1 Signer:', a)].join('\n'),
      'x.apk'
    )
  ).toBe(a);
  expect(() =>
    signerCertificateSha256(
      [line('V3.0 Signer:', a), line('V3.1 Signer:', b)].join('\n'),
      'x.apk'
    )
  ).toThrow('x.apk carries 2 signer certificates');
  expect(() => signerCertificateSha256('Verified', 'x.apk')).toThrow(
    'apksigner verify printed no signer certificate digest for x.apk'
  );
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
    /hashes to .*, not the .* lock entry android-publisher records/
  );
  expect(fetchImpl).toHaveBeenCalledTimes(1);
  await expect(
    resolveNativeLib({ name: '../escape.so', pin, into: root, nugetRoot })
  ).rejects.toThrow(/Invalid native library name/);
});

test('the signing key and the build-tools resolve from the staged secrets and the locked or newest SDK build-tools', () => {
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
  for (const version of ['34.0.0', '35.0.1', '36.1.0', '37.0.0'])
    for (const tool of ['zipalign', 'apksigner'])
      script(path.join(sdk, 'build-tools', version, tool), 'exit 0');
  const env = { PATH: '', ANDROID_HOME: sdk };
  expect(resolveBuildTools(env, { lockedVersion: null })).toEqual({
    zipalign: path.join(sdk, 'build-tools/37.0.0/zipalign'),
    apksigner: path.join(sdk, 'build-tools/37.0.0/apksigner')
  });
  // The App's toolchain lock wins over a newer install.
  expect(resolveBuildTools(env, { lockedVersion: '36.1.0' })).toEqual({
    zipalign: path.join(sdk, 'build-tools/36.1.0/zipalign'),
    apksigner: path.join(sdk, 'build-tools/36.1.0/apksigner')
  });
  for (const version of ['37.0.0', '36.1.0', '35.0.1'])
    fs.rmSync(path.join(sdk, 'build-tools', version), { recursive: true });
  expect(() => resolveBuildTools(env, { lockedVersion: '34.0.0' })).toThrow(
    /zipalign not found on PATH or in ANDROID_HOME\/build-tools\/<35\+>/
  );
});

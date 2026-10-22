import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { readProductVersion } from './product.mjs';
import {
  platformInventory,
  payloadEntry,
  requiredPayloadPaths,
  synchronizePayloadProjection
} from './payload-inventory.mjs';
import { listPayloadFiles, writeDeterministicZip } from './payload-zip.mjs';
import {
  assertReleaseBuildArgs,
  acquirePayloadLock,
  computePayloadInputs
} from './payload.mjs';
import {
  INTEROP_KEY,
  fetchInteropSnapshot,
  fetchLockedAndroidInput,
  gameLibsStoreFromEnvironment,
  interopDirectoryDigest,
  interopDirectoryRecords,
  isLockedLoader,
  lockedInteropEntry
} from './game-libs.mjs';
import { sha256File } from './android-apk.mjs';
import { releaseSourceIdentity } from './artifact-manifest.mjs';
import { readHistoryDatabaseContract } from './history-database.mjs';
import {
  androidApkKey,
  androidNonManagedPayloadDigest,
  managedPayloadKey
} from './manifest.mjs';
import { releasePlatform } from './release-platforms.mjs';

// The Android release (ADR 0010, docs/release.md "Android 发布"): prepare
// compiles the Android host against the locked Interop Snapshot and stages the
// Android Mod Payload; build zips and signs that Payload as the Managed Payload
// zip and, unless the APK of the locked (game version, loader, interop, native
// libraries) is already published, injects the official APK and signs it with
// the BPP Android release key (ADR 0009); upload (publish.mjs) streams both to
// R2.
// Every output lives under target/android/ at the workspace root, outside the
// release inputs, and is replaced as a whole by the next run.

const PLATFORM = 'android';
const sha256 = (bytes) =>
  crypto.createHash('sha256').update(bytes).digest('hex');
const jsonBytes = (value) => Buffer.from(`${JSON.stringify(value, null, 2)}\n`);
const readJson = (file) => JSON.parse(fs.readFileSync(file, 'utf8'));

const ANDROID_BUILD_RECORD_SCHEMA_VERSION = 1;
const ANDROID_ARTIFACT_SCHEMA_VERSION = 1;
// The Android host project, relative to bazaarplusplus-mod/.
const ANDROID_HOST_PROJECT =
  'src/BazaarPlusPlus.Android/BazaarPlusPlus.Android.csproj';
// Inventory producers the mod build stages; everything else on Android is
// produced by the APK injection or at runtime.
const MOD_PRODUCERS = new Set(['managed', 'history-contract']);
// The Patcher's `inject --deployment` takes a MelonLoader mirror; the Payload
// stages it under this root.
const DEPLOYMENT_ROOT = 'MelonLoader';
// Where the Patcher bakes the deployment inside the APK
// (LemonLoader.Patcher AndroidPayloadContract.DeploymentRoot).
const APK_DEPLOYMENT_ROOT = 'assets/LemonLoader/deployment';
const APK_LOADER_ENTRY = 'lib/arm64-v8a/libmain.so';
const APK_NATIVE_ROOT = 'lib/arm64-v8a';
// Stored native libraries must sit on 16 KiB page boundaries (zipalign -P).
const PAGE_ALIGNMENT_KIB = '16';
// zipalign -P needs build-tools 35 or newer.
const MIN_BUILD_TOOLS_MAJOR = 35;

export function androidPaths(workspaceRoot) {
  const root = path.join(workspaceRoot, 'target', 'android');
  const key = releasePlatform(PLATFORM).key;
  return {
    root,
    payload: path.join(root, 'payload'),
    buildRecord: path.join(root, 'payload-build.json'),
    artifactManifest: path.join(root, `${key}.json`),
    work: path.join(root, 'work')
  };
}

// The sidecar written beside each published APK: the facts a later fragment
// pointing at the same APK needs, without downloading 2.8 GB, and the
// non-managed payload digest the APK was built with.
export function androidApkRecordKey(apkKey) {
  return `${apkKey}.json`;
}

// The facts of a published Android Release APK its sidecar carries into every
// fragment that points at it.
export const ANDROID_APK_FACTS = Object.freeze([
  'size',
  'sha256',
  'versionCode',
  'signerSha256',
  'builtForProductVersion'
]);

export const androidApkFacts = (record) =>
  Object.fromEntries(ANDROID_APK_FACTS.map((name) => [name, record[name]]));

// The APK identity of a Snapshot Lock android-online entry (ADR 0010).
export function androidApkIdentity(entry) {
  const nonManagedPayloadDigest = androidNonManagedPayloadDigest({
    loader: entry.loader,
    interopSha256: entry.interopSha256,
    nativeLibs: entry.nativeLibs ?? {}
  });
  return {
    key: androidApkKey({
      gameVersion: entry.gameVersion,
      loaderTag: entry.loader.tag,
      nonManagedPayloadDigest
    }),
    nonManagedPayloadDigest
  };
}

export const androidApkKeyForEntry = (entry) => androidApkIdentity(entry).key;

function lockEntryRecord(entry) {
  return {
    key: INTEROP_KEY,
    gameVersion: entry.gameVersion,
    versionCode: entry.versionCode,
    apkSha256: entry.apkSha256,
    interopSha256: entry.interopSha256,
    loader: { tag: entry.loader.tag, sha256: entry.loader.sha256 },
    nativeLibs: entry.nativeLibs ?? {}
  };
}

function msbuildProperty(msbuildArgs, name) {
  const match = msbuildArgs
    .map((arg) => new RegExp(`^(?:-p:|--property:)${name}=(.+)$`).exec(arg))
    .find(Boolean);
  return match ? match[1] : null;
}

const withSlash = (directory) =>
  directory.endsWith(path.sep) ? directory : `${directory}${path.sep}`;

// The Interop Snapshot and Loader Release the host compiles against: the
// locked copies under game-libs/ (fetched when absent), or the -p: overrides,
// which prepare still checks against the lock entry.
async function resolveLockedSnapshot({
  workspaceRoot,
  msbuildArgs,
  gameLibsStore,
  log
}) {
  const interopOverride = msbuildProperty(msbuildArgs, 'InteropPath');
  const loaderOverride = msbuildProperty(msbuildArgs, 'LemonLoaderPath');
  if (interopOverride && loaderOverride)
    return { interopPath: interopOverride, loaderPath: loaderOverride };
  const fetched = await fetchInteropSnapshot({
    workspaceRoot,
    storeFactory: gameLibsStore,
    log
  });
  return {
    interopPath: interopOverride ?? fetched.interopPath,
    loaderPath: loaderOverride ?? fetched.loaderPath
  };
}

function produceWithDotnet({ workspaceRoot, buildArgs }) {
  const modRoot = path.join(workspaceRoot, 'bazaarplusplus-mod');
  execFileSync(
    'dotnet',
    ['build', ANDROID_HOST_PROJECT, '-c', 'Release', '--nologo', ...buildArgs],
    { cwd: modRoot, stdio: 'inherit' }
  );
}

function payloadOutput(payloadRoot) {
  return listPayloadFiles(payloadRoot, { platform: PLATFORM }).map(
    ({ path: name, absolutePath }) => ({
      path: name,
      sha256: sha256(fs.readFileSync(absolutePath))
    })
  );
}

// The staged Android Mod Payload holds every required mod-built file of the
// Android inventory and nothing the inventory does not give to the mod build.
function validateAndroidPayload(payloadRoot, productVersion) {
  const entries = platformInventory(PLATFORM);
  const files = fs.existsSync(payloadRoot)
    ? listPayloadFiles(payloadRoot, { platform: PLATFORM })
    : [];
  for (const { path: name } of files) {
    const entry = payloadEntry(name, entries);
    if (!entry || !MOD_PRODUCERS.has(entry.producer))
      throw new Error(
        `Android Mod Payload holds ${name}, which release/payload.json does not give to the mod build`
      );
  }
  const staged = new Set(files.map((file) => file.path));
  const modBuilt = new Set(
    entries
      .filter((entry) => MOD_PRODUCERS.has(entry.producer))
      .map((entry) => entry.path)
  );
  const missing = requiredPayloadPaths(PLATFORM).filter(
    (name) => modBuilt.has(name) && !staged.has(name)
  );
  if (missing.length)
    throw new Error(
      `Android Mod Payload lacks ${missing.join(', ')}; the Android host build must produce every required MelonLoader entry of release/payload.json`
    );
  const versionFile = path.join(
    payloadRoot,
    DEPLOYMENT_ROOT,
    'Mods',
    'BazaarPlusPlus.version'
  );
  const stagedVersion = fs.readFileSync(versionFile, 'utf8').trim();
  if (stagedVersion !== `${productVersion}.prod`)
    throw new Error(
      `Android Payload version mismatch: expected ${productVersion}.prod, found ${stagedVersion}. Run node release.mjs prepare --platform android.`
    );
  return files;
}

function androidInputs({ workspaceRoot, interopPath }) {
  return computePayloadInputs({
    workspaceRoot,
    gameRecords: interopDirectoryRecords(interopPath)
  });
}

async function prepareUnlocked({
  workspaceRoot,
  msbuildArgs = [],
  resolveSnapshot = resolveLockedSnapshot,
  produce = produceWithDotnet,
  gameLibsStore = gameLibsStoreFromEnvironment,
  log = () => {}
}) {
  assertReleaseBuildArgs(msbuildArgs, PLATFORM);
  synchronizePayloadProjection(workspaceRoot, { check: true });
  const entry = lockedInteropEntry(workspaceRoot);
  const snapshot = await resolveSnapshot({
    workspaceRoot,
    msbuildArgs,
    gameLibsStore,
    log
  });
  const interopPath = fs.realpathSync(snapshot.interopPath);
  const loaderPath = fs.realpathSync(snapshot.loaderPath);
  const digest = interopDirectoryDigest(interopPath);
  if (digest !== entry.interopSha256)
    throw new Error(
      `${interopPath} hashes to ${digest}, not the ${entry.interopSha256} lock entry ${INTEROP_KEY} records; build against the locked Interop Snapshot ('just mod::fetch android online')`
    );
  if (!isLockedLoader(loaderPath, entry.loader))
    throw new Error(
      `${loaderPath} is not the Loader Release ${entry.loader.tag} (${entry.loader.sha256}) lock entry ${INTEROP_KEY} records; build against the locked loader ('just mod::fetch android online')`
    );
  const productVersion = readProductVersion(workspaceRoot);
  const inputs = androidInputs({ workspaceRoot, interopPath });
  const paths = androidPaths(workspaceRoot);
  fs.mkdirSync(paths.root, { recursive: true });
  const stage = fs.mkdtempSync(path.join(paths.root, 'stage-'));
  try {
    const payloadRoot = path.join(stage, 'payload');
    produce({
      workspaceRoot,
      payloadRoot,
      buildArgs: [
        '-p:BuildProductionPackage=true',
        '-p:BppReleasePlatform=android',
        `-p:BppPayloadStagingRoot=${payloadRoot}`,
        `-p:InteropPath=${withSlash(interopPath)}`,
        `-p:LemonLoaderPath=${withSlash(loaderPath)}`
      ]
    });
    if (androidInputs({ workspaceRoot, interopPath }).digest !== inputs.digest)
      throw new Error('Release inputs changed while building the Payload');
    validateAndroidPayload(payloadRoot, productVersion);
    const record = {
      schemaVersion: ANDROID_BUILD_RECORD_SCHEMA_VERSION,
      productVersion,
      platform: PLATFORM,
      interopPath,
      lockEntry: lockEntryRecord(entry),
      inputDigest: inputs.digest,
      inputs: inputs.files,
      output: payloadOutput(payloadRoot)
    };
    fs.rmSync(paths.buildRecord, { force: true });
    fs.rmSync(paths.payload, { recursive: true, force: true });
    fs.renameSync(payloadRoot, paths.payload);
    fs.writeFileSync(paths.buildRecord, jsonBytes(record));
    return record;
  } finally {
    fs.rmSync(stage, { recursive: true, force: true });
  }
}

function installerRoot(workspaceRoot) {
  return path.join(workspaceRoot, 'bazaarplusplus-installer');
}

async function withAndroidLock(workspaceRoot, kind, operation) {
  const { release } = acquirePayloadLock(installerRoot(workspaceRoot), kind);
  try {
    return await operation();
  } finally {
    release();
  }
}

export async function prepareAndroidPayload(options) {
  return withAndroidLock(options.workspaceRoot, 'prepare', () =>
    prepareUnlocked(options)
  );
}

// The sealed prepare record still describes the staged Payload: same product
// version, same inputs, same output bytes.
export function verifyAndroidPayload(workspaceRoot) {
  const paths = androidPaths(workspaceRoot);
  if (!fs.existsSync(paths.buildRecord))
    throw new Error(
      'No Android Payload prepared. Run node release.mjs prepare --platform android.'
    );
  const record = readJson(paths.buildRecord);
  const productVersion = readProductVersion(workspaceRoot);
  if (
    record.schemaVersion !== ANDROID_BUILD_RECORD_SCHEMA_VERSION ||
    record.platform !== PLATFORM ||
    record.productVersion !== productVersion
  )
    throw new Error(
      'Android Payload build identity is stale. Run node release.mjs prepare --platform android.'
    );
  const entry = lockedInteropEntry(workspaceRoot);
  if (
    JSON.stringify(record.lockEntry) !== JSON.stringify(lockEntryRecord(entry))
  )
    throw new Error(
      `Lock entry ${INTEROP_KEY} changed since preparation. Run node release.mjs prepare --platform android.`
    );
  if (
    androidInputs({ workspaceRoot, interopPath: record.interopPath }).digest !==
    record.inputDigest
  )
    throw new Error(
      'Android Payload build inputs changed. Run node release.mjs prepare --platform android.'
    );
  if (
    JSON.stringify(payloadOutput(paths.payload)) !==
    JSON.stringify(record.output)
  )
    throw new Error(
      'Android Payload output changed since preparation. Run node release.mjs prepare --platform android.'
    );
  validateAndroidPayload(paths.payload, productVersion);
  return record;
}

function run(command, args, { cwd, env, quiet = false } = {}) {
  try {
    return execFileSync(command, args, {
      cwd,
      env: env ?? process.env,
      encoding: 'utf8',
      maxBuffer: 64 * 1024 * 1024,
      stdio: ['ignore', 'pipe', quiet ? 'pipe' : 2]
    });
  } catch (error) {
    throw new Error(
      `${path.basename(command)} ${args[0] ?? ''} failed: ${String(error.stderr || error.message).trim()}`
    );
  }
}

// Signs a file with the Tauri updater key (minisign) through the installer's
// pinned @tauri-apps/cli, as the Tauri updater packages are signed; returns
// the signature text the fragment carries. Reads the key from
// BPP_SIGNING_SECRETS_DIR/tauri-updater.key, else TAURI_SIGNING_PRIVATE_KEY.
export function signWithUpdaterKey({ workspaceRoot, file, env = process.env }) {
  const tauri = path.join(
    installerRoot(workspaceRoot),
    'node_modules',
    '.bin',
    process.platform === 'win32' ? 'tauri.cmd' : 'tauri'
  );
  if (!fs.existsSync(tauri))
    throw new Error(
      `Signing the Managed Payload zip needs the installer's Tauri CLI; run npm ci in bazaarplusplus-installer (missing ${tauri})`
    );
  const secrets = env.BPP_SIGNING_SECRETS_DIR;
  const keyFile = secrets ? path.join(secrets, 'tauri-updater.key') : '';
  const passwordFile = secrets
    ? path.join(secrets, 'tauri-updater.password')
    : '';
  const signing = { ...env };
  delete signing.TAURI_SIGNING_PRIVATE_KEY_PATH;
  if (keyFile && fs.existsSync(keyFile)) {
    delete signing.TAURI_SIGNING_PRIVATE_KEY;
    signing.TAURI_SIGNING_PRIVATE_KEY_PATH = keyFile;
  } else if (!env.TAURI_SIGNING_PRIVATE_KEY)
    throw new Error(
      'Signing the Managed Payload zip needs the Tauri updater key: BPP_SIGNING_SECRETS_DIR/tauri-updater.key or TAURI_SIGNING_PRIVATE_KEY'
    );
  if (passwordFile && fs.existsSync(passwordFile))
    signing.TAURI_SIGNING_PRIVATE_KEY_PASSWORD = fs
      .readFileSync(passwordFile, 'utf8')
      .replace(/\n+$/, '');
  signing.TAURI_SIGNING_PRIVATE_KEY_PASSWORD ??= '';
  fs.rmSync(`${file}.sig`, { force: true });
  run(tauri, ['signer', 'sign', file], { env: signing, quiet: true });
  return fs.readFileSync(`${file}.sig`, 'utf8').trim();
}

// The BPP Android release keystore (ADR 0009) staged in BPP_SIGNING_SECRETS_DIR
// by `node scripts/workspace.mjs run signing` or release-secrets.sh stage.
export function androidSigningKey(env = process.env) {
  const directory = env.BPP_SIGNING_SECRETS_DIR;
  const files = {
    keystore: 'android-release.p12',
    password: 'android-keystore-password',
    alias: 'android-key-alias'
  };
  const missing = directory
    ? Object.values(files).filter(
        (name) =>
          !fs.existsSync(path.join(directory, name)) ||
          fs.statSync(path.join(directory, name)).size === 0
      )
    : Object.values(files);
  if (missing.length)
    throw new Error(
      `Signing the Android Release APK needs ${missing.join(', ')} in BPP_SIGNING_SECRETS_DIR: keys/android-release.p12 and config.ini [signing] ANDROID_KEYSTORE_PASSWORD and ANDROID_KEY_ALIAS locally, the ANDROID_KEYSTORE* secrets in release.yml (docs/release.md)`
    );
  return {
    keystore: path.join(directory, files.keystore),
    passwordFile: path.join(directory, files.password),
    alias: fs
      .readFileSync(path.join(directory, files.alias), 'utf8')
      .replace(/\n+$/, '')
  };
}

// zipalign and apksigner: on PATH, else the newest build-tools of the Android
// SDK that supports page alignment.
export function resolveBuildTools(env = process.env) {
  const onPath = (name) => {
    for (const directory of (env.PATH ?? '').split(path.delimiter)) {
      const candidate = path.join(directory, name);
      if (directory && fs.existsSync(candidate)) return candidate;
    }
    return null;
  };
  const sdk = env.ANDROID_HOME || env.ANDROID_SDK_ROOT;
  const fromSdk = (name) => {
    const root = sdk ? path.join(sdk, 'build-tools') : null;
    if (!root || !fs.existsSync(root)) return null;
    const versions = fs
      .readdirSync(root)
      .filter(
        (version) => Number.parseInt(version, 10) >= MIN_BUILD_TOOLS_MAJOR
      )
      .sort((a, b) => a.localeCompare(b, 'en', { numeric: true }))
      .reverse();
    for (const version of versions) {
      const candidate = path.join(root, version, name);
      if (fs.existsSync(candidate)) return candidate;
    }
    return null;
  };
  const tool = (name) => {
    const found = onPath(name) ?? fromSdk(name);
    if (!found)
      throw new Error(
        `${name} not found on PATH or in ANDROID_HOME/build-tools/<${MIN_BUILD_TOOLS_MAJOR}+>; install the Android SDK build-tools`
      );
    return found;
  };
  return { zipalign: tool('zipalign'), apksigner: tool('apksigner') };
}

function defaultNugetRoot(env = process.env) {
  return env.NUGET_PACKAGES || path.join(os.homedir(), '.nuget', 'packages');
}

// One locked native library (nativeLibs of the lock entry) as a verified file:
// the NuGet cache when a restore put it there, else the package downloaded
// from nuget.org. Either way its sha256 must be the locked one.
export async function resolveNativeLib({
  name,
  pin,
  into,
  nugetRoot = defaultNugetRoot(),
  fetchImpl = fetch
}) {
  if (!/^[A-Za-z0-9._-]+\.so$/.test(name))
    throw new Error(
      `Invalid native library name in lock entry ${INTEROP_KEY}: ${name}`
    );
  const id = pin.package.toLowerCase();
  const entry = `runtimes/android-arm64/native/${name}`;
  const cached = path.join(nugetRoot, id, pin.version, entry);
  const target = path.join(into, name);
  fs.mkdirSync(into, { recursive: true });
  if (fs.existsSync(cached)) fs.copyFileSync(cached, target);
  else {
    const url = `https://api.nuget.org/v3-flatcontainer/${id}/${pin.version}/${id}.${pin.version}.nupkg`;
    const response = await fetchImpl(url, { redirect: 'follow' });
    if (!response.ok)
      throw new Error(`Downloading ${url} failed with HTTP ${response.status}`);
    const nupkg = path.join(into, `${id}.${pin.version}.nupkg`);
    fs.writeFileSync(nupkg, Buffer.from(await response.arrayBuffer()));
    try {
      fs.writeFileSync(
        target,
        execFileSync('unzip', ['-p', nupkg, entry], {
          maxBuffer: 256 * 1024 * 1024
        })
      );
    } finally {
      fs.rmSync(nupkg, { force: true });
    }
  }
  const digest = await sha256File(target);
  if (digest !== pin.sha256)
    throw new Error(
      `${pin.package} ${pin.version} ${name} hashes to ${digest}, not the ${pin.sha256} lock entry ${INTEROP_KEY} records`
    );
  return target;
}

// The pinned injection inputs of the lock entry as verified local files.
async function resolveInjectionInputs({
  workspaceRoot,
  entry,
  into,
  gameLibsStore,
  log
}) {
  const input = (kind) =>
    fetchLockedAndroidInput({
      workspaceRoot,
      kind,
      storeFactory: gameLibsStore,
      log
    });
  const nativeLibs = {};
  for (const [name, pin] of Object.entries(entry.nativeLibs ?? {}))
    nativeLibs[name] = await resolveNativeLib({ name, pin, into });
  return {
    officialApk: await input('apk'),
    loaderZip: await input('loader'),
    patcherArchive: await input('patcher'),
    nativeLibs
  };
}

function apkEntries(apk) {
  return new Set(
    run('unzip', ['-Z1', apk], { quiet: true }).split('\n').filter(Boolean)
  );
}

// Injects the official APK with the Patcher's `inject` (Loader Release,
// interop, and the staged Payload as deployment), adds the locked native
// libraries under lib/arm64-v8a/ (the Patcher packs only libmain.so there),
// then page-aligns with zipalign and signs with apksigner. Each intermediate
// APK is removed as soon as the next exists: one APK is about 2.8 GB.
export function injectAndroidApk({
  officialApk,
  loaderZip,
  patcherArchive,
  interopPath,
  deployment,
  nativeLibs,
  signing,
  tools,
  output,
  work,
  hostPlatform = process.platform,
  hostArch = process.arch
}) {
  if (hostPlatform !== 'linux' || hostArch !== 'x64')
    throw new Error(
      `APK injection runs the LemonLoader Patcher's Linux x64 CLI, and this is ${hostPlatform}-${hostArch}; build Android on Linux x64 (release.yml runs it on ubuntu-latest)`
    );
  fs.rmSync(work, { recursive: true, force: true });
  fs.mkdirSync(work, { recursive: true });
  const patcher = path.join(work, 'patcher');
  fs.mkdirSync(patcher);
  run('tar', ['-xzf', path.resolve(patcherArchive), '-C', patcher]);
  const injected = path.join(work, 'injected.apk');
  run(
    path.join(patcher, 'LemonLoader.Patcher.CLI'),
    [
      'inject',
      officialApk,
      '--output',
      injected,
      '--interop',
      interopPath,
      '--release',
      loaderZip,
      '--deployment',
      deployment
    ],
    { env: { ...process.env, TMPDIR: work } }
  );
  fs.rmSync(patcher, { recursive: true, force: true });
  const nativeRoot = path.join(work, 'native');
  const names = Object.keys(nativeLibs).sort();
  if (names.length) {
    fs.mkdirSync(path.join(nativeRoot, APK_NATIVE_ROOT), { recursive: true });
    for (const name of names)
      fs.copyFileSync(
        nativeLibs[name],
        path.join(nativeRoot, APK_NATIVE_ROOT, name)
      );
    // Stored, not deflated: the platform maps native libraries in place.
    run(
      'zip',
      [
        '-0',
        '-X',
        injected,
        ...names.map((name) => `${APK_NATIVE_ROOT}/${name}`)
      ],
      { cwd: nativeRoot, quiet: true }
    );
  }
  const aligned = path.join(work, 'aligned.apk');
  run(tools.zipalign, ['-P', PAGE_ALIGNMENT_KIB, '-f', '4', injected, aligned]);
  fs.rmSync(injected);
  run(tools.zipalign, ['-c', '-P', PAGE_ALIGNMENT_KIB, '4', aligned]);
  fs.mkdirSync(path.dirname(output), { recursive: true });
  fs.rmSync(output, { force: true });
  run(tools.apksigner, [
    'sign',
    '--ks',
    signing.keystore,
    '--ks-type',
    'PKCS12',
    '--ks-key-alias',
    signing.alias,
    '--ks-pass',
    `file:${signing.passwordFile}`,
    '--key-pass',
    `file:${signing.passwordFile}`,
    '--out',
    output,
    aligned
  ]);
  fs.rmSync(aligned);
  const certificates = run(tools.apksigner, [
    'verify',
    '--print-certs',
    output
  ]);
  const signer = /Signer #1 certificate SHA-256 digest: ([0-9a-f]{64})/.exec(
    certificates
  );
  if (!signer)
    throw new Error(
      `apksigner verify printed no signer certificate digest for ${output}`
    );
  const entries = apkEntries(output);
  const expected = [
    APK_LOADER_ENTRY,
    ...names.map((name) => `${APK_NATIVE_ROOT}/${name}`),
    ...listPayloadFiles(deployment).map(
      ({ path: name }) => `${APK_DEPLOYMENT_ROOT}/${name}`
    )
  ];
  const missing = expected.filter((name) => !entries.has(name));
  if (missing.length)
    throw new Error(
      `The injected APK lacks ${missing.join(', ')}; the Patcher or the native library step did not produce them`
    );
  fs.rmSync(work, { recursive: true, force: true });
  return { signerSha256: signer[1] };
}

// The APK published under an identity: null when nothing is stored at its
// key; { object, record } otherwise, where `record` is the sidecar and is null
// when an upload stopped between the APK and its sidecar (upload completes it).
export async function publishedAndroidApk(
  store,
  { key, nonManagedPayloadDigest }
) {
  const object = await store.head(key);
  if (!object) return null;
  const sidecar = await store.get(androidApkRecordKey(key));
  if (!sidecar) return { object, record: null };
  const record = JSON.parse(sidecar.bytes.toString('utf8'));
  if (
    record.size !== object.size ||
    record.sha256 !== object.sha256 ||
    record.nonManagedPayloadDigest !== nonManagedPayloadDigest
  )
    throw new Error(`${androidApkRecordKey(key)} does not describe ${key}`);
  return { object, record };
}

async function buildUnlocked({
  workspaceRoot,
  msbuildArgs = [],
  rebuildApk = false,
  releaseStore,
  resolveSnapshot,
  produce,
  gameLibsStore = gameLibsStoreFromEnvironment,
  resolveInputs = resolveInjectionInputs,
  inject = injectAndroidApk,
  sign = signWithUpdaterKey,
  signingKey = androidSigningKey,
  buildTools = resolveBuildTools,
  captureIdentity = releaseSourceIdentity,
  log = () => {}
}) {
  const rootDir = installerRoot(workspaceRoot);
  const paths = androidPaths(workspaceRoot);
  const startingCommit = captureIdentity(rootDir).commit;
  // An unsuccessful build must not leave a previous publishable receipt.
  fs.rmSync(paths.artifactManifest, { force: true });
  await prepareUnlocked({
    workspaceRoot,
    msbuildArgs,
    resolveSnapshot,
    produce,
    gameLibsStore,
    log
  });
  const identity = captureIdentity(rootDir);
  if (identity.commit !== startingCommit)
    throw new Error('Git identity changed during Payload preparation');
  const record = verifyAndroidPayload(workspaceRoot);
  const version = record.productVersion;
  const entry = lockedInteropEntry(workspaceRoot);
  const key = releasePlatform(PLATFORM).key;

  const zipPath = path.join(
    paths.root,
    path.posix.basename(managedPayloadKey(version, key))
  );
  for (const stale of fs.readdirSync(paths.root))
    if (/\.(?:apk|zip|sig)$/.test(stale))
      fs.rmSync(path.join(paths.root, stale), { force: true });
  const zip = writeDeterministicZip({
    sourceDir: paths.payload,
    outputPath: zipPath,
    platform: PLATFORM
  });
  const signature = sign({ workspaceRoot, file: zipPath });
  log(`Signed ${path.basename(zipPath)}`);

  const apkIdentity = androidApkIdentity(entry);
  const apkKey = apkIdentity.key;
  const published = await publishedAndroidApk(releaseStore(), apkIdentity);
  let apk;
  if (published?.record && !rebuildApk) {
    log(
      `${apkKey} is already published (built for ${published.record.builtForProductVersion}); skipping APK injection`
    );
    apk = { path: null, published: true, ...androidApkFacts(published.record) };
  } else {
    if (published && !published.record)
      log(
        `${apkKey} is on R2 without its sidecar (an interrupted upload); injecting again, and upload completes it only if this APK has the same bytes`
      );
    const apkPath = path.join(paths.root, path.posix.basename(apkKey));
    const signing = signingKey();
    const tools = buildTools();
    const inputs = await resolveInputs({
      workspaceRoot,
      entry,
      into: path.join(paths.root, 'native'),
      gameLibsStore,
      log
    });
    log(`Injecting ${entry.gameVersion} with ${entry.loader.tag}`);
    const { signerSha256 } = inject({
      ...inputs,
      interopPath: record.interopPath,
      deployment: path.join(paths.payload, DEPLOYMENT_ROOT),
      signing,
      tools,
      output: apkPath,
      work: paths.work
    });
    fs.rmSync(path.join(paths.root, 'native'), {
      recursive: true,
      force: true
    });
    apk = {
      path: path.relative(paths.root, apkPath),
      published: Boolean(published?.record),
      size: fs.statSync(apkPath).size,
      sha256: await sha256File(apkPath),
      versionCode: entry.versionCode,
      signerSha256,
      builtForProductVersion: version
    };
  }
  verifyAndroidPayload(workspaceRoot);
  if (JSON.stringify(captureIdentity(rootDir)) !== JSON.stringify(identity))
    throw new Error(
      'Product source or Git identity changed during packaging; no artifact manifest was written'
    );
  const contract = readHistoryDatabaseContract(
    path.join(
      paths.payload,
      DEPLOYMENT_ROOT,
      'Mods',
      'BazaarPlusPlus.history-database.json'
    )
  );
  const manifest = {
    schemaVersion: ANDROID_ARTIFACT_SCHEMA_VERSION,
    platform: key,
    buildPlatform: PLATFORM,
    appVersion: version,
    gitCommit: identity.commit,
    dirty: identity.dirty,
    managedPayload: {
      path: path.relative(paths.root, zipPath),
      size: fs.statSync(zipPath).size,
      sha256: zip.sha256,
      signature
    },
    apk: { key: apkKey, ...apk },
    historyDatabaseUserVersion: contract.historyDatabaseUserVersion,
    nonManagedPayloadDigest: apkIdentity.nonManagedPayloadDigest,
    game: {
      gameVersion: entry.gameVersion,
      versionCode: entry.versionCode,
      officialApkSha256: entry.apkSha256
    },
    loader: { tag: entry.loader.tag, sha256: entry.loader.sha256 },
    interopSha256: entry.interopSha256,
    lockEntry: record.lockEntry
  };
  fs.writeFileSync(paths.artifactManifest, jsonBytes(manifest));
  return manifest;
}

export async function buildAndroidRelease(options) {
  return withAndroidLock(options.workspaceRoot, 'build', () =>
    buildUnlocked(options)
  );
}

// The build receipt upload publishes, checked against the checkout and the
// files it names.
export async function readAndroidArtifacts(workspaceRoot) {
  const paths = androidPaths(workspaceRoot);
  if (!fs.existsSync(paths.artifactManifest))
    throw new Error(
      'No Android build to upload. Run node release.mjs build --platform android.'
    );
  const manifest = readJson(paths.artifactManifest);
  const version = readProductVersion(workspaceRoot);
  if (
    manifest.schemaVersion !== ANDROID_ARTIFACT_SCHEMA_VERSION ||
    manifest.buildPlatform !== PLATFORM ||
    manifest.appVersion !== version
  )
    throw new Error(
      `The Android build is not ${version}. Run node release.mjs build --platform android.`
    );
  if (manifest.dirty)
    throw new Error(
      'The Android build came from a dirty checkout; commit, then build again'
    );
  const file = (relative, record, what) => {
    const absolute = path.join(paths.root, relative);
    if (!fs.existsSync(absolute) || fs.statSync(absolute).size !== record.size)
      throw new Error(`${what} changed or is missing: ${absolute}`);
    return absolute;
  };
  const files = {
    managedPayload: file(
      manifest.managedPayload.path,
      manifest.managedPayload,
      'Managed Payload zip'
    ),
    apk: manifest.apk.path
      ? file(manifest.apk.path, manifest.apk, 'Android Release APK')
      : null
  };
  for (const [name, absolute] of Object.entries(files))
    if (absolute && (await sha256File(absolute)) !== manifest[name].sha256)
      throw new Error(`Artifact changed before upload: ${absolute}`);
  return { manifest, files };
}

import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { RELEASE_BASE_URL, readProductVersion } from './product.ts';
import {
  platformInventory,
  payloadEntry,
  requiredPayloadPaths,
  synchronizePayloadProjection
} from './payload-inventory.ts';
import { listPayloadFiles } from './payload-zip.ts';
import {
  MANAGED_PRODUCERS,
  readStagedManagedPayload,
  signWithUpdaterKey,
  writeManagedPayloadManifest,
  writeManagedPayloadZip
} from './managed-payload.ts';
import {
  assertReleaseBuildArgs,
  acquirePayloadLock,
  computePayloadInputs
} from './payload.ts';
import {
  INTEROP_KEY,
  fetchInteropSnapshot,
  gameLibsStoreFromEnvironment,
  interopDirectoryDigest,
  interopDirectoryRecords,
  isLockedLoader,
  lockedInteropEntry
} from './game-libs.ts';
import { sha256File } from './android-apk.ts';
import {
  CARRIED_RELEASE_DIR,
  CARRIED_RELEASE_FILE,
  RUNTIME_FACTS,
  buildInteropPackage,
  stageAndroidAppRuntime,
  stageCarriedRelease
} from './android-app.ts';
import {
  exportReleaseKey,
  interopStatement,
  releasePurpose,
  signWithReleaseKey
} from './android-interop.ts';
import { releaseSourceIdentity } from './artifact-manifest.ts';
import {
  ANDROID_RELEASE_SIGNER_SHA256,
  androidAppKey,
  androidCarriedRelease,
  androidAppVersionCode,
  androidNonManagedPayloadDigest,
  interopPackageKey,
  managedPayloadKey
} from './manifest.ts';
import { releasePlatform } from './release-platforms.ts';
import type { ProductVersion } from './product.ts';
import type { ReleasePlatformKey } from './release-platforms.ts';
import type { ObjectStore } from './r2-store.ts';
import type { RuntimeFacts } from './android-app.ts';
import type { ReleaseKeyExporter, ReleaseKeystore } from './android-interop.ts';
import type { InteropLockEntry, LoaderPin, NativeLibPin } from './game-libs.ts';
import type { PayloadInputRecord } from './payload.ts';
import type { AndroidCarriedRelease } from './manifest.ts';
import type { GitState } from './artifact-manifest.ts';

// The Android release (docs/adr/0012-android-app.md, docs/release.md
// "Android 发布"): prepare compiles the Android host against the locked
// Interop Snapshot and stages the Android Mod Payload; build zips and signs
// that Payload as the Managed Payload zip, stages the Android App Runtime,
// builds the Android App with Gradle and signs it with the BPP Android release
// key (ADR 0009), and packs the Interop Package and signs its interop
// statement with the same key; upload (publish.ts) streams the three
// artifacts to R2. Every output lives under target/android/ at the workspace
// root, outside the release inputs, and is replaced as a whole by the next
// run; key material only ever sits in a temporary directory outside it.

const PLATFORM = 'android';
const sha256 = (bytes: string | NodeJS.ArrayBufferView): string =>
  crypto.createHash('sha256').update(bytes).digest('hex');
const jsonBytes = (value: unknown): Buffer =>
  Buffer.from(`${JSON.stringify(value, null, 2)}\n`);
const readJson = (file: string): unknown =>
  JSON.parse(fs.readFileSync(file, 'utf8'));

const ANDROID_BUILD_RECORD_SCHEMA_VERSION = 2;
const ANDROID_ARTIFACT_SCHEMA_VERSION = 2;
// The Android host project, relative to bazaarplusplus-mod/.
const ANDROID_HOST_PROJECT =
  'src/BazaarPlusPlus.Android/BazaarPlusPlus.Android.csproj';
// The Android App project, relative to the workspace root.
const ANDROID_APP_ROOT = 'bazaarplusplus-android';
// Inventory producers the mod build stages: the Managed Payload, the Updater
// plugin, and the Managed Payload Manifest prepare writes beside them;
// everything else on Android is the Android App's or written at runtime.
const MOD_PRODUCERS: ReadonlySet<string> = new Set([
  ...MANAGED_PRODUCERS,
  'updater',
  'managed-manifest'
]);
// What MSBuild produces; prepare writes the manifest after it.
const BUILD_PRODUCERS: ReadonlySet<string> = new Set([
  ...MANAGED_PRODUCERS,
  'updater'
]);
// The staged Payload mirrors the App's external files directory.
const MOD_ROOT = 'MelonLoader';
// zipalign -P (page-aligned stored native libraries) needs build-tools 35 or
// newer.
const PAGE_ALIGNMENT_KIB = '16';
const MIN_BUILD_TOOLS_MAJOR = 35;

// Every output of the Android release under target/android/.
export interface AndroidPaths {
  root: string;
  payload: string;
  buildRecord: string;
  artifactManifest: string;
  runtime: string;
  work: string;
}

// The lock entry a prepare record seals.
export interface AndroidLockEntryRecord {
  key: string;
  gameVersion: string;
  versionCode: number;
  il2cppSha256: string;
  metadataSha256: string;
  interopSha256: string;
  loader: Pick<LoaderPin, 'tag' | 'sha256'>;
  nativeLibs: Record<string, NativeLibPin>;
}

// One file of the staged Android Mod Payload.
export interface PayloadOutputRecord {
  path: string;
  sha256: string;
}

// target/android/payload-build.json: what prepare sealed.
export interface AndroidBuildRecord {
  schemaVersion: number;
  productVersion: ProductVersion;
  platform: 'android';
  interopPath: string;
  loaderPath: string;
  lockEntry: AndroidLockEntryRecord;
  inputDigest: string;
  inputs: PayloadInputRecord[];
  output: PayloadOutputRecord[];
}

// The Interop Snapshot and Loader Release directories the host compiles
// against.
export interface LockedSnapshot {
  interopPath: string;
  loaderPath: string;
}

export type GameLibsStoreFactory = () => ObjectStore;
export type ReleaseLog = (line: string) => void;

export interface ResolveSnapshotOptions {
  workspaceRoot: string;
  msbuildArgs: readonly string[];
  gameLibsStore: GameLibsStoreFactory;
  log: ReleaseLog;
}

export type ResolveSnapshot = (
  options: ResolveSnapshotOptions
) => Promise<LockedSnapshot>;

export interface ProduceOptions {
  workspaceRoot: string;
  payloadRoot: string;
  buildArgs: string[];
}

export type ProducePayload = (options: ProduceOptions) => void;

export interface PrepareAndroidPayloadOptions {
  workspaceRoot: string;
  msbuildArgs?: readonly string[];
  resolveSnapshot?: ResolveSnapshot;
  produce?: ProducePayload;
  gameLibsStore?: GameLibsStoreFactory;
  log?: ReleaseLog;
}

interface RunOptions {
  cwd?: string;
  env?: NodeJS.ProcessEnv;
  quiet?: boolean;
}

// execFileSync fails with the child's stderr attached.
type RunError = Error & { stderr?: string | Buffer | null };

// The staged Android release keystore, its password file and key alias.
export interface AndroidSigningKey extends ReleaseKeystore {
  alias: string;
}

// The App's toolchain lock, as far as the build-tools lookup reads it.
interface AndroidToolchainLock {
  androidSdk: { buildTools: string };
}

export interface BuildTools {
  zipalign: string;
  apksigner: string;
}

export interface ResolveBuildToolsOptions {
  lockedVersion?: string | null;
}

export type NativeLibFetch = (
  url: string,
  init: RequestInit
) => Promise<Response>;

export interface ResolveNativeLibOptions {
  name: string;
  pin: NativeLibPin;
  into: string;
  nugetRoot?: string;
  fetchImpl?: NativeLibFetch;
}

export interface ResolveLockedNativeLibsOptions {
  entry: InteropLockEntry;
  into: string;
}

export interface AssembleOptions {
  workspaceRoot: string;
  version: ProductVersion;
  versionCode: number;
  runtimeDir: string;
}

export interface SignAndroidAppOptions {
  unsigned: string;
  signing: AndroidSigningKey;
  tools: BuildTools;
  output: string;
  work: string;
}

interface PackInteropOptions {
  root: string;
  interopPath: string;
  entry: InteropLockEntry;
  version: ProductVersion;
  signing: AndroidSigningKey;
  releaseKey: ReleaseKeyExporter;
  signerSha256: string;
}

// The Interop Package beside the APK and its signed statement.
export interface InteropPackageRecord {
  key: string;
  path: string;
  size: number;
  sha256: string;
  snapshotSha256: string;
  signature: string;
  statementPath: string;
}

// signWithUpdaterKey, or a test stand-in: the minisign signature of a file.
export type SignFile = (options: {
  workspaceRoot: string;
  file: string;
}) => string;

export interface BuildAndroidReleaseOptions extends PrepareAndroidPayloadOptions {
  resolveNativeLibs?: (
    options: ResolveLockedNativeLibsOptions
  ) => Promise<Record<string, string>>;
  assemble?: (options: AssembleOptions) => string;
  sign?: SignFile;
  signingKey?: () => AndroidSigningKey;
  buildTools?: () => BuildTools;
  releaseKey?: ReleaseKeyExporter;
  releaseSignerSha256?: string;
  captureIdentity?: (rootDir: string) => GitState;
}

// target/android/android-aarch64.json: the build receipt upload publishes.
export interface AndroidArtifactManifest {
  schemaVersion: number;
  platform: ReleasePlatformKey;
  buildPlatform: 'android';
  appVersion: ProductVersion;
  gitCommit: string;
  dirty: boolean;
  app: {
    key: string;
    path: string;
    size: number;
    sha256: string;
    versionCode: number;
    signerSha256: string;
  };
  interop: InteropPackageRecord;
  managedPayload: {
    path: string;
    size: number;
    sha256: string;
    signature: string;
  };
  game: {
    gameVersion: string;
    versionCode: number;
    il2cppSha256: string;
    metadataSha256: string;
  };
  loader: { tag: string; sha256: string };
  historyDatabaseUserVersion: number;
  nonManagedPayloadDigest: string;
  lockEntry: AndroidLockEntryRecord;
}

// The three artifacts of a build receipt as absolute paths.
export interface AndroidArtifactFiles {
  app: string;
  interop: string;
  managedPayload: string;
}

export interface AndroidArtifacts {
  manifest: AndroidArtifactManifest;
  files: AndroidArtifactFiles;
}

export function androidPaths(workspaceRoot: string): AndroidPaths {
  const root = path.join(workspaceRoot, 'target', 'android');
  const key = releasePlatform(PLATFORM).key;
  return {
    root,
    payload: path.join(root, 'payload'),
    buildRecord: path.join(root, 'payload-build.json'),
    artifactManifest: path.join(root, `${key}.json`),
    runtime: path.join(root, 'runtime'),
    work: path.join(root, 'work')
  };
}

// The non-managed payload digest of a Snapshot Lock android-online entry and
// the Updater plugin the Android App carries (ADR 0012): the Updater ships
// only inside the App, so a changed Updater needs the new App.
export const androidEntryNonManagedPayloadDigest = (
  entry: InteropLockEntry,
  updaterSha256: string
): string =>
  androidNonManagedPayloadDigest({
    loader: entry.loader,
    nativeLibs: entry.nativeLibs ?? {},
    updaterSha256
  });

function stagedUpdater(payloadRoot: string): string {
  const entry = platformInventory(PLATFORM).find(
    (candidate) => candidate.producer === 'updater'
  );
  if (!entry)
    throw new Error('release/payload.json names no Android Updater entry');
  return path.join(payloadRoot, ...entry.path.split('/'));
}

// The SHA-256 of the Updater plugin in a staged Android Payload.
export function stagedUpdaterSha256(payloadRoot: string): string {
  return sha256(fs.readFileSync(stagedUpdater(payloadRoot)));
}

function lockEntryRecord(entry: InteropLockEntry): AndroidLockEntryRecord {
  return {
    key: INTEROP_KEY,
    gameVersion: entry.gameVersion,
    versionCode: entry.versionCode,
    il2cppSha256: entry.il2cppSha256,
    metadataSha256: entry.metadataSha256,
    interopSha256: entry.interopSha256,
    loader: { tag: entry.loader.tag, sha256: entry.loader.sha256 },
    nativeLibs: entry.nativeLibs ?? {}
  };
}

function msbuildProperty(
  msbuildArgs: readonly string[],
  name: string
): string | null {
  const match = msbuildArgs
    .map((arg) => new RegExp(`^(?:-p:|--property:)${name}=(.+)$`).exec(arg))
    .find(Boolean);
  return match ? match[1] : null;
}

const withSlash = (directory: string): string =>
  directory.endsWith(path.sep) ? directory : `${directory}${path.sep}`;

// The Interop Snapshot and Loader Release the host compiles against: the
// locked copies under game-libs/ (fetched when absent), or the -p: overrides,
// which prepare still checks against the lock entry.
async function resolveLockedSnapshot({
  workspaceRoot,
  msbuildArgs,
  gameLibsStore,
  log
}: ResolveSnapshotOptions): Promise<LockedSnapshot> {
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

function produceWithDotnet({ workspaceRoot, buildArgs }: ProduceOptions): void {
  const modRoot = path.join(workspaceRoot, 'bazaarplusplus-mod');
  execFileSync(
    'dotnet',
    ['build', ANDROID_HOST_PROJECT, '-c', 'Release', '--nologo', ...buildArgs],
    { cwd: modRoot, stdio: 'inherit' }
  );
}

function payloadOutput(payloadRoot: string): PayloadOutputRecord[] {
  return listPayloadFiles(payloadRoot, { platform: PLATFORM }).map(
    ({ path: name, absolutePath }) => ({
      path: name,
      sha256: sha256(fs.readFileSync(absolutePath))
    })
  );
}

// The staged Android Mod Payload holds every required mod-built file of the
// Android inventory and nothing the inventory does not give to the mod build.
// `producers` narrows the check to what the host build alone produces,
// before prepare writes the Managed Payload Manifest.
function validateAndroidPayload(
  payloadRoot: string,
  productVersion: ProductVersion,
  producers: ReadonlySet<string> = MOD_PRODUCERS
) {
  const entries = platformInventory(PLATFORM);
  const files = fs.existsSync(payloadRoot)
    ? listPayloadFiles(payloadRoot, { platform: PLATFORM })
    : [];
  for (const { path: name } of files) {
    const entry = payloadEntry(name, entries);
    if (!entry || !producers.has(entry.producer))
      throw new Error(
        `Android Mod Payload holds ${name}, which release/payload.json does not give to the mod build`
      );
  }
  const staged = new Set(files.map((file) => file.path));
  const modBuilt = new Set(
    entries
      .filter((entry) => producers.has(entry.producer))
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
    MOD_ROOT,
    'Mods',
    'BazaarPlusPlus.version'
  );
  const stagedVersion = fs.readFileSync(versionFile, 'utf8').trim();
  if (stagedVersion !== `${productVersion}.prod`)
    throw new Error(
      `Android Payload version mismatch: expected ${productVersion}.prod, found ${stagedVersion}. Run node release.ts prepare --platform android.`
    );
  return files;
}

interface AndroidInputsOptions {
  workspaceRoot: string;
  interopPath: string;
}

function androidInputs({ workspaceRoot, interopPath }: AndroidInputsOptions) {
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
}: PrepareAndroidPayloadOptions): Promise<AndroidBuildRecord> {
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
    validateAndroidPayload(payloadRoot, productVersion, BUILD_PRODUCERS);
    writeManagedPayloadManifest({
      sourceDir: payloadRoot,
      platform: PLATFORM,
      version: productVersion,
      nonManagedPayloadDigest: androidEntryNonManagedPayloadDigest(
        entry,
        stagedUpdaterSha256(payloadRoot)
      )
    });
    validateAndroidPayload(payloadRoot, productVersion);
    const record: AndroidBuildRecord = {
      schemaVersion: ANDROID_BUILD_RECORD_SCHEMA_VERSION,
      productVersion,
      platform: PLATFORM,
      interopPath,
      loaderPath,
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

function installerRoot(workspaceRoot: string): string {
  return path.join(workspaceRoot, 'bazaarplusplus-installer');
}

async function withAndroidLock<T>(
  workspaceRoot: string,
  kind: 'prepare' | 'build',
  operation: () => Promise<T>
): Promise<T> {
  const { release } = acquirePayloadLock(installerRoot(workspaceRoot), kind);
  try {
    return await operation();
  } finally {
    release();
  }
}

export async function prepareAndroidPayload(
  options: PrepareAndroidPayloadOptions
): Promise<AndroidBuildRecord> {
  return withAndroidLock(options.workspaceRoot, 'prepare', () =>
    prepareUnlocked(options)
  );
}

// The sealed prepare record still describes the staged Payload: same product
// version, same inputs, same output bytes.
export function verifyAndroidPayload(
  workspaceRoot: string
): AndroidBuildRecord {
  const paths = androidPaths(workspaceRoot);
  if (!fs.existsSync(paths.buildRecord))
    throw new Error(
      'No Android Payload prepared. Run node release.ts prepare --platform android.'
    );
  const record = readJson(paths.buildRecord) as AndroidBuildRecord;
  const productVersion = readProductVersion(workspaceRoot);
  if (
    record.schemaVersion !== ANDROID_BUILD_RECORD_SCHEMA_VERSION ||
    record.platform !== PLATFORM ||
    record.productVersion !== productVersion
  )
    throw new Error(
      'Android Payload build identity is stale. Run node release.ts prepare --platform android.'
    );
  const entry = lockedInteropEntry(workspaceRoot);
  if (
    JSON.stringify(record.lockEntry) !== JSON.stringify(lockEntryRecord(entry))
  )
    throw new Error(
      `Lock entry ${INTEROP_KEY} changed since preparation. Run node release.ts prepare --platform android.`
    );
  if (
    androidInputs({ workspaceRoot, interopPath: record.interopPath }).digest !==
    record.inputDigest
  )
    throw new Error(
      'Android Payload build inputs changed. Run node release.ts prepare --platform android.'
    );
  if (
    JSON.stringify(payloadOutput(paths.payload)) !==
    JSON.stringify(record.output)
  )
    throw new Error(
      'Android Payload output changed since preparation. Run node release.ts prepare --platform android.'
    );
  validateAndroidPayload(paths.payload, productVersion);
  return record;
}

function run(
  command: string,
  args: string[],
  { cwd, env, quiet = false }: RunOptions = {}
): string {
  try {
    return execFileSync(command, args, {
      cwd,
      env: env ?? process.env,
      encoding: 'utf8',
      maxBuffer: 64 * 1024 * 1024,
      stdio: ['ignore', 'pipe', quiet ? 'pipe' : 2]
    });
  } catch (error) {
    const failure = error as RunError;
    throw new Error(
      `${path.basename(command)} ${args[0] ?? ''} failed: ${String(failure.stderr || failure.message).trim()}`
    );
  }
}

// The BPP Android release keystore (ADR 0009) staged in BPP_SIGNING_SECRETS_DIR
// by `node scripts/workspace.ts run signing` or release-secrets.sh stage.
export function androidSigningKey(
  env: NodeJS.ProcessEnv = process.env
): AndroidSigningKey {
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
  if (!directory || missing.length)
    throw new Error(
      `Signing the Android App needs ${missing.join(', ')} in BPP_SIGNING_SECRETS_DIR: keys/android-release.p12 and config.ini [signing] ANDROID_KEYSTORE_PASSWORD and ANDROID_KEY_ALIAS locally, the ANDROID_KEYSTORE* secrets in release.yml (docs/release.md)`
    );
  return {
    keystore: path.join(directory, files.keystore),
    passwordFile: path.join(directory, files.password),
    alias: fs
      .readFileSync(path.join(directory, files.alias), 'utf8')
      .replace(/\n+$/, '')
  };
}

// The build-tools version the Android App's toolchain lock pins.
function lockedBuildToolsVersion(workspaceRoot: string): string | null {
  try {
    return (
      readJson(
        path.join(workspaceRoot, ANDROID_APP_ROOT, 'toolchain.lock.json')
      ) as AndroidToolchainLock
    ).androidSdk.buildTools;
  } catch {
    return null;
  }
}

// zipalign and apksigner: on PATH, else the Android SDK's build-tools the
// App's toolchain lock pins, else its newest build-tools that page-aligns.
export function resolveBuildTools(
  env: NodeJS.ProcessEnv = process.env,
  {
    lockedVersion = lockedBuildToolsVersion(
      path.resolve(import.meta.dirname, '..')
    )
  }: ResolveBuildToolsOptions = {}
): BuildTools {
  const onPath = (name: string): string | null => {
    for (const directory of (env.PATH ?? '').split(path.delimiter)) {
      const candidate = path.join(directory, name);
      if (directory && fs.existsSync(candidate)) return candidate;
    }
    return null;
  };
  const sdk = env.ANDROID_HOME || env.ANDROID_SDK_ROOT;
  const fromSdk = (name: string): string | null => {
    const root = sdk ? path.join(sdk, 'build-tools') : null;
    if (!root || !fs.existsSync(root)) return null;
    const versions = fs
      .readdirSync(root)
      .filter(
        (version) => Number.parseInt(version, 10) >= MIN_BUILD_TOOLS_MAJOR
      )
      .sort((a, b) => a.localeCompare(b, 'en', { numeric: true }))
      .reverse();
    if (lockedVersion !== null && versions.includes(lockedVersion))
      versions.unshift(lockedVersion);
    for (const version of versions) {
      const candidate = path.join(root, version, name);
      if (fs.existsSync(candidate)) return candidate;
    }
    return null;
  };
  const tool = (name: string): string => {
    const found = onPath(name) ?? fromSdk(name);
    if (!found)
      throw new Error(
        `${name} not found on PATH or in ANDROID_HOME/build-tools/<${MIN_BUILD_TOOLS_MAJOR}+>; install the Android SDK build-tools`
      );
    return found;
  };
  return { zipalign: tool('zipalign'), apksigner: tool('apksigner') };
}

function defaultNugetRoot(env: NodeJS.ProcessEnv = process.env): string {
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
}: ResolveNativeLibOptions): Promise<string> {
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

// Every native library of the lock entry as a verified file in `into`.
async function resolveLockedNativeLibs({
  entry,
  into
}: ResolveLockedNativeLibsOptions): Promise<Record<string, string>> {
  const nativeLibs: Record<string, string> = {};
  for (const [name, pin] of Object.entries(entry.nativeLibs ?? {}))
    nativeLibs[name] = await resolveNativeLib({ name, pin, into });
  return nativeLibs;
}

// The Android App's release build: Gradle's unsigned release APK of
// bazaarplusplus-android/ with the product version, its versionCode and the
// staged runtime (app/build.gradle.kts reads the three properties).
export function assembleWithGradle({
  workspaceRoot,
  version,
  versionCode,
  runtimeDir
}: AssembleOptions): string {
  const appRoot = path.join(workspaceRoot, ANDROID_APP_ROOT);
  // AGP names an unsigned release app-release-unsigned.apk; the build takes
  // whichever one APK the output directory holds.
  const outputs = path.join(appRoot, 'app/build/outputs/apk/release');
  fs.rmSync(outputs, { recursive: true, force: true });
  execFileSync(
    path.join(appRoot, 'gradlew'),
    [
      '--no-daemon',
      ':app:assembleRelease',
      `-Pbpp.version=${version}`,
      `-Pbpp.versionCode=${versionCode}`,
      `-Pbpp.runtimeDir=${runtimeDir}`
    ],
    { cwd: appRoot, stdio: 'inherit' }
  );
  const apks = fs.existsSync(outputs)
    ? fs.readdirSync(outputs).filter((name) => name.endsWith('.apk'))
    : [];
  if (apks.length !== 1)
    throw new Error(
      `Gradle's assembleRelease wrote ${apks.length} APKs to ${outputs}, not one: ${apks.join(', ')}`
    );
  return path.join(outputs, apks[0]);
}

// Page-aligns the unsigned App with zipalign and signs it with apksigner and
// the Android release key; the keystore password reaches apksigner only
// through its file. Returns the signer certificate digest apksigner reports.
export function signAndroidApp({
  unsigned,
  signing,
  tools,
  output,
  work
}: SignAndroidAppOptions): string {
  fs.mkdirSync(work, { recursive: true });
  const aligned = path.join(work, 'aligned.apk');
  run(tools.zipalign, ['-P', PAGE_ALIGNMENT_KIB, '-f', '4', unsigned, aligned]);
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
    // No --key-pass: apksigner reads a file named twice line by line, so the
    // key password would come from a second line that does not exist. The
    // key's password is the keystore's (PKCS12), which apksigner tries first.
    '--ks-pass',
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
  return signerCertificateSha256(certificates, output);
}

// The one signer certificate digest `apksigner verify --print-certs` reports.
// It labels the signer by scheme: "Signer #1 certificate SHA-256 digest" when
// v1 or v2 signed, "V3.0 Signer: certificate SHA-256 digest" for an APK signed
// with v3 only (build-tools 37 when the minimum SDK is 24 or later). The release
// key is one certificate, so every line must name the same digest.
export function signerCertificateSha256(output: string, apk: string): string {
  const digests = new Set(
    [
      ...output.matchAll(
        /^(?:V\d+(?:\.\d+)? )?Signer(?: #\d+)?:? certificate SHA-256 digest: ([0-9a-f]{64})$/gm
      )
    ].map((match) => match[1])
  );
  if (digests.size === 0)
    throw new Error(
      `apksigner verify printed no signer certificate digest for ${apk}`
    );
  if (digests.size > 1)
    throw new Error(
      `${apk} carries ${digests.size} signer certificates (${[...digests].join(', ')}), not the one release key`
    );
  return [...digests][0];
}

function apkEntries(apk: string): Set<string> {
  return new Set(
    run('unzip', ['-Z1', apk], { quiet: true }).split('\n').filter(Boolean)
  );
}

// The signed App carries the runtime this release names: the Payload
// Inventory's android-app entries inside the APK, and the runtime facts the
// staging wrote, read back out of the APK, so the fragment and the App can
// never compute their digests apart.
function assertAppRuntime(
  apk: string,
  facts: RuntimeFacts,
  carried: AndroidCarriedRelease
): void {
  const entries = apkEntries(apk);
  const missing = platformInventory(PLATFORM)
    .filter(
      (entry) =>
        entry.producer === 'android-app' && /^(lib|assets)\//.test(entry.path)
    )
    .map((entry) => entry.path)
    .filter((name) => !entries.has(name));
  if (missing.length)
    throw new Error(
      `The Android App lacks ${missing.join(', ')} that release/payload.json gives it`
    );
  const packaged = JSON.parse(
    run('unzip', ['-p', apk, `assets/${RUNTIME_FACTS}`], { quiet: true })
  ) as RuntimeFacts;
  for (const field of [
    'nonManagedPayloadDigest',
    'updaterSha256',
    'runtimeZipSha256'
  ] as const)
    if (packaged[field] !== facts[field])
      throw new Error(
        `The Android App's assets/${RUNTIME_FACTS} names ${field} ${packaged[field]}, not the staged ${facts[field]}`
      );
  const carriedAsset = `assets/${CARRIED_RELEASE_DIR}`;
  const carriedDocument = run(
    'unzip',
    ['-p', apk, `${carriedAsset}/${CARRIED_RELEASE_FILE}`],
    { quiet: true }
  );
  if (JSON.stringify(JSON.parse(carriedDocument)) !== JSON.stringify(carried))
    throw new Error(
      `The Android App's ${carriedAsset}/${CARRIED_RELEASE_FILE} is not the release this build staged`
    );
  const record = carried.downloads[releasePlatform(PLATFORM).key];
  for (const { sha256: digest } of [record.interop, record.managedPayload])
    if (!entries.has(`${carriedAsset}/${digest}.zip`))
      throw new Error(`The Android App lacks ${carriedAsset}/${digest}.zip`);
}

// The Interop Package of the locked snapshot and its signed `release
// <version>` statement, written beside the APK as <zip sha256>.zip,
// .statement and .statement.sig. The signing certificate must be the
// release certificate the App was signed with.
function packInterop({
  root,
  interopPath,
  entry,
  version,
  signing,
  releaseKey,
  signerSha256
}: PackInteropOptions): InteropPackageRecord {
  const zip = buildInteropPackage(interopPath);
  const zipSha256 = sha256(zip);
  const zipPath = path.join(root, `${zipSha256}.zip`);
  fs.writeFileSync(zipPath, zip);
  const statement = interopStatement({
    zipSha256,
    il2cppSha256: entry.il2cppSha256,
    metadataSha256: entry.metadataSha256,
    purpose: releasePurpose(version)
  });
  const statementPath = path.join(root, `${zipSha256}.statement`);
  fs.writeFileSync(statementPath, statement);
  const signature = signWithReleaseKey({
    statement,
    signing,
    signerSha256,
    releaseKey
  });
  fs.writeFileSync(`${statementPath}.sig`, `${signature}\n`);
  return {
    key: interopPackageKey(zipSha256),
    path: path.relative(root, zipPath),
    size: zip.length,
    sha256: zipSha256,
    snapshotSha256: entry.interopSha256,
    signature,
    statementPath: path.relative(root, statementPath)
  };
}

async function buildUnlocked({
  workspaceRoot,
  msbuildArgs = [],
  resolveSnapshot,
  produce,
  gameLibsStore = gameLibsStoreFromEnvironment,
  resolveNativeLibs = resolveLockedNativeLibs,
  assemble = assembleWithGradle,
  sign = signWithUpdaterKey,
  signingKey = androidSigningKey,
  buildTools = resolveBuildTools,
  releaseKey = exportReleaseKey,
  releaseSignerSha256 = ANDROID_RELEASE_SIGNER_SHA256,
  captureIdentity = releaseSourceIdentity,
  log = () => {}
}: BuildAndroidReleaseOptions): Promise<AndroidArtifactManifest> {
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

  for (const stale of fs.readdirSync(paths.root))
    if (/\.(?:apk|zip|sig|statement)$/.test(stale))
      fs.rmSync(path.join(paths.root, stale), { force: true });
  fs.rmSync(paths.work, { recursive: true, force: true });

  const zipPath = path.join(
    paths.root,
    path.posix.basename(managedPayloadKey(version, key))
  );
  const zip = writeManagedPayloadZip({
    sourceDir: paths.payload,
    platform: PLATFORM,
    outputPath: zipPath,
    sign: ({ file }: { file: string }) => sign({ workspaceRoot, file })
  });
  const signature = sign({ workspaceRoot, file: zipPath });
  log(`Signed ${path.basename(zipPath)}`);

  const nonManagedPayloadDigest = androidEntryNonManagedPayloadDigest(
    entry,
    stagedUpdaterSha256(paths.payload)
  );
  const signing = signingKey();
  // Before the App, which carries both zips. The certificate is the pin: the
  // App signed below must match it too.
  const interop = packInterop({
    root: paths.root,
    interopPath: record.interopPath,
    entry,
    version,
    signing,
    releaseKey,
    signerSha256: releaseSignerSha256
  });
  log(`Signed the interop statement of ${interop.key}`);
  const { manifest: managed } = readStagedManagedPayload(
    paths.payload,
    PLATFORM
  );
  const nativeDir = path.join(paths.work, 'native');
  const facts = stageAndroidAppRuntime({
    loaderPath: record.loaderPath,
    loader: entry.loader,
    updaterDll: stagedUpdater(paths.payload),
    nativeLibs: await resolveNativeLibs({ entry, into: nativeDir }),
    nonManagedPayloadDigest,
    outDir: paths.runtime
  });
  const carried = androidCarriedRelease({
    version,
    gitCommit: identity.commit,
    baseUrl: RELEASE_BASE_URL,
    game: entry,
    interop,
    managedPayload: {
      size: fs.statSync(zipPath).size,
      sha256: zip.sha256,
      signature
    },
    loader: entry.loader,
    historyDatabaseUserVersion: managed.historyDatabaseUserVersion,
    nonManagedPayloadDigest
  });
  stageCarriedRelease({
    outDir: paths.runtime,
    release: carried,
    zips: [
      { sha256: interop.sha256, file: path.join(paths.root, interop.path) },
      { sha256: zip.sha256, file: zipPath }
    ]
  });
  const versionCode = androidAppVersionCode(version);
  log(`Building the Android App ${version} (versionCode ${versionCode})`);
  const unsigned = assemble({
    workspaceRoot,
    version,
    versionCode,
    runtimeDir: paths.runtime
  });
  const appKey = androidAppKey(version);
  const appPath = path.join(paths.root, path.posix.basename(appKey));
  const signerSha256 = signAndroidApp({
    unsigned,
    signing,
    tools: buildTools(),
    output: appPath,
    work: paths.work
  });
  if (signerSha256 !== releaseSignerSha256)
    throw new Error(
      `The Android App is signed by ${signerSha256}, not the release key ${releaseSignerSha256} (docs/adr/0009-android-release-key.md); check the staged keystore`
    );
  assertAppRuntime(appPath, facts, carried);
  log(`Signed ${path.basename(appPath)}`);
  fs.rmSync(paths.work, { recursive: true, force: true });

  verifyAndroidPayload(workspaceRoot);
  if (JSON.stringify(captureIdentity(rootDir)) !== JSON.stringify(identity))
    throw new Error(
      'Product source or Git identity changed during packaging; no artifact manifest was written'
    );
  if (managed.nonManagedPayloadDigest !== nonManagedPayloadDigest)
    throw new Error(
      'The Managed Payload Manifest names another non-managed payload than the Android App; run node release.ts prepare --platform android'
    );
  const manifest: AndroidArtifactManifest = {
    schemaVersion: ANDROID_ARTIFACT_SCHEMA_VERSION,
    platform: key,
    buildPlatform: PLATFORM,
    appVersion: version,
    gitCommit: identity.commit,
    dirty: identity.dirty,
    app: {
      key: appKey,
      path: path.relative(paths.root, appPath),
      size: fs.statSync(appPath).size,
      sha256: await sha256File(appPath),
      versionCode,
      signerSha256
    },
    interop,
    managedPayload: {
      path: path.relative(paths.root, zipPath),
      size: fs.statSync(zipPath).size,
      sha256: zip.sha256,
      signature
    },
    game: {
      gameVersion: entry.gameVersion,
      versionCode: entry.versionCode,
      il2cppSha256: entry.il2cppSha256,
      metadataSha256: entry.metadataSha256
    },
    loader: { tag: entry.loader.tag, sha256: entry.loader.sha256 },
    historyDatabaseUserVersion: managed.historyDatabaseUserVersion,
    nonManagedPayloadDigest,
    lockEntry: record.lockEntry
  };
  fs.writeFileSync(paths.artifactManifest, jsonBytes(manifest));
  return manifest;
}

export async function buildAndroidRelease(
  options: BuildAndroidReleaseOptions
): Promise<AndroidArtifactManifest> {
  return withAndroidLock(options.workspaceRoot, 'build', () =>
    buildUnlocked(options)
  );
}

// The build receipt upload publishes, checked against the checkout and the
// files it names.
export async function readAndroidArtifacts(
  workspaceRoot: string
): Promise<AndroidArtifacts> {
  const paths = androidPaths(workspaceRoot);
  if (!fs.existsSync(paths.artifactManifest))
    throw new Error(
      'No Android build to upload. Run node release.ts build --platform android.'
    );
  const manifest = readJson(paths.artifactManifest) as AndroidArtifactManifest;
  const version = readProductVersion(workspaceRoot);
  if (
    manifest.schemaVersion !== ANDROID_ARTIFACT_SCHEMA_VERSION ||
    manifest.buildPlatform !== PLATFORM ||
    manifest.appVersion !== version
  )
    throw new Error(
      `The Android build is not ${version}. Run node release.ts build --platform android.`
    );
  if (manifest.dirty)
    throw new Error(
      'The Android build came from a dirty checkout; commit, then build again'
    );
  const file = (
    record: { path: string; size: number },
    what: string
  ): string => {
    const absolute = path.join(paths.root, record.path);
    if (!fs.existsSync(absolute) || fs.statSync(absolute).size !== record.size)
      throw new Error(`${what} changed or is missing: ${absolute}`);
    return absolute;
  };
  const files: AndroidArtifactFiles = {
    app: file(manifest.app, 'Android App APK'),
    interop: file(manifest.interop, 'Interop Package'),
    managedPayload: file(manifest.managedPayload, 'Managed Payload zip')
  };
  for (const [name, absolute] of Object.entries(files) as [
    keyof AndroidArtifactFiles,
    string
  ][])
    if ((await sha256File(absolute)) !== manifest[name].sha256)
      throw new Error(`Artifact changed before upload: ${absolute}`);
  return { manifest, files };
}

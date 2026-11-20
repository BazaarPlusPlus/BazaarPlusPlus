import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import zlib from 'node:zlib';
import { execFileSync } from 'node:child_process';
import { readApk, sha256File } from './android-apk.ts';
import {
  SNAPSHOT_MANIFEST_SCHEMA_VERSION,
  gameLibsStoreFromEnvironment,
  managedDirectoryDigest,
  managedGameVersion
} from './game-assembly-snapshot.ts';
import type { DirectoryRecord } from './game-assembly-snapshot.ts';
import { WORKSPACE_ROOT } from './product.ts';
import { putReplaceable } from './r2-store.ts';
import type { ObjectStore } from './r2-store.ts';
import {
  hash,
  isSha256,
  jsonBytes,
  readSnapshotManifest,
  tar
} from './snapshot-files.ts';
import type { ExecFileError } from './snapshot-files.ts';
import {
  ANDROID_SOURCES,
  assertLockEntry,
  gameVersionKey,
  interopKey,
  interopManifestKey,
  interopSnapshotDirectory,
  lockWarnings,
  managedEntries,
  readGameLibsLock,
  writeGameLibsLockEntry
} from './snapshot-lock.ts';
import type {
  AndroidSource,
  GameLibsLock,
  InteropLockEntry,
  InteropLockKey,
  LoaderPin,
  NativeLibPin,
  PatcherPin
} from './snapshot-lock.ts';

export interface InteropSnapshotManifest extends InteropLockEntry {
  schemaVersion: typeof SNAPSHOT_MANIFEST_SCHEMA_VERSION;
  platform: 'android';
  channel: AndroidSource;
}

export type AndroidInputKind = 'apk' | 'loader' | 'patcher' | 'cpp2il';

export interface AndroidInput {
  extension: string;
  sha256s: (entry: InteropLockEntry) => string[];
}

export const ANDROID_INPUTS: Readonly<Record<AndroidInputKind, AndroidInput>> =
  Object.freeze({
    apk: {
      extension: '.apk',
      sha256s: (entry) => [entry.apkSha256, ...entry.splitApkSha256s]
    },
    loader: { extension: '.zip', sha256s: (entry) => [entry.loader.sha256] },
    patcher: {
      extension: '.tar.gz',
      sha256s: (entry) => [entry.patcher.sha256]
    },
    cpp2il: { extension: '', sha256s: (entry) => [entry.cpp2ilSha256] }
  });
// Where the Loader Release keeps the managed assemblies a host compiles
// against; build/InteropPath.props names the same directory.
export const LOADER_MANAGED_DIRECTORY =
  'assets/LemonLoader/runtime/loader/net6';
export const LOADER_MARKER = '.bpp-loader-sha256';
// Local paths of the inputs a capture read, so publish can upload them without
// copying a multi-gigabyte APK under game-libs/. Never uploaded.
const SOURCES_FILE = 'sources.json';

function inputKind(kind: AndroidInputKind): AndroidInput {
  if (!Object.hasOwn(ANDROID_INPUTS, kind))
    throw new Error(
      `Unknown Android input '${kind}'; expected ${Object.keys(ANDROID_INPUTS).join(', ')}`
    );
  return ANDROID_INPUTS[kind];
}

export function interopBlobKey(sha256: string): string {
  if (!isSha256(sha256)) throw new Error('Invalid interop sha256');
  return `game-libs/interop/${sha256}.tar.gz`;
}

export function androidInputKey(
  kind: AndroidInputKind,
  sha256: string
): string {
  const { extension } = inputKind(kind);
  if (!isSha256(sha256)) throw new Error(`Invalid ${kind} sha256`);
  return `game-libs/${kind}/${sha256}${extension}`;
}

export function loaderDirectory(modRoot: string, tag: string): string {
  return path.join(modRoot, 'game-libs', `loader-${tag}`);
}

function androidInputFile(
  modRoot: string,
  kind: AndroidInputKind,
  sha256: string
): string {
  return path.join(
    modRoot,
    'game-libs',
    'inputs',
    `${kind}-${sha256}${inputKind(kind).extension}`
  );
}

export function interopDirectoryRecords(
  interopPath: string
): DirectoryRecord[] {
  const records: DirectoryRecord[] = [];
  for (const entry of fs.readdirSync(interopPath, { withFileTypes: true })) {
    if (entry.isFile())
      records.push({
        path: `interop/${entry.name}`,
        sha256: hash(fs.readFileSync(path.join(interopPath, entry.name)))
      });
  }
  if (!records.some((record) => record.path === 'interop/Assembly-CSharp.dll'))
    throw new Error(`Interop path has no Assembly-CSharp.dll: ${interopPath}`);
  records.sort((a, b) => (a.path < b.path ? -1 : a.path > b.path ? 1 : 0));
  return records;
}

export function interopDirectoryDigest(interopPath: string): string {
  return hash(JSON.stringify(interopDirectoryRecords(interopPath)));
}

// macOS tar -t hides AppleDouble members, so inspect the headers directly.
export function tarballEntries(file: string): string[] {
  const bytes = zlib.gunzipSync(fs.readFileSync(file));
  const entries: string[] = [];
  let offset = 0;
  let longName: string | null = null;
  let paxPath: string | null = null;
  while (offset + 512 <= bytes.length) {
    const header = bytes.subarray(offset, offset + 512);
    offset += 512;
    if (header.every((byte) => byte === 0)) break;
    const field = (start: number, length: number): string =>
      header
        .subarray(start, start + length)
        .toString('utf8')
        .replace(/\0.*$/s, '');
    const size = parseInt(field(124, 12).trim() || '0', 8);
    const type = String.fromCharCode(header[156] || 48);
    const content = bytes.subarray(offset, offset + size);
    offset += Math.ceil(size / 512) * 512;
    if (type === 'L') {
      longName = content.toString('utf8').replace(/\0.*$/s, '');
      continue;
    }
    if (type === 'x') {
      for (const record of content.toString('utf8').split('\n')) {
        const match = /^\d+ path=(.*)$/.exec(record);
        if (match) paxPath = match[1];
      }
      continue;
    }
    if (type === 'g') continue;
    const prefix = field(345, 155);
    const name =
      paxPath ?? longName ?? (prefix ? `${prefix}/` : '') + field(0, 100);
    longName = null;
    paxPath = null;
    entries.push(name);
  }
  return entries;
}

// Extra members, including AppleDouble files, change the digest on Linux.
export function assertPortableInteropArchive(
  file: string,
  description: string = file
): void {
  const offender = tarballEntries(file).find((name) => {
    const parts = name.replace(/\/$/, '').split('/');
    return !(
      parts[0] === 'interop' &&
      parts.length <= 2 &&
      !(parts[1] ?? '').startsWith('._')
    );
  });
  if (offender !== undefined)
    throw new Error(
      `${description} carries the member ${offender}, which GNU tar extracts beside the interop files; pack it with COPYFILE_DISABLE=1 tar`
    );
}

interface InteropGeneratorManifest {
  unityVersion?: string;
  tools?: { cpp2IlVersion?: string; il2CppInteropVersion?: string };
}

interface InteropToolchain {
  unityVersion: string;
  cpp2ilVersion: string;
  il2cppInteropVersion: string;
}

function readInteropManifest(interopPath: string): InteropToolchain {
  const file = path.join(interopPath, 'interop-manifest.json');
  let manifest: InteropGeneratorManifest;
  try {
    manifest = JSON.parse(
      fs.readFileSync(file, 'utf8')
    ) as InteropGeneratorManifest;
  } catch (error) {
    throw new Error(
      `Cannot read ${file}; an interop directory comes from the Patcher's generate-interop: ${(error as Error).message}`
    );
  }
  const { unityVersion, tools = {} } = manifest;
  if (!unityVersion || !tools.cpp2IlVersion || !tools.il2CppInteropVersion)
    throw new Error(
      `${file} names no unityVersion, tools.cpp2IlVersion or tools.il2CppInteropVersion`
    );
  return {
    unityVersion,
    cpp2ilVersion: tools.cpp2IlVersion,
    il2cppInteropVersion: tools.il2CppInteropVersion
  };
}

function readZipEntry(zip: string, entry: string): Buffer {
  try {
    return execFileSync('unzip', ['-p', zip, entry], {
      stdio: ['ignore', 'pipe', 'pipe']
    });
  } catch (error) {
    const failure = error as ExecFileError;
    throw new Error(
      `Cannot read ${entry} from ${zip}: ${String(failure.stderr || failure.message).trim()}`
    );
  }
}

function unzipInto(zip: string, into: string): void {
  fs.mkdirSync(into, { recursive: true });
  try {
    execFileSync('unzip', ['-q', '-o', zip, '-d', into], {
      stdio: ['ignore', 'ignore', 'pipe']
    });
  } catch (error) {
    const failure = error as ExecFileError;
    throw new Error(
      `unzip ${zip} failed: ${String(failure.stderr || failure.message).trim()}`
    );
  }
}

// The store an Android step needs, created on first use so a step that is
// satisfied locally never needs credentials.
function lazyStore<Store>(
  storeFactory: () => Store,
  remedy: string
): () => Store {
  let store: Store | undefined;
  return () => {
    if (store) return store;
    try {
      store = storeFactory();
    } catch (error) {
      throw new Error(`${remedy}: ${(error as Error).message}`);
    }
    return store;
  };
}

const STORE_REMEDY =
  'Fetch from the private store with the Cloudflare operator token (just with-config release just mod::fetch android <source>)';

export interface FetchAndroidInputOptions {
  modRoot: string;
  kind: AndroidInputKind;
  sha256: string;
  store: () => Pick<ObjectStore, 'getFile'>;
  log?: (message: string) => void;
}

export async function fetchAndroidInput({
  modRoot,
  kind,
  sha256,
  store,
  log = () => {}
}: FetchAndroidInputOptions): Promise<string> {
  const file = androidInputFile(modRoot, kind, sha256);
  if (fs.existsSync(file)) {
    if ((await sha256File(file)) === sha256) return file;
    fs.rmSync(file, { force: true });
  }
  const key = androidInputKey(kind, sha256);
  fs.mkdirSync(path.dirname(file), { recursive: true });
  const partial = `${file}.fetch-${process.pid}`;
  try {
    log(`Fetching ${key} from the private store`);
    const fetched = await store().getFile(key, partial);
    if (!fetched)
      throw new Error(
        `The private store has no ${key}; run 'just mod::publish android <source>' on the machine that captured it`
      );
    const digest = await sha256File(partial);
    if (digest !== sha256)
      throw new Error(
        `Stored ${key} hashes to ${digest}, not the ${sha256} the lock records`
      );
    fs.renameSync(partial, file);
  } finally {
    fs.rmSync(partial, { force: true });
  }
  return file;
}

// Publishing device APKs by digest lets android-apk-watch.yml capture them on Linux.
export async function uploadApks(
  files: readonly string[],
  store: Pick<ObjectStore, 'head' | 'putFile'>,
  log: (message: string) => void = () => {}
): Promise<string[]> {
  const sha256s: string[] = [];
  for (const file of files) {
    const sha256 = await sha256File(file);
    const key = androidInputKey('apk', sha256);
    if (await store.head(key)) log(`Already stored: ${key}`);
    else if (await store.putFile(key, file, { sha256 })) log(`Uploaded ${key}`);
    else log(`Another publisher stored ${key} first`);
    sha256s.push(sha256);
  }
  return sha256s;
}

export function isLockedLoader(
  directory: string,
  loader: Pick<LoaderPin, 'sha256'>
): boolean {
  const marker = path.join(directory, LOADER_MARKER);
  return (
    fs.existsSync(
      path.join(directory, LOADER_MANAGED_DIRECTORY, 'MelonLoader.dll')
    ) &&
    fs.existsSync(marker) &&
    fs.readFileSync(marker, 'utf8').trim() === loader.sha256
  );
}

function verifiedLoader(modRoot: string, loader: LoaderPin): string | null {
  const directory = loaderDirectory(modRoot, loader.tag);
  return isLockedLoader(directory, loader) ? directory : null;
}

interface EnsureLoaderOptions {
  modRoot: string;
  entry: InteropLockEntry;
  zip?: string;
  store: () => Pick<ObjectStore, 'getFile'>;
  log: (message: string) => void;
}

async function ensureLoader({
  modRoot,
  entry,
  zip,
  store,
  log
}: EnsureLoaderOptions): Promise<string> {
  const existing = verifiedLoader(modRoot, entry.loader);
  if (existing) return existing;
  const archive =
    zip ??
    (await fetchAndroidInput({
      modRoot,
      kind: 'loader',
      sha256: entry.loader.sha256,
      store,
      log
    }));
  const directory = loaderDirectory(modRoot, entry.loader.tag);
  const staging = `${directory}.unpack-${process.pid}`;
  fs.rmSync(staging, { recursive: true, force: true });
  try {
    unzipInto(archive, staging);
    if (
      !fs.existsSync(
        path.join(staging, LOADER_MANAGED_DIRECTORY, 'MelonLoader.dll')
      )
    )
      throw new Error(
        `Loader Release ${entry.loader.tag} has no ${LOADER_MANAGED_DIRECTORY}/MelonLoader.dll`
      );
    fs.writeFileSync(
      path.join(staging, LOADER_MARKER),
      `${entry.loader.sha256}\n`
    );
    fs.rmSync(directory, { recursive: true, force: true });
    fs.renameSync(staging, directory);
  } finally {
    fs.rmSync(staging, { recursive: true, force: true });
  }
  return directory;
}

// The Patcher CLI requires Linux x64. Other hosts reuse output for the same APK via --interop.
export interface GenerateInteropOptions {
  patcher: string;
  cpp2il: string;
  il2cpp: string;
  metadata: string;
  unityVersion: string;
  output: string;
  workDirectory: string;
}

export function generateInterop({
  patcher,
  cpp2il,
  il2cpp,
  metadata,
  unityVersion,
  output,
  workDirectory
}: GenerateInteropOptions): void {
  if (process.platform !== 'linux' || process.arch !== 'x64')
    throw new Error(
      `Interop generation runs the LemonLoader Patcher's Linux x64 CLI, and this is ${process.platform}-${process.arch}; generate on Linux x64, or pass --interop <dir> with an interop directory generated from this APK`
    );
  const tools = path.join(workDirectory, 'patcher');
  fs.mkdirSync(tools, { recursive: true });
  tar(['-xzf', path.resolve(patcher), '-C', tools], workDirectory);
  const cpp2ilBinary = path.join(workDirectory, 'Cpp2IL');
  fs.copyFileSync(cpp2il, cpp2ilBinary);
  fs.chmodSync(cpp2ilBinary, 0o755);
  // Child output goes to stderr so the CLI's stdout keeps only its result.
  execFileSync(
    path.join(tools, 'LemonLoader.Patcher.CLI'),
    [
      'generate-interop',
      '--game-assembly',
      il2cpp,
      '--metadata',
      metadata,
      '--unity-version',
      unityVersion,
      '--output',
      output,
      '--cpp2il',
      cpp2ilBinary,
      '--il2cppinterop-cli',
      path.join(tools, 'Tools/Il2CppInterop/Il2CppInterop.CLI.dll')
    ],
    {
      stdio: ['ignore', 2, 2],
      env: { ...process.env, TMPDIR: workDirectory }
    }
  );
}

function defaultNugetRoot(): string {
  return (
    process.env.NUGET_PACKAGES || path.join(os.homedir(), '.nuget', 'packages')
  );
}

export function nativeLibsFromPackages(
  packages: readonly string[],
  nugetRoot: string = defaultNugetRoot()
): Record<string, NativeLibPin> {
  const libs: Record<string, NativeLibPin> = {};
  for (const spec of packages) {
    const match = /^([A-Za-z0-9._-]+)@([A-Za-z0-9._+-]+)$/.exec(spec);
    if (!match)
      throw new Error(
        `Native package ${JSON.stringify(spec)} must be <id>@<version>, such as SourceGear.sqlite3@3.53.4`
      );
    const [, id, version] = match;
    const native = path.join(
      nugetRoot,
      id.toLowerCase(),
      version,
      'runtimes',
      'android-arm64',
      'native'
    );
    const files = fs.existsSync(native)
      ? fs.readdirSync(native).filter((name) => name.endsWith('.so'))
      : [];
    if (files.length === 0)
      throw new Error(
        `${id} ${version} has no runtimes/android-arm64/native/*.so under ${nugetRoot}; restore a project that references it first`
      );
    for (const name of files.sort())
      libs[name] = {
        package: id,
        version,
        sha256: hash(fs.readFileSync(path.join(native, name)))
      };
  }
  return libs;
}

// Empty input paths reuse the current toolchain pins.
interface ToolchainOptions {
  loader: string;
  loaderTag: string;
  patcher: string;
  patcherTag: string;
  cpp2il: string;
}

interface CaptureToolchainOptions {
  modRoot: string;
  previous: InteropLockEntry | null;
  options: ToolchainOptions;
  store: () => Pick<ObjectStore, 'getFile'>;
  log: (message: string) => void;
}

interface CaptureToolchain {
  loader: { file: string; pin: LoaderPin };
  patcher: { file: string; pin: PatcherPin };
  cpp2il: { file: string; sha256: string };
}

interface LoaderRelease {
  managedRuntimeVersion: string;
}

async function captureToolchain({
  modRoot,
  previous,
  options,
  store,
  log
}: CaptureToolchainOptions): Promise<CaptureToolchain> {
  const pinned = async (kind: AndroidInputKind, locked: InteropLockEntry) =>
    fetchAndroidInput({
      modRoot,
      kind,
      sha256: ANDROID_INPUTS[kind].sha256s(locked)[0],
      store,
      log
    });
  const need = (flag: string): InteropLockEntry => {
    if (!previous)
      throw new Error(
        `Every Android lock entry is empty, so the first capture names the toolchain: pass ${flag}`
      );
    return previous;
  };
  let loader: CaptureToolchain['loader'];
  if (options.loader) {
    if (!options.loaderTag)
      throw new Error('--loader needs --loader-tag naming its release tag');
    const release = JSON.parse(
      readZipEntry(options.loader, 'lemonloader-release.json').toString('utf8')
    ) as LoaderRelease;
    loader = {
      file: options.loader,
      pin: {
        tag: options.loaderTag,
        sha256: await sha256File(options.loader),
        managedRuntimeVersion: release.managedRuntimeVersion
      }
    };
  } else {
    const locked = need(
      '--loader <LemonLoader-Android-arm64.zip> --loader-tag <tag>'
    );
    loader = {
      file: await pinned('loader', locked),
      pin: { ...locked.loader }
    };
  }
  let patcher: CaptureToolchain['patcher'];
  if (options.patcher) {
    if (!options.patcherTag)
      throw new Error('--patcher needs --patcher-tag naming its release tag');
    patcher = {
      file: options.patcher,
      pin: {
        tag: options.patcherTag,
        sha256: await sha256File(options.patcher)
      }
    };
  } else {
    const locked = need(
      '--patcher <LemonLoader.Patcher.CLI-linux-x64.tar.gz> --patcher-tag <tag>'
    );
    patcher = {
      file: await pinned('patcher', locked),
      pin: { ...locked.patcher }
    };
  }
  let cpp2il: CaptureToolchain['cpp2il'];
  if (options.cpp2il) {
    cpp2il = {
      file: options.cpp2il,
      sha256: await sha256File(options.cpp2il)
    };
  } else {
    const locked = need('--cpp2il <Cpp2IL-<version>-Linux>');
    cpp2il = {
      file: await pinned('cpp2il', locked),
      sha256: locked.cpp2ilSha256
    };
  }
  return { loader, patcher, cpp2il };
}

function copyInteropInto(
  source: string,
  directory: string,
  interopSha256: string
): void {
  const interop = path.join(directory, 'interop');
  if (fs.existsSync(interop)) {
    const existing = interopDirectoryDigest(interop);
    if (existing !== interopSha256)
      throw new Error(
        `Snapshot ${directory} already holds interop hashing to ${existing}, not the captured ${interopSha256}; delete the directory to recapture`
      );
    return;
  }
  const staging = `${directory}.capture-${process.pid}`;
  fs.rmSync(staging, { recursive: true, force: true });
  try {
    fs.mkdirSync(staging, { recursive: true });
    fs.cpSync(source, path.join(staging, 'interop'), { recursive: true });
    if (interopDirectoryDigest(path.join(staging, 'interop')) !== interopSha256)
      throw new Error('Interop directory changed while it was being copied');
    fs.rmSync(directory, { recursive: true, force: true });
    fs.renameSync(staging, directory);
  } finally {
    fs.rmSync(staging, { recursive: true, force: true });
  }
}

// An empty entry inherits toolchain and native libraries from the other source so both share one App runtime.
export interface CaptureInteropSnapshotOptions {
  workspaceRoot?: string;
  modRoot?: string;
  /** The base APK, then any split APKs. */
  apks: readonly string[];
  apkEtag?: string | null;
  interop?: string;
  loader?: string;
  loaderTag?: string;
  patcher?: string;
  patcherTag?: string;
  cpp2il?: string;
  nativePackages?: readonly string[];
  nugetRoot?: string;
  storeFactory?: () => Pick<ObjectStore, 'getFile'>;
  generate?: (options: GenerateInteropOptions) => void;
  now?: Date;
  log?: (message: string) => void;
}

export interface CapturedInteropSnapshot {
  key: InteropLockKey;
  directory: string;
  loaderPath: string;
  manifest: InteropSnapshotManifest;
}

export async function captureInteropSnapshot({
  workspaceRoot = WORKSPACE_ROOT,
  modRoot = path.join(workspaceRoot, 'bazaarplusplus-mod'),
  apks,
  apkEtag = null,
  interop = '',
  loader = '',
  loaderTag = '',
  patcher = '',
  patcherTag = '',
  cpp2il = '',
  nativePackages = [],
  nugetRoot,
  storeFactory = gameLibsStoreFromEnvironment,
  generate = generateInterop,
  now = new Date(),
  log = () => {}
}: CaptureInteropSnapshotOptions): Promise<CapturedInteropSnapshot> {
  if (apks.length === 0) throw new Error('No APK to capture');
  for (const apk of apks)
    if (!apk || !fs.existsSync(apk))
      throw new Error(`No APK at ${JSON.stringify(apk)}`);
  const entries = readGameLibsLock(workspaceRoot).entries;
  const store = lazyStore(
    storeFactory,
    `${STORE_REMEDY}, or pass the toolchain files explicitly`
  );
  const work = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-interop-'));
  try {
    log(`Reading ${apks.join(', ')}`);
    const facts = await readApk(apks, work);
    const versionKey = gameVersionKey(facts.versionName);
    const source = ANDROID_SOURCES.find(
      (name) => interopKey(name) === versionKey
    );
    if (source === undefined)
      throw new Error(
        `${apks[0]} is ${facts.versionName}, a ${versionKey} build, not an official Android build`
      );
    const key = interopKey(source);
    const previous =
      entries[key] ??
      ANDROID_SOURCES.map((name) => entries[interopKey(name)]).find(
        (entry) => entry !== null
      ) ??
      null;
    const tools = await captureToolchain({
      modRoot,
      previous,
      options: { loader, loaderTag, patcher, patcherTag, cpp2il },
      store,
      log
    });
    let generatedPath = interop;
    if (!generatedPath) {
      generatedPath = path.join(work, 'interop');
      log(`Generating interop for ${facts.versionName}`);
      generate({
        patcher: tools.patcher.file,
        cpp2il: tools.cpp2il.file,
        il2cpp: facts.il2cpp,
        metadata: facts.metadata,
        unityVersion: facts.unityVersion,
        output: generatedPath,
        workDirectory: work
      });
    }
    const generated = readInteropManifest(generatedPath);
    if (generated.unityVersion !== facts.unityVersion)
      throw new Error(
        `${generatedPath} was generated for Unity ${generated.unityVersion}, but ${apks[0]} is Unity ${facts.unityVersion}`
      );
    const interopSha256 = interopDirectoryDigest(generatedPath);
    const directory = interopSnapshotDirectory(
      modRoot,
      source,
      facts.versionCode
    );
    copyInteropInto(generatedPath, directory, interopSha256);
    const entry: InteropLockEntry = {
      gameVersion: facts.versionName,
      versionCode: facts.versionCode,
      apkSha256: facts.apkSha256,
      apkEtag,
      splitApkSha256s: facts.splitApkSha256s,
      il2cppSha256: facts.il2cppSha256,
      metadataSha256: facts.metadataSha256,
      unityVersion: facts.unityVersion,
      interopSha256,
      loader: tools.loader.pin,
      patcher: tools.patcher.pin,
      cpp2ilVersion: generated.cpp2ilVersion,
      cpp2ilSha256: tools.cpp2il.sha256,
      il2cppInteropVersion: generated.il2cppInteropVersion,
      nativeLibs: nativePackages.length
        ? nativeLibsFromPackages(nativePackages, nugetRoot)
        : (previous?.nativeLibs ?? {}),
      capturedAt: now.toISOString()
    };
    const manifest: InteropSnapshotManifest = {
      schemaVersion: SNAPSHOT_MANIFEST_SCHEMA_VERSION,
      platform: 'android',
      channel: source,
      ...entry
    };
    // The lock is written last, so a failed step leaves it unchanged.
    assertLockEntry(key, entry);
    const loaderPath = await ensureLoader({
      modRoot,
      entry,
      zip: tools.loader.file,
      store,
      log
    });
    fs.writeFileSync(
      path.join(directory, 'manifest.json'),
      jsonBytes(manifest)
    );
    fs.writeFileSync(
      path.join(directory, SOURCES_FILE),
      jsonBytes({
        apk: apks.map((apk) => path.resolve(apk)),
        loader: path.resolve(tools.loader.file),
        patcher: path.resolve(tools.patcher.file),
        cpp2il: path.resolve(tools.cpp2il.file)
      })
    );
    writeGameLibsLockEntry(workspaceRoot, key, entry);
    return { key, directory, loaderPath, manifest };
  } finally {
    fs.rmSync(work, { recursive: true, force: true });
  }
}

function verifiedInteropSnapshot(
  modRoot: string,
  source: AndroidSource,
  entry: InteropLockEntry
): { directory: string; interop: string } | null {
  const directory = interopSnapshotDirectory(
    modRoot,
    source,
    entry.versionCode
  );
  const interop = path.join(directory, 'interop');
  if (!fs.existsSync(path.join(interop, 'Assembly-CSharp.dll'))) return null;
  const digest = interopDirectoryDigest(interop);
  if (digest !== entry.interopSha256)
    throw new Error(
      `Snapshot ${directory} hashes to ${digest}, but the lock entry records ${entry.interopSha256}; delete the directory and fetch again`
    );
  return { directory, interop };
}

export function lockedInteropEntry(
  workspaceRoot: string,
  source: AndroidSource
): InteropLockEntry {
  const key = interopKey(source);
  const entry = readGameLibsLock(workspaceRoot).entries[key];
  if (entry === null)
    throw new Error(
      `Lock entry ${key} is empty; capture it with 'just mod::snapshot-android <APK>...', publish it with 'just mod::publish android ${source}', and commit the lock`
    );
  return entry;
}

function packInteropArchive(snapshotDirectory: string, temp: string): string {
  const archive = path.join(temp, 'interop.tar.gz');
  tar(
    [
      '-czf',
      path.basename(archive),
      '-C',
      path.resolve(snapshotDirectory).split(path.sep).join('/'),
      'interop'
    ],
    temp
  );
  assertPortableInteropArchive(archive, 'The packed interop archive');
  return archive;
}

async function storedInteropArchiveFault(
  store: Pick<ObjectStore, 'getFile'>,
  key: string,
  entry: InteropLockEntry,
  temp: string
): Promise<string | null> {
  const archive = path.join(temp, 'stored.tar.gz');
  const extracted = path.join(temp, 'stored');
  if (!(await store.getFile(key, archive))) return 'the object vanished';
  try {
    assertPortableInteropArchive(archive, `Stored ${key}`);
  } catch (error) {
    return (error as Error).message;
  }
  fs.mkdirSync(extracted, { recursive: true });
  tar(['-xzf', path.basename(archive), '-C', 'stored'], temp);
  const interop = path.join(extracted, 'interop');
  const digest = fs.existsSync(interop)
    ? interopDirectoryDigest(interop)
    : null;
  return digest === entry.interopSha256
    ? null
    : `it hashes to ${digest}, not the ${entry.interopSha256} the lock entry records`;
}

export interface PublishInteropSnapshotOptions {
  workspaceRoot?: string;
  modRoot?: string;
  source: AndroidSource;
  store: ObjectStore;
  log?: (message: string) => void;
}

export interface PublishedInteropSnapshot {
  interopKey: string;
  manifestKey: string;
}

type InteropSources = Partial<Record<AndroidInputKind, string | string[]>>;

export async function publishInteropSnapshot({
  workspaceRoot = WORKSPACE_ROOT,
  modRoot = path.join(workspaceRoot, 'bazaarplusplus-mod'),
  source,
  store,
  log = () => {}
}: PublishInteropSnapshotOptions): Promise<PublishedInteropSnapshot> {
  const entry = lockedInteropEntry(workspaceRoot, source);
  const snapshot = verifiedInteropSnapshot(modRoot, source, entry);
  if (!snapshot)
    throw new Error(
      `No Interop Snapshot under game-libs/ for ${interopKey(source)} ${entry.gameVersion}; run 'just mod::snapshot-android' on the machine that captured it`
    );
  const manifest = readSnapshotManifest<InteropSnapshotManifest>(
    snapshot.directory
  );
  if (!manifest || manifest.interopSha256 !== entry.interopSha256)
    throw new Error(
      `${snapshot.directory}/manifest.json is missing or does not match the lock entry; recapture the snapshot`
    );
  const sourcesFile = path.join(snapshot.directory, SOURCES_FILE);
  const sources: InteropSources = fs.existsSync(sourcesFile)
    ? (JSON.parse(fs.readFileSync(sourcesFile, 'utf8')) as InteropSources)
    : {};
  const upload = async (
    key: string,
    file: string,
    sha256: string
  ): Promise<void> => {
    if (await store.putFile(key, file, { sha256 })) log(`Uploaded ${key}`);
    else log(`Another publisher stored ${key} first`);
  };
  // The interop archive is keyed by the directory digest, not its own bytes,
  // so an object under the right key can still extract to the wrong files.
  const interopArchiveKey = interopBlobKey(entry.interopSha256);
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-interop-'));
  try {
    const stored = await store.head(interopArchiveKey);
    const fault = stored
      ? await storedInteropArchiveFault(store, interopArchiveKey, entry, temp)
      : null;
    if (stored && !fault)
      log(`Already stored and verified: ${interopArchiveKey}`);
    else {
      const archive = packInteropArchive(snapshot.directory, temp);
      const sha256 = await sha256File(archive);
      if (!stored) await upload(interopArchiveKey, archive, sha256);
      else {
        log(`Replacing ${interopArchiveKey}: ${fault}`);
        if (
          !(await store.putFile(interopArchiveKey, archive, {
            sha256,
            ifMatch: stored.etag
          }))
        )
          throw new Error(
            `Another publisher replaced ${interopArchiveKey} first; run the publish again`
          );
        log(`Replaced ${interopArchiveKey}`);
      }
    }
  } finally {
    fs.rmSync(temp, { recursive: true, force: true });
  }
  const inputs = (Object.keys(ANDROID_INPUTS) as AndroidInputKind[]).flatMap(
    (kind) =>
      ANDROID_INPUTS[kind].sha256s(entry).map((sha256) => ({ kind, sha256 }))
  );
  for (const { kind, sha256 } of inputs) {
    const key = androidInputKey(kind, sha256);
    if (await store.head(key)) {
      log(`Already stored: ${key}`);
      continue;
    }
    const candidates = [
      ...[sources[kind] ?? []].flat(),
      androidInputFile(modRoot, kind, sha256)
    ].filter((file) => fs.existsSync(file));
    let file: string | null = null;
    for (const candidate of candidates)
      if ((await sha256File(candidate)) === sha256) {
        file = candidate;
        break;
      }
    if (!file)
      throw new Error(
        `No local ${kind} with sha256 ${sha256} to upload; recapture with 'just mod::snapshot-android' passing that file`
      );
    log(`Uploading ${key} from ${file}`);
    await upload(key, file, sha256);
  }
  const manifestKey = interopManifestKey(source, entry.versionCode);
  await putReplaceable(
    store,
    manifestKey,
    jsonBytes(manifest),
    'application/json'
  );
  log(`Recorded ${manifestKey}`);
  return { interopKey: interopArchiveKey, manifestKey };
}

export interface FetchInteropSnapshotOptions {
  workspaceRoot?: string;
  modRoot?: string;
  source: AndroidSource;
  storeFactory?: () => Pick<ObjectStore, 'get' | 'getFile'>;
  log?: (message: string) => void;
}

export type InteropSource = 'snapshot' | 'store';

export interface FetchedInteropSnapshot {
  interopPath: string;
  loaderPath: string;
  source: InteropSource;
  gameVersion: string;
}

export async function fetchInteropSnapshot({
  workspaceRoot = WORKSPACE_ROOT,
  modRoot = path.join(workspaceRoot, 'bazaarplusplus-mod'),
  source: androidSource,
  storeFactory = gameLibsStoreFromEnvironment,
  log = () => {}
}: FetchInteropSnapshotOptions): Promise<FetchedInteropSnapshot> {
  const key = interopKey(androidSource);
  const entry = lockedInteropEntry(workspaceRoot, androidSource);
  const store = lazyStore(
    storeFactory,
    `Lock entry ${key} wants ${entry.gameVersion}, which is not under game-libs/. ${STORE_REMEDY}`
  );
  let snapshot = verifiedInteropSnapshot(modRoot, androidSource, entry);
  let source: InteropSource = 'snapshot';
  if (!snapshot) {
    source = 'store';
    const directory = interopSnapshotDirectory(
      modRoot,
      androidSource,
      entry.versionCode
    );
    const blobKey = interopBlobKey(entry.interopSha256);
    const staging = `${directory}.fetch-${process.pid}`;
    fs.rmSync(staging, { recursive: true, force: true });
    fs.mkdirSync(staging, { recursive: true });
    try {
      log(`Fetching ${blobKey} from the private store`);
      const archive = path.join(staging, 'interop.tar.gz');
      if (!(await store().getFile(blobKey, archive)))
        throw new Error(
          `The private store has no ${blobKey}; run 'just mod::publish android ${androidSource}' on the machine that captured it`
        );
      assertPortableInteropArchive(archive, `Stored snapshot ${blobKey}`);
      tar(['-xzf', path.basename(archive)], staging);
      fs.rmSync(archive);
      const interop = path.join(staging, 'interop');
      const digest = fs.existsSync(interop)
        ? interopDirectoryDigest(interop)
        : null;
      if (digest !== entry.interopSha256)
        throw new Error(
          `Stored snapshot ${blobKey} hashes to ${digest}, not the ${entry.interopSha256} the lock entry ${key} records`
        );
      const remote = await store().get(
        interopManifestKey(androidSource, entry.versionCode)
      );
      const manifest: InteropSnapshotManifest = remote
        ? (JSON.parse(remote.bytes.toString('utf8')) as InteropSnapshotManifest)
        : {
            schemaVersion: SNAPSHOT_MANIFEST_SCHEMA_VERSION,
            platform: 'android',
            channel: androidSource,
            ...entry
          };
      fs.writeFileSync(
        path.join(staging, 'manifest.json'),
        jsonBytes(manifest)
      );
      fs.rmSync(directory, { recursive: true, force: true });
      fs.renameSync(staging, directory);
    } finally {
      fs.rmSync(staging, { recursive: true, force: true });
    }
    snapshot = { directory, interop: path.join(directory, 'interop') };
    log(`Fetched ${key} ${entry.gameVersion} into ${directory}`);
  }
  const loaderPath = await ensureLoader({ modRoot, entry, store, log });
  return {
    interopPath: snapshot.interop,
    loaderPath,
    source,
    gameVersion: entry.gameVersion
  };
}

// Device setup expects APKs in install order, with the base APK first.
export interface FetchLockedAndroidInputOptions {
  workspaceRoot?: string;
  modRoot?: string;
  source: AndroidSource;
  kind: AndroidInputKind;
  storeFactory?: () => Pick<ObjectStore, 'getFile'>;
  log?: (message: string) => void;
}

export async function fetchLockedAndroidInput({
  workspaceRoot = WORKSPACE_ROOT,
  modRoot = path.join(workspaceRoot, 'bazaarplusplus-mod'),
  source,
  kind,
  storeFactory = gameLibsStoreFromEnvironment,
  log = () => {}
}: FetchLockedAndroidInputOptions): Promise<string[]> {
  const entry = lockedInteropEntry(workspaceRoot, source);
  const store = lazyStore(storeFactory, STORE_REMEDY);
  const files: string[] = [];
  for (const sha256 of inputKind(kind).sha256s(entry))
    files.push(await fetchAndroidInput({ modRoot, kind, sha256, store, log }));
  return files;
}

export interface CheckGameLibsLockOptions {
  workspaceRoot?: string;
  modRoot?: string;
  managedPath?: string;
  now?: Date;
}

export interface GameLibsLockCheck {
  lock: GameLibsLock;
  warnings: string[];
  verified: { gameVersion: string; keys: string[] } | null;
  interop: { key: InteropLockKey; gameVersion: string }[];
}

export function checkGameLibsLock({
  workspaceRoot = WORKSPACE_ROOT,
  modRoot = path.join(workspaceRoot, 'bazaarplusplus-mod'),
  managedPath = '',
  now = new Date()
}: CheckGameLibsLockOptions): GameLibsLockCheck {
  const lock = readGameLibsLock(workspaceRoot);
  const warnings = lockWarnings(lock, { now });
  let verified: GameLibsLockCheck['verified'] = null;
  if (managedPath) {
    const gameVersion = managedGameVersion(managedPath);
    const matching = managedEntries(lock).filter(
      ({ entry }) => entry.gameVersion === gameVersion
    );
    if (gameVersion === null)
      warnings.push(
        `${managedPath}: no manifest.json or globalgamemanagers beside it, so it cannot be checked against the lock`
      );
    else if (matching.length === 0)
      warnings.push(
        `${managedPath}: game version ${gameVersion} matches no lock entry`
      );
    else {
      const digest = managedDirectoryDigest(managedPath);
      for (const { key, entry } of matching)
        if (entry.sha256 !== digest)
          throw new Error(
            `${managedPath} is ${gameVersion} but hashes to ${digest}, not the ${entry.sha256} that lock entry ${key} records`
          );
      verified = { gameVersion, keys: matching.map(({ key }) => key) };
    }
  }
  const interop: GameLibsLockCheck['interop'] = [];
  for (const source of ANDROID_SOURCES) {
    const key = interopKey(source);
    const entry = lock.entries[key];
    if (entry && verifiedInteropSnapshot(modRoot, source, entry))
      interop.push({ key, gameVersion: entry.gameVersion });
  }
  return { lock, warnings, verified, interop };
}

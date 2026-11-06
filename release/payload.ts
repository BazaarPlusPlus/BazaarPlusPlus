import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { readProductVersion } from './product.ts';
import type { ProductVersion } from './product.ts';
import {
  defaultTargetBuildPlatforms,
  releasePlatform
} from './release-platforms.ts';
import type { BuildPlatform, TauriBuildPlatform } from './release-platforms.ts';
import {
  platformInventory,
  payloadEntry,
  synchronizePayloadProjection
} from './payload-inventory.ts';
import {
  ensureNativeRecorderInput,
  verifyNativeRecorderInput,
  NATIVE_RECORDER_LOCK_PATH
} from './native-recorder-input.ts';
import type {
  EnsureNativeRecorderInputOptions,
  VerifyNativeRecorderInputOptions
} from './native-recorder-input.ts';
import {
  preparePayloadZip,
  validatePayloadZip,
  listPayloadFiles
} from './payload-zip.ts';
import type { PayloadZipOptions } from './payload-zip.ts';
import {
  PAYLOAD_BUILD_RECORD_SCHEMA_VERSION,
  artifactManifestPath,
  createArtifactManifest,
  releaseSourceIdentity
} from './artifact-manifest.ts';
import type {
  CreateArtifactManifestOptions,
  CreatedArtifactManifest,
  GitState,
  ManagedPayloadArtifact
} from './artifact-manifest.ts';
import { lockEntryForManaged, managedDirectoryRecords } from './game-libs.ts';
import type { ManagedLockProvenance } from './game-libs.ts';
import {
  desktopNonManagedPayloadDigest,
  signWithUpdaterKey,
  writeManagedPayloadManifest,
  writeManagedPayloadZip
} from './managed-payload.ts';
import type { SignWithUpdaterKeyOptions } from './managed-payload.ts';
import { managedPayloadKey } from './manifest.ts';

/** The files of one platform's Payload under the installer directory. */
export interface PayloadPaths {
  source: string;
  archive: string;
  buildRecord: string;
  journal: string;
}

/** One hashed release input, or one hashed seed. */
export interface PayloadInputRecord {
  path: string;
  sha256: string;
}

/** The Payload input digest and the records it hashes. */
export interface PayloadInputs {
  digest: string;
  files: PayloadInputRecord[];
}

/** The options of computePayloadInputs: a Managed directory or game records. */
export type ComputePayloadInputsOptions =
  | {
      workspaceRoot: string;
      managedPath: string;
      gameRecords?: undefined;
    }
  | {
      workspaceRoot: string;
      managedPath?: undefined;
      gameRecords: readonly PayloadInputRecord[];
    };

/** One file of the prepared Payload output. */
export interface PayloadOutputRecord {
  path: string;
  mode: number;
  sha256: string;
}

/** The sealed Payload build record, payload-build.json. */
export interface PayloadBuildRecord {
  schemaVersion: number;
  productVersion: ProductVersion;
  platform: TauriBuildPlatform;
  managedPath: string;
  lockEntry: ManagedLockProvenance;
  inputDigest: string;
  inputs: PayloadInputRecord[];
  seeds: PayloadInputRecord[];
  output: PayloadOutputRecord[];
}

/** The release operation that holds the Payload lock. */
export type PayloadLockKind = 'prepare' | 'build';

/** The Payload lock file, src-tauri/target/payload.lock. */
export interface PayloadLock {
  pid: number;
  host: string;
  kind: PayloadLockKind;
  token: string;
}

/** A held Payload lock: its token and the function that releases it. */
export interface PayloadLockHandle {
  token: string;
  release: () => void;
}

/** The options of promotePayload. */
export interface PromotePayloadOptions {
  rootDir: string;
  stageRoot: string;
  stagedInstaller: string;
  platform: TauriBuildPlatform;
  rename?: (oldPath: string, newPath: string) => void;
}

/** What a Payload producer is given to stage the mod build into. */
export interface ProducePayloadOptions {
  rootDir: string;
  platform: TauriBuildPlatform;
  resources: string;
  remoteDataDirectory: string;
  managedPath: string;
  buildArgs: string[];
}

/** The options of preparePayload. */
export interface PreparePayloadOptions {
  workspaceRoot: string;
  rootDir?: string;
  platform: TauriBuildPlatform;
  msbuildArgs?: readonly string[];
  resolveManaged?: () => string;
  ensureNative?: (options: EnsureNativeRecorderInputOptions) => void;
  verifyNative?: (options: VerifyNativeRecorderInputOptions) => void;
  produce?: (options: ProducePayloadOptions) => void;
  validate?: (options: PayloadZipOptions) => void;
}

/** The options of verifyPayloadBuild. */
export interface VerifyPayloadBuildOptions {
  workspaceRoot: string;
  rootDir?: string;
  platform: TauriBuildPlatform;
  verifyNative?: (options: VerifyNativeRecorderInputOptions) => void;
}

/** The options of buildProduct: preparation plus packaging and recording. */
export interface BuildProductOptions extends PreparePayloadOptions {
  bundle: (options: { token: string }) => void;
  captureIdentity?: (rootDir: string) => GitState;
  writeArtifact?: (
    options: CreateArtifactManifestOptions
  ) => CreatedArtifactManifest;
  sign?: (options: SignWithUpdaterKeyOptions) => string;
}

// The Payload promotion journal, src-tauri/target/payload-promotion.json.
interface PromotionReplacement {
  source: string;
  destination: string;
  backup: string;
  existed: boolean;
}

interface PromotionJournal {
  schemaVersion: number;
  stageRoot: string;
  platform: TauriBuildPlatform;
  replacements: PromotionReplacement[];
}

function hash(bytes: string | NodeJS.ArrayBufferView): string {
  return crypto.createHash('sha256').update(bytes).digest('hex');
}
function json<T>(file: string): T {
  return JSON.parse(fs.readFileSync(file, 'utf8')) as T;
}
function writeJson(file: string, value: unknown): void {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, `${JSON.stringify(value, null, 2)}\n`);
}

export function payloadPaths(
  rootDir: string,
  platform: TauriBuildPlatform
): PayloadPaths {
  if (!defaultTargetBuildPlatforms().includes(platform))
    throw new Error(`Unsupported payload platform: ${platform}`);
  return {
    source: path.join(rootDir, 'src-tauri/resources/SourceForBuild', platform),
    archive: path.join(rootDir, 'src-tauri/resources/BepInExSource', platform),
    buildRecord: path.join(
      rootDir,
      'src-tauri/resources/BepInExSource',
      platform,
      'payload-build.json'
    ),
    journal: path.join(rootDir, 'src-tauri/target/payload-promotion.json')
  };
}

// Workspace files outside the scanned directories that feed every Payload.
// computePayloadInputs skips a missing one, so release-inputs.test.ts keeps
// every entry real.
export const PAYLOAD_TOOLING_INPUTS = [
  'release.ts',
  'scripts/git-command.ts',
  'bazaarplusplus-installer/src-tauri/history-database-compatibility.json'
];

// `gameRecords` replaces the Managed directory records for a host that does
// not compile against Managed: the Android host's Interop Snapshot.
export function computePayloadInputs({
  workspaceRoot,
  managedPath,
  gameRecords
}: ComputePayloadInputsOptions): PayloadInputs {
  const records: PayloadInputRecord[] = [];
  const scan = (directory: string, prefix: string) => {
    for (const entry of fs
      .readdirSync(directory, { withFileTypes: true })
      .sort((a, b) => a.name.localeCompare(b.name))) {
      if (
        ['bin', 'obj', 'node_modules', '.git', '.DS_Store'].includes(entry.name)
      )
        continue;
      const file = path.join(directory, entry.name);
      const relative = `${prefix}/${entry.name}`;
      if (/^mod\/native\/[^/]+\/build(?:\/|$)/.test(relative)) continue;
      if (
        relative === 'installer/scripts/release/native-recorder-input.lock.json'
      )
        continue;
      if (entry.isSymbolicLink())
        throw new Error(`Symlink in release inputs: ${relative}`);
      if (entry.isDirectory()) scan(file, relative);
      else if (entry.isFile())
        records.push({ path: relative, sha256: hash(fs.readFileSync(file)) });
    }
  };
  const modRoot = path.join(workspaceRoot, 'bazaarplusplus-mod');
  for (const directory of [
    'src',
    'build',
    'scripts',
    'tests',
    'native',
    '.config'
  ]) {
    if (fs.existsSync(path.join(modRoot, directory)))
      scan(path.join(modRoot, directory), `mod/${directory}`);
  }
  for (const entry of fs.readdirSync(modRoot, { withFileTypes: true })) {
    if (entry.isFile() && /\.(props|targets|json|sh)$/.test(entry.name))
      records.push({
        path: `mod/${entry.name}`,
        sha256: hash(fs.readFileSync(path.join(modRoot, entry.name)))
      });
  }
  scan(path.join(workspaceRoot, 'release'), 'release');
  const installerRoot = path.join(workspaceRoot, 'bazaarplusplus-installer');
  if (fs.existsSync(path.join(installerRoot, 'scripts/release')))
    scan(
      path.join(installerRoot, 'scripts/release'),
      'installer/scripts/release'
    );
  for (const name of PAYLOAD_TOOLING_INPUTS) {
    const file = path.join(workspaceRoot, name);
    if (fs.existsSync(file))
      records.push({ path: name, sha256: hash(fs.readFileSync(file)) });
  }
  records.push({
    path: 'VERSION',
    sha256: hash(fs.readFileSync(path.join(workspaceRoot, 'VERSION')))
  });
  // Game references are inputs, not just a path or a Steam branch name. The
  // same records, hashed alone, are the Snapshot Lock entry's sha256.
  records.push(...(gameRecords ?? managedDirectoryRecords(managedPath)));
  records.sort((a, b) => a.path.localeCompare(b.path));
  return { digest: hash(JSON.stringify(records)), files: records };
}

function assertNoPendingPromotion(rootDir: string): void {
  if (fs.existsSync(payloadPaths(rootDir, 'macos').journal))
    throw new Error(
      'Interrupted Payload promotion. Run node release.ts prepare to recover before packaging.'
    );
}

function payloadOutput(
  rootDir: string,
  platform: TauriBuildPlatform
): PayloadOutputRecord[] {
  return listPayloadFiles(payloadPaths(rootDir, platform).source, {
    platform
  }).map(({ path: name, absolutePath, mode }) => ({
    path: name,
    mode,
    sha256: hash(fs.readFileSync(absolutePath))
  }));
}

function verifyPayloadSource({
  workspaceRoot,
  rootDir,
  platform,
  verifyNative = verifyNativeRecorderInput
}: VerifyPayloadBuildOptions & { rootDir: string }): PayloadBuildRecord {
  assertNoPendingPromotion(rootDir);
  const record = json<PayloadBuildRecord>(
    payloadPaths(rootDir, platform).buildRecord
  );
  if (
    record.schemaVersion !== PAYLOAD_BUILD_RECORD_SCHEMA_VERSION ||
    record.platform !== platform ||
    record.productVersion !== readProductVersion(workspaceRoot)
  )
    throw new Error(
      'Payload build identity is stale. Run node release.ts prepare.'
    );
  const current = computePayloadInputs({
    workspaceRoot,
    managedPath: record.managedPath
  });
  if (current.digest !== record.inputDigest)
    throw new Error(
      'Payload build inputs changed. Run node release.ts prepare.'
    );
  if (
    JSON.stringify(payloadOutput(rootDir, platform)) !==
    JSON.stringify(record.output)
  )
    throw new Error(
      'Payload output changed since preparation. Run node release.ts prepare.'
    );
  verifyNative({ rootDir, platforms: [platform] });
  return record;
}

export function verifyPayloadBuild({
  workspaceRoot,
  rootDir = path.join(workspaceRoot, 'bazaarplusplus-installer'),
  platform,
  verifyNative
}: VerifyPayloadBuildOptions): PayloadBuildRecord {
  const record = verifyPayloadSource({
    workspaceRoot,
    rootDir,
    platform,
    verifyNative
  });
  validatePayloadZip({
    rootDir,
    platform,
    productVersion: record.productVersion
  });
  return record;
}

export function assertBuildOwner(
  rootDir: string,
  token: string | undefined = process.env.BPP_RELEASE_LOCK_TOKEN
): void {
  const lock = json<PayloadLock>(
    path.join(rootDir, 'src-tauri/target/payload.lock')
  );
  if (
    !token ||
    lock.token !== token ||
    lock.kind !== 'build' ||
    lock.host !== os.hostname()
  )
    throw new Error('Installer packaging requires the product build lock');
  process.kill(lock.pid, 0);
}

// One release operation per checkout at a time, desktop or Android. Returns
// the lock token and the function that releases it; an asynchronous holder
// (the Android release) releases it once its operation settles.
export function acquirePayloadLock(
  rootDir: string,
  kind: PayloadLockKind
): PayloadLockHandle {
  const lockPath = path.join(rootDir, 'src-tauri/target/payload.lock');
  const claimPath = `${lockPath}.claim`;
  fs.mkdirSync(path.dirname(lockPath), { recursive: true });
  // Serialize acquisition too: two reclaimers must not unlink each other's
  // newly created lock after both observed the same dead owner.
  try {
    fs.mkdirSync(claimPath);
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code === 'EEXIST')
      throw new Error(
        `Payload lock acquisition is active or interrupted; inspect ${claimPath}`
      );
    throw error;
  }
  const token = crypto.randomUUID();
  try {
    if (fs.existsSync(lockPath)) {
      const lock = json<PayloadLock>(lockPath);
      if (lock.host !== os.hostname())
        throw new Error('Payload build is locked by another host');
      try {
        process.kill(lock.pid, 0);
        throw new Error(`Payload build is already running (pid ${lock.pid})`);
      } catch (error) {
        if ((error as NodeJS.ErrnoException).code !== 'ESRCH') throw error;
      }
      fs.unlinkSync(lockPath);
    }
    const fd = fs.openSync(lockPath, 'wx');
    fs.writeFileSync(
      fd,
      JSON.stringify({ pid: process.pid, host: os.hostname(), kind, token })
    );
    fs.closeSync(fd);
  } finally {
    fs.rmdirSync(claimPath);
  }
  return { token, release: () => fs.unlinkSync(lockPath) };
}

function withPayloadLock<T>(
  rootDir: string,
  kind: PayloadLockKind,
  operation: (token: string) => T
): T {
  const { token, release } = acquirePayloadLock(rootDir, kind);
  try {
    return operation(token);
  } finally {
    release();
  }
}

function recoverPromotion(rootDir: string): void {
  const journalPath = payloadPaths(rootDir, 'macos').journal;
  if (!fs.existsSync(journalPath)) return;
  const journal = json<PromotionJournal>(journalPath);
  const target = path.join(rootDir, 'src-tauri/target');
  if (
    journal.schemaVersion !== 1 ||
    path.dirname(journal.stageRoot) !== target ||
    !path.basename(journal.stageRoot).startsWith('payload-stage-')
  )
    throw new Error('Invalid Payload promotion journal');
  const paths = payloadPaths(rootDir, journal.platform);
  const allowed = [
    paths.source,
    paths.archive,
    path.join(rootDir, NATIVE_RECORDER_LOCK_PATH)
  ];
  for (const [index, item] of journal.replacements.entries()) {
    if (
      item.destination !== allowed[index] ||
      item.backup !== path.join(journal.stageRoot, `backup-${index}`)
    )
      throw new Error('Invalid Payload promotion destination');
  }
  if (journal.replacements.length !== allowed.length)
    throw new Error('Incomplete Payload promotion journal');
  for (const item of [...journal.replacements].reverse()) {
    if (fs.existsSync(item.backup)) {
      fs.rmSync(item.destination, { recursive: true, force: true });
      fs.renameSync(item.backup, item.destination);
    } else if (!item.existed) {
      fs.rmSync(item.destination, { recursive: true, force: true });
    }
  }
  fs.unlinkSync(journalPath);
  fs.rmSync(journal.stageRoot, { recursive: true, force: true });
}

export function promotePayload({
  rootDir,
  stageRoot,
  stagedInstaller,
  platform,
  rename = fs.renameSync
}: PromotePayloadOptions): void {
  const target = payloadPaths(rootDir, platform);
  const staged = payloadPaths(stagedInstaller, platform);
  const replacements = [
    [staged.source, target.source],
    [staged.archive, target.archive],
    [
      path.join(stagedInstaller, NATIVE_RECORDER_LOCK_PATH),
      path.join(rootDir, NATIVE_RECORDER_LOCK_PATH)
    ]
  ].map(([source, destination], index): PromotionReplacement => ({
    source,
    destination,
    backup: path.join(stageRoot, `backup-${index}`),
    existed: fs.existsSync(destination)
  }));
  writeJson(target.journal, {
    schemaVersion: 1,
    stageRoot,
    platform,
    replacements
  });
  try {
    for (const item of replacements) {
      fs.mkdirSync(path.dirname(item.destination), { recursive: true });
      if (item.existed) rename(item.destination, item.backup);
      rename(item.source, item.destination);
    }
    // The journal is the packaging gate. A crash before its removal is recovered
    // to the prior generation, even if every rename had already completed.
    fs.unlinkSync(target.journal);
  } catch (error) {
    recoverPromotion(rootDir);
    throw error;
  }
}

function copySeedPayload(
  sourceDir: string,
  destinationDir: string,
  platform: TauriBuildPlatform
): void {
  const entries = platformInventory(platform);
  fs.mkdirSync(destinationDir, { recursive: true });
  const walk = (directory: string, prefix = '') => {
    for (const file of fs.readdirSync(directory, { withFileTypes: true })) {
      const relative = prefix ? `${prefix}/${file.name}` : file.name;
      if (file.isSymbolicLink())
        throw new Error(`Symlink in Payload seed: ${relative}`);
      const entry = payloadEntry(relative, entries);
      if (entry && !['bootstrap', 'native'].includes(entry.producer)) continue;
      if (file.isDirectory()) {
        walk(path.join(directory, file.name), relative);
        continue;
      }
      if (!entry) throw new Error(`Undeclared Payload seed: ${relative}`);
      const destination = path.join(destinationDir, relative);
      fs.mkdirSync(path.dirname(destination), { recursive: true });
      fs.copyFileSync(path.join(directory, file.name), destination);
      fs.chmodSync(
        destination,
        fs.statSync(path.join(directory, file.name)).mode
      );
    }
  };
  walk(sourceDir);
}

export function assertReleaseBuildArgs(
  msbuildArgs: readonly string[],
  platform?: BuildPlatform
): void {
  // Only the game reference directories are caller-selectable: Managed for a
  // desktop host, the Interop Snapshot and Loader Release for Android (each
  // still checked against the Snapshot Lock). Build graph, compiler, output and
  // version overrides would invalidate source provenance.
  const names =
    platform === 'android' ? 'InteropPath|LemonLoaderPath' : 'ManagedPath';
  const allowed = new RegExp(`^(?:-p:|--property:)(?:${names})=[^;\\r\\n]+$`);
  for (const arg of msbuildArgs) {
    if (!allowed.test(arg))
      throw new Error(`Unsupported release build override: ${arg}`);
  }
}

function preparePayloadUnlocked({
  workspaceRoot,
  rootDir,
  platform,
  msbuildArgs = [],
  resolveManaged,
  ensureNative = ensureNativeRecorderInput,
  verifyNative = verifyNativeRecorderInput,
  produce,
  validate = validatePayloadZip
}: PreparePayloadOptions & { rootDir: string }): PayloadBuildRecord {
  const hostPlatform =
    process.platform === 'darwin'
      ? 'macos'
      : process.platform === 'win32'
        ? 'windows'
        : null;
  if (!produce && hostPlatform !== platform)
    throw new Error(`Build ${platform} Payload on its native host`);
  assertReleaseBuildArgs(msbuildArgs);
  recoverPromotion(rootDir);
  synchronizePayloadProjection(workspaceRoot, { check: true });
  const modRoot = path.join(workspaceRoot, 'bazaarplusplus-mod');
  const managedPath = fs.realpathSync(
    resolveManaged
      ? resolveManaged()
      : execFileSync(
          'bash',
          [
            path.join(modRoot, 'scripts', 'game.sh'),
            'managed-path',
            ...msbuildArgs
          ],
          { cwd: modRoot, encoding: 'utf8' }
        ).trim()
  );
  const productVersion = readProductVersion(workspaceRoot);
  const inputs = computePayloadInputs({ workspaceRoot, managedPath });
  // The lock file sits under mod/build, so it is already in the input digest;
  // the entry is recorded for provenance, not as a second seal.
  const lockEntry = lockEntryForManaged({
    workspaceRoot,
    platform,
    managedPath
  });
  const stageRoot = fs.mkdtempSync(
    path.join(rootDir, 'src-tauri/target/payload-stage-')
  );
  const stagedInstaller = path.join(stageRoot, 'installer');
  try {
    const staged = payloadPaths(stagedInstaller, platform);
    copySeedPayload(
      payloadPaths(rootDir, platform).source,
      staged.source,
      platform
    );
    for (const relative of [
      'src-tauri/history-database-compatibility.json',
      NATIVE_RECORDER_LOCK_PATH
    ]) {
      const target = path.join(stagedInstaller, relative);
      fs.mkdirSync(path.dirname(target), { recursive: true });
      fs.copyFileSync(path.join(rootDir, relative), target);
    }
    ensureNative({ rootDir: stagedInstaller, sourceRoot: modRoot, platform });
    const resources = path.join(stagedInstaller, 'src-tauri/resources');
    const remoteDataDirectory = path.join(stageRoot, 'seeds');
    const buildArgs = [
      `-p:ManagedPath=${managedPath}`,
      `-p:BPPInstallerSourcePath=${resources}`,
      `-p:BppPayloadStagingRoot=${resources}`,
      `-p:RemoteEmbeddedDataDirectory=${remoteDataDirectory}`
    ];
    if (produce)
      produce({
        rootDir: stagedInstaller,
        platform,
        resources,
        remoteDataDirectory,
        managedPath,
        buildArgs
      });
    else
      execFileSync(
        'bash',
        [path.join(modRoot, 'scripts', 'build.sh'), 'produce', ...buildArgs],
        {
          cwd: modRoot,
          stdio: 'inherit',
          env: {
            ...process.env,
            BPP_RELEASE_ARTIFACTS: path.join(stageRoot, 'managed-artifacts')
          }
        }
      );
    if (
      computePayloadInputs({ workspaceRoot, managedPath }).digest !==
      inputs.digest
    )
      throw new Error('Release inputs changed while building the Payload');
    verifyNative({ rootDir: stagedInstaller, platforms: [platform] });
    // Every install carries the manifest of its own Managed Payload; it is
    // written once every non-managed file it digests is staged.
    writeManagedPayloadManifest({
      sourceDir: staged.source,
      platform,
      version: productVersion,
      nonManagedPayloadDigest: desktopNonManagedPayloadDigest(
        staged.source,
        platform
      )
    });
    preparePayloadZip({ rootDir: stagedInstaller, platform, productVersion });
    const archivePlaceholder = path.join(
      payloadPaths(rootDir, platform).archive,
      '.gitkeep'
    );
    if (fs.existsSync(archivePlaceholder))
      fs.copyFileSync(
        archivePlaceholder,
        path.join(staged.archive, '.gitkeep')
      );
    validate({ rootDir: stagedInstaller, platform, productVersion });
    const seeds = fs.existsSync(remoteDataDirectory)
      ? fs
          .readdirSync(remoteDataDirectory)
          .sort()
          .filter((file) => file.endsWith('.json'))
          .map((file) => ({
            path: file,
            sha256: hash(fs.readFileSync(path.join(remoteDataDirectory, file)))
          }))
      : [];
    writeJson(staged.buildRecord, {
      schemaVersion: PAYLOAD_BUILD_RECORD_SCHEMA_VERSION,
      productVersion,
      platform,
      managedPath,
      lockEntry,
      inputDigest: inputs.digest,
      inputs: inputs.files,
      seeds,
      output: payloadOutput(stagedInstaller, platform)
    });
    promotePayload({ rootDir, stageRoot, stagedInstaller, platform });
    return json<PayloadBuildRecord>(
      payloadPaths(rootDir, platform).buildRecord
    );
  } finally {
    // Preserve backups when recovery itself failed; the journal remains a gate.
    if (!fs.existsSync(payloadPaths(rootDir, platform).journal))
      fs.rmSync(stageRoot, { recursive: true, force: true });
  }
}

export function preparePayload(
  options: PreparePayloadOptions
): PayloadBuildRecord {
  const rootDir =
    options.rootDir ??
    path.join(options.workspaceRoot, 'bazaarplusplus-installer');
  return withPayloadLock(rootDir, 'prepare', () =>
    preparePayloadUnlocked({ ...options, rootDir })
  );
}

// The signed Managed Payload zip of the sealed Payload source, beside the
// release artifacts (mod ADR-0014).
function writeDesktopManagedPayload({
  rootDir,
  workspaceRoot,
  platform,
  version,
  sign
}: {
  rootDir: string;
  workspaceRoot: string;
  platform: TauriBuildPlatform;
  version: ProductVersion;
  sign: (options: SignWithUpdaterKeyOptions) => string;
}): ManagedPayloadArtifact {
  const outputPath = path.join(
    rootDir,
    'src-tauri/target/release-artifacts/managed',
    releasePlatform(platform).key,
    path.posix.basename(
      managedPayloadKey(version, releasePlatform(platform).key)
    )
  );
  fs.rmSync(path.dirname(outputPath), { recursive: true, force: true });
  const zip = writeManagedPayloadZip({
    sourceDir: payloadPaths(rootDir, platform).source,
    platform,
    outputPath,
    sign: ({ file }) => sign({ workspaceRoot, file })
  });
  return {
    path: zip.path,
    signature: sign({ workspaceRoot, file: zip.path }),
    historyDatabaseUserVersion: zip.manifest.historyDatabaseUserVersion,
    nonManagedPayloadDigest: zip.manifest.nonManagedPayloadDigest
  };
}

export function buildProduct(
  options: BuildProductOptions
): CreatedArtifactManifest {
  const rootDir =
    options.rootDir ??
    path.join(options.workspaceRoot, 'bazaarplusplus-installer');
  const {
    bundle,
    captureIdentity = releaseSourceIdentity,
    writeArtifact = createArtifactManifest,
    sign = signWithUpdaterKey
  } = options;
  return withPayloadLock(rootDir, 'build', (token) => {
    const startingCommit = captureIdentity(rootDir).commit;
    const manifestPath = artifactManifestPath(rootDir, options.platform);
    // An unsuccessful build must not leave a previous publishable receipt.
    fs.rmSync(manifestPath, { force: true });
    preparePayloadUnlocked({ ...options, rootDir });
    // Preparation can legitimately refresh tracked native producer outputs.
    // Capture packaging inputs after it, but never relabel a changed checkout.
    const identity = captureIdentity(rootDir);
    if (identity.commit !== startingCommit)
      throw new Error('Git identity changed during Payload preparation');
    verifyPayloadBuild({ ...options, rootDir });
    bundle({ token });
    // macOS signs a copy inside the archive. The unsigned source remains sealed.
    verifyPayloadSource({ ...options, rootDir });
    if (JSON.stringify(captureIdentity(rootDir)) !== JSON.stringify(identity))
      throw new Error(
        'Product source or Git identity changed during packaging; no artifact manifest was written'
      );
    const version = readProductVersion(options.workspaceRoot);
    const managed = writeDesktopManagedPayload({
      rootDir,
      workspaceRoot: options.workspaceRoot,
      platform: options.platform,
      version,
      sign
    });
    return writeArtifact({
      rootDir,
      platform: options.platform,
      version,
      managedPayload: managed,
      gitState: identity
    });
  });
}

import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { runGit } from '../scripts/git-command.ts';
import { tauriPlatform } from './release-platforms.ts';
import type {
  ReleasePlatformKey,
  TauriBuildPlatform,
  TauriReleasePlatform
} from './release-platforms.ts';
import type { ProductVersion } from './product.ts';
import type { PayloadBuildRecord } from './payload.ts';

/** The commit a release is built from, and whether its inputs are dirty. */
export interface GitState {
  commit: string;
  dirty: boolean;
}

/** GitState plus the digest of every release source input. */
export interface ReleaseSourceIdentity extends GitState {
  digest: string;
}

/** One artifact file, relative to the installer directory. */
export interface ArtifactRecord {
  path: string;
  size: number;
  sha256: string;
}

/** The signed Managed Payload zip a desktop build wrote (mod ADR-0014). */
export interface ManagedPayloadArtifact {
  path: string;
  signature: string;
  historyDatabaseUserVersion: number;
  nonManagedPayloadDigest: string;
}

/** The artifact manifest, src-tauri/target/release-artifacts/<platform>.json. */
export interface ArtifactManifest {
  schemaVersion: 1;
  appVersion: ProductVersion;
  buildPlatform: TauriBuildPlatform;
  releasePlatformKey: ReleasePlatformKey;
  gitCommit: string;
  dirty: boolean;
  payloadBuild: ArtifactRecord;
  installer: ArtifactRecord;
  updater: ArtifactRecord;
  signature: ArtifactRecord & { content: string };
  managedPayload: ArtifactRecord & { signature: string };
  historyDatabaseUserVersion: number;
  nonManagedPayloadDigest: string;
  builtAt: string;
}

/** The options of createArtifactManifest. */
export interface CreateArtifactManifestOptions {
  rootDir: string;
  platform: TauriBuildPlatform;
  version: ProductVersion;
  managedPayload: ManagedPayloadArtifact;
  gitState?: GitState;
  builtAt?: Date;
}

/** The artifact manifest createArtifactManifest wrote, and where. */
export interface CreatedArtifactManifest {
  manifest: ArtifactManifest;
  manifestPath: string;
}

/** The options of validateArtifactManifest. */
export interface ValidateArtifactManifestOptions {
  rootDir: string;
  manifestPath: string;
  platform: TauriBuildPlatform;
  version: ProductVersion;
  gitState?: GitState;
}

/** A validated artifact manifest and the absolute paths of its artifacts. */
export interface ValidatedArtifactManifest {
  manifest: ArtifactManifest;
  /** The Snapshot Lock entry the sealed Payload build record names. */
  lockEntry: PayloadBuildRecord['lockEntry'];
  installer: string;
  updater: string;
  signature: string;
  managedPayload: string;
}

// The artifacts one native build left for a platform.
interface DiscoveredArtifacts {
  installer: string;
  updater: string;
  signature: string;
}

// Release source inputs, relative to the installer directory. Git matches a
// missing path to nothing, so release-inputs.test.ts keeps every entry real.
export const RELEASE_SOURCE_PATHS = [
  '.',
  '../VERSION',
  '../release',
  '../scripts/git-command.ts',
  '../bazaarplusplus-mod'
];

// The sealed Payload build record payload.ts writes and this manifest seals:
// schema 3 names the Snapshot Lock entry the Payload compiled against, so
// every artifact answers which platform, channel and game version it was built
// for (ADR 0004). An older record is re-prepared, never accepted.
export const PAYLOAD_BUILD_RECORD_SCHEMA_VERSION = 1;

function sha256(buffer: string | NodeJS.ArrayBufferView): string {
  return crypto.createHash('sha256').update(buffer).digest('hex');
}

export function gitStateForRoot(rootDir: string): GitState {
  const commit = runGit(['rev-parse', 'HEAD'], { cwd: rootDir }).trim();
  // Include the product's producer and shared release inputs, but not unrelated
  // website/analyzer work or local root tooling.
  const status = runGit(
    [
      'status',
      '--porcelain',
      '--untracked-files=all',
      '--',
      ...RELEASE_SOURCE_PATHS
    ],
    { cwd: rootDir }
  ).trim();
  return { commit, dirty: status.length > 0 };
}

export function releaseSourceIdentity(rootDir: string): ReleaseSourceIdentity {
  const files = [
    ...new Set(
      runGit(
        [
          'ls-files',
          '--cached',
          '--others',
          '--exclude-standard',
          '-z',
          '--',
          ...RELEASE_SOURCE_PATHS
        ],
        { cwd: rootDir }
      )
        .split('\0')
        .filter(Boolean)
    )
  ].sort();
  const inputs = files.map((name): [string, string | null] => {
    const file = path.resolve(rootDir, name);
    const stat = fs.lstatSync(file, { throwIfNoEntry: false });
    const bytes = stat?.isSymbolicLink()
      ? fs.readlinkSync(file)
      : stat?.isFile()
        ? fs.readFileSync(file)
        : '';
    return [name, stat ? sha256(bytes) : null];
  });
  return {
    ...gitStateForRoot(rootDir),
    digest: sha256(JSON.stringify(inputs))
  };
}

function relativeArtifactPath(rootDir: string, filePath: string): string {
  const relativePath = path.relative(rootDir, filePath);
  if (
    !relativePath ||
    relativePath === '..' ||
    relativePath.startsWith(`..${path.sep}`) ||
    path.isAbsolute(relativePath)
  ) {
    throw new Error(`Artifact path escapes the project root: ${filePath}`);
  }
  return relativePath.split(path.sep).join('/');
}

function artifactRecord(rootDir: string, filePath: string): ArtifactRecord {
  const stat = fs.statSync(filePath, { throwIfNoEntry: false });
  if (!stat?.isFile()) throw new Error(`Missing artifact: ${filePath}`);
  const content = fs.readFileSync(filePath);
  return {
    path: relativeArtifactPath(rootDir, filePath),
    size: stat.size,
    sha256: sha256(content)
  };
}

function recursivelyFindFiles(
  rootDir: string,
  predicate: (filePath: string) => boolean
): string[] {
  if (!fs.statSync(rootDir, { throwIfNoEntry: false })?.isDirectory())
    return [];
  const matches: string[] = [];
  const walk = (directory: string) => {
    for (const dirent of fs.readdirSync(directory, { withFileTypes: true })) {
      const child = path.join(directory, dirent.name);
      if (dirent.isDirectory()) walk(child);
      else if (dirent.isFile() && predicate(child)) matches.push(child);
    }
  };
  walk(rootDir);
  return matches.sort();
}

function exactlyOne(
  candidates: readonly string[],
  description: string,
  platform: string
): string {
  if (candidates.length === 0) {
    throw new Error(`No ${description} found for ${platform}`);
  }
  if (candidates.length > 1) {
    throw new Error(
      `Multiple ${description}s found for ${platform}:\n${candidates.map((candidate) => `- ${candidate}`).join('\n')}`
    );
  }
  return candidates[0];
}

function assertFileNameContainsVersion(
  filePath: string,
  version: ProductVersion
): void {
  if (!path.basename(filePath).includes(version)) {
    throw new Error(
      `Artifact version mismatch: ${filePath} does not contain ${version}`
    );
  }
}

function matchesInstallerGlob(fileName: string, glob: string): boolean {
  if (!glob.startsWith('*'))
    throw new Error(`Unsupported installer glob: ${glob}`);
  return fileName.endsWith(glob.slice(1));
}

function discoverArtifacts(
  rootDir: string,
  platform: TauriReleasePlatform
): DiscoveredArtifacts {
  const installerDirectory = path.join(rootDir, platform.installerDir);
  const installerCandidates = fs
    .readdirSync(installerDirectory, { withFileTypes: true })
    .filter(
      (dirent) =>
        dirent.isFile() &&
        matchesInstallerGlob(dirent.name, platform.installerNameGlob) &&
        !dirent.name.endsWith('.sig')
    )
    .map((dirent) => path.join(installerDirectory, dirent.name))
    .sort();
  const installer = exactlyOne(
    installerCandidates,
    'installer artifact',
    platform.buildPlatform
  );
  const signature = exactlyOne(
    recursivelyFindFiles(path.join(rootDir, platform.bundleRoot), (filePath) =>
      filePath.endsWith('.sig')
    ),
    'updater signature',
    platform.buildPlatform
  );
  const updater = signature.slice(0, -'.sig'.length);
  if (!fs.statSync(updater, { throwIfNoEntry: false })?.isFile()) {
    throw new Error(`Missing updater artifact for signature: ${signature}`);
  }
  return { installer, updater, signature };
}

export function artifactManifestPath(
  rootDir: string,
  platform: TauriBuildPlatform
): string {
  return path.join(
    rootDir,
    'src-tauri',
    'target',
    'release-artifacts',
    `${platform}.json`
  );
}

// `managedPayload` is the signed Managed Payload zip build wrote (mod
// ADR-0014): its file, its signature, and the facts its manifest records.
export function createArtifactManifest({
  rootDir,
  platform,
  version,
  managedPayload,
  gitState = gitStateForRoot(rootDir),
  builtAt = new Date()
}: CreateArtifactManifestOptions): CreatedArtifactManifest {
  const definition = tauriPlatform(platform);
  const discovered = discoverArtifacts(rootDir, definition);
  assertFileNameContainsVersion(discovered.installer, version);
  const signatureContent = fs.readFileSync(discovered.signature, 'utf8').trim();
  if (!signatureContent) throw new Error('Updater signature is empty');

  if (
    !managedPayload?.signature?.trim() ||
    !Number.isSafeInteger(managedPayload.historyDatabaseUserVersion) ||
    !/^[a-f0-9]{64}$/.test(managedPayload.nonManagedPayloadDigest ?? '')
  )
    throw new Error('The build wrote no signed Managed Payload');
  const managedFile = path.resolve(managedPayload.path);
  assertFileNameContainsVersion(managedFile, version);

  const manifest: ArtifactManifest = {
    schemaVersion: 1,
    appVersion: version,
    buildPlatform: platform,
    releasePlatformKey: definition.key,
    gitCommit: gitState.commit,
    dirty: gitState.dirty,
    payloadBuild: artifactRecord(
      rootDir,
      path.join(
        rootDir,
        'src-tauri/resources/BepInExSource',
        platform,
        'payload-build.json'
      )
    ),
    installer: artifactRecord(rootDir, discovered.installer),
    updater: artifactRecord(rootDir, discovered.updater),
    signature: {
      ...artifactRecord(rootDir, discovered.signature),
      content: signatureContent
    },
    managedPayload: {
      ...artifactRecord(rootDir, managedFile),
      signature: managedPayload.signature.trim()
    },
    historyDatabaseUserVersion: managedPayload.historyDatabaseUserVersion,
    nonManagedPayloadDigest: managedPayload.nonManagedPayloadDigest,
    builtAt: builtAt.toISOString()
  };
  const manifestPath = artifactManifestPath(rootDir, platform);
  fs.mkdirSync(path.dirname(manifestPath), { recursive: true });
  fs.writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`);
  return { manifest, manifestPath };
}

function resolveManifestArtifact(
  rootDir: string,
  record: ArtifactRecord
): string {
  if (
    !record ||
    typeof record.path !== 'string' ||
    path.isAbsolute(record.path)
  ) {
    throw new Error('Artifact manifest contains an invalid path');
  }
  const resolved = path.resolve(rootDir, ...record.path.split('/'));
  const rootPrefix = `${path.resolve(rootDir)}${path.sep}`;
  if (!resolved.startsWith(rootPrefix)) {
    throw new Error(
      `Artifact manifest path escapes the project root: ${record.path}`
    );
  }
  return resolved;
}

function validateRecord(
  rootDir: string,
  label: string,
  record: ArtifactRecord
): string {
  const filePath = resolveManifestArtifact(rootDir, record);
  const stat = fs.statSync(filePath, { throwIfNoEntry: false });
  if (!stat?.isFile())
    throw new Error(`Missing artifact from manifest: ${filePath}`);
  const digest = sha256(fs.readFileSync(filePath));
  if (stat.size !== record.size || digest !== record.sha256) {
    throw new Error(
      `${label} size or SHA-256 does not match artifact manifest`
    );
  }
  return filePath;
}

export function validateArtifactManifest({
  rootDir,
  manifestPath,
  platform,
  version,
  gitState = gitStateForRoot(rootDir)
}: ValidateArtifactManifestOptions): ValidatedArtifactManifest {
  if (!fs.statSync(manifestPath, { throwIfNoEntry: false })?.isFile()) {
    throw new Error(`Missing artifact manifest: ${manifestPath}`);
  }
  const manifest = JSON.parse(
    fs.readFileSync(manifestPath, 'utf8')
  ) as ArtifactManifest;
  const definition = tauriPlatform(platform);
  if (manifest.schemaVersion !== 1)
    throw new Error('Unsupported artifact manifest schema');
  if (
    manifest.appVersion !== version ||
    manifest.buildPlatform !== platform ||
    manifest.releasePlatformKey !== definition.key
  ) {
    throw new Error(
      `Artifact manifest version mismatch or platform mismatch: expected ${version}/${definition.key}`
    );
  }
  if (manifest.gitCommit !== gitState.commit) {
    throw new Error(
      `Artifact manifest commit mismatch: built ${manifest.gitCommit}, current ${gitState.commit}`
    );
  }
  if (manifest.dirty)
    throw new Error('Artifact manifest records a dirty build');
  if (gitState.dirty)
    throw new Error('Current checkout is dirty; refusing upload');

  const payloadBuild = validateRecord(
    rootDir,
    'Payload build',
    manifest.payloadBuild
  );
  const payloadIdentity = JSON.parse(
    fs.readFileSync(payloadBuild, 'utf8')
  ) as PayloadBuildRecord;
  if (
    payloadIdentity.schemaVersion !== PAYLOAD_BUILD_RECORD_SCHEMA_VERSION ||
    payloadIdentity.productVersion !== version ||
    payloadIdentity.platform !== platform
  ) {
    throw new Error('Payload build identity does not match the installer');
  }

  const installer = validateRecord(rootDir, 'Installer', manifest.installer);
  const updater = validateRecord(rootDir, 'Updater', manifest.updater);
  const signature = validateRecord(rootDir, 'Signature', manifest.signature);
  if (
    fs.readFileSync(signature, 'utf8').trim() !== manifest.signature.content
  ) {
    throw new Error('Signature content does not match artifact manifest');
  }
  assertFileNameContainsVersion(installer, version);
  const managedPayload = validateRecord(
    rootDir,
    'Managed Payload',
    manifest.managedPayload
  );
  assertFileNameContainsVersion(managedPayload, version);
  return {
    manifest,
    lockEntry: payloadIdentity.lockEntry,
    installer,
    updater,
    signature,
    managedPayload
  };
}

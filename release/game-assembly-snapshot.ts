import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { WORKSPACE_ROOT } from './product.ts';
import { putReplaceable, r2StoreFromEnvironment } from './r2-store.ts';
import type { ObjectStore } from './r2-store.ts';
import {
  hash,
  jsonBytes,
  readSnapshotManifest,
  tar
} from './snapshot-files.ts';
import {
  ENTRY_FIELDS,
  GAME_LIBS_LOCK_PATH,
  GAME_VERSION_PATTERN,
  MANAGED_PLATFORMS,
  SNAPSHOT_CHANNELS,
  STEAM_BRANCHES,
  lockKey,
  managedEntries,
  parseGameVersion,
  readGameLibsLock,
  snapshotBlobKey,
  snapshotDirectory,
  snapshotManifestKey,
  writeGameLibsLockEntry
} from './snapshot-lock.ts';
import type {
  GameLibsLock,
  ManagedLockEntry,
  ManagedLockedEntry,
  ManagedPlatform,
  SnapshotChannel,
  SnapshotPlatform
} from './snapshot-lock.ts';

// Game Assembly Snapshots (root GLOSSARY.md, ADR 0004 and ADR 0015): capture
// from a mounted install or a Steam depot, publish and fetch, the Managed
// directory digest that the lock, the snapshot manifest and the sealed build
// record share, the lock entry a build used, and the private store that
// interop-snapshot.ts shares. snapshot-lock.ts owns the Snapshot Lock schema
// and snapshot-files.ts the shared file helpers; the CLI in game-libs.ts drives
// this module.

export const GAME_LIBS_BUCKET = 'bazaarplusplus-game-libs';
export const SNAPSHOT_MANIFEST_SCHEMA_VERSION = 1;

// Where a Steam capture reads each desktop platform (root ADR 0015). macOS is
// the arm64 depot that lock entries already use.
export const STEAM_APP_ID = 1617400;
export const STEAM_DEPOTS: Readonly<Partial<Record<SnapshotPlatform, number>>> =
  Object.freeze({
    macos: 1617401,
    windows: 1617402
  });

/** A desktop snapshot's manifest.json: its lock entry plus its identity. */
export interface ManagedSnapshotManifest extends ManagedLockEntry {
  schemaVersion: typeof SNAPSHOT_MANIFEST_SCHEMA_VERSION;
  platform: SnapshotPlatform;
  channel: SnapshotChannel;
}

/** One file of a Managed or interop directory digest. */
export interface DirectoryRecord {
  path: string;
  sha256: string;
}

export function gameVersionFile(managedPath: string): string {
  return path.join(path.dirname(managedPath), 'globalgamemanagers');
}

// Reads the game version from the Data directory above a Managed directory.
export function readGameVersion(managedPath: string): string {
  const file = gameVersionFile(managedPath);
  if (!fs.existsSync(file))
    throw new Error(
      `No globalgamemanagers beside ${managedPath}; the game version is read from the Data directory above Managed`
    );
  const text = fs.readFileSync(file).toString('latin1');
  const versions = [
    ...new Set(
      [...text.matchAll(GAME_VERSION_PATTERN)].map((match) => match[0])
    )
  ];
  if (versions.length !== 1)
    throw new Error(
      `Expected exactly one game version in ${file}, found ${versions.length}${versions.length ? `: ${versions.join(', ')}` : ''}`
    );
  return versions[0];
}

// The per-file records of a Managed directory: every top-level DLL, keyed as
// managed/<name>. payload.ts folds these into the sealed build record; the
// lock entry and the snapshot manifest hash them alone. The order is ordinal so
// the digest is identical on every machine and locale.
export function managedDirectoryRecords(
  managedPath: string
): DirectoryRecord[] {
  const records: DirectoryRecord[] = [];
  for (const entry of fs.readdirSync(managedPath, { withFileTypes: true })) {
    if (entry.isFile() && entry.name.endsWith('.dll'))
      records.push({
        path: `managed/${entry.name}`,
        sha256: hash(fs.readFileSync(path.join(managedPath, entry.name)))
      });
  }
  if (!records.some((record) => record.path === 'managed/Assembly-CSharp.dll'))
    throw new Error(`Managed path has no Assembly-CSharp.dll: ${managedPath}`);
  records.sort((a, b) => (a.path < b.path ? -1 : a.path > b.path ? 1 : 0));
  return records;
}

export function managedDirectoryDigest(managedPath: string): string {
  return hash(JSON.stringify(managedDirectoryRecords(managedPath)));
}

// Steam build ids only grow, so a preview channel below online carries no game
// change online lacks; the CompatCheck matrix skips it. Returns the online entry
// it is behind, or null.
export function previewBehindOnline(
  lock: GameLibsLock,
  platform: ManagedPlatform,
  channel: SnapshotChannel
): ManagedLockEntry | null {
  if (channel === 'online') return null;
  const preview = lock.entries[
    lockKey(platform, channel)
  ] as ManagedLockEntry | null;
  const online = lock.entries[
    lockKey(platform, 'online')
  ] as ManagedLockEntry | null;
  if (preview === null || online === null) return null;
  return Number(preview.buildid) < Number(online.buildid) ? online : null;
}

// The private store through CLOUDFLARE_API_TOKEN, which reads and writes the
// bucket.
export function gameLibsStoreFromEnvironment(
  env: NodeJS.ProcessEnv = process.env
): ObjectStore {
  return r2StoreFromEnvironment(env, { bucket: GAME_LIBS_BUCKET });
}

function entryFromManifest(
  manifest: ManagedSnapshotManifest
): ManagedLockEntry {
  // ENTRY_FIELDS names every ManagedLockEntry field.
  return Object.fromEntries(
    ENTRY_FIELDS.map((field) => [field, manifest[field]])
  ) as ManagedLockEntry;
}

// Archives the Managed directory of a mounted game install under game-libs/
// and records it in the lock. The capture's identity is the game version read
// beside Managed; the Steam facts are its source record.
export interface CaptureSnapshotOptions {
  workspaceRoot?: string;
  modRoot?: string;
  managedPath: string;
  buildid: string | number;
  steamBranch: string;
  now?: Date;
}

/** What a Managed capture wrote. */
export interface CapturedSnapshot {
  key: string;
  directory: string;
  manifest: ManagedSnapshotManifest;
}

export function captureSnapshot({
  workspaceRoot = WORKSPACE_ROOT,
  modRoot = path.join(workspaceRoot, 'bazaarplusplus-mod'),
  managedPath,
  buildid,
  steamBranch,
  now = new Date()
}: CaptureSnapshotOptions): CapturedSnapshot {
  const gameVersion = readGameVersion(managedPath);
  const { platform, channel } = parseGameVersion(gameVersion);
  if (steamBranch !== STEAM_BRANCHES[channel])
    throw new Error(
      `Game version ${gameVersion} is a ${channel} build, but Steam reports the ${steamBranch} branch mounted; let Steam finish switching branches, then snapshot again`
    );
  const sha256 = managedDirectoryDigest(managedPath);
  const directory = snapshotDirectory(modRoot, platform, channel, gameVersion);
  const managed = path.join(directory, 'Managed');
  if (fs.existsSync(managed)) {
    const existing = managedDirectoryDigest(managed);
    if (existing !== sha256)
      throw new Error(
        `Snapshot ${directory} already exists with sha256 ${existing}, but the mounted game hashes to ${sha256}; delete the directory to recapture`
      );
  } else {
    const staging = `${directory}.capture-${process.pid}`;
    fs.rmSync(staging, { recursive: true, force: true });
    fs.cpSync(managedPath, path.join(staging, 'Managed'), { recursive: true });
    if (managedDirectoryDigest(path.join(staging, 'Managed')) !== sha256)
      throw new Error('Managed directory changed while it was being copied');
    fs.renameSync(staging, directory);
  }
  const manifest: ManagedSnapshotManifest = {
    schemaVersion: SNAPSHOT_MANIFEST_SCHEMA_VERSION,
    platform,
    channel,
    gameVersion,
    sha256,
    buildid: String(buildid),
    steamBranch,
    capturedAt: now.toISOString()
  };
  fs.writeFileSync(path.join(directory, 'manifest.json'), jsonBytes(manifest));
  const key = lockKey(platform, channel);
  writeGameLibsLockEntry(workspaceRoot, key, entryFromManifest(manifest));
  return { key, directory, manifest };
}

/** Packs a snapshot directory's Managed/ into gzipped tar bytes. */
export type SnapshotTar = (directory: string) => Buffer;

function tarballOf(directory: string): Buffer {
  const file = path.join(
    fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-game-libs-')),
    'Managed.tar.gz'
  );
  try {
    // GNU tar treats the colon in a Windows archive path as a remote host.
    // Keep the archive name relative; -C still selects the absolute input.
    tar(
      [
        '-czf',
        path.basename(file),
        '-C',
        path.resolve(directory).split(path.sep).join('/'),
        'Managed'
      ],
      path.dirname(file)
    );
    return fs.readFileSync(file);
  } finally {
    fs.rmSync(path.dirname(file), { recursive: true, force: true });
  }
}

function extractTarball(bytes: Buffer, into: string): void {
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-game-libs-'));
  try {
    const file = path.join(temp, 'Managed.tar.gz');
    fs.writeFileSync(file, bytes);
    fs.mkdirSync(into, { recursive: true });
    // Snapshots packed on macOS before COPYFILE_DISABLE still carry `._<name>`
    // members; skip them so the extracted digest matches the lock entry.
    tar(
      [
        '-xzf',
        path.basename(file),
        '--exclude=._*',
        '-C',
        path.resolve(into).split(path.sep).join('/')
      ],
      temp
    );
  } finally {
    fs.rmSync(temp, { recursive: true, force: true });
  }
}

// Verifies a local snapshot against its lock entry before it is used or sent.
function verifiedSnapshot(
  modRoot: string,
  platform: string,
  channel: string,
  entry: ManagedLockEntry
): { directory: string; managed: string } | null {
  const directory = snapshotDirectory(
    modRoot,
    platform,
    channel,
    entry.gameVersion
  );
  const managed = path.join(directory, 'Managed');
  if (!fs.existsSync(path.join(managed, 'Assembly-CSharp.dll'))) return null;
  const digest = managedDirectoryDigest(managed);
  if (digest !== entry.sha256)
    throw new Error(
      `Snapshot ${directory} hashes to ${digest}, but the lock entry records ${entry.sha256}; delete the directory and fetch again`
    );
  return { directory, managed };
}

// Uploads a captured snapshot to the private store: the tarball under its
// content address, the manifest under the entry identity. Either write is
// skipped when the object is already there; a tarball is not byte-stable
// across tar implementations, so its presence is the only check.
export interface PublishSnapshotOptions {
  workspaceRoot?: string;
  modRoot?: string;
  platform: ManagedPlatform;
  channel: SnapshotChannel;
  store: Pick<ObjectStore, 'get' | 'head' | 'put'>;
  tar?: SnapshotTar;
  log?: (message: string) => void;
}

/** Where a publish stored a snapshot. */
export interface PublishedSnapshot {
  blobKey: string;
  manifestKey: string;
}

export async function publishSnapshot({
  workspaceRoot = WORKSPACE_ROOT,
  modRoot = path.join(workspaceRoot, 'bazaarplusplus-mod'),
  platform,
  channel,
  store,
  tar = tarballOf,
  log = () => {}
}: PublishSnapshotOptions): Promise<PublishedSnapshot> {
  const key = lockKey(platform, channel);
  const entry = readGameLibsLock(workspaceRoot).entries[key];
  if (entry === null)
    throw new Error(
      `Lock entry ${key} is empty; run 'just mod::snapshot' first`
    );
  const snapshot = verifiedSnapshot(modRoot, platform, channel, entry);
  if (!snapshot)
    throw new Error(
      `No snapshot under game-libs/ for ${key} ${entry.gameVersion}; run 'just mod::snapshot' on the machine that captured it`
    );
  const manifest = readSnapshotManifest<ManagedSnapshotManifest>(
    snapshot.directory
  );
  if (!manifest || manifest.sha256 !== entry.sha256)
    throw new Error(
      `${snapshot.directory}/manifest.json is missing or does not match the lock entry; recapture the snapshot`
    );
  const blobKey = snapshotBlobKey(entry.sha256);
  if (await store.head(blobKey)) {
    log(`Snapshot tarball already stored: ${blobKey}`);
  } else {
    const bytes = tar(snapshot.directory);
    if (await store.put(blobKey, bytes, { ifNoneMatch: true }))
      log(`Uploaded ${blobKey} (${bytes.length} bytes)`);
    else log(`Another publisher stored ${blobKey} first`);
  }
  const manifestKey = snapshotManifestKey(platform, channel, entry.gameVersion);
  await putReplaceable(
    store,
    manifestKey,
    jsonBytes(manifest),
    'application/json'
  );
  log(`Recorded ${manifestKey}`);
  return { blobKey, manifestKey };
}

// Resolves the Managed directory that satisfies one lock entry, in the order
// ADR 0004 fixes: a verified snapshot under game-libs/, the local Steam install
// when its game version and sha256 match, then the private store. With
// allowUnlocked, an empty entry accepts a local install of the right channel
// (decompile reads whatever is mounted; nothing it produces is a build input).
export interface FetchSnapshotOptions {
  workspaceRoot?: string;
  modRoot?: string;
  platform: ManagedPlatform;
  channel: SnapshotChannel;
  steamManagedPath?: string;
  allowUnlocked?: boolean;
  storeFactory?: () => Pick<ObjectStore, 'get'>;
  log?: (message: string) => void;
}

/** Where a resolved Managed directory came from. */
export type ManagedSource = 'steam-unlocked' | 'snapshot' | 'steam' | 'store';

/** The Managed directory that satisfies a lock entry. */
export interface FetchedSnapshot {
  managedPath: string;
  source: ManagedSource;
  gameVersion: string;
}

export async function fetchSnapshot({
  workspaceRoot = WORKSPACE_ROOT,
  modRoot = path.join(workspaceRoot, 'bazaarplusplus-mod'),
  platform,
  channel,
  steamManagedPath = '',
  allowUnlocked = false,
  storeFactory = gameLibsStoreFromEnvironment,
  log = () => {}
}: FetchSnapshotOptions): Promise<FetchedSnapshot> {
  const key = lockKey(platform, channel);
  const entry = readGameLibsLock(workspaceRoot).entries[key];
  const steamPresent =
    steamManagedPath &&
    fs.existsSync(path.join(steamManagedPath, 'Assembly-CSharp.dll'));
  const steamVersion = steamPresent ? readGameVersion(steamManagedPath) : null;
  if (entry === null) {
    if (allowUnlocked && steamVersion) {
      const mounted = parseGameVersion(steamVersion);
      if (mounted.platform === platform && mounted.channel === channel) {
        log(
          `Lock entry ${key} is empty; using the mounted ${steamVersion} unverified`
        );
        return {
          managedPath: steamManagedPath,
          source: 'steam-unlocked',
          gameVersion: steamVersion
        };
      }
    }
    throw new Error(
      `Lock entry ${key} is empty${steamVersion ? ` and the mounted game is ${steamVersion}` : ''}; capture it with 'just mod::snapshot' on a ${platform} machine with the ${STEAM_BRANCHES[channel]} branch mounted, publish it, and commit the lock`
    );
  }
  const snapshot = verifiedSnapshot(modRoot, platform, channel, entry);
  if (snapshot)
    return {
      managedPath: snapshot.managed,
      source: 'snapshot',
      gameVersion: entry.gameVersion
    };
  if (steamVersion === entry.gameVersion) {
    const digest = managedDirectoryDigest(steamManagedPath);
    if (digest !== entry.sha256)
      throw new Error(
        `The mounted game is ${steamVersion} as the lock entry ${key} expects, but its Managed directory hashes to ${digest} instead of ${entry.sha256}; verify the game files in Steam or recapture the snapshot`
      );
    return {
      managedPath: steamManagedPath,
      source: 'steam',
      gameVersion: entry.gameVersion
    };
  }
  const mismatch = `Lock entry ${key} wants ${entry.gameVersion}; the local Steam install is ${steamVersion ?? 'not found'}.`;
  let store;
  try {
    store = storeFactory();
  } catch (error) {
    throw new Error(
      `${mismatch} Switch The Bazaar to the ${STEAM_BRANCHES[channel]} branch in Steam, wait for a lock update, or fetch the snapshot from the private store with the Cloudflare operator token (just with-config release just mod::fetch ${platform} ${channel}): ${(error as Error).message}`
    );
  }
  const blobKey = snapshotBlobKey(entry.sha256);
  log(`${mismatch} Fetching ${blobKey} from the private store`);
  const blob = await store.get(blobKey, { maxBytes: 1024 * 1024 * 1024 });
  if (!blob)
    throw new Error(
      `${mismatch} The private store has no ${blobKey}; run 'just mod::publish ${platform} ${channel}' on the machine that captured it`
    );
  const directory = snapshotDirectory(
    modRoot,
    platform,
    channel,
    entry.gameVersion
  );
  const staging = `${directory}.fetch-${process.pid}`;
  fs.rmSync(staging, { recursive: true, force: true });
  try {
    extractTarball(blob.bytes, staging);
    const managed = path.join(staging, 'Managed');
    const digest = fs.existsSync(managed)
      ? managedDirectoryDigest(managed)
      : null;
    if (digest !== entry.sha256)
      throw new Error(
        `Stored snapshot ${blobKey} hashes to ${digest}, not the ${entry.sha256} the lock entry ${key} records`
      );
    const remote = await store.get(
      snapshotManifestKey(platform, channel, entry.gameVersion)
    );
    const manifest: ManagedSnapshotManifest = remote
      ? (JSON.parse(remote.bytes.toString('utf8')) as ManagedSnapshotManifest)
      : {
          schemaVersion: SNAPSHOT_MANIFEST_SCHEMA_VERSION,
          platform,
          channel,
          ...entry
        };
    fs.writeFileSync(path.join(staging, 'manifest.json'), jsonBytes(manifest));
    fs.rmSync(directory, { recursive: true, force: true });
    fs.renameSync(staging, directory);
  } finally {
    fs.rmSync(staging, { recursive: true, force: true });
  }
  log(`Fetched ${key} ${entry.gameVersion} into ${directory}`);
  return {
    managedPath: path.join(directory, 'Managed'),
    source: 'store',
    gameVersion: entry.gameVersion
  };
}

// ---------------------------------------------------------------------------
// Steam captures (ADR 0015). bazaarplusplus-mod/tools/SteamSnapshot reads the
// branch and downloads a depot's snapshot files; captureSnapshot then treats
// them as it treats a mounted install.

/** The last line tools/SteamSnapshot prints; `state` adds updatedAt. */
export interface SteamToolOutput {
  buildid: string;
  /** When the branch last changed, in Unix seconds. */
  updatedAt?: number;
}

/** Runs tools/SteamSnapshot with `args` and parses its result line. */
export type SteamToolRunner = (args: string[]) => SteamToolOutput;

/** What steam-stale prints: the desktop entries behind the branch. */
export interface SteamStaleResult {
  buildid: string;
  updatedAt: number | undefined;
  platforms: ManagedPlatform[];
}

/** What a Steam capture changed against the lock before it. */
export type SteamChangeKind = 'code' | 'content' | 'none';

function steamToolRunner(modRoot: string): SteamToolRunner {
  return (args) => {
    const output = execFileSync(
      'dotnet',
      [
        'run',
        '--project',
        'tools/SteamSnapshot/SteamSnapshot.csproj',
        '-c',
        'Release',
        '--no-launch-profile',
        '-v',
        'q',
        '--',
        ...args
      ],
      { cwd: modRoot, encoding: 'utf8', stdio: ['ignore', 'pipe', 'inherit'] }
    );
    const line = output.trim().split(/\r?\n/).at(-1);
    // split always returns at least one line.
    if (line === undefined) throw new Error('SteamSnapshot printed nothing');
    return JSON.parse(line) as SteamToolOutput;
  };
}

// Steam moves the buildid for any change to the branch, so it says when to look
// and steamChangeKind says what the look found.
export function staleSteamPlatforms(
  lock: GameLibsLock,
  channel: SnapshotChannel,
  buildid: string | number
): ManagedPlatform[] {
  return MANAGED_PLATFORMS.filter(
    (platform) =>
      lock.entries[lockKey(platform, channel)]?.buildid !== String(buildid)
  );
}

// What a capture changed against the lock before it: code when any Managed
// directory differs, content when only a game version does, none when the build
// changed neither, which the lock has no reason to record.
export function steamChangeKind(
  previous: GameLibsLock,
  current: GameLibsLock,
  channel: SnapshotChannel
): SteamChangeKind {
  const entries = MANAGED_PLATFORMS.map((platform) => {
    const key = lockKey(platform, channel);
    return [previous.entries[key], current.entries[key]];
  });
  if (entries.some(([before, after]) => before?.sha256 !== after?.sha256))
    return 'code';
  return entries.some(
    ([before, after]) => before?.gameVersion !== after?.gameVersion
  )
    ? 'content'
    : 'none';
}

export interface SteamStaleOptions {
  workspaceRoot?: string;
  modRoot?: string;
  channel: SnapshotChannel;
  runTool?: SteamToolRunner;
}

export function steamStale({
  workspaceRoot = WORKSPACE_ROOT,
  modRoot = path.join(workspaceRoot, 'bazaarplusplus-mod'),
  channel,
  runTool = steamToolRunner(modRoot)
}: SteamStaleOptions): SteamStaleResult {
  lockKey(MANAGED_PLATFORMS[0], channel);
  const { buildid, updatedAt } = runTool([
    'state',
    '--app',
    String(STEAM_APP_ID),
    '--branch',
    STEAM_BRANCHES[channel],
    '--depots',
    MANAGED_PLATFORMS.map((platform) => STEAM_DEPOTS[platform]).join(',')
  ]);
  return {
    buildid,
    updatedAt,
    platforms: staleSteamPlatforms(
      readGameLibsLock(workspaceRoot),
      channel,
      buildid
    )
  };
}

export interface SteamCaptureOptions {
  workspaceRoot?: string;
  modRoot?: string;
  platform: SnapshotPlatform;
  channel: SnapshotChannel;
  runTool?: SteamToolRunner;
  now?: Date;
}

export function steamCapture({
  workspaceRoot = WORKSPACE_ROOT,
  modRoot = path.join(workspaceRoot, 'bazaarplusplus-mod'),
  platform,
  channel,
  runTool = steamToolRunner(modRoot),
  now = new Date()
}: SteamCaptureOptions): CapturedSnapshot {
  const key = lockKey(platform, channel);
  const depot = STEAM_DEPOTS[platform];
  if (!depot) throw new Error(`Lock entry ${key} has no Steam depot`);
  const steamBranch = STEAM_BRANCHES[channel];
  const download = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-steam-'));
  try {
    const { buildid } = runTool([
      'download',
      '--app',
      String(STEAM_APP_ID),
      '--branch',
      steamBranch,
      '--depot',
      String(depot),
      '--out',
      download
    ]);
    const managedPath = path.join(download, 'Data', 'Managed');
    const gameVersion = readGameVersion(managedPath);
    const parsed = parseGameVersion(gameVersion);
    if (parsed.platform !== platform || parsed.channel !== channel)
      throw new Error(
        `Depot ${depot} on branch ${steamBranch} holds ${gameVersion}, which is not a ${key} build`
      );
    return captureSnapshot({
      workspaceRoot,
      modRoot,
      managedPath,
      buildid,
      steamBranch,
      now
    });
  } finally {
    fs.rmSync(download, { recursive: true, force: true });
  }
}

// The game version a Managed directory carries: its snapshot manifest, else the
// globalgamemanagers beside it, else null (a bare copied directory).
export function managedGameVersion(managedPath: string): string | null {
  const manifest = readSnapshotManifest<ManagedSnapshotManifest>(
    path.dirname(managedPath)
  );
  if (manifest) return manifest.gameVersion;
  return fs.existsSync(gameVersionFile(managedPath))
    ? readGameVersion(managedPath)
    : null;
}

// The lock entry a build used, for the sealed build record: the entry whose
// sha256 is the digest of the Managed directory the build compiled against.
// The same bytes may satisfy several entries (two platforms' Managed can be
// identical); the build platform's entry wins, then online, staging, ptr. A
// Managed directory no entry records is not a release input (ADR 0004).
export interface LockEntryForManagedOptions {
  workspaceRoot?: string;
  lock?: GameLibsLock;
  /** The build platform, whose entry wins a tie. */
  platform: string;
  managedPath: string;
}

/** The lock entry a build compiled against, as the sealed build record keeps it. */
export interface ManagedLockProvenance {
  key: string;
  platform: ManagedPlatform;
  channel: SnapshotChannel;
  gameVersion: string;
  sha256: string;
  buildid: string;
}

export function lockEntryForManaged({
  workspaceRoot = WORKSPACE_ROOT,
  lock = readGameLibsLock(workspaceRoot),
  platform,
  managedPath
}: LockEntryForManagedOptions): ManagedLockProvenance {
  lockKey(platform, 'online');
  const digest = managedDirectoryDigest(managedPath);
  const matching = managedEntries(lock).filter(
    ({ entry }) => entry.sha256 === digest
  );
  if (matching.length === 0) {
    const gameVersion = managedGameVersion(managedPath);
    const locked = managedEntries(lock)
      .map(({ key, entry }) => `${key} ${entry.gameVersion}`)
      .join(', ');
    throw new Error(
      `${managedPath} hashes to ${digest}, which no Snapshot Lock entry records${gameVersion ? ` (it is ${gameVersion})` : ''}; locked entries: ${locked || 'none'}. Build against a locked snapshot ('just mod::fetch ${platform} online'), or capture this game with 'just mod::snapshot', publish it and commit ${GAME_LIBS_LOCK_PATH}.`
    );
  }
  const rank = ({
    platform: entryPlatform,
    channel
  }: ManagedLockedEntry): number =>
    (entryPlatform === platform ? 0 : SNAPSHOT_CHANNELS.length) +
    SNAPSHOT_CHANNELS.indexOf(channel);
  matching.sort((a, b) => rank(a) - rank(b));
  const { key, platform: entryPlatform, channel, entry } = matching[0];
  return {
    key,
    platform: entryPlatform,
    channel,
    gameVersion: entry.gameVersion,
    sha256: entry.sha256,
    buildid: entry.buildid
  };
}

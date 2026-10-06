import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { parseArgs } from 'node:util';
import { WORKSPACE_ROOT } from './product.mjs';
import { putReplaceable, r2StoreFromEnvironment } from './r2-store.mjs';

// Game Assembly Snapshots and the Snapshot Lock (root CONTEXT.md, ADR 0004).
// This module owns the lock schema, the Managed-directory digest that the lock,
// the snapshot manifest and the sealed build record all share, and the private
// store layout. bazaarplusplus-mod/scripts/game.sh drives it through the CLI at
// the bottom; build/ManagedPath.props reads the lock file directly.

export const GAME_LIBS_LOCK_PATH =
  'bazaarplusplus-mod/build/game-libs.lock.json';
export const GAME_LIBS_BUCKET = 'bazaarplusplus-game-libs';
export const LOCK_SCHEMA_VERSION = 1;
export const SNAPSHOT_MANIFEST_SCHEMA_VERSION = 1;
export const SNAPSHOT_PLATFORMS = Object.freeze(['macos', 'windows']);
export const SNAPSHOT_CHANNELS = Object.freeze(['online', 'staging', 'ptr']);
// Steam branch names are the source record of a capture, never its identity.
export const STEAM_BRANCHES = Object.freeze({
  online: 'public',
  staging: 'staging',
  ptr: 'public_test_realm'
});
const ENTRY_FIELDS = Object.freeze([
  'gameVersion',
  'sha256',
  'buildid',
  'steamBranch',
  'capturedAt'
]);
// Unity's Application.version as the game ships it: build number, optional
// channel token, platform, architecture, game commit. It lives in
// globalgamemanagers beside Managed, not in any Managed DLL.
const GAME_VERSION_PATTERN =
  /\d+\.\d+\.\d+(?:-[a-z0-9]+)*-(?:macos|windows)-[a-z0-9]+-[0-9a-f]+/g;

const hash = (bytes) => crypto.createHash('sha256').update(bytes).digest('hex');
const jsonBytes = (value) => Buffer.from(`${JSON.stringify(value, null, 2)}\n`);

export function lockKey(platform, channel) {
  if (!SNAPSHOT_PLATFORMS.includes(platform))
    throw new Error(
      `Unknown snapshot platform '${platform}'; expected ${SNAPSHOT_PLATFORMS.join(' or ')}`
    );
  if (!SNAPSHOT_CHANNELS.includes(channel))
    throw new Error(
      `Unknown snapshot channel '${channel}'; expected ${SNAPSHOT_CHANNELS.join(', ')}`
    );
  return `${platform}-${channel}`;
}

export function lockKeys() {
  return SNAPSHOT_PLATFORMS.flatMap((platform) =>
    SNAPSHOT_CHANNELS.map((channel) => lockKey(platform, channel))
  );
}

export function parseGameVersion(gameVersion) {
  const pattern = new RegExp(`^${GAME_VERSION_PATTERN.source}$`);
  if (typeof gameVersion !== 'string' || !pattern.test(gameVersion))
    throw new Error(
      `Invalid game version ${JSON.stringify(gameVersion)}; expected the Application.version string such as 1.0.12575-staging-macos-arm64-fecb8f8e`
    );
  const platform = gameVersion.includes('-macos-') ? 'macos' : 'windows';
  // Mirrors GameBuildInfoResolver: a "-staging" or "-ptr" token names the
  // channel; the production build carries neither.
  const channel = gameVersion.includes('-staging-')
    ? 'staging'
    : gameVersion.includes('-ptr-')
      ? 'ptr'
      : 'online';
  return { platform, channel };
}

export function gameVersionFile(managedPath) {
  return path.join(path.dirname(managedPath), 'globalgamemanagers');
}

// Reads the game version from the Data directory above a Managed directory.
export function readGameVersion(managedPath) {
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
// managed/<name>. payload.mjs folds these into the sealed build record; the
// lock entry and the snapshot manifest hash them alone. The order is ordinal so
// the digest is identical on every machine and locale.
export function managedDirectoryRecords(managedPath) {
  const records = [];
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

export function managedDirectoryDigest(managedPath) {
  return hash(JSON.stringify(managedDirectoryRecords(managedPath)));
}

function assertEntry(key, entry) {
  const [platform, channel] = key.split('-');
  if (entry === null) return;
  if (typeof entry !== 'object' || Array.isArray(entry))
    throw new Error(`Lock entry ${key} must be null or an object`);
  const keys = Object.keys(entry);
  if (
    keys.length !== ENTRY_FIELDS.length ||
    ENTRY_FIELDS.some((field) => !keys.includes(field))
  )
    throw new Error(
      `Lock entry ${key} must have exactly the fields ${ENTRY_FIELDS.join(', ')}`
    );
  const parsed = parseGameVersion(entry.gameVersion);
  if (parsed.platform !== platform || parsed.channel !== channel)
    throw new Error(
      `Lock entry ${key} names game version ${entry.gameVersion}, which is a ${parsed.platform}-${parsed.channel} build`
    );
  if (!/^[0-9a-f]{64}$/.test(entry.sha256))
    throw new Error(`Lock entry ${key} has an invalid sha256`);
  if (typeof entry.buildid !== 'string' || !/^[1-9]\d*$/.test(entry.buildid))
    throw new Error(
      `Lock entry ${key} buildid must be a Steam build id string`
    );
  if (entry.steamBranch !== STEAM_BRANCHES[channel])
    throw new Error(
      `Lock entry ${key} steamBranch must be ${STEAM_BRANCHES[channel]}, got ${JSON.stringify(entry.steamBranch)}`
    );
  if (
    typeof entry.capturedAt !== 'string' ||
    Number.isNaN(Date.parse(entry.capturedAt)) ||
    !/Z$/.test(entry.capturedAt)
  )
    throw new Error(
      `Lock entry ${key} capturedAt must be an ISO 8601 UTC instant`
    );
}

export function validateGameLibsLock(lock) {
  if (!lock || typeof lock !== 'object' || Array.isArray(lock))
    throw new Error('Snapshot lock must be a JSON object');
  if (lock.schemaVersion !== LOCK_SCHEMA_VERSION)
    throw new Error(
      `Snapshot lock schemaVersion must be ${LOCK_SCHEMA_VERSION}`
    );
  if (!Number.isInteger(lock.staleAfterDays) || lock.staleAfterDays <= 0)
    throw new Error('Snapshot lock staleAfterDays must be a positive integer');
  const expected = lockKeys();
  const entries = lock.entries;
  if (!entries || typeof entries !== 'object' || Array.isArray(entries))
    throw new Error('Snapshot lock entries must be an object');
  const keys = Object.keys(entries);
  if (
    keys.length !== expected.length ||
    expected.some((key) => !keys.includes(key))
  )
    throw new Error(
      `Snapshot lock must hold exactly the six keys ${expected.join(', ')}`
    );
  if (
    Object.keys(lock).some(
      (key) => !['schemaVersion', 'staleAfterDays', 'entries'].includes(key)
    )
  )
    throw new Error('Snapshot lock has unknown top-level fields');
  for (const key of expected) assertEntry(key, entries[key]);
  return lock;
}

export function gameLibsLockPath(workspaceRoot = WORKSPACE_ROOT) {
  return path.join(workspaceRoot, GAME_LIBS_LOCK_PATH);
}

export function readGameLibsLock(workspaceRoot = WORKSPACE_ROOT) {
  const file = gameLibsLockPath(workspaceRoot);
  let parsed;
  try {
    parsed = JSON.parse(fs.readFileSync(file, 'utf8'));
  } catch (error) {
    throw new Error(`Cannot read ${GAME_LIBS_LOCK_PATH}: ${error.message}`);
  }
  return validateGameLibsLock(parsed);
}

export function writeGameLibsLockEntry(workspaceRoot, key, entry) {
  const lock = readGameLibsLock(workspaceRoot);
  const next = {
    ...lock,
    entries: Object.fromEntries(
      lockKeys().map((name) => [
        name,
        name === key ? entry : lock.entries[name]
      ])
    )
  };
  validateGameLibsLock(next);
  fs.writeFileSync(gameLibsLockPath(workspaceRoot), jsonBytes(next));
  return next;
}

// Warnings never fail a gate: an empty key is work not yet done, a stale key is
// a reminder that the game moved on. A malformed lock fails in readGameLibsLock.
export function lockWarnings(lock, { now = new Date() } = {}) {
  const warnings = [];
  for (const key of lockKeys()) {
    const entry = lock.entries[key];
    const [platform, channel] = key.split('-');
    if (entry === null) {
      warnings.push(
        `${key}: no snapshot captured; run 'just mod::snapshot' on a ${platform} machine with The Bazaar's ${STEAM_BRANCHES[channel]} branch mounted, then 'just mod::publish ${platform} ${channel}' and commit the lock`
      );
      continue;
    }
    const ageDays = Math.floor(
      (now.getTime() - Date.parse(entry.capturedAt)) / 86_400_000
    );
    if (ageDays > lock.staleAfterDays)
      warnings.push(
        `${key}: ${entry.gameVersion} was captured ${ageDays} days ago (staleAfterDays ${lock.staleAfterDays}); recapture it if the game updated`
      );
  }
  return warnings;
}

export function lockedEntries(lock) {
  return lockKeys()
    .filter((key) => lock.entries[key] !== null)
    .map((key) => {
      const [platform, channel] = key.split('-');
      return { key, platform, channel, entry: lock.entries[key] };
    });
}

export function snapshotDirectory(modRoot, platform, channel, gameVersion) {
  return path.join(
    modRoot,
    'game-libs',
    `${lockKey(platform, channel)}-${gameVersion}`
  );
}

export function snapshotBlobKey(sha256) {
  if (!/^[0-9a-f]{64}$/.test(sha256))
    throw new Error('Invalid snapshot sha256');
  return `game-libs/${sha256}.tar.gz`;
}

export function snapshotManifestKey(platform, channel, gameVersion) {
  parseGameVersion(gameVersion);
  return `game-libs/${lockKey(platform, channel)}-${gameVersion}.json`;
}

// The private store through a Cloudflare API token that can read (publish:
// write) the bucket: the operator token locally and in release.yml, the
// read-only BPP_GAME_LIBS_TOKEN in checks.yml.
export function gameLibsStoreFromEnvironment(env = process.env) {
  return r2StoreFromEnvironment(env, { bucket: GAME_LIBS_BUCKET });
}

function readSnapshotManifest(directory) {
  const file = path.join(directory, 'manifest.json');
  if (!fs.existsSync(file)) return null;
  return JSON.parse(fs.readFileSync(file, 'utf8'));
}

function entryFromManifest(manifest) {
  return Object.fromEntries(
    ENTRY_FIELDS.map((field) => [field, manifest[field]])
  );
}

// Archives the Managed directory of a mounted game install under game-libs/
// and records it in the lock. The capture's identity is the game version read
// beside Managed; the Steam facts are its source record.
export function captureSnapshot({
  workspaceRoot = WORKSPACE_ROOT,
  modRoot = path.join(workspaceRoot, 'bazaarplusplus-mod'),
  managedPath,
  buildid,
  steamBranch,
  now = new Date()
}) {
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
  const manifest = {
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

function tar(args, cwd) {
  try {
    execFileSync('tar', args, { cwd, stdio: ['ignore', 'ignore', 'pipe'] });
  } catch (error) {
    throw new Error(
      `tar ${args[0]} failed: ${String(error.stderr ?? error.message).trim()}`
    );
  }
}

function tarballOf(directory) {
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

function extractTarball(bytes, into) {
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-game-libs-'));
  try {
    const file = path.join(temp, 'Managed.tar.gz');
    fs.writeFileSync(file, bytes);
    fs.mkdirSync(into, { recursive: true });
    tar(
      [
        '-xzf',
        path.basename(file),
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
function verifiedSnapshot(modRoot, platform, channel, entry) {
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
export async function publishSnapshot({
  workspaceRoot = WORKSPACE_ROOT,
  modRoot = path.join(workspaceRoot, 'bazaarplusplus-mod'),
  platform,
  channel,
  store,
  tar = tarballOf,
  log = () => {}
}) {
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
  const manifest = readSnapshotManifest(snapshot.directory);
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
export async function fetchSnapshot({
  workspaceRoot = WORKSPACE_ROOT,
  modRoot = path.join(workspaceRoot, 'bazaarplusplus-mod'),
  platform,
  channel,
  steamManagedPath = '',
  allowUnlocked = false,
  storeFactory = gameLibsStoreFromEnvironment,
  log = () => {}
}) {
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
      `${mismatch} Switch The Bazaar to the ${STEAM_BRANCHES[channel]} branch in Steam, wait for a lock update, or fetch the snapshot from the private store with the Cloudflare operator token (just with-config release just mod::fetch ${platform} ${channel}): ${error.message}`
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
    const manifest = remote
      ? JSON.parse(remote.bytes.toString('utf8'))
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

// The game version a Managed directory carries: its snapshot manifest, else the
// globalgamemanagers beside it, else null (a bare copied directory).
function managedGameVersion(managedPath) {
  const manifest = readSnapshotManifest(path.dirname(managedPath));
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
export function lockEntryForManaged({
  workspaceRoot = WORKSPACE_ROOT,
  lock = readGameLibsLock(workspaceRoot),
  platform,
  managedPath
}) {
  lockKey(platform, 'online');
  const digest = managedDirectoryDigest(managedPath);
  const matching = lockedEntries(lock).filter(
    ({ entry }) => entry.sha256 === digest
  );
  if (matching.length === 0) {
    const gameVersion = managedGameVersion(managedPath);
    const locked = lockedEntries(lock)
      .map(({ key, entry }) => `${key} ${entry.gameVersion}`)
      .join(', ');
    throw new Error(
      `${managedPath} hashes to ${digest}, which no Snapshot Lock entry records${gameVersion ? ` (it is ${gameVersion})` : ''}; locked entries: ${locked || 'none'}. Build against a locked snapshot ('just mod::fetch ${platform} online'), or capture this game with 'just mod::snapshot', publish it and commit ${GAME_LIBS_LOCK_PATH}.`
    );
  }
  const rank = ({ platform: entryPlatform, channel }) =>
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

// The `just mod::check` gate: a malformed lock fails, empty or stale entries
// warn, and a resolved Managed directory whose game version a lock entry names
// must hash to that entry.
export function checkGameLibsLock({
  workspaceRoot = WORKSPACE_ROOT,
  managedPath = '',
  now = new Date()
}) {
  const lock = readGameLibsLock(workspaceRoot);
  const warnings = lockWarnings(lock, { now });
  let verified = null;
  if (managedPath) {
    const gameVersion = managedGameVersion(managedPath);
    const matching = lockedEntries(lock).filter(
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
  return { lock, warnings, verified };
}

function printLine(value) {
  process.stdout.write(`${value}\n`);
}

const log = (message) => process.stderr.write(`${message}\n`);

const usage = `Game Assembly Snapshot commands (driven by bazaarplusplus-mod/scripts/game.sh):
  node release/game-libs.mjs game-version <managed-dir>
  node release/game-libs.mjs digest <managed-dir>
  node release/game-libs.mjs snapshot --managed <dir> --buildid <id> --steam-branch <branch>
  node release/game-libs.mjs publish --platform <macos|windows> --channel <online|staging|ptr>
  node release/game-libs.mjs fetch --platform <p> --channel <c> [--steam-managed <dir>] [--allow-unlocked]
  node release/game-libs.mjs lock-check [--managed <dir>]
  node release/game-libs.mjs entries
publish always, and fetch when neither a local snapshot nor the mounted game
satisfies the entry, use the private bucket ${GAME_LIBS_BUCKET} through
CLOUDFLARE_API_TOKEN and CLOUDFLARE_ACCOUNT_ID (the S3 pair is derived from
the token).`;

export async function cliMain(argv, { workspaceRoot = WORKSPACE_ROOT } = {}) {
  const [verb, ...rest] = argv;
  try {
    if (!verb || verb === '--help') {
      printLine(usage);
      return 0;
    }
    const { values, positionals } = parseArgs({
      args: rest,
      strict: true,
      allowPositionals: true,
      options: {
        managed: { type: 'string' },
        buildid: { type: 'string' },
        'steam-branch': { type: 'string' },
        platform: { type: 'string' },
        channel: { type: 'string' },
        'steam-managed': { type: 'string' },
        'allow-unlocked': { type: 'boolean' }
      }
    });
    const modRoot = path.join(workspaceRoot, 'bazaarplusplus-mod');
    const required = (name) => {
      if (!values[name])
        throw new Error(`${verb} requires --${name}\n${usage}`);
      return values[name];
    };
    switch (verb) {
      case 'game-version':
      case 'digest': {
        if (positionals.length !== 1)
          throw new Error(`${verb} takes one Managed directory\n${usage}`);
        printLine(
          verb === 'digest'
            ? managedDirectoryDigest(positionals[0])
            : readGameVersion(positionals[0])
        );
        return 0;
      }
      case 'snapshot': {
        const result = captureSnapshot({
          workspaceRoot,
          modRoot,
          managedPath: required('managed'),
          buildid: required('buildid'),
          steamBranch: required('steam-branch')
        });
        log(
          `Captured ${result.key} ${result.manifest.gameVersion} (buildid ${result.manifest.buildid}) into ${result.directory} and recorded it in ${GAME_LIBS_LOCK_PATH}`
        );
        printLine(result.directory);
        return 0;
      }
      case 'publish': {
        await publishSnapshot({
          workspaceRoot,
          modRoot,
          platform: required('platform'),
          channel: required('channel'),
          store: gameLibsStoreFromEnvironment(),
          log
        });
        return 0;
      }
      case 'fetch': {
        const result = await fetchSnapshot({
          workspaceRoot,
          modRoot,
          platform: required('platform'),
          channel: required('channel'),
          steamManagedPath: values['steam-managed'] ?? '',
          allowUnlocked: values['allow-unlocked'] === true,
          log
        });
        printLine(result.managedPath);
        return 0;
      }
      case 'lock-check': {
        const result = checkGameLibsLock({
          workspaceRoot,
          managedPath: values.managed ?? ''
        });
        for (const warning of result.warnings) log(`WARNING: ${warning}`);
        if (result.verified)
          log(
            `Managed ${result.verified.gameVersion} matches lock ${result.verified.keys.join(', ')}`
          );
        return 0;
      }
      case 'entries': {
        for (const { platform, channel } of lockedEntries(
          readGameLibsLock(workspaceRoot)
        ))
          printLine(`${platform} ${channel}`);
        return 0;
      }
      default:
        throw new Error(`Unknown game-libs verb: ${verb}\n${usage}`);
    }
  } catch (error) {
    log(error instanceof Error ? error.message : String(error));
    return 1;
  }
}

if (import.meta.main) {
  process.exitCode = await cliMain(process.argv.slice(2));
}

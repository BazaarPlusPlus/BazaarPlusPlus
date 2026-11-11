import fs from 'node:fs';
import path from 'node:path';
import { WORKSPACE_ROOT } from './product.ts';
import { isSha256, jsonBytes } from './snapshot-files.ts';

// The Snapshot Lock (root GLOSSARY.md, ADR 0004 and ADR 0008):
// bazaarplusplus-mod/build/game-libs.lock.json. This module owns the lock
// schema, its platform-channel keys, the game version that names an entry,
// reading, validating and writing the lock, and where a locked snapshot lives
// under game-libs/ locally and in the private store.
// game-assembly-snapshot.ts and interop-snapshot.ts capture, publish and fetch
// the snapshots; build/ManagedPath.props and build/InteropPath.props read the
// lock file directly.

export const GAME_LIBS_LOCK_PATH =
  'bazaarplusplus-mod/build/game-libs.lock.json';
export const LOCK_SCHEMA_VERSION = 1;
/** A platform the Snapshot Lock keys an entry by. */
export type SnapshotPlatform = 'macos' | 'windows' | 'android';
/** A channel the Snapshot Lock keys an entry by. */
export type SnapshotChannel = 'online' | 'staging' | 'ptr';
/** A platform whose snapshot is a Managed directory captured from Steam. */
export type ManagedPlatform = 'macos' | 'windows';
/** The Steam branch a desktop channel is captured from. */
export type SteamBranch = 'public' | 'staging' | 'public_test_realm';
/** The lock key of a Managed entry: `<platform>-<channel>`. */
export type ManagedLockKey = `${ManagedPlatform}-${SnapshotChannel}`;

export const SNAPSHOT_PLATFORMS: readonly SnapshotPlatform[] = Object.freeze([
  'macos',
  'windows',
  'android'
]);
export const SNAPSHOT_CHANNELS: readonly SnapshotChannel[] = Object.freeze([
  'online',
  'staging',
  'ptr'
]);
// Desktop platforms capture a Steam install's Managed directory on three
// branches. Android has only the production APK, so its one key is online and
// its snapshot is the Interop assemblies generated from that APK.
export const PLATFORM_CHANNELS: Readonly<
  Record<SnapshotPlatform, readonly SnapshotChannel[]>
> = Object.freeze({
  macos: SNAPSHOT_CHANNELS,
  windows: SNAPSHOT_CHANNELS,
  android: Object.freeze<SnapshotChannel[]>(['online'])
});
export const MANAGED_PLATFORMS: readonly ManagedPlatform[] = Object.freeze([
  'macos',
  'windows'
]);
export const INTEROP_KEY = 'android-online';
// Steam branch names are the source record of a capture, never its identity.
export const STEAM_BRANCHES: Readonly<Record<SnapshotChannel, SteamBranch>> =
  Object.freeze({
    online: 'public',
    staging: 'staging',
    ptr: 'public_test_realm'
  });

/** A desktop lock entry: a Managed directory captured from a Steam branch. */
// A type alias, not an interface: entryFromManifest casts a record to it.
export type ManagedLockEntry = {
  gameVersion: string;
  sha256: string;
  buildid: string;
  steamBranch: SteamBranch;
  capturedAt: string;
};

/** The Loader Release an Interop Snapshot entry pins. */
export interface LoaderPin {
  tag: string;
  sha256: string;
  managedRuntimeVersion: string;
}

/** The LemonLoader Patcher release an Interop Snapshot entry pins. */
export interface PatcherPin {
  tag: string;
  sha256: string;
}

/** A native library a host ships beside the loader, pinned by NuGet package. */
export interface NativeLibPin {
  package: string;
  version: string;
  sha256: string;
}

/** The android-online lock entry: an Interop Snapshot and its toolchain. */
export interface InteropLockEntry {
  gameVersion: string;
  versionCode: number;
  apkSha256: string;
  apkEtag: string | null;
  il2cppSha256: string;
  metadataSha256: string;
  unityVersion: string;
  interopSha256: string;
  loader: LoaderPin;
  patcher: PatcherPin;
  cpp2ilVersion: string;
  cpp2ilSha256: string;
  il2cppInteropVersion: string;
  nativeLibs: Record<string, NativeLibPin>;
  capturedAt: string;
}

export type LockEntry = ManagedLockEntry | InteropLockEntry;

/** The lock's entries: every lock key, null until it is captured. */
export interface GameLibsLockEntries {
  [key: string]: LockEntry | null;
  'macos-online': ManagedLockEntry | null;
  'macos-staging': ManagedLockEntry | null;
  'macos-ptr': ManagedLockEntry | null;
  'windows-online': ManagedLockEntry | null;
  'windows-staging': ManagedLockEntry | null;
  'windows-ptr': ManagedLockEntry | null;
  'android-online': InteropLockEntry | null;
}

/** bazaarplusplus-mod/build/game-libs.lock.json after validation. */
// A type alias, not an interface: validateGameLibsLock casts a record to it.
export type GameLibsLock = {
  schemaVersion: typeof LOCK_SCHEMA_VERSION;
  staleAfterDays: number;
  entries: GameLibsLockEntries;
};

/** The platform and channel a game version names. */
export interface ParsedGameVersion {
  platform: SnapshotPlatform;
  channel: SnapshotChannel;
}

/** A captured lock entry with the platform and channel of its key. */
export interface LockedEntry {
  key: string;
  platform: SnapshotPlatform;
  channel: SnapshotChannel;
  entry: LockEntry;
}

/** A captured lock entry whose snapshot is a Managed directory. */
export interface ManagedLockedEntry extends LockedEntry {
  platform: ManagedPlatform;
  entry: ManagedLockEntry;
}

export const ENTRY_FIELDS: readonly (keyof ManagedLockEntry)[] = Object.freeze([
  'gameVersion',
  'sha256',
  'buildid',
  'steamBranch',
  'capturedAt'
]);
// An Interop Snapshot entry: the APK's identity, the Interop assemblies'
// digest, and the pinned toolchain a Linux capture regenerates them with. An
// entry captured with --interop records the digest of a directory generated
// elsewhere (ADR 0008), so the toolchain pins are the regeneration inputs, not
// proof of where interopSha256 came from. The official APK, the Loader
// Release, the Patcher and the Cpp2IL Linux binary are stored by sha256 beside
// the interop collection, so a capture can be regenerated from the lock alone.
// apkEtag is the strong ETag the publisher served the APK with, or null for an
// APK captured from a file of unknown origin; android-apk-watch.yml compares it.
const INTEROP_ENTRY_FIELDS: readonly (keyof InteropLockEntry)[] = Object.freeze(
  [
    'gameVersion',
    'versionCode',
    'apkSha256',
    'apkEtag',
    'il2cppSha256',
    'metadataSha256',
    'unityVersion',
    'interopSha256',
    'loader',
    'patcher',
    'cpp2ilVersion',
    'cpp2ilSha256',
    'il2cppInteropVersion',
    'nativeLibs',
    'capturedAt'
  ]
);
// Unity's Application.version as the game ships it: build number, optional
// channel token, platform, then exactly one architecture token on desktop and
// none on Android, then the game commit. Desktop reads it from
// globalgamemanagers beside Managed; Android carries it as the APK
// versionName. build/ManagedPath.props mirrors this pattern as
// BppGameVersionPattern.
export const GAME_VERSION_PATTERN =
  /\d+\.\d+\.\d+(?:-[a-z0-9]+)*-(?:(?:macos|windows)-[a-z0-9]+|android)-[0-9a-f]+/g;

// A desktop platform and a channel always make a Managed lock key.
export function lockKey(
  platform: ManagedPlatform,
  channel: SnapshotChannel
): ManagedLockKey;
export function lockKey(platform: string, channel: string): string;
export function lockKey(platform: string, channel: string): string {
  const known = SNAPSHOT_PLATFORMS.find((name) => name === platform);
  if (known === undefined)
    throw new Error(
      `Unknown snapshot platform '${platform}'; expected ${SNAPSHOT_PLATFORMS.join(', ')}`
    );
  if (!(PLATFORM_CHANNELS[known] as readonly string[]).includes(channel))
    throw new Error(
      `Unknown ${platform} snapshot channel '${channel}'; expected ${PLATFORM_CHANNELS[known].join(', ')}`
    );
  return `${platform}-${channel}`;
}

export function lockKeys(): string[] {
  return SNAPSHOT_PLATFORMS.flatMap((platform) =>
    PLATFORM_CHANNELS[platform].map((channel) => lockKey(platform, channel))
  );
}

export const isInteropPlatform = (platform: string): boolean =>
  !(MANAGED_PLATFORMS as readonly string[]).includes(platform);

export function parseGameVersion(gameVersion: unknown): ParsedGameVersion {
  const pattern = new RegExp(`^${GAME_VERSION_PATTERN.source}$`);
  if (typeof gameVersion !== 'string' || !pattern.test(gameVersion))
    throw new Error(
      `Invalid game version ${JSON.stringify(gameVersion)}; expected the Application.version string such as 1.0.12575-staging-macos-arm64-fecb8f8e or 1.0.12825-production-android-d9601ded`
    );
  const platform = SNAPSHOT_PLATFORMS.find((name) =>
    gameVersion.includes(`-${name}-`)
  );
  // GAME_VERSION_PATTERN always carries a -<platform>- token.
  if (platform === undefined)
    throw new Error(`Game version ${gameVersion} names no snapshot platform`);
  // Mirrors GameBuildInfoResolver: a "-staging" or "-ptr" token names the
  // channel; the production build carries neither ("prod" on desktop,
  // "production" on Android).
  const channel = gameVersion.includes('-staging-')
    ? 'staging'
    : gameVersion.includes('-ptr-')
      ? 'ptr'
      : 'online';
  return { platform, channel };
}

const isObject = (value: unknown): value is Record<string, unknown> =>
  value !== null && typeof value === 'object' && !Array.isArray(value);

function assertFields(
  label: string,
  value: unknown,
  fields: readonly string[]
): asserts value is Record<string, unknown> {
  if (!isObject(value)) throw new Error(`${label} must be an object`);
  const keys = Object.keys(value);
  if (
    keys.length !== fields.length ||
    fields.some((field) => !keys.includes(field))
  )
    throw new Error(
      `${label} must have exactly the fields ${fields.join(', ')}`
    );
}

export const isApkEtag = (value: unknown): value is string =>
  typeof value === 'string' && /^[\x21\x23-\x7e]{1,256}$/.test(value);

function assertSha256(key: string, label: string, value: unknown): void {
  if (!isSha256(value))
    throw new Error(`Lock entry ${key} has an invalid ${label}`);
}

function assertToken(key: string, label: string, value: unknown): void {
  if (typeof value !== 'string' || !/^[A-Za-z0-9][A-Za-z0-9._+-]*$/.test(value))
    throw new Error(
      `Lock entry ${key} ${label} must be a version or tag token, got ${JSON.stringify(value)}`
    );
}

function assertCapturedAt(key: string, capturedAt: unknown): void {
  if (
    typeof capturedAt !== 'string' ||
    Number.isNaN(Date.parse(capturedAt)) ||
    !/Z$/.test(capturedAt)
  )
    throw new Error(
      `Lock entry ${key} capturedAt must be an ISO 8601 UTC instant`
    );
}

function assertInteropEntry(key: string, entry: Record<string, unknown>): void {
  if (
    typeof entry.versionCode !== 'number' ||
    !Number.isInteger(entry.versionCode) ||
    entry.versionCode <= 0
  )
    throw new Error(
      `Lock entry ${key} versionCode must be the APK's positive integer versionCode`
    );
  for (const field of [
    'apkSha256',
    'il2cppSha256',
    'metadataSha256',
    'interopSha256',
    'cpp2ilSha256'
  ])
    assertSha256(key, field, entry[field]);
  if (entry.apkEtag !== null && !isApkEtag(entry.apkEtag))
    throw new Error(
      `Lock entry ${key} apkEtag must be null or a strong ETag's value without quotes`
    );
  if (!/^\d+\.\d+\.\d+[abfp]\d+$/.test(String(entry.unityVersion ?? '')))
    throw new Error(
      `Lock entry ${key} unityVersion must be a Unity version such as 6000.3.11f1`
    );
  assertFields(`Lock entry ${key} loader`, entry.loader, [
    'tag',
    'sha256',
    'managedRuntimeVersion'
  ]);
  assertToken(key, 'loader.tag', entry.loader.tag);
  assertSha256(key, 'loader.sha256', entry.loader.sha256);
  assertToken(
    key,
    'loader.managedRuntimeVersion',
    entry.loader.managedRuntimeVersion
  );
  assertFields(`Lock entry ${key} patcher`, entry.patcher, ['tag', 'sha256']);
  assertToken(key, 'patcher.tag', entry.patcher.tag);
  assertSha256(key, 'patcher.sha256', entry.patcher.sha256);
  assertToken(key, 'cpp2ilVersion', entry.cpp2ilVersion);
  assertToken(key, 'il2cppInteropVersion', entry.il2cppInteropVersion);
  if (!isObject(entry.nativeLibs))
    throw new Error(`Lock entry ${key} nativeLibs must be an object`);
  for (const [name, lib] of Object.entries(entry.nativeLibs)) {
    if (!/^lib[A-Za-z0-9._+-]+\.so$/.test(name))
      throw new Error(
        `Lock entry ${key} nativeLibs key ${JSON.stringify(name)} must be a shared library file name`
      );
    assertFields(`Lock entry ${key} nativeLibs.${name}`, lib, [
      'package',
      'version',
      'sha256'
    ]);
    assertToken(key, `nativeLibs.${name}.package`, lib.package);
    assertToken(key, `nativeLibs.${name}.version`, lib.version);
    assertSha256(key, `nativeLibs.${name}.sha256`, lib.sha256);
  }
  assertCapturedAt(key, entry.capturedAt);
}

export function assertLockEntry(key: string, entry: unknown): void {
  // Every lock key is <platform>-<channel>, as lockKey builds it.
  const [platform, channel] = key.split('-') as [
    SnapshotPlatform,
    SnapshotChannel
  ];
  if (entry === null) return;
  if (!isObject(entry))
    throw new Error(`Lock entry ${key} must be null or an object`);
  assertFields(
    `Lock entry ${key}`,
    entry,
    isInteropPlatform(platform) ? INTEROP_ENTRY_FIELDS : ENTRY_FIELDS
  );
  // build/InteropPath.props reads gameVersion and loader.tag by position.
  if (
    isInteropPlatform(platform) &&
    Object.keys(entry).join() !== INTEROP_ENTRY_FIELDS.join()
  )
    throw new Error(
      `Lock entry ${key} must list its fields in the order ${INTEROP_ENTRY_FIELDS.join(', ')}`
    );
  const parsed = parseGameVersion(entry.gameVersion);
  if (parsed.platform !== platform || parsed.channel !== channel)
    throw new Error(
      `Lock entry ${key} names game version ${entry.gameVersion}, which is a ${parsed.platform}-${parsed.channel} build`
    );
  if (isInteropPlatform(platform)) {
    assertInteropEntry(key, entry);
    return;
  }
  if (!isSha256(entry.sha256))
    throw new Error(`Lock entry ${key} has an invalid sha256`);
  if (typeof entry.buildid !== 'string' || !/^[1-9]\d*$/.test(entry.buildid))
    throw new Error(
      `Lock entry ${key} buildid must be a Steam build id string`
    );
  if (entry.steamBranch !== STEAM_BRANCHES[channel])
    throw new Error(
      `Lock entry ${key} steamBranch must be ${STEAM_BRANCHES[channel]}, got ${JSON.stringify(entry.steamBranch)}`
    );
  assertCapturedAt(key, entry.capturedAt);
}

export function validateGameLibsLock(lock: unknown): GameLibsLock {
  if (!isObject(lock)) throw new Error('Snapshot lock must be a JSON object');
  if (lock.schemaVersion !== LOCK_SCHEMA_VERSION)
    throw new Error(
      `Snapshot lock schemaVersion must be ${LOCK_SCHEMA_VERSION}`
    );
  if (
    typeof lock.staleAfterDays !== 'number' ||
    !Number.isInteger(lock.staleAfterDays) ||
    lock.staleAfterDays <= 0
  )
    throw new Error('Snapshot lock staleAfterDays must be a positive integer');
  const expected = lockKeys();
  const entries = lock.entries;
  if (!isObject(entries))
    throw new Error('Snapshot lock entries must be an object');
  const keys = Object.keys(entries);
  if (
    keys.length !== expected.length ||
    expected.some((key) => !keys.includes(key))
  )
    throw new Error(
      `Snapshot lock must hold exactly the keys ${expected.join(', ')}`
    );
  if (
    Object.keys(lock).some(
      (key) => !['schemaVersion', 'staleAfterDays', 'entries'].includes(key)
    )
  )
    throw new Error('Snapshot lock has unknown top-level fields');
  for (const key of expected) assertLockEntry(key, entries[key]);
  // Every field was checked above.
  return lock as GameLibsLock;
}

export function gameLibsLockPath(
  workspaceRoot: string = WORKSPACE_ROOT
): string {
  return path.join(workspaceRoot, GAME_LIBS_LOCK_PATH);
}

export function readGameLibsLock(
  workspaceRoot: string = WORKSPACE_ROOT
): GameLibsLock {
  const file = gameLibsLockPath(workspaceRoot);
  let parsed: unknown;
  try {
    parsed = JSON.parse(fs.readFileSync(file, 'utf8'));
  } catch (error) {
    throw new Error(
      `Cannot read ${GAME_LIBS_LOCK_PATH}: ${(error as Error).message}`
    );
  }
  return validateGameLibsLock(parsed);
}

export function writeGameLibsLockEntry(
  workspaceRoot: string,
  key: string,
  entry: LockEntry | null
): GameLibsLock {
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
  const validated = validateGameLibsLock(next);
  fs.writeFileSync(gameLibsLockPath(workspaceRoot), jsonBytes(next));
  return validated;
}

// Warnings never fail a gate: an empty key is work not yet done, a stale key is
// a reminder that the game moved on. A malformed lock fails in readGameLibsLock.
export interface LockWarningsOptions {
  now?: Date;
}

export function lockWarnings(
  lock: GameLibsLock,
  { now = new Date() }: LockWarningsOptions = {}
): string[] {
  const warnings: string[] = [];
  for (const key of lockKeys()) {
    const entry = lock.entries[key];
    // Every lock key is <platform>-<channel>, as lockKey builds it.
    const [platform, channel] = key.split('-') as [
      SnapshotPlatform,
      SnapshotChannel
    ];
    if (entry === null) {
      warnings.push(
        isInteropPlatform(platform)
          ? `${key}: no Interop Snapshot captured; run 'just mod::snapshot-android <TheBazaar.apk>', then 'just mod::publish ${platform} ${channel}' and commit the lock`
          : `${key}: no snapshot captured; run 'just mod::snapshot' on a ${platform} machine with The Bazaar's ${STEAM_BRANCHES[channel]} branch mounted, then 'just mod::publish ${platform} ${channel}' and commit the lock`
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

export function lockedEntries(lock: GameLibsLock): LockedEntry[] {
  return lockKeys()
    .filter((key) => lock.entries[key] !== null)
    .map((key) => {
      // Every lock key is <platform>-<channel>, as lockKey builds it.
      const [platform, channel] = key.split('-') as [
        SnapshotPlatform,
        SnapshotChannel
      ];
      const entry = lock.entries[key];
      // The filter above dropped the empty entries.
      if (entry === null) throw new Error(`Lock entry ${key} is empty`);
      return { key, platform, channel, entry };
    });
}

// The captured entries whose snapshot is a Managed directory: what desktop
// builds, the sealed build record and the CompatCheck matrix compile against.
export function managedEntries(lock: GameLibsLock): ManagedLockedEntry[] {
  // A validated lock holds a Managed entry under every desktop key.
  return lockedEntries(lock).filter(
    (locked): locked is ManagedLockedEntry =>
      !isInteropPlatform(locked.platform)
  );
}

export function snapshotDirectory(
  modRoot: string,
  platform: string,
  channel: string,
  gameVersion: string
): string {
  return path.join(
    modRoot,
    'game-libs',
    `${lockKey(platform, channel)}-${gameVersion}`
  );
}

export function snapshotBlobKey(sha256: string): string {
  if (!/^[0-9a-f]{64}$/.test(sha256))
    throw new Error('Invalid snapshot sha256');
  return `game-libs/${sha256}.tar.gz`;
}

export function snapshotManifestKey(
  platform: string,
  channel: string,
  gameVersion: string
): string {
  parseGameVersion(gameVersion);
  return `game-libs/${lockKey(platform, channel)}-${gameVersion}.json`;
}

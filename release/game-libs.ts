import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import zlib from 'node:zlib';
import { execFileSync } from 'node:child_process';
import { parseArgs } from 'node:util';
import { readApk, sha256File } from './android-apk.ts';
import {
  ANDROID_GAME_STATUS_KEY,
  GAME_STATUS_KEY,
  GAME_STATUS_PREFIX
} from './downloads.ts';
import type { AndroidGameStatus } from './downloads.ts';
import { WORKSPACE_ROOT } from './product.ts';
import {
  RELEASE_BUCKET,
  putReplaceable,
  r2StoreFromEnvironment
} from './r2-store.ts';
import type { ObjectStore } from './r2-store.ts';

// Game Assembly Snapshots, Interop Snapshots and the Snapshot Lock (root
// GLOSSARY.md, ADR 0004 and ADR 0008). This module owns the lock schema, the
// Managed- and interop-directory digests that the lock, the snapshot manifest
// and the sealed build record share, and the private store layout.
// bazaarplusplus-mod/scripts/game.sh drives it through the CLI at the bottom;
// build/ManagedPath.props and build/InteropPath.props read the lock file
// directly.

export const GAME_LIBS_LOCK_PATH =
  'bazaarplusplus-mod/build/game-libs.lock.json';
export const GAME_LIBS_BUCKET = 'bazaarplusplus-game-libs';
export const LOCK_SCHEMA_VERSION = 1;
export const SNAPSHOT_MANIFEST_SCHEMA_VERSION = 1;
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
// Where a Steam capture reads each desktop platform (root ADR 0015). macOS is
// the arm64 depot that lock entries already use.
export const STEAM_APP_ID = 1617400;
export const STEAM_DEPOTS: Readonly<Partial<Record<SnapshotPlatform, number>>> =
  Object.freeze({
    macos: 1617401,
    windows: 1617402
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

/** A desktop snapshot's manifest.json: its lock entry plus its identity. */
export interface ManagedSnapshotManifest extends ManagedLockEntry {
  schemaVersion: typeof SNAPSHOT_MANIFEST_SCHEMA_VERSION;
  platform: SnapshotPlatform;
  channel: SnapshotChannel;
}

/** An Interop Snapshot's manifest.json: its lock entry plus its identity. */
export interface InteropSnapshotManifest extends InteropLockEntry {
  schemaVersion: typeof SNAPSHOT_MANIFEST_SCHEMA_VERSION;
  platform: SnapshotPlatform;
  channel: SnapshotChannel;
}

/** One file of a Managed or interop directory digest. */
export interface DirectoryRecord {
  path: string;
  sha256: string;
}

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

const ENTRY_FIELDS: readonly (keyof ManagedLockEntry)[] = Object.freeze([
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

const hash = (bytes: string | NodeJS.ArrayBufferView): string =>
  crypto.createHash('sha256').update(bytes).digest('hex');
const jsonBytes = (value: unknown): Buffer =>
  Buffer.from(`${JSON.stringify(value, null, 2)}\n`);

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

const isObject = (value: unknown): value is Record<string, unknown> =>
  value !== null && typeof value === 'object' && !Array.isArray(value);
// String() is the conversion RegExp.prototype.test applies to its argument.
const isSha256 = (value: unknown): boolean =>
  /^[0-9a-f]{64}$/.test(String(value ?? ''));

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

const isApkEtag = (value: unknown): value is string =>
  typeof value === 'string' && /^[\x21\x23-\x7e]{1,256}$/.test(value);

// The value of a strong ETag header ("abc" -> abc); null for a weak, missing or
// malformed one, which identifies no bytes.
export function strongEtag(header: string | null | undefined): string | null {
  const match = /^"([\x21\x23-\x7e]{1,256})"$/.exec(header?.trim() ?? '');
  return match ? match[1] : null;
}

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

function assertEntry(key: string, entry: unknown): void {
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
  for (const key of expected) assertEntry(key, entries[key]);
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

// The private store through CLOUDFLARE_API_TOKEN, which reads and writes the
// bucket.
export function gameLibsStoreFromEnvironment(
  env: NodeJS.ProcessEnv = process.env
): ObjectStore {
  return r2StoreFromEnvironment(env, { bucket: GAME_LIBS_BUCKET });
}

function readSnapshotManifest<
  Manifest extends ManagedSnapshotManifest | InteropSnapshotManifest
>(directory: string): Manifest | null {
  const file = path.join(directory, 'manifest.json');
  if (!fs.existsSync(file)) return null;
  return JSON.parse(fs.readFileSync(file, 'utf8')) as Manifest;
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

// execFileSync fails with the child's stderr attached.
type ExecFileError = Error & { stderr?: Buffer | string };

// macOS tar otherwise packs every extended attribute as an AppleDouble
// `._<name>` member that GNU tar extracts as a real file, which changes the
// directory digest on Linux. COPYFILE_DISABLE turns that off; GNU tar ignores
// it.
function tar(args: readonly string[], cwd: string): void {
  try {
    execFileSync('tar', args, {
      cwd,
      stdio: ['ignore', 'ignore', 'pipe'],
      env: { ...process.env, COPYFILE_DISABLE: '1' }
    });
  } catch (error) {
    const failure = error as ExecFileError;
    throw new Error(
      `tar ${args[0]} failed: ${String(failure.stderr ?? failure.message).trim()}`
    );
  }
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

/** A branch's Steam state, as steam-stale prints it for game-status. */
export interface SteamState {
  buildid: string | number;
  /** When the branch last changed, in Unix seconds. */
  updatedAt: number;
}

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

// The public Game Status (root GLOSSARY.md) from a Steam state and the lock that
// records that build, or null while the lock holds another build of an entry:
// staging and ptr stay private, so only online is ever written.
/** One desktop platform of the Game Status: its game and `Managed/` digest. */
export interface GameStatusPlatform {
  gameVersion: string;
  managedSha256: string;
}

/** The public Game Status document; decodeGameStatus in downloads.ts reads it. */
export interface GameStatusDocument {
  schemaVersion: 1;
  channel: 'online';
  buildid: string;
  updatedAt: string;
  platforms: Partial<Record<ManagedPlatform, GameStatusPlatform>>;
}

export function gameStatus(
  lock: GameLibsLock,
  { buildid, updatedAt }: SteamState
): GameStatusDocument | null {
  const platforms: Partial<Record<ManagedPlatform, GameStatusPlatform>> = {};
  for (const platform of MANAGED_PLATFORMS) {
    const entry = lock.entries[lockKey(platform, 'online')];
    if (entry?.buildid !== String(buildid)) return null;
    platforms[platform] = {
      gameVersion: entry.gameVersion,
      managedSha256: entry.sha256
    };
  }
  if (!Number.isSafeInteger(updatedAt) || updatedAt <= 0) return null;
  return {
    schemaVersion: 1,
    channel: 'online',
    buildid: String(buildid),
    updatedAt: new Date(updatedAt * 1000).toISOString(),
    platforms
  };
}

export interface PublishGameStatusOptions {
  workspaceRoot?: string;
  state: SteamState;
  store: Pick<ObjectStore, 'get' | 'put'>;
}

export async function publishGameStatus({
  workspaceRoot = WORKSPACE_ROOT,
  state,
  store
}: PublishGameStatusOptions): Promise<GameStatusDocument | null> {
  const status = gameStatus(readGameLibsLock(workspaceRoot), state);
  if (!status) return null;
  await putReplaceable(
    store,
    GAME_STATUS_KEY,
    jsonBytes(status),
    'application/json'
  );
  return status;
}

// The Android Game Status (root GLOSSARY.md): the android-online entry's game
// and its game code identity, or null while the lock has no such entry. The
// Android APK watch writes it once the lock, or the lock pull request it
// opens, records the official APK.
/** The public Android Game Status document; decodeAndroidGameStatus reads it. */
export interface AndroidGameStatusDocument extends AndroidGameStatus {
  schemaVersion: 1;
}

export function androidGameStatus(
  lock: GameLibsLock
): AndroidGameStatusDocument | null {
  const entry = lock.entries['android-online'];
  if (!entry) return null;
  return {
    schemaVersion: 1,
    gameVersion: entry.gameVersion,
    versionCode: entry.versionCode,
    il2cppSha256: entry.il2cppSha256,
    metadataSha256: entry.metadataSha256
  };
}

export interface PublishAndroidGameStatusOptions {
  workspaceRoot?: string;
  store: Pick<ObjectStore, 'get' | 'put'>;
}

export async function publishAndroidGameStatus({
  workspaceRoot = WORKSPACE_ROOT,
  store
}: PublishAndroidGameStatusOptions): Promise<AndroidGameStatusDocument | null> {
  const status = androidGameStatus(readGameLibsLock(workspaceRoot));
  if (!status) return null;
  await putReplaceable(
    store,
    ANDROID_GAME_STATUS_KEY,
    jsonBytes(status),
    'application/json'
  );
  return status;
}

// ---------------------------------------------------------------------------
// Interop Snapshots (ADR 0008). The android-online entry pins an official APK
// and the toolchain that turned it into Il2CppInterop assemblies. Locally the
// snapshot lives at game-libs/android-online-<gameVersion>/interop/, the Loader
// Release at game-libs/loader-<tag>/, and other inputs fetched by sha256 under
// game-libs/inputs/. Each input is stored in the private bucket under its own
// content address.

/** A pinned input of the android-online entry, stored by its sha256. */
export type AndroidInputKind = 'apk' | 'loader' | 'patcher' | 'cpp2il';

/** Where an Android input lives and which entry field pins it. */
export interface AndroidInput {
  extension: string;
  sha256: (entry: InteropLockEntry) => string;
}

export const ANDROID_INPUTS: Readonly<Record<AndroidInputKind, AndroidInput>> =
  Object.freeze({
    apk: { extension: '.apk', sha256: (entry) => entry.apkSha256 },
    loader: { extension: '.zip', sha256: (entry) => entry.loader.sha256 },
    patcher: { extension: '.tar.gz', sha256: (entry) => entry.patcher.sha256 },
    cpp2il: { extension: '', sha256: (entry) => entry.cpp2ilSha256 }
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

// Where the publisher serves the official Android APK. It overwrites the file in
// place on every game update, which is why the private bucket keeps each one.
export const OFFICIAL_ANDROID_APK_URL =
  'https://data.playthebazaar.com/game/android/TheBazaar.apk';

// Whether the official APK differs from the one the android-online entry
// locks, read without downloading either: the publisher's Content-Length
// against the stored copy's size, and its ETag against the one the entry
// recorded. An entry without an ETag (a manual capture) compares sizes only.
/** The HEAD request officialApkChanged sends: `fetch`, or a test double. */
export type ApkHeadFetch = (
  url: string,
  init: RequestInit
) => Promise<Pick<Response, 'ok' | 'status' | 'headers'>>;

export interface OfficialApkChangedOptions {
  workspaceRoot?: string;
  url?: string;
  storeFactory?: () => Pick<ObjectStore, 'head'>;
  fetchImpl?: ApkHeadFetch;
}

/** The publisher's APK against the one the android-online entry locks. */
export interface OfficialApkCheck {
  changed: boolean;
  officialSize: number;
  officialEtag: string | null;
  lockedSize: number;
  lockedEtag: string | null;
  lockedGameVersion: string;
}

export async function officialApkChanged({
  workspaceRoot = WORKSPACE_ROOT,
  url = OFFICIAL_ANDROID_APK_URL,
  storeFactory = gameLibsStoreFromEnvironment,
  fetchImpl = fetch
}: OfficialApkChangedOptions): Promise<OfficialApkCheck> {
  const response = await fetchImpl(url, { method: 'HEAD', redirect: 'follow' });
  if (!response.ok) throw new Error(`HEAD ${url} answered ${response.status}`);
  const officialSize = Number(response.headers.get('content-length'));
  if (!Number.isSafeInteger(officialSize) || officialSize <= 0)
    throw new Error(`HEAD ${url} gave no Content-Length`);
  const officialEtag = strongEtag(response.headers.get('etag'));
  const entry = lockedInteropEntry(workspaceRoot);
  const key = androidInputKey('apk', entry.apkSha256);
  const stored = await storeFactory().head(key);
  if (!stored) throw new Error(`The locked APK ${key} is not in the store`);
  return {
    changed:
      stored.size !== officialSize ||
      (entry.apkEtag !== null && officialEtag !== entry.apkEtag),
    officialSize,
    officialEtag,
    lockedSize: stored.size,
    lockedEtag: entry.apkEtag,
    lockedGameVersion: entry.gameVersion
  };
}

// Records the ETag the publisher now serves the locked APK with, after
// checking that `apk` is that APK: the publisher can change the ETag of
// unchanged bytes, and an entry that kept the old one would be downloaded on
// every check.
export interface RecordApkEtagOptions {
  workspaceRoot?: string;
  apk: string;
  etag: string;
}

export async function recordApkEtag({
  workspaceRoot = WORKSPACE_ROOT,
  apk,
  etag
}: RecordApkEtagOptions): Promise<string> {
  if (!isApkEtag(etag)) throw new Error(`Not a strong ETag value: ${etag}`);
  const entry = lockedInteropEntry(workspaceRoot);
  const sha256 = await sha256File(apk);
  if (sha256 !== entry.apkSha256)
    throw new Error(
      `${apk} is ${sha256}, not the locked APK ${entry.apkSha256}; capture it with snapshot-android`
    );
  writeGameLibsLockEntry(workspaceRoot, INTEROP_KEY, {
    ...entry,
    apkEtag: etag
  });
  return entry.gameVersion;
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

// Every top-level file of an interop directory, keyed interop/<name>: the
// proxy assemblies plus the generator's databases and manifest, which the
// injector packs beside them. Ordinal order, as for Managed.
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

// Every member name of a gzipped tar, as GNU tar extracts it: pax `path`
// records and GNU long names resolved. tar -t on macOS hides AppleDouble
// members, so this reads the headers itself.
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

// An interop archive holds `interop/` and its files, nothing else; a member
// GNU tar would extract beside them (an AppleDouble `._` file, a nested
// directory) changes the digest on Linux.
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

// What interop-manifest.json is read for; the generator writes more.
interface InteropGeneratorManifest {
  unityVersion?: string;
  tools?: { cpp2IlVersion?: string; il2CppInteropVersion?: string };
}

/** The toolchain an interop directory was generated with. */
interface InteropToolchain {
  unityVersion: string;
  cpp2ilVersion: string;
  il2cppInteropVersion: string;
}

// The generator records its own toolchain in interop-manifest.json.
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
  'Fetch from the private store with the Cloudflare operator token (just with-config release just mod::fetch android online)';

// Makes one pinned Android input available as a verified local file: the
// game-libs/inputs/ cache, else the private store.
export interface FetchAndroidInputOptions {
  modRoot: string;
  kind: AndroidInputKind;
  sha256: string;
  /** The store, created on first use. */
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
        `The private store has no ${key}; run 'just mod::publish android online' on the machine that captured it`
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

// Whether `directory` holds the Loader Release `loader` (a lock entry's tag and
// sha256) as ensureLoader unpacked it: the managed loader assemblies and the
// marker recording the zip's sha256.
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

// Unpacks the Loader Release zip whose sha256 the lock entry pins into
// game-libs/loader-<tag>/, recording that sha256 beside it.
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

// Runs the Patcher's generate-interop with the pinned Cpp2IL and the
// Il2CppInterop CLI the Patcher release carries. The Patcher CLI is a Linux x64
// binary; elsewhere a capture reuses an interop directory generated from the
// same APK (--interop).
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

// Native libraries a host ships beside the loader, pinned by NuGet package:
// every runtimes/android-arm64/native/*.so of <id>@<version>.
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

// The toolchain of a capture: files given on the command line pin a new tag,
// otherwise the current entry's pins are reused and fetched by sha256.
// The toolchain files a capture names on the command line; '' reuses a pin.
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

// A toolchain file of a capture and the pin it records.
interface CaptureToolchain {
  loader: { file: string; pin: LoaderPin };
  patcher: { file: string; pin: PatcherPin };
  cpp2il: { file: string; sha256: string };
}

// What lemonloader-release.json is read for.
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
      sha256: ANDROID_INPUTS[kind].sha256(locked),
      store,
      log
    });
  const need = (flag: string): InteropLockEntry => {
    if (!previous)
      throw new Error(
        `The ${INTEROP_KEY} lock entry is empty, so the first capture names the toolchain: pass ${flag}`
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

// Captures an official APK as the android-online entry: reads its identity,
// generates (or takes, with `interop`) the Interop assemblies, unpacks the
// Loader Release, and records the entry. Publish uploads every input.
export interface CaptureInteropSnapshotOptions {
  workspaceRoot?: string;
  modRoot?: string;
  apk: string;
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

/** What an Interop Snapshot capture wrote. */
export interface CapturedInteropSnapshot {
  key: typeof INTEROP_KEY;
  directory: string;
  loaderPath: string;
  manifest: InteropSnapshotManifest;
}

export async function captureInteropSnapshot({
  workspaceRoot = WORKSPACE_ROOT,
  modRoot = path.join(workspaceRoot, 'bazaarplusplus-mod'),
  apk,
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
  if (!apk || !fs.existsSync(apk))
    throw new Error(`No APK at ${JSON.stringify(apk)}`);
  const previous = readGameLibsLock(workspaceRoot).entries[INTEROP_KEY];
  const store = lazyStore(
    storeFactory,
    `${STORE_REMEDY}, or pass the toolchain files explicitly`
  );
  const work = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-interop-'));
  try {
    log(`Reading ${apk}`);
    const facts = await readApk(apk, work);
    const { platform, channel } = parseGameVersion(facts.versionName);
    if (`${platform}-${channel}` !== INTEROP_KEY)
      throw new Error(
        `${apk} is ${facts.versionName}, a ${platform}-${channel} build, not an ${INTEROP_KEY} APK`
      );
    const tools = await captureToolchain({
      modRoot,
      previous,
      options: { loader, loaderTag, patcher, patcherTag, cpp2il },
      store,
      log
    });
    let source = interop;
    if (!source) {
      source = path.join(work, 'interop');
      log(`Generating interop for ${facts.versionName}`);
      generate({
        patcher: tools.patcher.file,
        cpp2il: tools.cpp2il.file,
        il2cpp: facts.il2cpp,
        metadata: facts.metadata,
        unityVersion: facts.unityVersion,
        output: source,
        workDirectory: work
      });
    }
    const generated = readInteropManifest(source);
    if (generated.unityVersion !== facts.unityVersion)
      throw new Error(
        `${source} was generated for Unity ${generated.unityVersion}, but ${apk} is Unity ${facts.unityVersion}`
      );
    const interopSha256 = interopDirectoryDigest(source);
    const directory = snapshotDirectory(
      modRoot,
      platform,
      channel,
      facts.versionName
    );
    copyInteropInto(source, directory, interopSha256);
    const entry: InteropLockEntry = {
      gameVersion: facts.versionName,
      versionCode: facts.versionCode,
      apkSha256: facts.apkSha256,
      apkEtag,
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
      platform,
      channel,
      ...entry
    };
    // The lock is written last, so a failed step leaves it unchanged.
    assertEntry(INTEROP_KEY, entry);
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
        apk: path.resolve(apk),
        loader: path.resolve(tools.loader.file),
        patcher: path.resolve(tools.patcher.file),
        cpp2il: path.resolve(tools.cpp2il.file)
      })
    );
    writeGameLibsLockEntry(workspaceRoot, INTEROP_KEY, entry);
    return { key: INTEROP_KEY, directory, loaderPath, manifest };
  } finally {
    fs.rmSync(work, { recursive: true, force: true });
  }
}

function verifiedInteropSnapshot(
  modRoot: string,
  entry: InteropLockEntry
): { directory: string; interop: string } | null {
  const directory = snapshotDirectory(
    modRoot,
    'android',
    'online',
    entry.gameVersion
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
  workspaceRoot: string = WORKSPACE_ROOT
): InteropLockEntry {
  const entry = readGameLibsLock(workspaceRoot).entries[INTEROP_KEY];
  if (entry === null)
    throw new Error(
      `Lock entry ${INTEROP_KEY} is empty; capture it with 'just mod::snapshot-android <TheBazaar.apk>', publish it with 'just mod::publish android online', and commit the lock`
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

// Why a stored interop archive cannot serve a Linux fetch, else null.
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
    : `it hashes to ${digest}, not the ${entry.interopSha256} the lock entry ${INTEROP_KEY} records`;
}

// Uploads the interop collection and every pinned input the store lacks, each
// under its content address, then the manifest under the entry identity. An
// input already stored is never read locally; the interop archive is read
// back and verified, since its key is the directory digest, not its own.
export interface PublishInteropSnapshotOptions {
  workspaceRoot?: string;
  modRoot?: string;
  store: ObjectStore;
  log?: (message: string) => void;
}

/** Where a publish stored an Interop Snapshot. */
export interface PublishedInteropSnapshot {
  interopKey: string;
  manifestKey: string;
}

// sources.json: the local file each input was captured from.
type InteropSources = Partial<Record<AndroidInputKind, string>>;

export async function publishInteropSnapshot({
  workspaceRoot = WORKSPACE_ROOT,
  modRoot = path.join(workspaceRoot, 'bazaarplusplus-mod'),
  store,
  log = () => {}
}: PublishInteropSnapshotOptions): Promise<PublishedInteropSnapshot> {
  const entry = lockedInteropEntry(workspaceRoot);
  const snapshot = verifiedInteropSnapshot(modRoot, entry);
  if (!snapshot)
    throw new Error(
      `No Interop Snapshot under game-libs/ for ${INTEROP_KEY} ${entry.gameVersion}; run 'just mod::snapshot-android' on the machine that captured it`
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
  const interopKey = interopBlobKey(entry.interopSha256);
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-interop-'));
  try {
    const stored = await store.head(interopKey);
    const fault = stored
      ? await storedInteropArchiveFault(store, interopKey, entry, temp)
      : null;
    if (stored && !fault) log(`Already stored and verified: ${interopKey}`);
    else {
      const archive = packInteropArchive(snapshot.directory, temp);
      const sha256 = await sha256File(archive);
      if (!stored) await upload(interopKey, archive, sha256);
      else {
        log(`Replacing ${interopKey}: ${fault}`);
        if (
          !(await store.putFile(interopKey, archive, {
            sha256,
            ifMatch: stored.etag
          }))
        )
          throw new Error(
            `Another publisher replaced ${interopKey} first; run the publish again`
          );
        log(`Replaced ${interopKey}`);
      }
    }
  } finally {
    fs.rmSync(temp, { recursive: true, force: true });
  }
  // ANDROID_INPUTS is frozen, so its keys are exactly the input kinds.
  for (const kind of Object.keys(ANDROID_INPUTS) as AndroidInputKind[]) {
    const sha256 = ANDROID_INPUTS[kind].sha256(entry);
    const key = androidInputKey(kind, sha256);
    if (await store.head(key)) {
      log(`Already stored: ${key}`);
      continue;
    }
    const candidates = [
      sources[kind],
      androidInputFile(modRoot, kind, sha256)
    ].filter((file): file is string => !!file && fs.existsSync(file));
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
  const manifestKey = snapshotManifestKey(
    'android',
    'online',
    entry.gameVersion
  );
  await putReplaceable(
    store,
    manifestKey,
    jsonBytes(manifest),
    'application/json'
  );
  log(`Recorded ${manifestKey}`);
  return { interopKey, manifestKey };
}

// Resolves the android-online entry to a local interop directory and Loader
// Release: verified copies under game-libs/, else the private store.
export interface FetchInteropSnapshotOptions {
  workspaceRoot?: string;
  modRoot?: string;
  storeFactory?: () => Pick<ObjectStore, 'get' | 'getFile'>;
  log?: (message: string) => void;
}

/** Where a resolved Interop Snapshot came from. */
export type InteropSource = 'snapshot' | 'store';

/** The local interop directory and Loader Release of the android-online entry. */
export interface FetchedInteropSnapshot {
  interopPath: string;
  loaderPath: string;
  source: InteropSource;
  gameVersion: string;
}

export async function fetchInteropSnapshot({
  workspaceRoot = WORKSPACE_ROOT,
  modRoot = path.join(workspaceRoot, 'bazaarplusplus-mod'),
  storeFactory = gameLibsStoreFromEnvironment,
  log = () => {}
}: FetchInteropSnapshotOptions): Promise<FetchedInteropSnapshot> {
  const entry = lockedInteropEntry(workspaceRoot);
  const store = lazyStore(
    storeFactory,
    `Lock entry ${INTEROP_KEY} wants ${entry.gameVersion}, which is not under game-libs/. ${STORE_REMEDY}`
  );
  let snapshot = verifiedInteropSnapshot(modRoot, entry);
  let source: InteropSource = 'snapshot';
  if (!snapshot) {
    source = 'store';
    const directory = snapshotDirectory(
      modRoot,
      'android',
      'online',
      entry.gameVersion
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
          `The private store has no ${blobKey}; run 'just mod::publish android online' on the machine that captured it`
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
          `Stored snapshot ${blobKey} hashes to ${digest}, not the ${entry.interopSha256} the lock entry ${INTEROP_KEY} records`
        );
      const remote = await store().get(
        snapshotManifestKey('android', 'online', entry.gameVersion)
      );
      const manifest: InteropSnapshotManifest = remote
        ? (JSON.parse(remote.bytes.toString('utf8')) as InteropSnapshotManifest)
        : {
            schemaVersion: SNAPSHOT_MANIFEST_SCHEMA_VERSION,
            platform: 'android',
            channel: 'online',
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
    log(`Fetched ${INTEROP_KEY} ${entry.gameVersion} into ${directory}`);
  }
  const loaderPath = await ensureLoader({ modRoot, entry, store, log });
  return {
    interopPath: snapshot.interop,
    loaderPath,
    source,
    gameVersion: entry.gameVersion
  };
}

// One pinned input of the android-online entry as a verified local file: the
// APK a release job injects, the Patcher and Cpp2IL a recapture runs.
export interface FetchLockedAndroidInputOptions {
  workspaceRoot?: string;
  modRoot?: string;
  kind: AndroidInputKind;
  storeFactory?: () => Pick<ObjectStore, 'getFile'>;
  log?: (message: string) => void;
}

export async function fetchLockedAndroidInput({
  workspaceRoot = WORKSPACE_ROOT,
  modRoot = path.join(workspaceRoot, 'bazaarplusplus-mod'),
  kind,
  storeFactory = gameLibsStoreFromEnvironment,
  log = () => {}
}: FetchLockedAndroidInputOptions): Promise<string> {
  const entry = lockedInteropEntry(workspaceRoot);
  return fetchAndroidInput({
    modRoot,
    kind,
    sha256: inputKind(kind).sha256(entry),
    store: lazyStore(storeFactory, STORE_REMEDY),
    log
  });
}

// The game version a Managed directory carries: its snapshot manifest, else the
// globalgamemanagers beside it, else null (a bare copied directory).
function managedGameVersion(managedPath: string): string | null {
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

// The `just mod::check` gate: a malformed lock fails, empty or stale entries
// warn, a resolved Managed directory whose game version a lock entry names
// must hash to that entry, and so must a local Interop Snapshot of the
// android-online entry.
export interface CheckGameLibsLockOptions {
  workspaceRoot?: string;
  modRoot?: string;
  managedPath?: string;
  now?: Date;
}

/** What `lock-check` found. */
export interface GameLibsLockCheck {
  lock: GameLibsLock;
  warnings: string[];
  /** The resolved Managed directory and the entries it matched. */
  verified: { gameVersion: string; keys: string[] } | null;
  /** The local Interop Snapshot that matched the android-online entry. */
  interop: { gameVersion: string } | null;
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
  const interopEntry = lock.entries[INTEROP_KEY];
  const interop = interopEntry
    ? verifiedInteropSnapshot(modRoot, interopEntry)
    : null;
  return {
    lock,
    warnings,
    verified,
    // interop is only ever found for a captured interopEntry.
    interop: interop &&
      interopEntry && { gameVersion: interopEntry.gameVersion }
  };
}

function printLine(value: string): void {
  process.stdout.write(`${value}\n`);
}

const log = (message: string): boolean => process.stderr.write(`${message}\n`);

const usage = `Game Assembly and Interop Snapshot commands (driven by bazaarplusplus-mod/scripts/game.sh):
  bun release/game-libs.ts game-version <managed-dir>
  bun release/game-libs.ts digest <managed-dir>
  bun release/game-libs.ts snapshot --managed <dir> --buildid <id> --steam-branch <branch>
  bun release/game-libs.ts steam-stale --channel <online|staging|ptr>
                                             Print {buildid, updatedAt, platforms}: the platforms whose lock
                                             entry is behind Steam (needs STEAM_SNAPSHOT_LOGIN)
  bun release/game-libs.ts steam-capture --platform <macos|windows> --channel <online|staging|ptr>
                                             Capture a lock entry from its Steam depot (needs
                                             STEAM_SNAPSHOT_LOGIN, and STEAM_BETA_PASSWORDS for
                                             staging and ptr)
  bun release/game-libs.ts steam-change --previous <lock.json> --channel <online|staging|ptr>
                                             Print code, content or none: what the captures
                                             changed against that earlier lock
  bun release/game-libs.ts game-status --state <steam-stale.json>
                                             Write the public Game Status when the lock records
                                             that online build
  bun release/game-libs.ts android-game-status
                                             Write the public Android Game Status from the lock's
                                             android-online entry
  bun release/game-libs.ts snapshot-android --apk <TheBazaar.apk> [--apk-etag <etag>] [--interop <dir>]
      [--loader <zip> --loader-tag <tag>] [--patcher <tar.gz> --patcher-tag <tag>]
      [--cpp2il <file>] [--native-package <id>@<version> ...]
  bun release/game-libs.ts publish --platform <macos|windows|android> --channel <online|staging|ptr>
  bun release/game-libs.ts fetch --platform <p> --channel <c> [--steam-managed <dir>] [--allow-unlocked]
  bun release/game-libs.ts fetch-input --input <apk|loader|patcher|cpp2il>
  bun release/game-libs.ts lock-check [--managed <dir>]
  bun release/game-libs.ts entries
  bun release/game-libs.ts behind-online --platform <macos|windows> --channel <staging|ptr>
                                             Print the online game version when this entry's
                                             Steam build is older than online's, else nothing
  bun release/game-libs.ts official-apk     Print changed or unchanged: the publisher's APK against
                                             the android-online entry's (size and ETag, no download)
  bun release/game-libs.ts record-apk-etag --apk <TheBazaar.apk> --apk-etag <etag>
                                             Record the ETag the locked APK is now served with
snapshot-android reuses the android-online entry's toolchain unless files are
given, and generates interop with the Patcher's Linux x64 CLI unless --interop
names a directory generated from the same APK. fetch android online prints the
interop directory and unpacks the Loader Release beside it. publish always, and
fetch or fetch-input when no verified local copy exists, use the private bucket
${GAME_LIBS_BUCKET} through CLOUDFLARE_API_TOKEN and CLOUDFLARE_ACCOUNT_ID (the
S3 pair is derived from the token).`;

export interface CliOptions {
  workspaceRoot?: string;
}

export async function cliMain(
  argv: readonly string[],
  { workspaceRoot = WORKSPACE_ROOT }: CliOptions = {}
): Promise<number> {
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
        previous: { type: 'string' },
        state: { type: 'string' },
        buildid: { type: 'string' },
        'steam-branch': { type: 'string' },
        platform: { type: 'string' },
        channel: { type: 'string' },
        'steam-managed': { type: 'string' },
        'allow-unlocked': { type: 'boolean' },
        apk: { type: 'string' },
        interop: { type: 'string' },
        loader: { type: 'string' },
        'loader-tag': { type: 'string' },
        patcher: { type: 'string' },
        'patcher-tag': { type: 'string' },
        cpp2il: { type: 'string' },
        'native-package': { type: 'string', multiple: true },
        input: { type: 'string' },
        'apk-etag': { type: 'string' }
      }
    });
    const modRoot = path.join(workspaceRoot, 'bazaarplusplus-mod');
    // The options that take one string value.
    type StringOption = {
      [Name in keyof typeof values]-?: (typeof values)[Name] extends
        | string
        | undefined
        ? Name
        : never;
    }[keyof typeof values];
    const required = (name: StringOption): string => {
      const value = values[name];
      if (!value) throw new Error(`${verb} requires --${name}\n${usage}`);
      return value;
    };
    switch (verb) {
      case 'official-apk': {
        const result = await officialApkChanged({ workspaceRoot });
        log(
          `Official APK ${result.officialSize} bytes, ETag ${result.officialEtag}; locked ${result.lockedGameVersion} ${result.lockedSize} bytes, ETag ${result.lockedEtag}`
        );
        printLine(result.changed ? 'changed' : 'unchanged');
        return 0;
      }
      case 'record-apk-etag': {
        const gameVersion = await recordApkEtag({
          workspaceRoot,
          apk: required('apk'),
          etag: required('apk-etag')
        });
        log(`Recorded the ETag of ${gameVersion} in ${GAME_LIBS_LOCK_PATH}`);
        return 0;
      }
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
      case 'steam-stale': {
        printLine(
          JSON.stringify(
            steamStale({
              workspaceRoot,
              modRoot,
              // steamStale checks the channel.
              channel: required('channel') as SnapshotChannel
            })
          )
        );
        return 0;
      }
      case 'steam-capture': {
        // steamCapture checks the platform and channel.
        const result = steamCapture({
          workspaceRoot,
          modRoot,
          platform: required('platform') as SnapshotPlatform,
          channel: required('channel') as SnapshotChannel
        });
        log(
          `Captured ${result.key} ${result.manifest.gameVersion} (buildid ${result.manifest.buildid}) into ${result.directory} and recorded it in ${GAME_LIBS_LOCK_PATH}`
        );
        printLine(result.directory);
        return 0;
      }
      case 'steam-change': {
        printLine(
          steamChangeKind(
            validateGameLibsLock(
              JSON.parse(fs.readFileSync(required('previous'), 'utf8'))
            ),
            readGameLibsLock(workspaceRoot),
            // steamChangeKind checks the channel.
            required('channel') as SnapshotChannel
          )
        );
        return 0;
      }
      case 'game-status': {
        const status = await publishGameStatus({
          workspaceRoot,
          state: JSON.parse(
            fs.readFileSync(required('state'), 'utf8')
          ) as SteamState,
          store: r2StoreFromEnvironment(process.env, {
            bucket: RELEASE_BUCKET,
            prefix: GAME_STATUS_PREFIX
          })
        });
        log(
          status
            ? `Game Status: online build ${status.buildid}`
            : 'The lock does not record this online build yet; the Game Status stays as it is'
        );
        return 0;
      }
      case 'android-game-status': {
        const status = await publishAndroidGameStatus({
          workspaceRoot,
          store: r2StoreFromEnvironment(process.env, {
            bucket: RELEASE_BUCKET,
            prefix: GAME_STATUS_PREFIX
          })
        });
        log(
          status
            ? `Android Game Status: ${status.gameVersion}`
            : 'The lock has no android-online entry; the Android Game Status stays as it is'
        );
        return 0;
      }
      case 'snapshot-android': {
        const result = await captureInteropSnapshot({
          workspaceRoot,
          modRoot,
          apk: required('apk'),
          apkEtag: values['apk-etag'] ?? null,
          interop: values.interop ?? '',
          loader: values.loader ?? '',
          loaderTag: values['loader-tag'] ?? '',
          patcher: values.patcher ?? '',
          patcherTag: values['patcher-tag'] ?? '',
          cpp2il: values.cpp2il ?? '',
          nativePackages: values['native-package'] ?? [],
          log
        });
        log(
          `Captured ${result.key} ${result.manifest.gameVersion} into ${result.directory} (loader ${result.loaderPath}) and recorded it in ${GAME_LIBS_LOCK_PATH}`
        );
        printLine(result.directory);
        return 0;
      }
      case 'publish': {
        const platform = required('platform');
        const channel = required('channel');
        if (isInteropPlatform(platform)) {
          lockKey(platform, channel);
          await publishInteropSnapshot({
            workspaceRoot,
            modRoot,
            store: gameLibsStoreFromEnvironment(),
            log
          });
          return 0;
        }
        // Every platform that is not an interop one is a Managed one;
        // publishSnapshot checks the channel.
        await publishSnapshot({
          workspaceRoot,
          modRoot,
          platform: platform as ManagedPlatform,
          channel: channel as SnapshotChannel,
          store: gameLibsStoreFromEnvironment(),
          log
        });
        return 0;
      }
      case 'fetch-input': {
        printLine(
          await fetchLockedAndroidInput({
            workspaceRoot,
            modRoot,
            // inputKind checks the input kind.
            kind: required('input') as AndroidInputKind,
            log
          })
        );
        return 0;
      }
      case 'fetch': {
        if (isInteropPlatform(required('platform'))) {
          lockKey(required('platform'), required('channel'));
          const result = await fetchInteropSnapshot({
            workspaceRoot,
            modRoot,
            log
          });
          log(`Loader Release ${result.loaderPath}`);
          printLine(result.interopPath);
          return 0;
        }
        // Every platform that is not an interop one is a Managed one;
        // fetchSnapshot checks the channel.
        const result = await fetchSnapshot({
          workspaceRoot,
          modRoot,
          platform: required('platform') as ManagedPlatform,
          channel: required('channel') as SnapshotChannel,
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
        if (result.interop)
          log(
            `Interop ${result.interop.gameVersion} matches lock ${INTEROP_KEY}`
          );
        return 0;
      }
      case 'entries': {
        for (const { platform, channel } of managedEntries(
          readGameLibsLock(workspaceRoot)
        ))
          printLine(`${platform} ${channel}`);
        return 0;
      }
      case 'behind-online': {
        const online = previewBehindOnline(
          readGameLibsLock(workspaceRoot),
          required('platform') as ManagedPlatform,
          required('channel') as SnapshotChannel
        );
        if (online) printLine(online.gameVersion);
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

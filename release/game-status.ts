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
import type { FetchImpl, ObjectStore } from './r2-store.ts';
import { jsonBytes } from './snapshot-files.ts';
import {
  MANAGED_PLATFORMS,
  lockKey,
  readGameLibsLock
} from './snapshot-lock.ts';
import type { GameLibsLock, ManagedPlatform } from './snapshot-lock.ts';

// The Game Status and the Android Game Status (root GLOSSARY.md): the public
// documents the site's download page reads, built from the Snapshot Lock, and
// the public store they are written to. downloads.ts owns their object keys
// and decoders; the game-status and android-game-status verbs of game-libs.ts
// write them.

/** A branch's Steam state, as steam-stale prints it for game-status. */
export interface SteamState {
  buildid: string | number;
  /** When the branch last changed, in Unix seconds. */
  updatedAt: number;
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

/** The options of gameStatusStoreFromEnvironment; bucket and prefix are fixed. */
export interface GameStatusStoreOptions {
  /** The fetch the store sends its R2 requests with. */
  fetchImpl?: FetchImpl;
}

// The public store the Game Status and the Android Game Status are written to:
// the release bucket under game-status/. Never the bucket's default installer/
// prefix: GAME_STATUS_KEY and the Release Manifest are both latest.json, so a
// Game Status written there would replace the production Release Manifest.
export function gameStatusStoreFromEnvironment(
  env: NodeJS.ProcessEnv = process.env,
  { fetchImpl }: GameStatusStoreOptions = {}
): ObjectStore {
  return r2StoreFromEnvironment(env, {
    ...(fetchImpl === undefined ? {} : { fetchImpl }),
    bucket: RELEASE_BUCKET,
    prefix: GAME_STATUS_PREFIX
  });
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

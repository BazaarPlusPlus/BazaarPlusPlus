import {
  RELEASE_BASE_URL,
  ANDROID_GAME_STATUS_URL,
  DOWNLOAD_PLATFORM_KEYS,
  GAME_STATUS_URL,
  decodeAndroidDownload,
  decodeAndroidGameStatus,
  decodeDesktopGameRelease,
  decodeGameStatus,
  decodeMainlandDownloadUrl,
  platformManifestPath,
  type AndroidDownload,
  type AndroidGameRelease,
  type AndroidGameStatus,
  type DesktopGameRelease,
  type DownloadPlatform as ReleasePlatform,
  type GameStatus
} from '../../../../release/downloads';

export { ANDROID_SOURCES } from '../../../../release/downloads';
export type {
  AndroidDownload,
  AndroidGameStatus,
  AndroidSource,
  DesktopGameRelease,
  GameStatus
} from '../../../../release/downloads';
export type DownloadPlatform = Exclude<ReleasePlatform, 'android'>;
export const GITHUB_RELEASE_URL =
  'https://github.com/BazaarPlusPlus/BazaarPlusPlus/releases/latest';

export type PlatformDownload = {
  version: string;
  downloadUrl: string;
  mainlandDownloadUrl: string | null;
  game: DesktopGameRelease | null;
};

export type LatestInstaller = {
  downloads: Record<DownloadPlatform, PlatformDownload | null>;
};

function decodeVersion(payload: unknown, path: string): string {
  if (
    payload == null ||
    typeof payload !== 'object' ||
    typeof (payload as { version?: unknown }).version !== 'string' ||
    !(payload as { version: string }).version
  ) {
    throw new Error(`${path} missing string \`version\``);
  }
  return (payload as { version: string }).version;
}

function decodeDownloadUrl(
  payload: unknown,
  path: string,
  platformKey: string,
  version: string
): string {
  const downloads = (payload as { downloads?: unknown }).downloads;
  const record =
    downloads && typeof downloads === 'object'
      ? (downloads as Record<string, unknown>)[platformKey]
      : undefined;
  const value =
    record && typeof record === 'object'
      ? (record as { url?: unknown }).url
      : undefined;
  if (typeof value !== 'string')
    throw new Error(`${path} missing ${platformKey} download`);
  const url = new URL(value);
  const release = `/${version}/${platformKey}/installer/`;
  if (
    url.username ||
    url.password ||
    url.hash ||
    url.search ||
    !url.href.startsWith(`${RELEASE_BASE_URL}${release}`)
  ) {
    throw new Error(`${path} has an invalid ${platformKey} download`);
  }
  return value;
}

async function fetchPlatformManifest(
  path: string,
  signal: AbortSignal
): Promise<unknown> {
  const response = await fetch(`${RELEASE_BASE_URL}/${path}`, {
    headers: { Accept: 'application/json' },
    signal
  });
  if (!response.ok) {
    throw new Error(`${path} responded with ${response.status}`);
  }
  return response.json();
}

async function loadPlatformDownload(
  platform: DownloadPlatform,
  signal: AbortSignal
): Promise<PlatformDownload> {
  const platformKey = DOWNLOAD_PLATFORM_KEYS[platform];
  const path = platformManifestPath(platformKey);
  const payload = await fetchPlatformManifest(path, signal);
  const version = decodeVersion(payload, path);
  return {
    version,
    downloadUrl: decodeDownloadUrl(payload, path, platformKey, version),
    mainlandDownloadUrl: decodeMainlandDownloadUrl(payload, platformKey),
    game: decodeDesktopGameRelease(payload, platformKey)
  };
}

export async function loadAndroidRelease(
  signal: AbortSignal
): Promise<AndroidDownload> {
  const platformKey = DOWNLOAD_PLATFORM_KEYS.android;
  const path = platformManifestPath(platformKey);
  const release = decodeAndroidDownload(
    await fetchPlatformManifest(path, signal),
    platformKey
  );
  if (!release)
    throw new Error(`${path} has an invalid ${platformKey} release`);
  return release;
}

async function fetchGameStatus<T>(
  url: string,
  decode: (payload: unknown) => T | null,
  signal: AbortSignal
): Promise<T> {
  const response = await fetch(url, {
    headers: { Accept: 'application/json' },
    signal
  });
  if (!response.ok)
    throw new Error(`Game Status responded with ${response.status}`);
  const status = decode(await response.json());
  if (!status) throw new Error('Game Status is invalid');
  return status;
}

export function loadGameStatus(signal: AbortSignal): Promise<GameStatus> {
  return fetchGameStatus(GAME_STATUS_URL, decodeGameStatus, signal);
}

export function loadAndroidGameStatus(
  signal: AbortSignal
): Promise<AndroidGameStatus> {
  return fetchGameStatus(
    ANDROID_GAME_STATUS_URL,
    decodeAndroidGameStatus,
    signal
  );
}

/** Content-only game updates preserve Game Code Identity and stay adapted (ADR 0016). */
export function sameDesktopGame(
  adapted: DesktopGameRelease,
  online: DesktopGameRelease
): boolean {
  return adapted.managedSha256 === online.managedSha256;
}

export function sameAndroidGame(
  adapted: AndroidGameRelease,
  online: AndroidGameRelease
): boolean {
  return (
    adapted.il2cppSha256 === online.il2cppSha256 &&
    adapted.metadataSha256 === online.metadataSha256
  );
}

export function gameReleaseNumber(gameVersion: string): string {
  return /^\d+(?:\.\d+)+/.exec(gameVersion)?.[0] ?? gameVersion;
}

export async function loadLatestInstaller(
  signal: AbortSignal
): Promise<LatestInstaller> {
  const platforms: DownloadPlatform[] = ['windows', 'mac'];
  const settled = await Promise.allSettled(
    platforms.map((platform) => loadPlatformDownload(platform, signal))
  );
  const downloads = {
    windows: null,
    mac: null
  } as LatestInstaller['downloads'];
  const failures: unknown[] = [];
  settled.forEach((result, index) => {
    const platform = platforms[index];
    if (result.status === 'fulfilled') downloads[platform] = result.value;
    else failures.push(result.reason);
  });
  if (failures.length === platforms.length) {
    throw failures[0] instanceof Error
      ? failures[0]
      : new Error(String(failures[0]));
  }
  return { downloads };
}

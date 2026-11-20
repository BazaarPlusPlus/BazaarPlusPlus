// Browser-safe Product Release facts shared by the website, installer and writer.
export const RELEASE_BASE_URL = 'https://cdn.bazaarplusplus.com/installer';

export const DOWNLOAD_PLATFORM_KEYS = Object.freeze({
  windows: 'windows-x86_64',
  mac: 'darwin-aarch64',
  android: 'android-aarch64'
});

export type DownloadPlatform = keyof typeof DOWNLOAD_PLATFORM_KEYS;

export const RELEASE_MANIFEST_PATH = 'latest.json';

export function platformManifestPath(platformKey: string): string {
  return `latest/${platformKey}.json`;
}

// The mod and installer use separate URLs for the same Platform Release Manifest.
export function modManifestPath(platformKey: string): string {
  return `latest/mod/${platformKey}.json`;
}

// Tauri falls through on non-2xx responses, so latest.json is the fallback for missing platform manifests.
export const UPDATER_ENDPOINTS = Object.freeze([
  `${RELEASE_BASE_URL}/latest/{{target}}-{{arch}}.json`,
  `${RELEASE_BASE_URL}/${RELEASE_MANIFEST_PATH}`
]);

export function decodeMainlandDownloadUrl(
  manifest: unknown,
  platformKey: string
): string | null {
  const downloads = (manifest as { downloads?: unknown } | null)?.downloads;
  const record =
    downloads && typeof downloads === 'object'
      ? (downloads as Record<string, unknown>)[platformKey]
      : undefined;
  const value =
    record && typeof record === 'object'
      ? (record as { mainlandUrl?: unknown }).mainlandUrl
      : undefined;
  if (typeof value !== 'string') return null;
  let url: URL;
  try {
    url = new URL(value);
  } catch {
    return null;
  }
  if (url.protocol !== 'https:' || url.username || url.password) return null;
  return value;
}

export type AndroidAppDownload = {
  url: string;
  size: number;
  sha256: string;
  versionCode: number;
  signerSha256: string;
};

export type ManagedPayloadDownload = {
  url: string;
  size: number;
  sha256: string;
  signature: string;
};

// The Android App verifies the interop statement's base64 SHA256withRSA signature before loading the zip.
export type InteropPackageDownload = {
  url: string;
  size: number;
  sha256: string;
  snapshotSha256: string;
  signature: string;
};

export type AndroidGameRelease = {
  gameVersion: string;
  versionCode: number;
  il2cppSha256: string;
  metadataSha256: string;
};

export type AndroidSource = 'publisher' | 'play';
export const ANDROID_SOURCES: readonly AndroidSource[] = Object.freeze([
  'publisher',
  'play'
]);

export type AndroidAdaptation = {
  signerSha256: string;
  game: AndroidGameRelease;
  interop: InteropPackageDownload;
  managedPayload: ManagedPayloadDownload;
};

export type DesktopGameRelease = {
  gameVersion: string;
  managedSha256: string;
};

export type AndroidDownload = {
  version: string;
  app: AndroidAppDownload;
  managedPayload: ManagedPayloadDownload;
  interop: InteropPackageDownload;
  /** The publisher's adapted game. */
  game: AndroidGameRelease;
  /** The adapted game of each Android source, in ANDROID_SOURCES order. */
  games: { source: AndroidSource; game: AndroidGameRelease }[];
  mainlandUrl: string | null;
};

type Json = Record<string, unknown>;
const SHA256 = /^[a-f0-9]{64}$/;

function object(value: unknown): Json | null {
  return value && typeof value === 'object' && !Array.isArray(value)
    ? (value as Json)
    : null;
}

function positiveInteger(value: unknown): value is number {
  return Number.isSafeInteger(value) && (value as number) > 0;
}

function sha256(value: unknown): value is string {
  return typeof value === 'string' && SHA256.test(value);
}

function releaseUrlUnder(value: unknown, prefix: string): value is string {
  if (typeof value !== 'string') return false;
  let url: URL;
  try {
    url = new URL(value);
  } catch {
    return false;
  }
  const base = new URL(RELEASE_BASE_URL);
  const fullPrefix = `${base.pathname.replace(/\/$/, '')}${prefix}`;
  return (
    url.origin === base.origin &&
    !url.username &&
    !url.password &&
    !url.search &&
    !url.hash &&
    url.pathname.startsWith(fullPrefix) &&
    url.pathname.length > fullPrefix.length
  );
}

function artifact(value: unknown, prefix: string): Json | null {
  const record = object(value);
  return record &&
    releaseUrlUnder(record.url, prefix) &&
    positiveInteger(record.size) &&
    sha256(record.sha256)
    ? record
    : null;
}

const signed = (value: unknown): value is string =>
  typeof value === 'string' && value.trim() !== '';

const nonEmpty = (value: unknown): value is string =>
  typeof value === 'string' && value !== '';

function decodeAndroidGame(value: unknown): AndroidGameRelease | null {
  const game = object(value);
  return game &&
    nonEmpty(game.gameVersion) &&
    positiveInteger(game.versionCode) &&
    sha256(game.il2cppSha256) &&
    sha256(game.metadataSha256)
    ? {
        gameVersion: game.gameVersion,
        versionCode: game.versionCode,
        il2cppSha256: game.il2cppSha256,
        metadataSha256: game.metadataSha256
      }
    : null;
}

function decodeDesktopGame(value: unknown): DesktopGameRelease | null {
  const game = object(value);
  return game && nonEmpty(game.gameVersion) && sha256(game.managedSha256)
    ? { gameVersion: game.gameVersion, managedSha256: game.managedSha256 }
    : null;
}

export function decodeDesktopGameRelease(
  manifest: unknown,
  platformKey: string
): DesktopGameRelease | null {
  const downloads = object(object(manifest)?.downloads);
  return decodeDesktopGame(object(downloads?.[platformKey])?.game);
}

// Manifests without adaptations still support the publisher's game (ADR 0019).
export function decodeAndroidDownload(
  manifest: unknown,
  platformKey: string = DOWNLOAD_PLATFORM_KEYS.android
): AndroidDownload | null {
  const root = object(manifest);
  const version = root?.version;
  if (typeof version !== 'string' || !version) return null;
  const record = object(object(root?.downloads)?.[platformKey]);
  const app = artifact(record?.app, `/${version}/${platformKey}/app/`);
  const managedPayload = artifact(
    record?.managedPayload,
    `/${version}/${platformKey}/managed/`
  );
  const interop = artifact(record?.interop, '/android/interop/');
  const game = decodeAndroidGame(record?.game);
  const adaptations = object(record?.adaptations);
  const games = adaptations
    ? ANDROID_SOURCES.map((source) => ({
        source,
        game: decodeAndroidGame(object(adaptations[source])?.game)
      }))
    : [{ source: 'publisher' as const, game }];
  if (games.some(({ game: adapted }) => !adapted)) return null;
  if (
    !app ||
    !positiveInteger(app.versionCode) ||
    !sha256(app.signerSha256) ||
    !managedPayload ||
    !signed(managedPayload.signature) ||
    !interop ||
    !(interop.url as string).endsWith(
      `/android/interop/${interop.sha256}.zip`
    ) ||
    !sha256(interop.snapshotSha256) ||
    !signed(interop.signature) ||
    !game
  )
    return null;
  return {
    version,
    app: {
      url: app.url as string,
      size: app.size as number,
      sha256: app.sha256 as string,
      versionCode: app.versionCode,
      signerSha256: app.signerSha256
    },
    managedPayload: {
      url: managedPayload.url as string,
      size: managedPayload.size as number,
      sha256: managedPayload.sha256 as string,
      signature: managedPayload.signature
    },
    interop: {
      url: interop.url as string,
      size: interop.size as number,
      sha256: interop.sha256 as string,
      snapshotSha256: interop.snapshotSha256,
      signature: interop.signature
    },
    game,
    games: games as AndroidDownload['games'],
    mainlandUrl: decodeMainlandDownloadUrl(manifest, platformKey)
  };
}

// Separate objects let each watch replace its own Game Status atomically.
export const GAME_STATUS_PREFIX = 'game-status';
export const GAME_STATUS_KEY = 'latest.json';
export const ANDROID_GAME_STATUS_KEY = 'android.json';
const GAME_STATUS_BASE_URL = `https://cdn.bazaarplusplus.com/${GAME_STATUS_PREFIX}`;
export const GAME_STATUS_URL = `${GAME_STATUS_BASE_URL}/${GAME_STATUS_KEY}`;
export const ANDROID_GAME_STATUS_URL = `${GAME_STATUS_BASE_URL}/${ANDROID_GAME_STATUS_KEY}`;

export type GameStatus = Record<'macos' | 'windows', DesktopGameRelease>;

export function decodeGameStatus(payload: unknown): GameStatus | null {
  const status = object(payload);
  const platforms = object(status?.platforms);
  const macos = decodeDesktopGame(platforms?.macos);
  const windows = decodeDesktopGame(platforms?.windows);
  if (status?.schemaVersion !== 1 || !macos || !windows) return null;
  return { macos, windows };
}

export type AndroidGameStatus = AndroidGameRelease;

export function decodeAndroidGameStatus(
  payload: unknown
): AndroidGameStatus | null {
  const status = object(payload);
  return status?.schemaVersion === 1 ? decodeAndroidGame(status) : null;
}

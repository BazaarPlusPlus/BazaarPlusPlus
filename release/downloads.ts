// Browser-safe Product Release facts shared by the website, installer and writer.
export const RELEASE_BASE_URL = 'https://cdn.bazaarplusplus.com/installer';

export const DOWNLOAD_PLATFORM_KEYS = Object.freeze({
  windows: 'windows-x86_64',
  mac: 'darwin-aarch64',
  android: 'android-aarch64'
});

export type DownloadPlatform = keyof typeof DOWNLOAD_PLATFORM_KEYS;

// Each platform has its own Platform Release Manifest and may be ahead of the
// others. The Release Manifest at `latest.json` names the newest release every
// Lockstep Platform (Windows and macOS) promoted together; Android has only its
// Platform Release Manifest.
export const RELEASE_MANIFEST_PATH = 'latest.json';

export function platformManifestPath(platformKey: string): string {
  return `latest/${platformKey}.json`;
}

// The mod reads a separate endpoint of the same Platform Release Manifest.
// Both endpoints are published together from the complete release artifacts.
export function modManifestPath(platformKey: string): string {
  return `latest/mod/${platformKey}.json`;
}

// The Tauri updater expands `{{target}}-{{arch}}` to the platform key and
// falls through to the next endpoint on a non-2xx response, so the lockstep
// manifest remains the fallback while a platform file is missing.
export const UPDATER_ENDPOINTS = Object.freeze([
  `${RELEASE_BASE_URL}/latest/{{target}}-{{arch}}.json`,
  `${RELEASE_BASE_URL}/${RELEASE_MANIFEST_PATH}`
]);

// The mainland mirror address is published in each Platform Release Manifest
// (and the lockstep Release Manifest) as `downloads[platform].mainlandUrl`;
// the release writer records it from an explicit operator-supplied URL.
// Consumers read it here and never derive it.
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

// The Android Release APK: the official game APK with the loader, interop and
// the Mods of `builtForProductVersion` injected, signed by BPP. Every product
// version has its own, under `android/apk/`.
export type AndroidApkDownload = {
  url: string;
  size: number;
  sha256: string;
  versionCode: number;
  signerSha256: string;
  builtForProductVersion: string;
};

// The Managed Payload zip of one product version, minisign-signed with the
// Tauri updater key; the mod's self-update installs it.
export type ManagedPayloadDownload = {
  url: string;
  size: number;
  sha256: string;
  signature: string;
};

export type AndroidGameRelease = {
  gameVersion: string;
  versionCode: number;
  officialApkSha256: string;
};

export type AndroidDownload = {
  version: string;
  apk: AndroidApkDownload;
  managedPayload: ManagedPayloadDownload;
  game: AndroidGameRelease;
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

// True when `value` is an https URL below RELEASE_BASE_URL and `prefix`.
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
    typeof record.sha256 === 'string' &&
    SHA256.test(record.sha256)
    ? record
    : null;
}

// Reads `downloads[platformKey]` of an Android Platform Release Manifest: the
// APK, the Managed Payload zip and the game they were built for. Null when any
// part is missing or points outside the release origin; the mainland mirror is
// optional and decoded like the desktop one.
export function decodeAndroidDownload(
  manifest: unknown,
  platformKey: string = DOWNLOAD_PLATFORM_KEYS.android
): AndroidDownload | null {
  const root = object(manifest);
  const version = root?.version;
  if (typeof version !== 'string' || !version) return null;
  const record = object(object(root?.downloads)?.[platformKey]);
  const apk = artifact(record?.apk, '/android/apk/');
  const managedPayload = artifact(
    record?.managedPayload,
    `/${version}/${platformKey}/managed/`
  );
  const game = object(record?.game);
  if (
    !apk ||
    !positiveInteger(apk.versionCode) ||
    typeof apk.signerSha256 !== 'string' ||
    !SHA256.test(apk.signerSha256) ||
    typeof apk.builtForProductVersion !== 'string' ||
    !managedPayload ||
    typeof managedPayload.signature !== 'string' ||
    !managedPayload.signature.trim() ||
    !game ||
    typeof game.gameVersion !== 'string' ||
    !game.gameVersion ||
    !positiveInteger(game.versionCode) ||
    typeof game.officialApkSha256 !== 'string' ||
    !SHA256.test(game.officialApkSha256)
  )
    return null;
  return {
    version,
    apk: {
      url: apk.url as string,
      size: apk.size as number,
      sha256: apk.sha256 as string,
      versionCode: apk.versionCode,
      signerSha256: apk.signerSha256,
      builtForProductVersion: apk.builtForProductVersion
    },
    managedPayload: {
      url: managedPayload.url as string,
      size: managedPayload.size as number,
      sha256: managedPayload.sha256 as string,
      signature: managedPayload.signature
    },
    game: {
      gameVersion: game.gameVersion,
      versionCode: game.versionCode,
      officialApkSha256: game.officialApkSha256
    },
    mainlandUrl: decodeMainlandDownloadUrl(manifest, platformKey)
  };
}

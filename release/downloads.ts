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

// The Android App APK of one product version (docs/adr/0012-android-app.md),
// signed by the BPP Android release key; `versionCode` follows the product
// version.
export type AndroidAppDownload = {
  url: string;
  size: number;
  sha256: string;
  versionCode: number;
  signerSha256: string;
};

// The Managed Payload zip of one product version, minisign-signed with the
// Tauri updater key; the mod's self-update installs it.
export type ManagedPayloadDownload = {
  url: string;
  size: number;
  sha256: string;
  signature: string;
};

// The Interop Package the release is adapted with, content-addressed by the
// zip's sha256 under `android/interop/`. `signature` is the base64
// SHA256withRSA signature of its interop statement; the Android App verifies
// it before loading the zip.
export type InteropPackageDownload = {
  url: string;
  size: number;
  sha256: string;
  snapshotSha256: string;
  signature: string;
};

// The official game the release is adapted to: its identity and its Game
// Code Hash (`libil2cpp.so` and `global-metadata.dat`).
export type AndroidGameRelease = {
  gameVersion: string;
  versionCode: number;
  il2cppSha256: string;
  metadataSha256: string;
};

export type AndroidDownload = {
  version: string;
  app: AndroidAppDownload;
  managedPayload: ManagedPayloadDownload;
  interop: InteropPackageDownload;
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

function sha256(value: unknown): value is string {
  return typeof value === 'string' && SHA256.test(value);
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
    sha256(record.sha256)
    ? record
    : null;
}

const signed = (value: unknown): value is string =>
  typeof value === 'string' && value.trim() !== '';

// Reads `downloads[platformKey]` of an Android Platform Release Manifest: the
// Android App APK, the Managed Payload zip, the Interop Package and the game
// they are adapted to. Null when any part is missing or points outside the
// release origin, and for the ADR 0010 shape (`apk`, no `app`); the mainland
// mirror is optional and decoded like the desktop one.
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
  const game = object(record?.game);
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
    !game ||
    typeof game.gameVersion !== 'string' ||
    !game.gameVersion ||
    !positiveInteger(game.versionCode) ||
    !sha256(game.il2cppSha256) ||
    !sha256(game.metadataSha256)
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
    game: {
      gameVersion: game.gameVersion,
      versionCode: game.versionCode,
      il2cppSha256: game.il2cppSha256,
      metadataSha256: game.metadataSha256
    },
    mainlandUrl: decodeMainlandDownloadUrl(manifest, platformKey)
  };
}

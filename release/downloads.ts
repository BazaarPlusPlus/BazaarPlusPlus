// Browser-safe Product Release facts shared by the website, installer and writer.
export const RELEASE_BASE_URL = 'https://bppinstaller.bazaarplusplus.com';

export const DOWNLOAD_PLATFORM_KEYS = Object.freeze({
  windows: 'windows-x86_64',
  mac: 'darwin-aarch64'
});

export type DownloadPlatform = keyof typeof DOWNLOAD_PLATFORM_KEYS;

// Each platform has its own Platform Release Manifest and may be ahead of the
// other. The Release Manifest at `latest.json` names the newest release every
// platform promoted together; clients built before the per-platform endpoint
// read only that file.
export const RELEASE_MANIFEST_PATH = 'latest.json';

export function platformManifestPath(platformKey: string): string {
  return `latest/${platformKey}.json`;
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

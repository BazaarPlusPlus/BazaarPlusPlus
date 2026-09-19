// Browser-safe Product Release facts shared by the website, installer and writer.
export const RELEASE_BASE_URL = 'https://bppinstaller.bazaarplusplus.com';
export const MAINLAND_DOWNLOAD_BASE = 'https://cauyxy.lanzout.com';

export const DOWNLOAD_PLATFORM_KEYS = Object.freeze({
  windows: 'windows-x86_64',
  mac: 'darwin-aarch64'
});

export type DownloadPlatform = keyof typeof DOWNLOAD_PLATFORM_KEYS;

export function buildMainlandDownloadUrl(
  platform: DownloadPlatform,
  version: string
): string {
  const platformSlug = platform === 'windows' ? 'win' : 'mac';
  return `${MAINLAND_DOWNLOAD_BASE}/bpp${platformSlug}${version.replaceAll('.', '')}`;
}

export const MAINLAND_DOWNLOAD_BASE = 'https://cauyxy.lanzout.com';

export type MainlandDownloadPlatform = 'windows' | 'mac';

export function detectMainlandDownloadPlatform(
  userAgent: string
): MainlandDownloadPlatform {
  return userAgent.includes('Windows') ? 'windows' : 'mac';
}

export function buildMainlandDownloadUrl(
  platform: MainlandDownloadPlatform,
  version: string
): string {
  const platformSlug = platform === 'windows' ? 'win' : 'mac';
  return `${MAINLAND_DOWNLOAD_BASE}/bpp${platformSlug}${version.replaceAll('.', '')}`;
}

export function getMainlandDownloadUrl(version: string): string {
  const userAgent = typeof navigator === 'undefined' ? '' : navigator.userAgent;
  return buildMainlandDownloadUrl(
    detectMainlandDownloadPlatform(userAgent),
    version
  );
}

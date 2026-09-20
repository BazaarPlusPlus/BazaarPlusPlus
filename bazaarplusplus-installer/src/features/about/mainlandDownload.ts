import {
  buildMainlandDownloadUrl,
  type DownloadPlatform as MainlandDownloadPlatform
} from '../../../../release/downloads';

export function detectMainlandDownloadPlatform(
  userAgent: string
): MainlandDownloadPlatform {
  return userAgent.includes('Windows') ? 'windows' : 'mac';
}

export function getMainlandDownloadUrl(version: string): string {
  const userAgent = typeof navigator === 'undefined' ? '' : navigator.userAgent;
  return buildMainlandDownloadUrl(
    detectMainlandDownloadPlatform(userAgent),
    version
  );
}

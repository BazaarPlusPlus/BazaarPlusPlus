import {
  RELEASE_BASE_URL as INSTALLER_BASE,
  DOWNLOAD_PLATFORM_KEYS,
  buildMainlandDownloadUrl,
  type DownloadPlatform,
} from '../../../../release/downloads';

export { INSTALLER_BASE };
export type { DownloadPlatform } from '../../../../release/downloads';
export const GITHUB_RELEASE_URL =
  'https://github.com/BazaarPlusPlus/BazaarPlusPlus/releases/latest';

export type LatestInstaller = {
  version: string;
  downloads: Record<
    DownloadPlatform,
    {
      downloadUrl: string;
      mainlandDownloadUrl: string;
    }
  >;
};

export type InstallerManifestTransport = {
  load(options?: { signal?: AbortSignal }): Promise<unknown>;
};

type HttpTransportOptions = {
  fetchImpl?: typeof fetch;
};

export function createInstallerManifestHttpTransport(
  options: HttpTransportOptions = {}
): InstallerManifestTransport {
  const manifestUrl = `${INSTALLER_BASE}/latest.json`;

  return {
    async load(loadOptions = {}) {
      const response = await (options.fetchImpl ?? globalThis.fetch)(manifestUrl, {
        headers: { Accept: 'application/json' },
        signal: loadOptions.signal,
      });
      if (!response.ok) {
        throw new Error(`latest.json responded with ${response.status}`);
      }
      return response.json();
    },
  };
}

function decodeVersion(payload: unknown): string {
  if (
    payload == null ||
    typeof payload !== 'object' ||
    typeof (payload as { version?: unknown }).version !== 'string' ||
    !(payload as { version: string }).version
  ) {
    throw new Error('latest.json missing string `version`');
  }
  return (payload as { version: string }).version;
}

function decodeDownloadUrl(payload: unknown, platform: string, version: string): string {
  const downloads = (payload as { downloads?: unknown }).downloads;
  const record =
    downloads && typeof downloads === 'object'
      ? (downloads as Record<string, unknown>)[platform]
      : undefined;
  const value =
    record && typeof record === 'object' ? (record as { url?: unknown }).url : undefined;
  if (typeof value !== 'string') throw new Error(`latest.json missing ${platform} download`);
  const url = new URL(value);
  if (
    url.origin !== INSTALLER_BASE ||
    url.username ||
    url.password ||
    url.hash ||
    url.search ||
    !url.pathname.startsWith(`/${version}/${platform}/installer/`)
  ) {
    throw new Error(`latest.json has an invalid ${platform} download`);
  }
  return value;
}

export async function loadLatestInstaller(
  transport: InstallerManifestTransport,
  options: { signal?: AbortSignal } = {}
): Promise<LatestInstaller> {
  const payload = await transport.load({ signal: options.signal });
  const version = decodeVersion(payload);
  return {
    version,
    downloads: {
      windows: {
        downloadUrl: decodeDownloadUrl(payload, DOWNLOAD_PLATFORM_KEYS.windows, version),
        mainlandDownloadUrl: buildMainlandDownloadUrl('windows', version),
      },
      mac: {
        downloadUrl: decodeDownloadUrl(payload, DOWNLOAD_PLATFORM_KEYS.mac, version),
        mainlandDownloadUrl: buildMainlandDownloadUrl('mac', version),
      },
    },
  };
}

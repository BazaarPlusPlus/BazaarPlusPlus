export const INSTALLER_BASE = 'https://bppinstaller.bazaarplusplus.com';
export const MAINLAND_DOWNLOAD_BASE = 'https://cauyxy.lanzout.com';
export const GITHUB_RELEASE_URL = 'https://github.com/BazaarPlusPlus/BazaarPlusPlus/releases/latest';

export type DownloadPlatform = 'windows' | 'mac';

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

function buildDownloadUrl(platform: DownloadPlatform, version: string): string {
  if (platform === 'windows') {
    return `${INSTALLER_BASE}/${version}/windows-x86_64/installer/BazaarPlusPlus_${version}_x64-setup.exe`;
  }
  return `${INSTALLER_BASE}/${version}/darwin-aarch64/installer/BazaarPlusPlus_${version}_aarch64.dmg`;
}

function buildMainlandDownloadUrl(platform: DownloadPlatform, version: string): string {
  const platformSlug = platform === 'windows' ? 'win' : 'mac';
  return `${MAINLAND_DOWNLOAD_BASE}/bpp${platformSlug}${version.replaceAll('.', '')}`;
}

export async function loadLatestInstaller(
  transport: InstallerManifestTransport,
  options: { signal?: AbortSignal } = {}
): Promise<LatestInstaller> {
  const version = decodeVersion(await transport.load({ signal: options.signal }));
  return {
    version,
    downloads: {
      windows: {
        downloadUrl: buildDownloadUrl('windows', version),
        mainlandDownloadUrl: buildMainlandDownloadUrl('windows', version),
      },
      mac: {
        downloadUrl: buildDownloadUrl('mac', version),
        mainlandDownloadUrl: buildMainlandDownloadUrl('mac', version),
      },
    },
  };
}

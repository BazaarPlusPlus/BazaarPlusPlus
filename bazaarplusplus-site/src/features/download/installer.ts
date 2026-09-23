import {
  RELEASE_BASE_URL as INSTALLER_BASE,
  DOWNLOAD_PLATFORM_KEYS,
  decodeMainlandDownloadUrl,
  platformManifestPath,
  type DownloadPlatform,
} from '../../../../release/downloads';

export { INSTALLER_BASE };
export type { DownloadPlatform } from '../../../../release/downloads';
export const GITHUB_RELEASE_URL =
  'https://github.com/BazaarPlusPlus/BazaarPlusPlus/releases/latest';

export type PlatformDownload = {
  version: string;
  downloadUrl: string;
  /** Published in the Platform Release Manifest; null when this release has no mirror. */
  mainlandDownloadUrl: string | null;
};

/** Each platform resolves from its own manifest; null means that platform failed. */
export type LatestInstaller = {
  downloads: Record<DownloadPlatform, PlatformDownload | null>;
};

export type InstallerManifestTransport = {
  load(path: string, options?: { signal?: AbortSignal }): Promise<unknown>;
};

type HttpTransportOptions = {
  fetchImpl?: typeof fetch;
};

export function createInstallerManifestHttpTransport(
  options: HttpTransportOptions = {}
): InstallerManifestTransport {
  return {
    async load(path, loadOptions = {}) {
      const response = await (options.fetchImpl ?? globalThis.fetch)(`${INSTALLER_BASE}/${path}`, {
        headers: { Accept: 'application/json' },
        signal: loadOptions.signal,
      });
      if (!response.ok) {
        throw new Error(`${path} responded with ${response.status}`);
      }
      return response.json();
    },
  };
}

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
    record && typeof record === 'object' ? (record as { url?: unknown }).url : undefined;
  if (typeof value !== 'string') throw new Error(`${path} missing ${platformKey} download`);
  const url = new URL(value);
  if (
    url.origin !== INSTALLER_BASE ||
    url.username ||
    url.password ||
    url.hash ||
    url.search ||
    !url.pathname.startsWith(`/${version}/${platformKey}/installer/`)
  ) {
    throw new Error(`${path} has an invalid ${platformKey} download`);
  }
  return value;
}

async function loadPlatformDownload(
  transport: InstallerManifestTransport,
  platform: DownloadPlatform,
  signal: AbortSignal | undefined
): Promise<PlatformDownload> {
  const platformKey = DOWNLOAD_PLATFORM_KEYS[platform];
  const path = platformManifestPath(platformKey);
  const payload = await transport.load(path, { signal });
  const version = decodeVersion(payload, path);
  return {
    version,
    downloadUrl: decodeDownloadUrl(payload, path, platformKey, version),
    mainlandDownloadUrl: decodeMainlandDownloadUrl(payload, platformKey),
  };
}

export async function loadLatestInstaller(
  transport: InstallerManifestTransport,
  options: { signal?: AbortSignal } = {}
): Promise<LatestInstaller> {
  const platforms: DownloadPlatform[] = ['windows', 'mac'];
  const settled = await Promise.allSettled(
    platforms.map((platform) => loadPlatformDownload(transport, platform, options.signal))
  );
  const downloads = { windows: null, mac: null } as LatestInstaller['downloads'];
  const failures: unknown[] = [];
  settled.forEach((result, index) => {
    const platform = platforms[index];
    if (result.status === 'fulfilled') downloads[platform] = result.value;
    else failures.push(result.reason);
  });
  if (failures.length === platforms.length) {
    throw failures[0] instanceof Error ? failures[0] : new Error(String(failures[0]));
  }
  return { downloads };
}

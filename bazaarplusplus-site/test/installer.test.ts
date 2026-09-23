import { describe, expect, test, vi } from 'vitest';
import macFixture from '../../release/fixtures/latest/darwin-aarch64.json';
import windowsFixture from '../../release/fixtures/latest/windows-x86_64.json';

import {
  createInstallerManifestHttpTransport,
  GITHUB_RELEASE_URL,
  INSTALLER_BASE,
  loadLatestInstaller,
  type InstallerManifestTransport,
} from '../src/features/download/installer';

const WINDOWS_PATH = 'latest/windows-x86_64.json';
const MAC_PATH = 'latest/darwin-aarch64.json';

type Manifests = Record<string, unknown>;

/** A transport serving one payload per manifest path; an Error value rejects that path. */
function makeTransport(manifests: Manifests): InstallerManifestTransport {
  return {
    load: vi.fn(async (path: string) => {
      const payload = manifests[path];
      if (payload instanceof Error) throw payload;
      if (!(path in manifests)) throw new Error(`${path} responded with 404`);
      return payload;
    }),
  };
}

const bothPlatforms = (): Manifests => ({
  [WINDOWS_PATH]: structuredClone(windowsFixture),
  [MAC_PATH]: structuredClone(macFixture),
});

type MirrorRecord = { mainlandUrl?: unknown };
function withWindowsMirror(mainlandUrl: unknown): Manifests {
  const manifests = bothPlatforms();
  const windows = manifests[WINDOWS_PATH] as typeof windowsFixture;
  const record = windows.downloads['windows-x86_64'] as MirrorRecord;
  if (mainlandUrl === undefined) delete record.mainlandUrl;
  else record.mainlandUrl = mainlandUrl;
  return manifests;
}

describe('latest installer interface', () => {
  test('resolves each platform from its own Platform Release Manifest', async () => {
    const installer = await loadLatestInstaller(makeTransport(bothPlatforms()));

    expect(installer).toEqual({
      downloads: {
        windows: {
          version: '3.1.2',
          downloadUrl: windowsFixture.downloads['windows-x86_64'].url,
          mainlandDownloadUrl: windowsFixture.downloads['windows-x86_64'].mainlandUrl,
        },
        mac: {
          version: '3.1.1',
          downloadUrl: macFixture.downloads['darwin-aarch64'].url,
          mainlandDownloadUrl: macFixture.downloads['darwin-aarch64'].mainlandUrl,
        },
      },
    });
    expect(installer.downloads.windows?.version).not.toBe(installer.downloads.mac?.version);
    expect(GITHUB_RELEASE_URL).toBe(
      'https://github.com/BazaarPlusPlus/BazaarPlusPlus/releases/latest'
    );
  });

  test.each([
    ['absent', undefined],
    ['not https', 'http://cauyxy.lanzout.com/bppwin312'],
    ['carrying credentials', 'https://user:secret@cauyxy.lanzout.com/bppwin312'],
    ['not a URL', 'bppwin312'],
  ])(
    'a release whose mirror address is %s keeps its primary download and no mirror',
    async (_, mainlandUrl) => {
      const installer = await loadLatestInstaller(makeTransport(withWindowsMirror(mainlandUrl)));
      expect(installer.downloads.windows).toEqual({
        version: '3.1.2',
        downloadUrl: windowsFixture.downloads['windows-x86_64'].url,
        mainlandDownloadUrl: null,
      });
      expect(installer.downloads.mac?.mainlandDownloadUrl).toBe(
        macFixture.downloads['darwin-aarch64'].mainlandUrl
      );
    }
  );

  test.each([
    ['a failing manifest request', new Error('unavailable')],
    ['a manifest without a version', { ver: '3.1.2' }],
    ['a manifest with an empty version', { version: '' }],
    ['a manifest without its own download', { version: '3.1.2', downloads: {} }],
    [
      'a download outside the release origin',
      {
        version: '3.1.2',
        downloads: { 'windows-x86_64': { url: 'https://untrusted.example/setup.exe' } },
      },
    ],
    [
      'a download from another version',
      {
        version: '3.1.2',
        downloads: {
          'windows-x86_64': {
            url: `${INSTALLER_BASE}/3.1.1/windows-x86_64/installer/BazaarPlusPlus_3.1.1_x64-setup.exe`,
          },
        },
      },
    ],
  ])('%s degrades only that platform', async (_, windowsPayload) => {
    const installer = await loadLatestInstaller(
      makeTransport({ ...bothPlatforms(), [WINDOWS_PATH]: windowsPayload })
    );
    expect(installer.downloads.windows).toBeNull();
    expect(installer.downloads.mac).toMatchObject({ version: '3.1.1' });
  });

  test('fails as a whole only when every platform fails', async () => {
    await expect(
      loadLatestInstaller(
        makeTransport({ [WINDOWS_PATH]: new Error('windows down'), [MAC_PATH]: { version: '' } })
      )
    ).rejects.toThrow(/windows down/);
    await expect(loadLatestInstaller(makeTransport({}))).rejects.toThrow(/404/);
  });

  test('uses the actual installer filename from the release instead of guessing it', async () => {
    const manifests = bothPlatforms();
    const windows = manifests[WINDOWS_PATH] as typeof windowsFixture;
    windows.downloads['windows-x86_64'].url =
      `${INSTALLER_BASE}/3.1.2/windows-x86_64/installer/actual-build.exe`;
    const installer = await loadLatestInstaller(makeTransport(manifests));
    expect(installer.downloads.windows?.downloadUrl).toBe(windows.downloads['windows-x86_64'].url);
  });

  test('requests every platform manifest and passes cancellation through the transport seam', async () => {
    const transport = makeTransport(bothPlatforms());
    const controller = new AbortController();

    await loadLatestInstaller(transport, { signal: controller.signal });

    expect(transport.load).toHaveBeenCalledTimes(2);
    expect(transport.load).toHaveBeenCalledWith(WINDOWS_PATH, { signal: controller.signal });
    expect(transport.load).toHaveBeenCalledWith(MAC_PATH, { signal: controller.signal });
  });
});

describe('installer manifest HTTP adapter', () => {
  test('loads unknown JSON from the platform manifest location under the release origin', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ version: '3.1.2' }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      })
    );
    const transport = createInstallerManifestHttpTransport({ fetchImpl });
    const controller = new AbortController();

    await expect(transport.load(WINDOWS_PATH, { signal: controller.signal })).resolves.toEqual({
      version: '3.1.2',
    });
    expect(fetchImpl).toHaveBeenCalledWith(
      `${INSTALLER_BASE}/${WINDOWS_PATH}`,
      expect.objectContaining({
        headers: { Accept: 'application/json' },
        signal: controller.signal,
      })
    );
  });

  test('reports a failed manifest response by path', async () => {
    const transport = createInstallerManifestHttpTransport({
      fetchImpl: vi.fn().mockResolvedValue(new Response('boom', { status: 503 })),
    });

    await expect(transport.load(MAC_PATH)).rejects.toThrow(
      /darwin-aarch64\.json responded with 503/
    );
  });
});

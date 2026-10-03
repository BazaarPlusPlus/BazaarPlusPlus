import { afterEach, describe, expect, test, vi } from 'vitest';
import macFixture from '../../release/fixtures/latest/darwin-aarch64.json';
import windowsFixture from '../../release/fixtures/latest/windows-x86_64.json';

import { RELEASE_BASE_URL } from '../../release/downloads';
import { GITHUB_RELEASE_URL, loadLatestInstaller } from '../src/features/download/installer';

const WINDOWS_PATH = 'latest/windows-x86_64.json';
const MAC_PATH = 'latest/darwin-aarch64.json';

type Manifests = Record<string, unknown>;

/**
 * Stubs global `fetch` with one payload per manifest path under the release origin; an Error
 * value rejects that request, and an unknown path answers 404.
 */
function stubManifests(manifests: Record<string, unknown>) {
  const fetchStub = vi.fn(async (url: string) => {
    const path = url.startsWith(`${RELEASE_BASE_URL}/`)
      ? url.slice(RELEASE_BASE_URL.length + 1)
      : url;
    const payload = manifests[path];
    if (payload instanceof Error) throw payload;
    if (!(path in manifests)) return new Response('missing', { status: 404 });
    return new Response(JSON.stringify(payload), {
      status: 200,
      headers: { 'content-type': 'application/json' },
    });
  });
  vi.stubGlobal('fetch', fetchStub);
  return fetchStub;
}

function load(manifests: Manifests) {
  stubManifests(manifests);
  return loadLatestInstaller(new AbortController().signal);
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
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  test('resolves each platform from its own Platform Release Manifest', async () => {
    const installer = await load(bothPlatforms());

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
      const installer = await load(withWindowsMirror(mainlandUrl));
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
            url: `${RELEASE_BASE_URL}/3.1.1/windows-x86_64/installer/BazaarPlusPlus_3.1.1_x64-setup.exe`,
          },
        },
      },
    ],
  ])('%s degrades only that platform', async (_, windowsPayload) => {
    const installer = await load({ ...bothPlatforms(), [WINDOWS_PATH]: windowsPayload });
    expect(installer.downloads.windows).toBeNull();
    expect(installer.downloads.mac).toMatchObject({ version: '3.1.1' });
  });

  test('fails as a whole only when every platform fails', async () => {
    await expect(
      load({ [WINDOWS_PATH]: new Error('windows down'), [MAC_PATH]: { version: '' } })
    ).rejects.toThrow(/windows down/);
    await expect(load({})).rejects.toThrow(/404/);
  });

  test('uses the actual installer filename from the release instead of guessing it', async () => {
    const manifests = bothPlatforms();
    const windows = manifests[WINDOWS_PATH] as typeof windowsFixture;
    windows.downloads['windows-x86_64'].url =
      `${RELEASE_BASE_URL}/3.1.2/windows-x86_64/installer/actual-build.exe`;
    const installer = await load(manifests);
    expect(installer.downloads.windows?.downloadUrl).toBe(windows.downloads['windows-x86_64'].url);
  });

  test('requests every platform manifest from the release origin and passes cancellation through', async () => {
    const fetchStub = stubManifests(bothPlatforms());
    const controller = new AbortController();

    await loadLatestInstaller(controller.signal);

    expect(fetchStub).toHaveBeenCalledTimes(2);
    for (const path of [WINDOWS_PATH, MAC_PATH]) {
      expect(fetchStub).toHaveBeenCalledWith(`${RELEASE_BASE_URL}/${path}`, {
        headers: { Accept: 'application/json' },
        signal: controller.signal,
      });
    }
  });
});

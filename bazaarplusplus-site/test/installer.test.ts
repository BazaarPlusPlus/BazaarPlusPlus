import { afterEach, describe, expect, test, vi } from 'vite-plus/test';
import androidGameStatusFixture from '../../release/fixtures/android-game-status.json';
import gameStatusFixture from '../../release/fixtures/game-status.json';
import androidFixture from '../../release/fixtures/latest/android-aarch64.json';
import macFixture from '../../release/fixtures/latest/darwin-aarch64.json';
import windowsFixture from '../../release/fixtures/latest/windows-x86_64.json';

import {
  ANDROID_GAME_STATUS_URL,
  GAME_STATUS_URL,
  RELEASE_BASE_URL,
  decodeAndroidGameStatus,
  decodeGameStatus
} from '../../release/downloads';
import {
  GITHUB_RELEASE_URL,
  loadAndroidGameStatus,
  loadAndroidRelease,
  loadGameStatus,
  loadLatestInstaller,
  sameAndroidGame,
  sameDesktopGame
} from '../src/features/download/installer';

const WINDOWS_PATH = 'latest/windows-x86_64.json';
const MAC_PATH = 'latest/darwin-aarch64.json';
const ANDROID_PATH = 'latest/android-aarch64.json';

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
      headers: { 'content-type': 'application/json' }
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
  [MAC_PATH]: structuredClone(macFixture)
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
          mainlandDownloadUrl:
            windowsFixture.downloads['windows-x86_64'].mainlandUrl,
          game: windowsFixture.downloads['windows-x86_64'].game
        },
        mac: {
          version: '3.1.1',
          downloadUrl: macFixture.downloads['darwin-aarch64'].url,
          mainlandDownloadUrl:
            macFixture.downloads['darwin-aarch64'].mainlandUrl,
          game: macFixture.downloads['darwin-aarch64'].game
        }
      }
    });
    expect(installer.downloads.windows?.version).not.toBe(
      installer.downloads.mac?.version
    );
    expect(GITHUB_RELEASE_URL).toBe(
      'https://github.com/BazaarPlusPlus/BazaarPlusPlus/releases/latest'
    );
  });

  test.each([
    ['absent', undefined],
    ['not https', 'http://cauyxy.lanzout.com/bppwin312'],
    [
      'carrying credentials',
      'https://user:secret@cauyxy.lanzout.com/bppwin312'
    ],
    ['not a URL', 'bppwin312']
  ])(
    'a release whose mirror address is %s keeps its primary download and no mirror',
    async (_, mainlandUrl) => {
      const installer = await load(withWindowsMirror(mainlandUrl));
      expect(installer.downloads.windows).toEqual({
        version: '3.1.2',
        downloadUrl: windowsFixture.downloads['windows-x86_64'].url,
        mainlandDownloadUrl: null,
        game: windowsFixture.downloads['windows-x86_64'].game
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
    [
      'a manifest without its own download',
      { version: '3.1.2', downloads: {} }
    ],
    [
      'a download outside the release origin',
      {
        version: '3.1.2',
        downloads: {
          'windows-x86_64': { url: 'https://untrusted.example/setup.exe' }
        }
      }
    ],
    [
      'a download outside the installer/ prefix of the release origin',
      {
        version: '3.1.2',
        downloads: {
          'windows-x86_64': {
            url: 'https://cdn.bazaarplusplus.com/3.1.2/windows-x86_64/installer/setup.exe'
          }
        }
      }
    ],
    [
      'a download from another version',
      {
        version: '3.1.2',
        downloads: {
          'windows-x86_64': {
            url: `${RELEASE_BASE_URL}/3.1.1/windows-x86_64/installer/BazaarPlusPlus_3.1.1_x64-setup.exe`
          }
        }
      }
    ]
  ])('%s degrades only that platform', async (_, windowsPayload) => {
    const installer = await load({
      ...bothPlatforms(),
      [WINDOWS_PATH]: windowsPayload
    });
    expect(installer.downloads.windows).toBeNull();
    expect(installer.downloads.mac).toMatchObject({ version: '3.1.1' });
  });

  test('fails as a whole only when every platform fails', async () => {
    await expect(
      load({
        [WINDOWS_PATH]: new Error('windows down'),
        [MAC_PATH]: { version: '' }
      })
    ).rejects.toThrow(/windows down/);
    await expect(load({})).rejects.toThrow(/404/);
  });

  test('uses the actual installer filename from the release instead of guessing it', async () => {
    const manifests = bothPlatforms();
    const windows = manifests[WINDOWS_PATH] as typeof windowsFixture;
    windows.downloads['windows-x86_64'].url =
      `${RELEASE_BASE_URL}/3.1.2/windows-x86_64/installer/actual-build.exe`;
    const installer = await load(manifests);
    expect(installer.downloads.windows?.downloadUrl).toBe(
      windows.downloads['windows-x86_64'].url
    );
  });

  test('requests every platform manifest from the release origin and passes cancellation through', async () => {
    const fetchStub = stubManifests(bothPlatforms());
    const controller = new AbortController();

    await loadLatestInstaller(controller.signal);

    expect(fetchStub).toHaveBeenCalledTimes(2);
    for (const path of [WINDOWS_PATH, MAC_PATH]) {
      expect(fetchStub).toHaveBeenCalledWith(`${RELEASE_BASE_URL}/${path}`, {
        headers: { Accept: 'application/json' },
        signal: controller.signal
      });
    }
  });
});

describe('Android release interface', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  test('reads the Android App, its game, Managed Payload and Interop Package from its own manifest', async () => {
    const fetchStub = stubManifests({
      [ANDROID_PATH]: structuredClone(androidFixture)
    });
    const controller = new AbortController();
    const release = await loadAndroidRelease(controller.signal);
    const record = androidFixture.downloads['android-aarch64'];
    expect(release).toEqual({
      version: androidFixture.version,
      app: record.app,
      managedPayload: record.managedPayload,
      interop: record.interop,
      game: record.game,
      games: [
        { source: 'publisher', game: record.adaptations.publisher.game },
        { source: 'play', game: record.adaptations.play.game }
      ],
      mainlandUrl: record.mainlandUrl
    });
    expect(fetchStub).toHaveBeenCalledTimes(1);
    expect(fetchStub).toHaveBeenCalledWith(
      `${RELEASE_BASE_URL}/${ANDROID_PATH}`,
      {
        headers: { Accept: 'application/json' },
        signal: controller.signal
      }
    );
  });

  test('a manifest promoted before ADR 0019 adapts the publisher game only', async () => {
    const manifest = structuredClone(androidFixture);
    const record: Record<string, unknown> =
      manifest.downloads['android-aarch64'];
    delete record.adaptations;
    stubManifests({ [ANDROID_PATH]: manifest });
    const release = await loadAndroidRelease(new AbortController().signal);
    expect(release.games).toEqual([
      {
        source: 'publisher',
        game: androidFixture.downloads['android-aarch64'].game
      }
    ]);
  });

  test('the desktop installer load never requests the Android manifest', async () => {
    const fetchStub = stubManifests({
      [ANDROID_PATH]: structuredClone(androidFixture)
    });
    await expect(
      loadLatestInstaller(new AbortController().signal)
    ).rejects.toThrow(/404/);
    expect(fetchStub).not.toHaveBeenCalledWith(
      `${RELEASE_BASE_URL}/${ANDROID_PATH}`,
      expect.anything()
    );
  });

  test.each([
    ['a missing manifest', {}],
    ['a desktop manifest', { [ANDROID_PATH]: structuredClone(macFixture) }],
    [
      'an App outside the release origin',
      {
        [ANDROID_PATH]: (() => {
          const manifest = structuredClone(androidFixture);
          manifest.downloads['android-aarch64'].app.url =
            'https://untrusted.example/a.apk';
          return manifest;
        })()
      }
    ],
    [
      'the injected APK of ADR 0010',
      {
        [ANDROID_PATH]: (() => {
          const manifest = structuredClone(androidFixture);
          const record: Record<string, unknown> =
            manifest.downloads['android-aarch64'];
          record.apk = record.app;
          delete record.app;
          return manifest;
        })()
      }
    ],
    [
      'an adaptation without its game',
      {
        [ANDROID_PATH]: (() => {
          const manifest = structuredClone(androidFixture);
          const play: Record<string, unknown> =
            manifest.downloads['android-aarch64'].adaptations.play;
          delete play.game;
          return manifest;
        })()
      }
    ]
  ])('%s fails the Android release', async (_, manifests) => {
    stubManifests(manifests);
    await expect(
      loadAndroidRelease(new AbortController().signal)
    ).rejects.toThrow(/latest\/android-aarch64\.json/);
  });
});

describe('Game Status', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  test('decodes the online game and its game code identity of each desktop platform', () => {
    expect(decodeGameStatus(gameStatusFixture)).toEqual({
      macos: gameStatusFixture.platforms.macos,
      windows: gameStatusFixture.platforms.windows
    });
  });

  test('decodes the online Android game the APK watch writes', () => {
    const { schemaVersion: _, ...game } = androidGameStatusFixture;
    expect(decodeAndroidGameStatus(androidGameStatusFixture)).toEqual(game);
  });

  test.each([
    ['null', null],
    ['another schema', { ...gameStatusFixture, schemaVersion: 2 }],
    ['a missing platform', { ...gameStatusFixture, platforms: { macos: {} } }],
    [
      'a platform without its Managed digest',
      {
        ...gameStatusFixture,
        platforms: {
          ...gameStatusFixture.platforms,
          windows: {
            gameVersion: gameStatusFixture.platforms.windows.gameVersion
          }
        }
      }
    ]
  ])('refuses %s', (_name, payload) => {
    expect(decodeGameStatus(payload)).toBeNull();
  });

  test.each([
    ['another schema', { ...androidGameStatusFixture, schemaVersion: 2 }],
    [
      'a missing hash',
      { ...androidGameStatusFixture, metadataSha256: undefined }
    ]
  ])('refuses an Android Game Status with %s', (_name, payload) => {
    expect(decodeAndroidGameStatus(payload)).toBeNull();
  });

  // The shared fixtures: macOS runs its adapted game, Windows has a newer game, and Android has a
  // content-only update of its adapted game.
  test('compares game code identity, not the game version', () => {
    const status = decodeGameStatus(gameStatusFixture)!;
    expect(
      sameDesktopGame(macFixture.downloads['darwin-aarch64'].game, status.macos)
    ).toBe(true);
    expect(
      sameDesktopGame(
        windowsFixture.downloads['windows-x86_64'].game,
        status.windows
      )
    ).toBe(false);
    const android = androidFixture.downloads['android-aarch64'].game;
    const online = decodeAndroidGameStatus(androidGameStatusFixture)!;
    expect(online.gameVersion).not.toBe(android.gameVersion);
    expect(sameAndroidGame(android, online)).toBe(true);
    expect(
      sameAndroidGame(android, { ...online, il2cppSha256: 'a'.repeat(64) })
    ).toBe(false);
  });

  test('fails on a response it cannot use', async () => {
    const fetchStub = vi.fn(async () => new Response('', { status: 404 }));
    vi.stubGlobal('fetch', fetchStub);
    await expect(loadGameStatus(new AbortController().signal)).rejects.toThrow(
      /404/
    );
    expect(fetchStub).toHaveBeenCalledWith(GAME_STATUS_URL, expect.anything());
    await expect(
      loadAndroidGameStatus(new AbortController().signal)
    ).rejects.toThrow(/404/);
    expect(fetchStub).toHaveBeenCalledWith(
      ANDROID_GAME_STATUS_URL,
      expect.anything()
    );
  });
});

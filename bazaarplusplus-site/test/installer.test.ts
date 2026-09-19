import { describe, expect, test, vi } from 'vitest';
import releaseFixture from '../../release/fixtures/latest.json';

import {
  createInstallerManifestHttpTransport,
  GITHUB_RELEASE_URL,
  INSTALLER_BASE,
  loadLatestInstaller,
  type InstallerManifestTransport,
} from '../src/features/download/installer';

function makeTransport(payload: unknown): InstallerManifestTransport {
  return { load: vi.fn().mockResolvedValue(payload) };
}

describe('latest installer interface', () => {
  test('turns one manifest into complete platform and source downloads', async () => {
    const installer = await loadLatestInstaller(makeTransport(releaseFixture));

    expect(installer).toEqual({
      version: '3.1.1',
      downloads: {
        windows: {
          downloadUrl:
            'https://bppinstaller.bazaarplusplus.com/3.1.1/windows-x86_64/installer/BazaarPlusPlus_3.1.1_x64-setup.exe',
          mainlandDownloadUrl: 'https://cauyxy.lanzout.com/bppwin311',
        },
        mac: {
          downloadUrl:
            'https://bppinstaller.bazaarplusplus.com/3.1.1/darwin-aarch64/installer/BazaarPlusPlus_3.1.1_aarch64.dmg',
          mainlandDownloadUrl: 'https://cauyxy.lanzout.com/bppmac311',
        },
      },
    });
    expect(GITHUB_RELEASE_URL).toBe(
      'https://github.com/BazaarPlusPlus/BazaarPlusPlus/releases/latest'
    );
  });

  test.each([{ ver: '3.1.1' }, { version: '' }, null])(
    'rejects an invalid manifest %#',
    async (payload) => {
      await expect(loadLatestInstaller(makeTransport(payload))).rejects.toThrow(/version/i);
    }
  );

  test('passes cancellation through the transport seam', async () => {
    const transport = makeTransport(releaseFixture);
    const controller = new AbortController();

    await loadLatestInstaller(transport, { signal: controller.signal });

    expect(transport.load).toHaveBeenCalledWith({ signal: controller.signal });
  });

  test('uses the actual installer filename from the release instead of guessing it', async () => {
    const payload = structuredClone(releaseFixture);
    payload.downloads['windows-x86_64'].url =
      `${INSTALLER_BASE}/3.1.1/windows-x86_64/installer/actual-build.exe`;
    const installer = await loadLatestInstaller(makeTransport(payload));
    expect(installer.downloads.windows.downloadUrl).toBe(payload.downloads['windows-x86_64'].url);
  });

  test.each([
    { version: '3.1.1' },
    {
      ...releaseFixture,
      downloads: { 'darwin-aarch64': releaseFixture.downloads['darwin-aarch64'] },
    },
    {
      ...releaseFixture,
      downloads: {
        ...releaseFixture.downloads,
        'windows-x86_64': { url: 'https://untrusted.example/setup.exe' },
      },
    },
  ])('rejects absent or unsafe download facts %#', async (payload) => {
    await expect(loadLatestInstaller(makeTransport(payload))).rejects.toThrow(/download/);
  });
});

describe('installer manifest HTTP adapter', () => {
  test('loads unknown JSON from the production manifest location', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ version: '3.1.1' }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      })
    );
    const transport = createInstallerManifestHttpTransport({ fetchImpl });

    await expect(transport.load()).resolves.toEqual({ version: '3.1.1' });
    expect(fetchImpl).toHaveBeenCalledWith(
      `${INSTALLER_BASE}/latest.json`,
      expect.objectContaining({ headers: { Accept: 'application/json' } })
    );
  });

  test('reports a failed manifest response', async () => {
    const transport = createInstallerManifestHttpTransport({
      fetchImpl: vi.fn().mockResolvedValue(new Response('boom', { status: 503 })),
    });

    await expect(transport.load()).rejects.toThrow(/503/);
  });
});

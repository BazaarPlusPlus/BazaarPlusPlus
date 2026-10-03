import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { afterEach, describe, expect, test, vi } from 'vitest';
import macFixture from '../../release/fixtures/latest/darwin-aarch64.json';
import windowsFixture from '../../release/fixtures/latest/windows-x86_64.json';

import { RELEASE_BASE_URL } from '../../release/downloads';
import DownloadPage from '../src/features/download/DownloadPage';
import { locationAt } from './location';

const WINDOWS_PATH = 'latest/windows-x86_64.json';
const MAC_PATH = 'latest/darwin-aarch64.json';

function downloadLocation() {
  return locationAt('/download?lang=en');
}

function makeTestClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: false, staleTime: 0, gcTime: 0 },
    },
  });
}

function renderWithClient(ui: ReactNode): QueryClient {
  const client = makeTestClient();
  render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
  return client;
}

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

function platformManifests(overrides: Record<string, unknown> = {}) {
  return {
    [WINDOWS_PATH]: structuredClone(windowsFixture),
    [MAC_PATH]: structuredClone(macFixture),
    ...overrides,
  };
}

const WINDOWS_MIRROR = windowsFixture.downloads['windows-x86_64'].mainlandUrl;
const MAC_MIRROR = macFixture.downloads['darwin-aarch64'].mainlandUrl;

describe('DownloadPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  test('renders each platform card from its own Platform Release Manifest', async () => {
    stubManifests(platformManifests());
    renderWithClient(<DownloadPage location={downloadLocation()} />);

    expect(screen.getByRole('heading', { level: 2, name: 'Windows' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2, name: 'macOS' })).toBeInTheDocument();

    // Each card carries its own platform's version; wait for both to resolve.
    await screen.findByText('v3.1.2');
    await screen.findByText('v3.1.1');
    const winLink = screen.getByRole('link', { name: /Download \.exe/ });
    const macLink = screen.getByRole('link', { name: /Download \.dmg/ });
    const winMainlandLink = screen.getByRole('link', { name: 'Windows mainland mirror' });
    const macMainlandLink = screen.getByRole('link', { name: 'macOS mainland mirror' });

    expect(winLink).toHaveAttribute('href', windowsFixture.downloads['windows-x86_64'].url);
    expect(macLink).toHaveAttribute('href', macFixture.downloads['darwin-aarch64'].url);
    expect(winMainlandLink).toHaveAttribute('href', WINDOWS_MIRROR);
    expect(macMainlandLink).toHaveAttribute('href', MAC_MIRROR);
    expect(winMainlandLink).not.toHaveAttribute('aria-disabled');
    expect(macMainlandLink).not.toHaveAttribute('aria-disabled');
    expect(winMainlandLink).toHaveAttribute('target', '_blank');
    expect(macMainlandLink).toHaveAttribute('target', '_blank');

    expect(screen.queryByText(/Cannot reach the latest version right now/)).not.toBeInTheDocument();
    expect(
      screen.queryByRole('heading', { level: 2, name: 'Want the preview build?' })
    ).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Open Preview downloads/ })).not.toBeInTheDocument();
    expect(
      screen.getByText(/check the troubleshooting notes or reinstall the game and BazaarPlusPlus/)
    ).toBeInTheDocument();
    expect(screen.queryByText(/deleting the entire The Bazaar directory/)).not.toBeInTheDocument();
  });

  test('a release published without a mirror keeps its direct download and disables only the mirror entry', async () => {
    const manifests = platformManifests();
    const windows = manifests[WINDOWS_PATH];
    delete (windows.downloads['windows-x86_64'] as { mainlandUrl?: string }).mainlandUrl;
    stubManifests(manifests);
    renderWithClient(<DownloadPage location={downloadLocation()} />);

    await screen.findByText('v3.1.2');
    const winLink = screen.getByRole('link', { name: /Download \.exe/ });
    const winMainlandLink = screen.getByRole('link', { name: 'Windows mainland mirror' });
    const macMainlandLink = screen.getByRole('link', { name: 'macOS mainland mirror' });

    expect(winLink).not.toHaveAttribute('aria-disabled');
    expect(winMainlandLink).toHaveAttribute('aria-disabled', 'true');
    expect(macMainlandLink).toHaveAttribute('href', MAC_MIRROR);
    expect(screen.queryByText(/Cannot reach the latest version right now/)).not.toBeInTheDocument();
  });

  test('a platform whose manifest fails degrades only its own card', async () => {
    stubManifests(platformManifests({ [MAC_PATH]: new Error('mac down') }));
    renderWithClient(<DownloadPage location={downloadLocation()} />);

    await screen.findByText('v3.1.2');
    const winLink = screen.getByRole('link', { name: /Download \.exe/ });
    expect(winLink).not.toHaveAttribute('aria-disabled');

    const macLink = screen.getByRole('link', { name: /Download \.dmg/ });
    const macMainlandLink = screen.getByRole('link', { name: 'macOS mainland mirror' });
    expect(macLink).toHaveAttribute('aria-disabled', 'true');
    expect(macMainlandLink).toHaveAttribute('aria-disabled', 'true');
    expect(screen.getByText('Unavailable')).toBeInTheDocument();
    expect(screen.queryByText(/Cannot reach the latest version right now/)).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'GitHub Release' })).not.toBeInTheDocument();
  });

  test('shows fallback message and GitHub release link only when every platform fails', async () => {
    stubManifests({
      [WINDOWS_PATH]: new Error('unavailable'),
      [MAC_PATH]: new Error('unavailable'),
    });
    renderWithClient(<DownloadPage location={downloadLocation()} />);

    await waitFor(() =>
      expect(screen.getByText(/Cannot reach the latest version right now/)).toBeInTheDocument()
    );

    const releaseLink = screen.getByRole('link', { name: 'GitHub Release' });
    expect(releaseLink).toHaveAttribute(
      'href',
      'https://github.com/BazaarPlusPlus/BazaarPlusPlus/releases/latest'
    );

    expect(screen.getByRole('link', { name: /Download \.exe/ })).toHaveAttribute(
      'aria-disabled',
      'true'
    );
    expect(screen.getByRole('link', { name: /Download \.dmg/ })).toHaveAttribute(
      'aria-disabled',
      'true'
    );
    expect(screen.getAllByText('Unavailable')).toHaveLength(2);
  });
});

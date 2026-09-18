import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { describe, expect, test, vi } from 'vitest';

import DownloadPage from '../src/features/download/DownloadPage';
import type { InstallerManifestTransport } from '../src/features/download/installer';
import { createMemorySpaLocationAdapter, createSpaLocation } from '../src/app/router';

function downloadLocation() {
  const memory = createMemorySpaLocationAdapter('/download?lang=en');
  return createSpaLocation(memory.adapter).current();
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

function makeTransport(
  result: { payload: unknown } | { error: Error }
): InstallerManifestTransport {
  return {
    load:
      'payload' in result
        ? vi.fn().mockResolvedValue(result.payload)
        : vi.fn().mockRejectedValue(result.error),
  };
}

describe('DownloadPage', () => {
  test('renders both platform cards from one resolved latest installer', async () => {
    renderWithClient(
      <DownloadPage
        location={downloadLocation()}
        transport={makeTransport({ payload: { version: '3.1.1' } })}
      />
    );

    expect(screen.getByRole('heading', { level: 2, name: 'Windows' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2, name: 'macOS' })).toBeInTheDocument();

    const winLink = await screen.findByRole('link', { name: /Download \.exe/ });
    const macLink = await screen.findByRole('link', { name: /Download \.dmg/ });
    const winMainlandLink = screen.getByRole('link', { name: 'Windows mainland mirror' });
    const macMainlandLink = screen.getByRole('link', { name: 'macOS mainland mirror' });

    expect(winLink).toHaveAttribute(
      'href',
      'https://bppinstaller.bazaarplusplus.com/3.1.1/windows-x86_64/installer/BazaarPlusPlus_3.1.1_x64-setup.exe'
    );
    expect(macLink).toHaveAttribute(
      'href',
      'https://bppinstaller.bazaarplusplus.com/3.1.1/darwin-aarch64/installer/BazaarPlusPlus_3.1.1_aarch64.dmg'
    );
    expect(winMainlandLink).toHaveAttribute('href', 'https://cauyxy.lanzout.com/bppwin311');
    expect(macMainlandLink).toHaveAttribute('href', 'https://cauyxy.lanzout.com/bppmac311');
    expect(winMainlandLink).toHaveAttribute('target', '_blank');
    expect(macMainlandLink).toHaveAttribute('target', '_blank');

    expect(screen.getAllByText(/v3\.1\.1/)).toHaveLength(2);
    expect(
      screen.queryByRole('heading', { level: 2, name: 'Want the preview build?' })
    ).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Open Preview downloads/ })).not.toBeInTheDocument();
    expect(
      screen.getByText(/check the troubleshooting notes or reinstall the game and BazaarPlusPlus/)
    ).toBeInTheDocument();
    expect(screen.queryByText(/deleting the entire The Bazaar directory/)).not.toBeInTheDocument();
  });

  test('shows fallback message and GitHub release link when the manifest fails', async () => {
    renderWithClient(
      <DownloadPage
        location={downloadLocation()}
        transport={makeTransport({ error: new Error('unavailable') })}
      />
    );

    await waitFor(() =>
      expect(screen.getByText(/Cannot reach the latest version right now/)).toBeInTheDocument()
    );

    const releaseLink = screen.getByRole('link', { name: 'GitHub Release' });
    expect(releaseLink).toHaveAttribute(
      'href',
      'https://github.com/BazaarPlusPlus/BazaarPlusPlus/releases/latest'
    );

    const winButton = screen.getByRole('link', { name: /Download \.exe/ });
    expect(winButton).toHaveAttribute('aria-disabled', 'true');
  });
});

import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';

import App from '../src/app/App';
import Providers from '../src/app/Providers';

describe('App SPA navigation integration', () => {
  let originalUrl: string;

  beforeEach(() => {
    originalUrl = window.location.href;
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        new Response(JSON.stringify([]), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        })
      )
    );
  });

  afterEach(() => {
    window.history.replaceState({}, '', originalUrl);
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  test('intercepts an internal primary click and pushes a client-side location', async () => {
    window.history.replaceState({}, '', '/tutorial?lang=en');
    const push = vi.spyOn(window.history, 'pushState');
    render(
      <Providers>
        <App />
      </Providers>
    );

    fireEvent.click(screen.getByRole('link', { name: 'Support' }));

    expect(push).toHaveBeenCalledWith({}, '', '/support?lang=en');
    expect(window.location.pathname).toBe('/support');
    expect(
      await screen.findByRole('heading', { level: 1, name: 'Support BazaarPlusPlus' })
    ).toBeInTheDocument();
  });

  test('canonical alias replacement preserves search and uses replace semantics', async () => {
    window.history.replaceState({}, '', '/supporters/?lang=en');
    const replace = vi.spyOn(window.history, 'replaceState');
    render(
      <Providers>
        <App />
      </Providers>
    );

    await waitFor(() => expect(window.location.href).toContain('/support?lang=en'));

    expect(replace).toHaveBeenCalledWith({}, '', '/support?lang=en');
    expect(
      screen.getByRole('heading', { level: 1, name: 'Support BazaarPlusPlus' })
    ).toBeInTheDocument();
  });

  test('canonicalizes Hero scope without waiting for the Hero Metrics Dataset', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => new Promise<Response>(() => {}))
    );
    window.history.replaceState({}, '', '/heroes?lang=zh&w=invalid&t=high&s=all&keep=yes');
    const replace = vi.spyOn(window.history, 'replaceState');
    render(
      <Providers>
        <App />
      </Providers>
    );

    await waitFor(() => expect(window.location.href).toContain('/heroes?keep=yes'));

    expect(replace).toHaveBeenCalledWith({}, '', '/heroes?keep=yes');
  });

  test('popstate refreshes locale, document metadata, and rendered copy', async () => {
    window.history.replaceState({}, '', '/tutorial?lang=en');
    render(
      <Providers>
        <App />
      </Providers>
    );
    expect(document.documentElement.lang).toBe('en');

    window.history.pushState({}, '', '/tutorial');
    window.dispatchEvent(new PopStateEvent('popstate'));

    await screen.findByRole('heading', { level: 1, name: /BazaarPlusPlus 官方指南/ });
    expect(document.documentElement.lang).toBe('zh-CN');
    expect(document.title).toBe('教程 | BazaarPlusPlus');
  });
});

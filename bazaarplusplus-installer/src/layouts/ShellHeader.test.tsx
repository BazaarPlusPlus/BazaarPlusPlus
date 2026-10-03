// @vitest-environment jsdom

import { act } from 'react';
import { MemoryRouter } from 'react-router-dom';
import { createRoot } from 'react-dom/client';
import { renderToStaticMarkup } from 'react-dom/server';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { AppBootstrapController } from '../features/about/useAppBootstrap';
import { UpdaterProvider } from '../features/about/UpdaterProvider';
import { LocaleProvider } from '../i18n/LocaleProvider';
import { LOCALE_STORAGE_KEY } from '../i18n/messages';
import { idleStreamStatus } from '../api/previewDefaults';
import { commandClient } from '../api/commandClient';
import { ShellHeader } from './ShellHeader';

const tauriWindow = vi.hoisted(() => {
  const state: { resizeHandler?: () => void } = {};
  const unlisten = vi.fn();
  return {
    state,
    api: {
      minimize: vi.fn(async () => undefined),
      toggleMaximize: vi.fn(async () => undefined),
      close: vi.fn(async () => undefined),
      isMaximized: vi.fn(async () => false),
      onResized: vi.fn(async (handler: () => void) => {
        state.resizeHandler = handler;
        return unlisten;
      })
    }
  };
});

vi.mock('@tauri-apps/api/window', () => ({
  getCurrentWindow: () => tauriWindow.api
}));

vi.mock('../api/commandClient', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/commandClient')>();
  return {
    commandClient: {
      ...actual.commandClient,
      getStreamStatus: vi.fn(async () => ({ running: false }))
    }
  };
});
const { getStreamStatus } = commandClient;

const bootstrap: AppBootstrapController['bootstrap'] = {
  app_version: '4.4.0',
  bundled_bpp_version: '4.4.0',
  links: {
    github: 'https://example.com/github',
    x: 'https://example.com/x',
    bilibili_project: 'https://example.com/bilibili-project',
    bilibili_author: 'https://example.com/bilibili-author',
    bilibili_core_dev: 'https://example.com/bilibili-core-dev',
    xiaohongshu: 'https://example.com/xiaohongshu',
    kofi: 'https://example.com/kofi',
    supporter_list: 'https://example.com/supporters'
  },
  credits: [],
  licenses: []
};

const app: AppBootstrapController = {
  bootstrap,
  resource: {
    phase: 'authoritative',
    data: bootstrap,
    source: 'native',
    unavailableFields: [],
    problem: null,
    retrying: false
  },
  retry: () => undefined
};

function renderHeader({
  showBilibili = false
}: {
  showBilibili?: boolean;
} = {}) {
  return renderToStaticMarkup(
    <MemoryRouter>
      <LocaleProvider>
        <UpdaterProvider>
          <ShellHeader
            app={app}
            showBilibili={showBilibili}
            onToggleBilibili={() => undefined}
            onCloseBilibili={() => undefined}
          />
        </UpdaterProvider>
      </LocaleProvider>
    </MemoryRouter>
  );
}

describe('ShellHeader', () => {
  // These assertions are written against the Chinese copy, and the locale now
  // follows the host language when nothing is stored.
  beforeEach(() => {
    window.localStorage.setItem(LOCALE_STORAGE_KEY, 'zh');
    vi.mocked(getStreamStatus).mockClear();
  });

  it('exposes controlled keyboard-operable disclosure semantics', () => {
    const closed = renderHeader();
    const bilibiliOpen = renderHeader({ showBilibili: true });

    expect(closed).toContain('aria-controls="shell-bilibili-menu"');
    expect(closed.match(/aria-expanded="false"/g)).toHaveLength(1);
    expect(bilibiliOpen).toContain('id="shell-bilibili-menu"');
    expect(bilibiliOpen).toContain('aria-expanded="true"');
  });

  it('never polls the stream service off Windows', async () => {
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    await act(async () => {
      root.render(
        <MemoryRouter>
          <LocaleProvider>
            <UpdaterProvider>
              <ShellHeader
                app={app}
                showBilibili={false}
                onToggleBilibili={() => undefined}
                onCloseBilibili={() => undefined}
              />
            </UpdaterProvider>
          </LocaleProvider>
        </MemoryRouter>
      );
    });

    expect(
      container.querySelectorAll('.bpp-window-control-button')
    ).toHaveLength(0);
    expect(getStreamStatus).not.toHaveBeenCalled();

    await act(async () => root.unmount());
    container.remove();
  });

  it('renders Windows controls and switches maximize copy after resize', async () => {
    const userAgent = Object.getOwnPropertyDescriptor(navigator, 'userAgent');
    Object.defineProperty(navigator, 'userAgent', {
      configurable: true,
      value: 'Windows'
    });
    Object.defineProperty(window, '__TAURI_INTERNALS__', {
      configurable: true,
      value: {}
    });
    tauriWindow.api.isMaximized.mockResolvedValue(false);
    tauriWindow.state.resizeHandler = undefined;

    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    await act(async () => {
      root.render(
        <MemoryRouter>
          <LocaleProvider>
            <UpdaterProvider>
              <ShellHeader
                app={app}
                showBilibili={false}
                onToggleBilibili={() => undefined}
                onCloseBilibili={() => undefined}
              />
            </UpdaterProvider>
          </LocaleProvider>
        </MemoryRouter>
      );
    });

    expect(
      container.querySelectorAll('.bpp-window-control-button')
    ).toHaveLength(3);
    const maximize = container.querySelector('button[aria-label="最大化窗口"]');
    expect(maximize).not.toBeNull();
    expect(maximize?.getAttribute('title')).toBe('最大化窗口');
    expect(
      container.querySelector('button[aria-label="最小化窗口"]')
    ).not.toBeNull();
    expect(
      container.querySelector('button[aria-label="关闭窗口"]')
    ).not.toBeNull();

    await act(async () => {
      maximize?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    expect(tauriWindow.api.toggleMaximize).toHaveBeenCalledOnce();

    tauriWindow.api.isMaximized.mockResolvedValue(true);
    await act(async () => {
      tauriWindow.state.resizeHandler?.();
    });
    const restore = container.querySelector('button[aria-label="还原窗口"]');
    expect(restore).not.toBeNull();
    expect(restore?.getAttribute('title')).toBe('还原窗口');

    await act(async () => root.unmount());
    container.remove();
    delete (window as Window & { __TAURI_INTERNALS__?: unknown })
      .__TAURI_INTERNALS__;
    if (userAgent) {
      Object.defineProperty(navigator, 'userAgent', userAgent);
    }
  });
  it.each([
    ['zh', '/history', '隐藏到托盘'],
    ['en', '/history?page=2', 'Hide to tray'],
    ['zh', '/stream', '隐藏到托盘（直播服务仍在运行）'],
    ['en', '/stream', 'Hide to tray (stream service keeps running)']
  ] as const)(
    'keeps the %s close action accurate at %s',
    async (locale, path, label) => {
      const userAgent = Object.getOwnPropertyDescriptor(navigator, 'userAgent');
      Object.defineProperty(navigator, 'userAgent', {
        configurable: true,
        value: 'Windows'
      });
      Object.defineProperty(window, '__TAURI_INTERNALS__', {
        configurable: true,
        value: {}
      });
      window.localStorage.setItem(LOCALE_STORAGE_KEY, locale);
      vi.mocked(getStreamStatus).mockResolvedValue({
        ...idleStreamStatus,
        running: true
      });
      const container = document.createElement('div');
      document.body.append(container);
      const root = createRoot(container);
      try {
        await act(async () =>
          root.render(
            <MemoryRouter initialEntries={[path]}>
              <LocaleProvider>
                <UpdaterProvider>
                  <ShellHeader
                    app={app}
                    showBilibili={false}
                    onToggleBilibili={() => undefined}
                    onCloseBilibili={() => undefined}
                  />
                </UpdaterProvider>
              </LocaleProvider>
            </MemoryRouter>
          )
        );
        const close = container.querySelector(
          '.bpp-window-control-button:last-child'
        );
        expect(close?.getAttribute('aria-label')).toBe(label);
        expect(close?.getAttribute('title')).toBe(label);
      } finally {
        await act(async () => root.unmount());
        container.remove();
        delete (window as Window & { __TAURI_INTERNALS__?: unknown })
          .__TAURI_INTERNALS__;
        if (userAgent) Object.defineProperty(navigator, 'userAgent', userAgent);
        vi.mocked(getStreamStatus).mockResolvedValue(idleStreamStatus);
      }
    }
  );
});

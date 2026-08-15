// @vitest-environment jsdom

import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { renderToStaticMarkup } from 'react-dom/server';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { AppBootstrapController } from '../features/about/useAppBootstrap';
import { UpdaterProvider } from '../features/about/UpdaterProvider';
import { LocaleProvider } from '../i18n/LocaleProvider';
import { LOCALE_STORAGE_KEY } from '../i18n/messages';
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

vi.mock('../features/shared/streamSessionApi', () => ({
  getStreamStatus: vi.fn(async () => ({ running: false }))
}));

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
  showBilibili = false,
  showSupport = false
}: {
  showBilibili?: boolean;
  showSupport?: boolean;
} = {}) {
  return renderToStaticMarkup(
    <LocaleProvider>
      <UpdaterProvider>
        <ShellHeader
          app={app}
          showBilibili={showBilibili}
          onToggleBilibili={() => undefined}
          showSupport={showSupport}
          onToggleSupport={() => undefined}
          onOpenPayment={() => undefined}
          onCloseBilibili={() => undefined}
          onCloseSupport={() => undefined}
        />
      </UpdaterProvider>
    </LocaleProvider>
  );
}

describe('ShellHeader', () => {
  // These assertions are written against the Chinese copy, and the locale now
  // follows the host language when nothing is stored.
  beforeEach(() => {
    window.localStorage.setItem(LOCALE_STORAGE_KEY, 'zh');
  });

  it('shows the brand logo and language icon without an update check', () => {
    const html = renderHeader();

    expect(html).not.toContain('检查更新');
    expect(html).toContain('lucide-languages');
    expect(html).toContain('bpp-brand-logo');
  });

  it('groups community links before application actions', () => {
    const html = renderHeader();
    const community = html.indexOf('data-header-group="community"');
    const divider = html.indexOf('bpp-header-actions-divider');
    const application = html.indexOf('data-header-group="application"');
    const github = html.indexOf('aria-label="GitHub"');
    const support = html.indexOf('aria-controls="shell-support-menu"');

    expect(community).toBeGreaterThanOrEqual(0);
    expect(divider).toBeGreaterThan(community);
    expect(application).toBeGreaterThan(divider);
    expect(github).toBeGreaterThan(community);
    expect(github).toBeLessThan(divider);
    expect(support).toBeGreaterThan(application);
  });

  it('shows the author, CoreDev, and project entries in order', () => {
    const html = renderHeader({ showBilibili: true });

    const authorHrefIndex = html.indexOf('https://example.com/bilibili-author');
    const coreDevHrefIndex = html.indexOf(
      'https://example.com/bilibili-core-dev'
    );
    const projectHrefIndex = html.indexOf(
      'https://example.com/bilibili-project'
    );
    const authorIndex = html.indexOf('仓鼠小猫', authorHrefIndex);
    const authorSubtitleIndex = html.indexOf('BazaarLine 作者', authorIndex);
    const coreDevIndex = html.indexOf('hisenser', coreDevHrefIndex);
    const coreDevSubtitleIndex = html.indexOf('CoreDev', coreDevIndex);
    const projectIndex = html.indexOf('BazaarPlusPlus', projectHrefIndex);
    const projectSubtitleIndex = html.indexOf(
      '教程、演示和项目内容',
      projectIndex
    );

    expect(authorHrefIndex).toBeGreaterThanOrEqual(0);
    expect(coreDevHrefIndex).toBeGreaterThanOrEqual(0);
    expect(projectHrefIndex).toBeGreaterThanOrEqual(0);
    expect(authorIndex).toBeGreaterThanOrEqual(0);
    expect(authorSubtitleIndex).toBeGreaterThan(authorIndex);
    expect(coreDevIndex).toBeGreaterThanOrEqual(0);
    expect(coreDevSubtitleIndex).toBeGreaterThan(coreDevIndex);
    expect(authorHrefIndex).toBeLessThan(coreDevHrefIndex);
    expect(coreDevHrefIndex).toBeLessThan(projectHrefIndex);
    expect(authorIndex).toBeLessThan(coreDevIndex);
    expect(authorSubtitleIndex).toBeLessThan(coreDevIndex);
    expect(coreDevSubtitleIndex).toBeLessThan(projectIndex);
    expect(projectSubtitleIndex).toBeGreaterThan(projectIndex);
  });

  it('exposes controlled keyboard-operable disclosure semantics', () => {
    const closed = renderHeader();
    const bilibiliOpen = renderHeader({ showBilibili: true });
    const supportOpen = renderHeader({ showSupport: true });

    expect(closed).toContain('aria-controls="shell-bilibili-menu"');
    expect(closed).toContain('aria-controls="shell-support-menu"');
    expect(closed.match(/aria-expanded="false"/g)).toHaveLength(2);
    expect(bilibiliOpen).toContain('id="shell-bilibili-menu"');
    expect(bilibiliOpen).toContain('aria-expanded="true"');
    expect(supportOpen).toContain('id="shell-support-menu"');
    expect(supportOpen).toContain('aria-expanded="true"');
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
        <LocaleProvider>
          <UpdaterProvider>
            <ShellHeader
              app={app}
              showBilibili={false}
              onToggleBilibili={() => undefined}
              showSupport={false}
              onToggleSupport={() => undefined}
              onOpenPayment={() => undefined}
              onCloseBilibili={() => undefined}
              onCloseSupport={() => undefined}
            />
          </UpdaterProvider>
        </LocaleProvider>
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
});

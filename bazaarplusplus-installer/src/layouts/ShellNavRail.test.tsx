// @vitest-environment jsdom

import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it } from 'vitest';
import type { AppBootstrapController } from '../features/about/useAppBootstrap';
import { LocaleProvider } from '../i18n/LocaleProvider';
import { LOCALE_STORAGE_KEY } from '../i18n/messages';
import { ShellNavRail } from './ShellNavRail';

const bootstrap = {
  links: {
    kofi: 'https://example.com/kofi',
    supporter_list: 'https://example.com/supporters'
  }
} as AppBootstrapController['bootstrap'];

function renderRail({
  path = '/',
  showSupport = false
}: { path?: string; showSupport?: boolean } = {}) {
  return renderToStaticMarkup(
    <LocaleProvider>
      <MemoryRouter initialEntries={[path]}>
        <ShellNavRail
          bootstrap={bootstrap}
          showSupport={showSupport}
          onToggleSupport={() => undefined}
          onOpenPayment={() => undefined}
          onCloseSupport={() => undefined}
        />
      </MemoryRouter>
    </LocaleProvider>
  );
}

describe('ShellNavRail', () => {
  beforeEach(() => {
    window.localStorage.setItem(LOCALE_STORAGE_KEY, 'zh');
  });

  it('lists the four pages and marks the current one', () => {
    const html = renderRail({ path: '/history/run-1' });

    for (const label of ['安装', '战绩', '直播', '关于']) {
      expect(html).toContain(`>${label}</span>`);
    }
    expect(html.match(/aria-current="page"/g)).toHaveLength(1);
    expect(html).toMatch(/<a[^>]*aria-current="page"[^>]*href="\/history"/);
  });

  it('puts support and language at the foot of the rail', () => {
    const closed = renderRail();
    const open = renderRail({ showSupport: true });

    expect(closed).toContain('aria-controls="shell-support-menu"');
    expect(closed).toContain('aria-expanded="false"');
    expect(closed).toContain('lucide-languages');
    expect(open).toContain('id="shell-support-menu"');
    expect(open).toContain('https://example.com/kofi');
    expect(closed.indexOf('shell-support-menu')).toBeGreaterThan(
      closed.indexOf('href="/about"')
    );
  });
});

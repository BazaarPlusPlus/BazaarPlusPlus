import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import type { AppBootstrapSnapshot } from '../features/about/appBootstrap';
import { createUiProblem } from '../features/shared/problems';
import { LocaleProvider } from '../i18n/LocaleProvider';
import type { AppBootstrap } from '../types/backend';
import { AboutView } from './About';

const fallback: AppBootstrap = {
  app_version: '4.5.0',
  bundled_bpp_version: null,
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

function render(resource: AppBootstrapSnapshot) {
  return renderToStaticMarkup(
    <LocaleProvider>
      <AboutView resource={resource} onRetry={() => undefined} />
    </LocaleProvider>
  );
}

describe('About bootstrap feedback', () => {
  it('announces loading while keeping packaged version data readable', () => {
    const html = render({
      phase: 'initial-loading',
      data: fallback,
      source: 'packaged-fallback',
      unavailableFields: ['bundled_bpp_version'],
      problem: null,
      retrying: false
    });

    expect(html).toContain('role="status"');
    expect(html).toContain('正在获取本机应用信息');
    expect(html).toContain('aria-label="应用 4.5.0"');
    expect(html).toContain('selectable');
  });

  it('renders retry and optional diagnostics as accessible native controls', () => {
    const html = render({
      phase: 'fallback',
      data: fallback,
      source: 'packaged-fallback',
      unavailableFields: ['bundled_bpp_version'],
      problem: createUiProblem('about_bootstrap_failed', {
        params: { operation: 'load_bootstrap' },
        diagnostic: 'IPC unavailable'
      }),
      retrying: false
    });

    expect(html).toContain('role="alert"');
    expect(html).toContain('<button type="button"');
    expect(html).toContain('>重试</span>');
    expect(html).toContain('<details');
    expect(html).toContain('IPC unavailable');
    expect(html).toContain('aria-label="插件 不可用"');
  });

  it('uses distinct Credits and contributor heading levels', () => {
    const html = render({
      phase: 'authoritative',
      data: {
        ...fallback,
        credits: [
          {
            name: 'Team Member',
            role: 'AUTHOR',
            href: null,
            group: 'team'
          },
          {
            name: 'Data Source',
            role: 'GAMEDATA SOURCE',
            href: null,
            group: 'acknowledgement'
          }
        ]
      },
      source: 'native',
      unavailableFields: [],
      problem: null,
      retrying: false
    });

    expect(html.match(/>致谢<\/h[34]>/g)).toHaveLength(1);
    expect(html).toContain('>贡献者</h4>');
    expect(html).toContain('>数据与灵感</h4>');
    expect(html).toContain('AUTHOR');
    expect(html).toContain('GAMEDATA SOURCE');
  });
});

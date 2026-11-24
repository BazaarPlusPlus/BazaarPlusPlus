// @vitest-environment-options {"url":"https://example.test/"}

import { afterEach, describe, expect, test, vi } from 'vite-plus/test';

import { createSpaLocation, navigationPage, runHref } from '../src/app/router';

function setup(href: string) {
  window.history.replaceState(null, '', href);
  const location = createSpaLocation();
  const actions: Array<{ mode: 'push' | 'replace'; href: string }> = [];
  vi.spyOn(window.history, 'pushState').mockImplementation(function (
    this: History,
    ...args
  ) {
    actions.push({ mode: 'push', href: String(args[2]) });
    History.prototype.pushState.apply(this, args);
  });
  vi.spyOn(window.history, 'replaceState').mockImplementation(function (
    this: History,
    ...args
  ) {
    actions.push({ mode: 'replace', href: String(args[2]) });
    History.prototype.replaceState.apply(this, args);
  });
  return { actions, location };
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe('deep SPA location interface', () => {
  test('builds route links from the route catalog with default query values omitted', () => {
    const current = setup(
      'https://example.test/heroes?lang=en&w=3d&s=legend'
    ).location.current();

    expect(current.navigation.homeHref).toBe('/?lang=en');
    expect(current.navigation.heroesHref).toBe('/heroes?lang=en');
  });

  test('locale hrefs preserve path, scope, unrelated query values, and hash', () => {
    const en = setup(
      'https://example.test/heroes?w=3d&s=legend&keep=yes&lang=en#trend'
    ).location.current();

    expect(en.navigation.localeHrefs.zh).toBe(
      '/heroes?w=3d&s=legend&keep=yes#trend'
    );
  });

  test('scope changes use replace, omit defaults, preserve supported current values, and add no history entry', () => {
    const { actions, location } = setup(
      'https://example.test/heroes?keep=yes&lang=en&w=3d&s=non_legend#trend'
    );
    const initialLength = window.history.length;

    location.replaceScope({ window: '1d', segment: 'all' });

    expect(window.history.length).toBe(initialLength);
    expect(actions).toEqual([
      { mode: 'replace', href: '/heroes?keep=yes&lang=en' }
    ]);
    expect(location.current()).toMatchObject({
      locale: 'en',
      scope: { window: '1d', segment: 'all' }
    });
  });

  test('resolves a Run detail path and keeps it under My records', () => {
    const { location } = setup('https://example.test/records/run%2F1?lang=en');
    const current = location.current();

    expect(current.route).toMatchObject({ page: 'run', runId: 'run/1' });
    expect(navigationPage(current.route.page)).toBe('records');
    expect(runHref('run/1', 'zh')).toBe('/records/run%2F1');
    expect(
      location.handleLinkClick({
        href: 'https://example.test/records/other?lang=en',
        button: 0
      })
    ).toBe(true);
    expect(location.current().route).toMatchObject({ runId: 'other' });
  });

  test.each([
    ['external origin', { href: 'https://other.test/support', button: 0 }],
    [
      'target',
      { href: 'https://example.test/support', button: 0, target: '_blank' }
    ],
    [
      'download',
      { href: 'https://example.test/support', button: 0, download: true }
    ],
    [
      'modifier',
      { href: 'https://example.test/support', button: 0, metaKey: true }
    ],
    ['non-primary', { href: 'https://example.test/support', button: 1 }],
    ['unknown route', { href: 'https://example.test/unknown', button: 0 }],
    [
      'same-page hash',
      { href: 'https://example.test/tutorial?lang=en#part', button: 0 }
    ]
  ])('retains browser-default behavior for %s links', (_label, click) => {
    const { actions, location } = setup(
      'https://example.test/tutorial?lang=en'
    );

    expect(location.handleLinkClick(click)).toBe(false);
    expect(actions).toEqual([]);
  });
});

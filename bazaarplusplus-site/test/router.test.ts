// @vitest-environment node

import { describe, expect, test, vi } from 'vitest';

import { createMemorySpaLocationAdapter, createSpaLocation } from '../src/app/router';

function setup(href: string) {
  const memory = createMemorySpaLocationAdapter(href);
  const location = createSpaLocation(memory.adapter);
  return { memory, location };
}

describe('deep SPA location interface', () => {
  test.each([
    ['https://example.test/', 'support'],
    ['https://example.test/support', 'support'],
    ['https://example.test/support/', 'support'],
    ['https://example.test/tutorial/', 'tutorial'],
    ['https://example.test/download/', 'download'],
    ['https://example.test/heroes/', 'heroes'],
  ] as const)('resolves %s to %s', (href, page) => {
    expect(setup(href).location.current().route.page).toBe(page);
  });

  test.each([
    '/heroes/Mak',
    '/archetypes',
    '/cards',
    '/builds',
    '/download/preview',
    '/release/preview',
    '/unknown',
  ])('keeps unknown or removed route %s not found', (path) => {
    expect(setup(`https://example.test${path}`).location.current().route.page).toBe('not-found');
  });

  test('canonicalizes support aliases with replace semantics while preserving search', () => {
    const { memory, location } = setup('https://example.test/supporters/?lang=en&w=3d#lost');

    expect(location.current()).toMatchObject({
      route: { page: 'support' },
      canonicalHref: '/support?lang=en&w=3d',
    });
    location.canonicalize();

    expect(memory.actions).toEqual([{ mode: 'replace', href: '/support?lang=en&w=3d' }]);
    expect(location.current().pathname).toBe('/support');
  });

  test('parses locale and Analysis Scope defaults from one resolved model', () => {
    expect(setup('https://example.test/heroes').location.current()).toMatchObject({
      locale: 'zh',
      scope: { window: '1d', segment: 'all' },
    });
    expect(
      setup('https://example.test/heroes?lang=en&w=7d&s=legend').location.current()
    ).toMatchObject({ locale: 'en', scope: { window: '7d', segment: 'legend' } });
    expect(
      setup('https://example.test/heroes?lang=ja&w=30d&s=platinum').location.current()
    ).toMatchObject({ locale: 'zh', scope: { window: '1d', segment: 'all' } });
  });

  test('builds route links from the route catalog with default query values omitted', () => {
    const current = setup('https://example.test/heroes?lang=en&w=3d&s=legend').location.current();

    expect(current.navigation.homeHref).toBe('/?lang=en');
    expect(current.navigation.heroesHref).toBe('/heroes?lang=en');
    expect(current.navigation.items).toEqual([
      { page: 'heroes', group: 'primary', href: '/heroes?lang=en' },
      { page: 'tutorial', group: 'secondary', href: '/tutorial?lang=en' },
      { page: 'download', group: 'secondary', href: '/download?lang=en' },
      { page: 'support', group: 'secondary', href: '/support?lang=en' },
    ]);
  });

  test('locale hrefs preserve path, scope, unrelated query values, and hash', () => {
    const en = setup(
      'https://example.test/heroes?w=3d&s=legend&keep=yes&lang=en#trend'
    ).location.current();
    const zh = setup('https://example.test/heroes?w=7d&keep=yes#trend').location.current();

    expect(en.navigation.localeHrefs.zh).toBe('/heroes?w=3d&s=legend&keep=yes#trend');
    expect(zh.navigation.localeHrefs.en).toBe('/heroes?w=7d&keep=yes&lang=en#trend');
  });

  test('scope changes use replace, omit defaults, preserve supported current values, and add no history entry', () => {
    const { memory, location } = setup(
      'https://example.test/heroes?keep=yes&lang=en&w=3d&s=non_legend#trend'
    );
    const initialLength = memory.entries.length;

    location.replaceScope({ window: '1d', segment: 'all' });

    expect(memory.entries).toHaveLength(initialLength);
    expect(memory.actions).toEqual([{ mode: 'replace', href: '/heroes?keep=yes&lang=en' }]);
    expect(location.current()).toMatchObject({
      locale: 'en',
      scope: { window: '1d', segment: 'all' },
    });
  });

  test('exposes explicit, invalid, and retired Hero scope values as one canonical href', () => {
    const { memory, location } = setup(
      'https://example.test/heroes?lang=zh&w=invalid&s=all&t=high&keep=yes'
    );

    expect(location.current()).toMatchObject({
      scope: { window: '1d', segment: 'all' },
      canonicalHref: '/heroes?keep=yes',
    });
    location.canonicalize();

    expect(memory.actions).toEqual([{ mode: 'replace', href: '/heroes?keep=yes' }]);
  });

  test('does not rewrite unrelated Hero query encoding when scope is already canonical', () => {
    const { memory, location } = setup('https://example.test/heroes?campaign=two%20words&w=3d');

    expect(location.current().canonicalHref).toBeNull();
    location.canonicalize();

    expect(memory.actions).toEqual([]);
  });

  test('normal internal navigation uses push semantics', () => {
    const { memory, location } = setup('https://example.test/tutorial?lang=en');

    expect(
      location.handleLinkClick({
        href: 'https://example.test/support?lang=en',
        button: 0,
      })
    ).toBe(true);

    expect(memory.actions).toEqual([{ mode: 'push', href: '/support?lang=en' }]);
    expect(memory.entries).toHaveLength(2);
  });

  test.each([
    ['external origin', { href: 'https://other.test/support', button: 0 }],
    ['target', { href: 'https://example.test/support', button: 0, target: '_blank' }],
    ['download', { href: 'https://example.test/support', button: 0, download: true }],
    ['modifier', { href: 'https://example.test/support', button: 0, metaKey: true }],
    ['non-primary', { href: 'https://example.test/support', button: 1 }],
    ['unknown route', { href: 'https://example.test/unknown', button: 0 }],
    ['same-page hash', { href: 'https://example.test/tutorial?lang=en#part', button: 0 }],
  ])('retains browser-default behavior for %s links', (_label, click) => {
    const { memory, location } = setup('https://example.test/tutorial?lang=en');

    expect(location.handleLinkClick(click)).toBe(false);
    expect(memory.actions).toEqual([]);
  });

  test('popstate refreshes subscribers from the in-memory adapter', () => {
    const { memory, location } = setup('https://example.test/tutorial');
    const listener = vi.fn();
    const unsubscribe = location.subscribe(listener);
    location.handleLinkClick({ href: 'https://example.test/support', button: 0 });
    listener.mockClear();

    memory.back();

    expect(listener).toHaveBeenCalledWith(
      expect.objectContaining({
        route: { page: 'tutorial', path: '/tutorial', group: 'secondary' },
      })
    );
    unsubscribe();
  });
});

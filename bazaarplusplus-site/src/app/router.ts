import type { AnalysisScope, MetricWindow } from '../features/heroes/hero-analysis';
import type { HeroMetricsSegment } from '../features/heroes/hero-metrics-dataset';

export type Locale = 'en' | 'zh';

const DEFAULT_LOCALE: Locale = 'zh';

export type SpaPage = 'heroes' | 'tutorial' | 'download' | 'support' | 'not-found';

type NavigationGroup = 'primary' | 'secondary';

type RouteDefinition = {
  page: Exclude<SpaPage, 'not-found'>;
  path: string;
  group: NavigationGroup;
};

const ROUTES = [
  { page: 'heroes', path: '/heroes', group: 'primary' },
  { page: 'tutorial', path: '/tutorial', group: 'secondary' },
  { page: 'download', path: '/download', group: 'secondary' },
  { page: 'support', path: '/support', group: 'secondary' },
] as const satisfies readonly RouteDefinition[];

export type PrimaryNavigationPage = Extract<(typeof ROUTES)[number], { group: 'primary' }>['page'];
export type SecondaryNavigationPage = Extract<
  (typeof ROUTES)[number],
  { group: 'secondary' }
>['page'];

export type ResolvedSpaLocation = {
  pathname: string;
  search: string;
  hash: string;
  route: RouteDefinition | { page: 'not-found'; path: string; group: null };
  locale: Locale;
  scope: AnalysisScope;
  canonicalHref: string | null;
  navigation: {
    homeHref: string;
    heroesHref: string;
    items: Array<{
      page: RouteDefinition['page'];
      group: NavigationGroup;
      href: string;
    }>;
    localeHrefs: Record<Locale, string>;
  };
};

export type SpaLinkClick = {
  href: string;
  button: number;
  defaultPrevented?: boolean;
  metaKey?: boolean;
  ctrlKey?: boolean;
  shiftKey?: boolean;
  altKey?: boolean;
  target?: string;
  download?: boolean;
};

type RawSpaLocation = {
  pathname: string;
  search: string;
  hash: string;
  origin: string;
};

export type SpaLocation = {
  current(): ResolvedSpaLocation;
  subscribe(listener: (location: ResolvedSpaLocation) => void): () => void;
  canonicalize(): void;
  replaceScope(scope: AnalysisScope): void;
  handleLinkClick(click: SpaLinkClick): boolean;
};

const ALIASES: Record<string, string> = {
  '/supporters': '/support',
};

function normalizePathname(pathname: string): string {
  return pathname.replace(/\/+$/, '') || '/';
}

function parseLocale(value: string | null): Locale {
  return value === 'en' || value === 'zh' ? value : DEFAULT_LOCALE;
}

function parseMetricWindow(value: string | null): MetricWindow {
  return value === '3d' || value === '7d' ? value : '1d';
}

function parseHeroMetricsSegment(value: string | null): HeroMetricsSegment {
  return value === 'legend' || value === 'non_legend' ? value : 'all';
}

function resolveRoute(pathname: string): ResolvedSpaLocation['route'] {
  const normalized = normalizePathname(pathname);
  const resolvedPath = ALIASES[normalized] ?? normalized;
  if (resolvedPath === '/') {
    return ROUTES.find((route) => route.page === 'support')!;
  }
  return (
    ROUTES.find((route) => route.path === resolvedPath) ?? {
      page: 'not-found',
      path: resolvedPath,
      group: null,
    }
  );
}

function buildRouteHref(pathname: string, locale: Locale): string {
  const search = new URLSearchParams();
  if (locale !== DEFAULT_LOCALE) {
    search.set('lang', locale);
  }
  const query = search.toString();
  return query ? `${pathname}?${query}` : pathname;
}

function buildLocaleHref(raw: RawSpaLocation, locale: Locale): string {
  const search = new URLSearchParams(raw.search);
  if (locale === DEFAULT_LOCALE) {
    search.delete('lang');
  } else {
    search.set('lang', locale);
  }
  const query = search.toString();
  return `${raw.pathname}${query ? `?${query}` : ''}${raw.hash}`;
}

function buildScopeSearch(currentSearch: string, locale: Locale, scope: AnalysisScope): string {
  const search = new URLSearchParams(currentSearch);
  if (scope.window === '1d') {
    search.delete('w');
  } else {
    search.set('w', scope.window);
  }
  search.delete('t');
  if (scope.segment === 'all') {
    search.delete('s');
  } else {
    search.set('s', scope.segment);
  }
  if (locale === DEFAULT_LOCALE) {
    search.delete('lang');
  } else {
    search.set('lang', locale);
  }
  const query = search.toString();
  return query ? `?${query}` : '';
}

function scopeSearchNeedsCanonicalization(
  search: URLSearchParams,
  locale: Locale,
  scope: AnalysisScope
): boolean {
  const hasCanonicalValue = (key: string, value: string) =>
    search.getAll(key).length === 1 && search.get(key) === value;

  return (
    search.has('t') ||
    (scope.window === '1d' ? search.has('w') : !hasCanonicalValue('w', scope.window)) ||
    (scope.segment === 'all' ? search.has('s') : !hasCanonicalValue('s', scope.segment)) ||
    (locale === DEFAULT_LOCALE ? search.has('lang') : !hasCanonicalValue('lang', locale))
  );
}

function resolveLocation(raw: RawSpaLocation): ResolvedSpaLocation {
  const search = new URLSearchParams(raw.search);
  const locale = parseLocale(search.get('lang'));
  const scope: AnalysisScope = {
    window: parseMetricWindow(search.get('w')),
    segment: parseHeroMetricsSegment(search.get('s')),
  };
  const normalized = normalizePathname(raw.pathname);
  const canonicalPath = ALIASES[normalized] ?? null;
  const route = resolveRoute(raw.pathname);
  const canonicalSearch =
    route.page === 'heroes' && scopeSearchNeedsCanonicalization(search, locale, scope)
      ? buildScopeSearch(raw.search, locale, scope)
      : raw.search;
  const canonicalHref = `${canonicalPath ?? raw.pathname}${canonicalSearch}`;
  const items = ROUTES.map((candidate) => ({
    page: candidate.page,
    group: candidate.group,
    href: buildRouteHref(candidate.path, locale),
  }));

  return {
    pathname: raw.pathname,
    search: raw.search,
    hash: raw.hash,
    route,
    locale,
    scope,
    canonicalHref: canonicalPath != null || canonicalSearch !== raw.search ? canonicalHref : null,
    navigation: {
      homeHref: buildRouteHref('/', locale),
      heroesHref: buildRouteHref('/heroes', locale),
      items,
      localeHrefs: {
        zh: buildLocaleHref(raw, 'zh'),
        en: buildLocaleHref(raw, 'en'),
      },
    },
  };
}

function toRelativeHref(url: URL): string {
  return `${url.pathname}${url.search}${url.hash}`;
}

function buildScopeHref(location: ResolvedSpaLocation, scope: AnalysisScope): string {
  return `${location.pathname}${buildScopeSearch(location.search, location.locale, scope)}`;
}

function readWindowLocation(): RawSpaLocation {
  return {
    pathname: window.location.pathname,
    search: window.location.search,
    hash: window.location.hash,
    origin: window.location.origin,
  };
}

function current(): ResolvedSpaLocation {
  return resolveLocation(readWindowLocation());
}

export function createSpaLocation(): SpaLocation {
  const listeners = new Set<(location: ResolvedSpaLocation) => void>();
  let subscribedToPop = false;

  function notify() {
    const location = current();
    for (const listener of listeners) {
      listener(location);
    }
  }

  return {
    current,
    subscribe(listener) {
      listeners.add(listener);
      if (!subscribedToPop) {
        window.addEventListener('popstate', notify);
        subscribedToPop = true;
      }
      return () => {
        listeners.delete(listener);
        if (listeners.size === 0) {
          window.removeEventListener('popstate', notify);
          subscribedToPop = false;
        }
      };
    },
    canonicalize() {
      const canonicalHref = current().canonicalHref;
      if (canonicalHref) {
        window.history.replaceState({}, '', canonicalHref);
        notify();
      }
    },
    replaceScope(scope) {
      const location = current();
      const href = buildScopeHref(location, scope);
      if (`${location.pathname}${location.search}` !== href) {
        window.history.replaceState({}, '', href);
        notify();
      }
    },
    handleLinkClick(click) {
      if (
        click.defaultPrevented ||
        click.button !== 0 ||
        click.metaKey ||
        click.ctrlKey ||
        click.shiftKey ||
        click.altKey ||
        click.target ||
        click.download
      ) {
        return false;
      }

      const raw = readWindowLocation();
      const next = new URL(click.href, `${raw.origin}${raw.pathname}${raw.search}${raw.hash}`);
      if (next.origin !== raw.origin || resolveRoute(next.pathname).page === 'not-found') {
        return false;
      }
      if (next.pathname === raw.pathname && next.search === raw.search && next.hash.length > 0) {
        return false;
      }

      window.history.pushState({}, '', toRelativeHref(next));
      notify();
      return true;
    },
  };
}

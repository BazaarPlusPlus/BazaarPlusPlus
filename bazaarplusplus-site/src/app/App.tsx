import { lazy, Suspense, useEffect, useMemo, useState } from 'react';

import { getPageTitle } from '../content/site-copy';
import DownloadPage from '../features/download/DownloadPage';
import { createHeroMetricsHttpTransport } from '../features/heroes/hero-metrics-dataset';
import SupportPage from '../features/support/SupportPage';
import TutorialPage from '../features/tutorial/TutorialPage';
import {
  createBrowserSpaLocationAdapter,
  createSpaLocation,
  type ResolvedSpaLocation,
} from './router';
import { LoadingScreen, NotFoundScreen } from './screens';

const HeroOverviewPage = lazy(() =>
  import('./route-pages').then((module) => ({ default: module.HeroOverviewPage }))
);

export default function App() {
  const transport = useMemo(() => createHeroMetricsHttpTransport(), []);
  const spaLocation = useMemo(() => createSpaLocation(createBrowserSpaLocationAdapter()), []);
  const [location, setLocation] = useState<ResolvedSpaLocation>(() => spaLocation.current());

  useEffect(() => spaLocation.subscribe(setLocation), [spaLocation]);

  useEffect(() => {
    document.title = getPageTitle(location.route.page, location.locale);
    document.documentElement.lang = location.locale === 'zh' ? 'zh-CN' : 'en';
  }, [location.locale, location.route.page]);

  useEffect(() => {
    if (location.canonicalHref) {
      spaLocation.canonicalize();
    }
  }, [location.canonicalHref, spaLocation]);

  useEffect(() => {
    function handleClick(event: MouseEvent) {
      const target = event.target instanceof Element ? event.target.closest('a[href]') : null;
      if (!(target instanceof HTMLAnchorElement)) {
        return;
      }

      const handled = spaLocation.handleLinkClick({
        href: target.href,
        button: event.button,
        defaultPrevented: event.defaultPrevented,
        metaKey: event.metaKey,
        ctrlKey: event.ctrlKey,
        shiftKey: event.shiftKey,
        altKey: event.altKey,
        target: target.target,
        download: target.hasAttribute('download'),
      });
      if (handled) {
        event.preventDefault();
      }
    }

    document.addEventListener('click', handleClick);
    return () => document.removeEventListener('click', handleClick);
  }, [spaLocation]);

  if (location.route.page === 'heroes') {
    return (
      <Suspense fallback={<LoadingScreen location={location} />}>
        <HeroOverviewPage
          transport={transport}
          location={location}
          onScopeChange={spaLocation.replaceScope}
        />
      </Suspense>
    );
  }

  if (location.route.page === 'tutorial') {
    return <TutorialPage location={location} />;
  }

  if (location.route.page === 'download') {
    return <DownloadPage location={location} />;
  }

  if (location.route.page === 'support') {
    return <SupportPage location={location} />;
  }

  return <NotFoundScreen location={location} />;
}

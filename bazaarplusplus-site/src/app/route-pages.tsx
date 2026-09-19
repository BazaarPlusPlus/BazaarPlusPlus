import { useQuery } from '@tanstack/react-query';

import HeroOverviewDashboard from '../features/heroes/HeroOverviewDashboard';
import type { HeroMetricsDataset } from '../features/heroes/hero-metrics-dataset';
import type { AnalysisScope } from '../features/heroes/hero-analysis';
import {
  loadHeroMetricsDataset,
  type HeroMetricsTransport,
} from '../features/heroes/hero-metrics-dataset';
import { ErrorScreen, LoadingScreen } from './screens';
import type { ResolvedSpaLocation } from './router';

type RoutePageProps = {
  transport: HeroMetricsTransport;
  location: ResolvedSpaLocation;
  onScopeChange: (scope: AnalysisScope) => void;
};

function usePageQuery<T>(
  queryKey: readonly unknown[],
  queryFn: (signal: AbortSignal) => Promise<T>
): { data: T | undefined; error: unknown; isLoading: boolean } {
  return useQuery({
    queryKey,
    queryFn: ({ signal }) => queryFn(signal),
  });
}

export function HeroOverviewPage({ transport, location, onScopeChange }: RoutePageProps) {
  const { data, error, isLoading } = usePageQuery(['hero-overview'], (signal) =>
    loadHeroMetricsDataset(transport, { signal })
  );

  if (isLoading) {
    return <LoadingScreen location={location} />;
  }

  if (!data) {
    return <ErrorScreen location={location} error={error} />;
  }

  return <HeroOverviewRouteContent data={data} location={location} onScopeChange={onScopeChange} />;
}

function HeroOverviewRouteContent({
  data,
  location,
  onScopeChange,
}: {
  data: HeroMetricsDataset;
  location: ResolvedSpaLocation;
  onScopeChange: (scope: AnalysisScope) => void;
}) {
  return (
    <HeroOverviewDashboard
      locale={location.locale}
      location={location}
      dataset={data}
      requestedScope={location.scope}
      onScopeChange={onScopeChange}
    />
  );
}

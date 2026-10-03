import { useQuery } from '@tanstack/react-query';

import HeroOverviewDashboard from '../features/heroes/HeroOverviewDashboard';
import type { AnalysisScope } from '../features/heroes/hero-analysis';
import {
  HeroMetricsHttpError,
  loadHeroMetricsDataset,
} from '../features/heroes/hero-metrics-dataset';
import { ErrorScreen, LoadingScreen } from './screens';
import type { ResolvedSpaLocation } from './router';

type RoutePageProps = {
  location: ResolvedSpaLocation;
  onScopeChange: (scope: AnalysisScope) => void;
};

function isClientError(error: Error): boolean {
  return error instanceof HeroMetricsHttpError && error.status >= 400 && error.status < 500;
}

export function HeroOverviewPage({ location, onScopeChange }: RoutePageProps) {
  const { data, isLoading, isFetching, refetch } = useQuery({
    queryKey: ['hero-overview'],
    queryFn: ({ signal }) => loadHeroMetricsDataset(signal),
    retry: (failureCount, error) => failureCount < 2 && !isClientError(error),
  });

  if (isLoading) {
    return <LoadingScreen location={location} />;
  }

  if (!data) {
    return <ErrorScreen location={location} retrying={isFetching} onRetry={() => void refetch()} />;
  }

  return <HeroOverviewDashboard location={location} dataset={data} onScopeChange={onScopeChange} />;
}

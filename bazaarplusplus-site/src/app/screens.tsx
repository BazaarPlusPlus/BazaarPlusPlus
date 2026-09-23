import { getSiteCopy } from '../content/site-copy';
import Button from '../shared/components/Button';
import { RefreshIcon } from '../shared/components/icons';
import PageLayout, { PageHeading } from '../shared/components/PageLayout';
import StatusPanel from '../shared/components/StatusPanel';
import type { ResolvedSpaLocation } from './router';

export function LoadingScreen({ location }: { location: ResolvedSpaLocation }) {
  const copy = getSiteCopy(location.locale);
  const loadingCopy = copy.common.loading;

  return (
    <PageLayout location={location} width="wide" busy>
      <PageHeading title={copy.stats.heroes.title} />
      <StatusPanel
        kind="loading"
        title={loadingCopy.title}
        body={loadingCopy.body}
        progressLabel={loadingCopy.progressAriaLabel}
      />
    </PageLayout>
  );
}

export function ErrorScreen({
  location,
  onRetry,
  retrying = false,
}: {
  location: ResolvedSpaLocation;
  onRetry: () => void;
  retrying?: boolean;
}) {
  const copy = getSiteCopy(location.locale);
  const errorCopy = copy.common.error;

  return (
    <PageLayout location={location} width="wide">
      <PageHeading title={copy.stats.heroes.title} />
      <StatusPanel
        kind="error"
        title={errorCopy.title}
        body={errorCopy.body}
        actions={
          <Button variant="primary" icon={<RefreshIcon />} busy={retrying} onClick={onRetry}>
            {errorCopy.retry}
          </Button>
        }
      />
    </PageLayout>
  );
}

export function NotFoundScreen({ location }: { location: ResolvedSpaLocation }) {
  const copy = getSiteCopy(location.locale).common.notFound;

  return (
    <PageLayout location={location}>
      <StatusPanel
        kind="not-found"
        headingLevel={1}
        title={copy.title}
        actions={<Button href={location.navigation.heroesHref}>{copy.backToHeroes}</Button>}
      />
    </PageLayout>
  );
}

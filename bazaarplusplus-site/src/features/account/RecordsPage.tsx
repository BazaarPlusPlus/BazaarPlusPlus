import { useInfiniteQuery } from '@tanstack/react-query';
import { Button, StatusPanel } from '@bpp/ui';

import {
  buildRouteHref,
  runHref,
  type AccountRoutePage,
  type Locale,
  type ResolvedSpaLocation
} from '../../app/router';
import { getSiteCopy } from '../../content/site-copy';
import HeroBadge from '../../shared/components/HeroBadge';
import PageLayout, { PageHeading } from '../../shared/components/PageLayout';
import { getIntlLocale } from '../../shared/lib/intl-locale';
import { loadRuns, type RunSummary } from './account-api';
import {
  ACCOUNT_QUERY,
  RunFacts,
  RunOutcome,
  heroName,
  useSignedOutRedirect
} from './account-ui';

type Props = {
  location: ResolvedSpaLocation;
  replacePage: (page: AccountRoutePage) => void;
};

function RunRow({ run, locale }: { run: RunSummary; locale: Locale }) {
  const date = new Intl.DateTimeFormat(getIntlLocale(locale), {
    dateStyle: 'medium',
    timeStyle: 'short'
  });
  return (
    <li className="border-b border-line last:border-b-0">
      <a
        href={runHref(run.run_id, locale)}
        className="grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-4 gap-y-1.5 px-4 py-3 text-body text-text-1 no-underline transition-colors duration-(--t-fast) hover:bg-hover sm:grid-cols-[10rem_9rem_minmax(0,1fr)_auto]"
      >
        <span className="min-w-0">
          <HeroBadge hero={heroName(run.hero ?? '—')} label="full" />
        </span>
        <span className="justify-self-end sm:justify-self-start">
          <RunOutcome
            locale={locale}
            victories={run.victories}
            losses={run.losses}
          />
        </span>
        <span className="flex min-w-0 flex-wrap items-baseline gap-x-3 text-small tabular-nums">
          <RunFacts run={run} locale={locale} />
        </span>
        <span className="justify-self-end text-small text-text-3 tabular-nums">
          {run.ended_at_ms == null ? '—' : date.format(run.ended_at_ms)}
        </span>
      </a>
    </li>
  );
}

export default function RecordsPage({ location, replacePage }: Props) {
  const siteCopy = getSiteCopy(location.locale);
  const copy = siteCopy.account;
  const records = useInfiniteQuery({
    queryKey: [...ACCOUNT_QUERY, 'runs'],
    queryFn: ({ pageParam, signal }) => loadRuns(pageParam, signal),
    initialPageParam: null as string | null,
    getNextPageParam: (page) => page.next_cursor,
    staleTime: 0,
    retry: false
  });
  const signedOut = useSignedOutRedirect([records.error], replacePage);
  if (signedOut) return null;
  const runs = records.data?.pages.flatMap((page) => page.runs) ?? [];
  return (
    <PageLayout location={location} busy={records.isPending}>
      <PageHeading
        title={siteCopy.pageTitles.records}
        actions={
          <Button href={buildRouteHref('/account', location.locale)}>
            {siteCopy.pageTitles.account}
          </Button>
        }
      />
      {records.isPending && (
        <StatusPanel kind="loading" compact title={copy.loading} />
      )}
      {records.isError && (
        <StatusPanel
          kind="error"
          title={copy.loadError}
          actions={
            <Button
              onClick={() =>
                void (records.isFetchNextPageError
                  ? records.fetchNextPage()
                  : records.refetch())
              }
            >
              {copy.retry}
            </Button>
          }
        />
      )}
      {records.data &&
        (runs.length ? (
          <ul className="bpp-panel overflow-hidden">
            {runs.map((run) => (
              <RunRow key={run.run_id} run={run} locale={location.locale} />
            ))}
          </ul>
        ) : (
          <StatusPanel kind="empty" title={copy.noRuns} />
        ))}
      {records.hasNextPage && !records.isFetchNextPageError && (
        <Button
          className="self-center"
          busy={records.isFetchingNextPage}
          onClick={() => void records.fetchNextPage()}
        >
          {copy.more}
        </Button>
      )}
    </PageLayout>
  );
}

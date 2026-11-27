import { useQuery } from '@tanstack/react-query';
import { Button, StatusPanel } from '@bazaarplusplus/ui';

import {
  buildRouteHref,
  runHref,
  type AccountRoutePage,
  type Locale,
  type ResolvedSpaLocation
} from '../../app/router';
import { fillCopy, getSiteCopy } from '../../content/site-copy';
import HeroBadge from '../../shared/components/HeroBadge';
import PageLayout, { PageHeading } from '../../shared/components/PageLayout';
import { getIntlLocale } from '../../shared/lib/intl-locale';
import { loadMe, loadRuns, type AccountRecord } from './account-api';
import {
  ACCOUNT_QUERY,
  RunFacts,
  RunOutcome,
  heroName,
  useSignedOutRedirect
} from './account-ui';
import {
  groupByDay,
  recordStats,
  type RatingPoint,
  type RecordDay,
  type RecordStats
} from './records';

type Props = {
  location: ResolvedSpaLocation;
  replacePage: (page: AccountRoutePage) => void;
};

const DAY_MS = 24 * 60 * 60 * 1000;
const CHART_WIDTH = 320;
const CHART_HEIGHT = 72;
const CHART_PAD = 6;

function Totals({ stats, locale }: { stats: RecordStats; locale: Locale }) {
  const copy = getSiteCopy(locale).account;
  const intl = getIntlLocale(locale);
  const number = new Intl.NumberFormat(intl);
  const decimal = new Intl.NumberFormat(intl, {
    minimumFractionDigits: 1,
    maximumFractionDigits: 1
  });
  const signed = new Intl.NumberFormat(intl, { signDisplay: 'exceptZero' });
  return (
    <dl className="bpp-panel grid grid-cols-2 sm:grid-cols-4">
      {[
        [copy.statRuns, number.format(stats.runs)],
        [copy.avgVictories, decimal.format(stats.avgVictories)],
        [copy.statTenWins, number.format(stats.tenWins)],
        [
          copy.ratingChange,
          stats.ratingChange == null ? '—' : signed.format(stats.ratingChange)
        ]
      ].map(([label, value], index) => (
        <div
          key={label}
          className={`flex flex-col-reverse items-center gap-1 px-2 py-4 ${index % 2 ? 'border-l border-line' : ''} ${index > 1 ? 'border-t border-line sm:border-t-0' : ''} ${index === 2 ? 'sm:border-l' : ''}`}
        >
          <dt className="text-small text-text-3">{label}</dt>
          <dd className="text-display leading-none font-semibold text-text-1 tabular-nums">
            {value}
          </dd>
        </div>
      ))}
    </dl>
  );
}

function RatingChart({
  points,
  locale
}: {
  points: RatingPoint[];
  locale: Locale;
}) {
  const copy = getSiteCopy(locale).account;
  const last = points.at(-1);
  if (!last) return null;
  const values = points.map((p) => p.rating);
  const min = Math.min(...values);
  const span = Math.max(Math.max(...values) - min, 1);
  const xy = points.map((p, i) => [
    points.length === 1
      ? CHART_WIDTH / 2
      : CHART_PAD + ((CHART_WIDTH - 2 * CHART_PAD) * i) / (points.length - 1),
    CHART_PAD + (CHART_HEIGHT - 2 * CHART_PAD) * (1 - (p.rating - min) / span)
  ]);
  const [lx, ly] = xy.at(-1)!;
  const number = new Intl.NumberFormat(getIntlLocale(locale));
  return (
    <div className="bpp-panel bpp-panel-pad">
      <div className="flex items-baseline justify-between gap-3">
        <span className="text-small text-text-3">{copy.rating}</span>
        <span className="text-small text-text-2">{last.rank}</span>
      </div>
      <div className="flex items-end gap-4">
        <span className="text-display leading-none font-semibold text-text-1 tabular-nums">
          {number.format(last.rating)}
        </span>
        <svg
          role="img"
          aria-label={fillCopy(copy.ratingChart, { count: points.length })}
          viewBox={`0 0 ${CHART_WIDTH} ${CHART_HEIGHT}`}
          preserveAspectRatio="none"
          className="h-[72px] min-w-0 flex-1 overflow-visible"
        >
          <polyline
            points={xy.map(([x, y]) => `${x},${y}`).join(' ')}
            fill="none"
            stroke="var(--accent)"
            strokeWidth="2"
            strokeLinejoin="round"
            strokeLinecap="round"
            vectorEffect="non-scaling-stroke"
          />
          {/* A zero-length round cap stays circular while the chart stretches. */}
          <line
            x1={lx}
            y1={ly}
            x2={lx}
            y2={ly}
            stroke="var(--accent)"
            strokeWidth="7"
            strokeLinecap="round"
            vectorEffect="non-scaling-stroke"
          />
        </svg>
      </div>
    </div>
  );
}

function HeroTable({ stats, locale }: { stats: RecordStats; locale: Locale }) {
  const copy = getSiteCopy(locale).account;
  const intl = getIntlLocale(locale);
  const number = new Intl.NumberFormat(intl);
  const decimal = new Intl.NumberFormat(intl, {
    minimumFractionDigits: 1,
    maximumFractionDigits: 1
  });
  if (stats.heroes.length === 0) return null;
  return (
    <div className="bpp-panel overflow-x-auto">
      <table className="w-full text-left text-body tabular-nums">
        <thead className="text-small text-text-3">
          <tr>
            <th className="px-4 py-3 font-medium">{copy.hero}</th>
            <th className="px-2 py-3 text-right font-medium">
              {copy.heroRuns}
            </th>
            <th className="px-2 py-3 text-right font-medium">
              {copy.avgVictories}
            </th>
            <th className="px-4 py-3 text-right font-medium">
              {copy.statTenWins}
            </th>
          </tr>
        </thead>
        <tbody>
          {stats.heroes.map((row) => (
            <tr key={row.hero} className="border-t border-line">
              <td className="px-4 py-2.5">
                <HeroBadge hero={heroName(row.hero)} label="full" />
              </td>
              <td className="px-2 py-2.5 text-right">
                {number.format(row.runs)}
              </td>
              <td className="px-2 py-2.5 text-right">
                {decimal.format(row.avgVictories)}
              </td>
              <td className="px-4 py-2.5 text-right">
                {number.format(row.tenWins)}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function RunRow({ run, locale }: { run: AccountRecord; locale: Locale }) {
  const time = new Intl.DateTimeFormat(getIntlLocale(locale), {
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
          {time.format(run.ended_at_ms)}
        </span>
      </a>
    </li>
  );
}

function dayLabel(dayStartMs: number, locale: Locale): string {
  const copy = getSiteCopy(locale).account;
  const now = new Date();
  const today = new Date(
    now.getFullYear(),
    now.getMonth(),
    now.getDate()
  ).getTime();
  // Round: a day that crosses a daylight-saving change is not 24 hours long.
  const daysAgo = Math.round((today - dayStartMs) / DAY_MS);
  if (daysAgo === 0) return copy.today;
  if (daysAgo === 1) return copy.yesterday;
  return new Intl.DateTimeFormat(getIntlLocale(locale), {
    month: 'long',
    day: 'numeric',
    weekday: 'short'
  }).format(dayStartMs);
}

function RunDays({ days, locale }: { days: RecordDay[]; locale: Locale }) {
  return (
    <div className="flex flex-col gap-3">
      {days.map((day) => (
        <section key={day.dayStartMs} className="flex flex-col gap-2">
          <h2 className="text-small font-medium text-text-2">
            {dayLabel(day.dayStartMs, locale)}
          </h2>
          <ul className="bpp-panel overflow-hidden">
            {day.runs.map((run) => (
              <RunRow key={run.run_id} run={run} locale={locale} />
            ))}
          </ul>
        </section>
      ))}
    </div>
  );
}

export default function RecordsPage({ location, replacePage }: Props) {
  const { locale } = location;
  const siteCopy = getSiteCopy(locale);
  const copy = siteCopy.account;
  const me = useQuery({
    queryKey: [...ACCOUNT_QUERY, 'me'],
    queryFn: ({ signal }) => loadMe(signal),
    staleTime: 0,
    retry: false
  });
  const records = useQuery({
    queryKey: [...ACCOUNT_QUERY, 'runs'],
    queryFn: ({ signal }) => loadRuns(signal),
    staleTime: 0,
    retry: false
  });
  const signedOut = useSignedOutRedirect(
    [me.error, records.error],
    replacePage
  );
  if (signedOut) return null;
  const pending = me.isPending || records.isPending;
  const failed = me.isError || records.isError;
  const runs = records.data ?? [];
  return (
    <PageLayout location={location} busy={pending}>
      <PageHeading
        title={siteCopy.pageTitles.records}
        intro={copy.recordsWindow}
        actions={
          <Button href={buildRouteHref('/account', locale)}>
            {siteCopy.pageTitles.account}
          </Button>
        }
      />
      {pending && !failed && (
        <StatusPanel kind="loading" compact title={copy.loading} />
      )}
      {failed && (
        <StatusPanel
          kind="error"
          title={copy.loadError}
          actions={
            <Button
              onClick={() => {
                void me.refetch();
                void records.refetch();
              }}
            >
              {copy.retry}
            </Button>
          }
        />
      )}
      {me.data &&
        records.data &&
        (!me.data.game ? (
          <StatusPanel
            kind="empty"
            title={copy.recordsUnlinked}
            actions={
              <Button
                variant="primary"
                href={buildRouteHref('/account', locale)}
              >
                {copy.goBind}
              </Button>
            }
          />
        ) : runs.length === 0 ? (
          <StatusPanel kind="empty" title={copy.noRuns} />
        ) : (
          <Records runs={runs} locale={locale} />
        ))}
    </PageLayout>
  );
}

function Records({ runs, locale }: { runs: AccountRecord[]; locale: Locale }) {
  const stats = recordStats(runs);
  return (
    <>
      <Totals stats={stats} locale={locale} />
      <RatingChart points={stats.rating} locale={locale} />
      <HeroTable stats={stats} locale={locale} />
      <RunDays days={groupByDay(runs)} locale={locale} />
    </>
  );
}

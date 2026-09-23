import { useMemo, useState } from 'react';

import type { Locale, ResolvedSpaLocation } from '../../app/router';
import { BAZAARDB_ICON_PATH, BAZAARDB_META_URL, getSiteCopy } from '../../content/site-copy';
import Badge from '../../shared/components/Badge';
import Button from '../../shared/components/Button';
import HeroBadge from '../../shared/components/HeroBadge';
import { ExternalLinkIcon } from '../../shared/components/icons';
import { SectionHeading } from '../../shared/components/PageLayout';
import { SegmentedButton, SegmentedControl } from '../../shared/components/ScopeFilterPanel';
import StatsPageShell from '../../shared/components/StatsPageShell';
import StatusPanel from '../../shared/components/StatusPanel';
import {
  WINDOW_LABELS,
  formatInteger,
  formatNullablePercent,
  formatShortDate,
} from '../../shared/lib/dashboard';
import { getHeroShortLabel } from '../../shared/lib/heroes';
import {
  analyzeHeroes,
  type AnalysisScope,
  type HeroMatchup,
  type MetricWindow,
} from './hero-analysis';
import type { HeroMetricsDataset, HeroMetricsSegment } from './hero-metrics-dataset';
import HeroRankingTable from './HeroRankingTable';
import HeroTrendPanel from './HeroTrendPanel';

type HeroOverviewDashboardProps = {
  locale: Locale;
  location: ResolvedSpaLocation;
  dataset: HeroMetricsDataset;
  requestedScope: AnalysisScope;
  onScopeChange: (scope: AnalysisScope) => void;
};

export default function HeroOverviewDashboard({
  locale,
  location,
  dataset,
  requestedScope,
  onScopeChange,
}: HeroOverviewDashboardProps) {
  const copy = getSiteCopy(locale);
  const heroCopy = copy.stats.heroes;
  const scopeCopy = copy.common.scope;
  const coverageCopy = heroCopy.coverage;
  const noValueLabel = copy.common.noValueLabel;

  const [selectedWindow, setSelectedWindow] = useState<MetricWindow>(requestedScope.window);
  const [selectedSegment, setSelectedSegment] = useState<HeroMetricsSegment>(
    requestedScope.segment
  );
  const [focusedHero, setFocusedHero] = useState<string | null>(null);
  const [syncedScope, setSyncedScope] = useState(requestedScope);

  // Adopt a new requested scope during render instead of in an effect.
  if (
    syncedScope.window !== requestedScope.window ||
    syncedScope.segment !== requestedScope.segment
  ) {
    setSyncedScope(requestedScope);
    setSelectedWindow(requestedScope.window);
    setSelectedSegment(requestedScope.segment);
  }

  const analysis = useMemo(
    () => analyzeHeroes(dataset, { window: selectedWindow, segment: selectedSegment }, focusedHero),
    [dataset, focusedHero, selectedSegment, selectedWindow]
  );

  // Keep the resolved hero so a scope change does not reset the focus.
  if (analysis.focus.hero !== focusedHero) {
    setFocusedHero(analysis.focus.hero);
  }

  const selectSegment = (segment: HeroMetricsSegment) => {
    setSelectedSegment(segment);
    onScopeChange({ window: selectedWindow, segment });
  };

  const selectWindow = (window: MetricWindow) => {
    setSelectedWindow(window);
    onScopeChange({ window, segment: selectedSegment });
  };

  const availableWindows = analysis.scope.availableWindows;
  const availableSegments = analysis.scope.availableSegments;
  const loadedWindowDays = analysis.coverage.usableDates;
  const failedWindowDays = analysis.coverage.failedDates;
  const resolvedFocusedHero = analysis.focus.hero;
  const focusedTrendSeries = analysis.focus.trend ?? undefined;
  const matchups = analysis.focus.matchups;
  const hasAnyData = dataset.days.length > 0;
  const rankingHasRows = analysis.ranking.length > 0;

  return (
    <StatsPageShell
      location={location}
      title={heroCopy.title}
      generatedAt={analysis.generatedAt}
      actions={
        <Button
          href={BAZAARDB_META_URL}
          target="_blank"
          rel="noreferrer"
          icon={
            <img
              src={BAZAARDB_ICON_PATH}
              alt=""
              aria-hidden="true"
              className="size-4 shrink-0 rounded-[3px] object-contain"
              decoding="async"
            />
          }
        >
          {heroCopy.detailLinkLabel}
          <ExternalLinkIcon className="text-text-3" />
        </Button>
      }
      filters={null}
    >
      {!hasAnyData ? (
        <StatusPanel
          kind="empty"
          title={heroCopy.unavailable.title}
          body={heroCopy.unavailable.body}
        />
      ) : (
        <>
          <section data-testid="hero-focus-panel" className="grid min-w-0 gap-4">
            <div className="flex flex-col gap-1">
              <SectionHeading>{heroCopy.trend.title}</SectionHeading>
              <p className="text-[13px] text-text-2">
                {WINDOW_LABELS['7d']} {heroCopy.trend.winrateTrend}
              </p>
            </div>

            <div className="grid min-w-0 gap-4 xl:grid-cols-[minmax(0,1.65fr)_minmax(300px,0.85fr)]">
              <HeroTrendPanel
                locale={locale}
                trend={analysis.trend}
                focusedHero={resolvedFocusedHero}
                onFocusHero={setFocusedHero}
              />

              <section data-testid="matchup-panel" className="panel flex flex-col gap-4 p-4 sm:p-5">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <h3 className="text-[15px] font-semibold text-text-1">
                    {heroCopy.matchups.title}
                  </h3>
                  {focusedTrendSeries ? (
                    <div
                      data-testid="selected-matchup-hero"
                      aria-label={`${heroCopy.matchups.selectedHeroLabel}: ${focusedTrendSeries.hero}`}
                      className="min-w-0"
                    >
                      <HeroBadge hero={focusedTrendSeries.hero} label="full" selected>
                        {getHeroShortLabel(focusedTrendSeries.hero)} ·{' '}
                        {WINDOW_LABELS[selectedWindow]}
                      </HeroBadge>
                    </div>
                  ) : null}
                </div>
                <MatchupList
                  rows={matchups}
                  matchupCopy={heroCopy.matchups}
                  locale={locale}
                  noValueLabel={noValueLabel}
                />
              </section>
            </div>
          </section>

          <section data-testid="ranking-panel" className="panel overflow-hidden">
            <div className="flex flex-col gap-4 border-b border-line px-5 py-4 lg:flex-row lg:items-end lg:justify-between">
              <div className="flex flex-wrap items-center gap-3">
                <SectionHeading>{heroCopy.snapshot.title}</SectionHeading>
                <Badge>{heroCopy.snapshot.label}</Badge>
              </div>

              {availableWindows.length > 0 ? (
                <div
                  data-testid="ranking-filters"
                  role="group"
                  aria-label={copy.common.filters}
                  className="flex flex-wrap items-end gap-x-6 gap-y-3"
                >
                  <div
                    role="group"
                    aria-label={scopeCopy.window}
                    className="flex min-w-0 flex-col gap-1.5"
                  >
                    <p className="text-xs font-medium text-text-2">{scopeCopy.window}</p>
                    <SegmentedControl>
                      {availableWindows.map((option) => (
                        <SegmentedButton
                          key={option}
                          active={option === selectedWindow}
                          onClick={() => selectWindow(option)}
                        >
                          {WINDOW_LABELS[option]}
                        </SegmentedButton>
                      ))}
                    </SegmentedControl>
                  </div>
                  <div
                    role="group"
                    aria-label={scopeCopy.segment}
                    className="flex min-w-0 flex-col gap-1.5"
                  >
                    <p className="text-xs font-medium text-text-2">{scopeCopy.segment}</p>
                    <SegmentedControl>
                      {availableSegments.map((option) => (
                        <SegmentedButton
                          key={option}
                          active={option === selectedSegment}
                          onClick={() => selectSegment(option)}
                        >
                          {scopeCopy.segmentLabels[option]}
                        </SegmentedButton>
                      ))}
                    </SegmentedControl>
                  </div>
                </div>
              ) : null}
            </div>

            {failedWindowDays.length > 0 && rankingHasRows ? (
              <p className="flex flex-wrap items-center gap-x-2 gap-y-1 border-b border-line px-5 py-2.5 text-[13px] text-text-2">
                <span aria-hidden="true" className="size-1.5 shrink-0 rounded-full bg-warning" />
                {coverageCopy.someDaysUnavailable}{' '}
                <span className="text-text-3 tabular-nums">
                  {loadedWindowDays.length > 0
                    ? `${formatShortDate(loadedWindowDays[0], locale)} – ${formatShortDate(
                        loadedWindowDays.at(-1)!,
                        locale
                      )}`
                    : ''}
                </span>
              </p>
            ) : null}

            {rankingHasRows ? (
              <HeroRankingTable
                locale={locale}
                rows={analysis.ranking}
                focusedHero={resolvedFocusedHero}
                onFocusHero={setFocusedHero}
              />
            ) : (
              <StatusPanel
                kind="empty"
                framed={false}
                headingLevel={3}
                title={
                  loadedWindowDays.length === 0
                    ? heroCopy.unavailable.title
                    : heroCopy.segmentEmpty.title
                }
                body={
                  loadedWindowDays.length === 0
                    ? heroCopy.unavailable.body
                    : heroCopy.segmentEmpty.body
                }
              />
            )}
          </section>
        </>
      )}
    </StatsPageShell>
  );
}

type MatchupsCopy = ReturnType<typeof getSiteCopy>['stats']['heroes']['matchups'];

function MatchupList({
  rows,
  matchupCopy,
  locale,
  noValueLabel,
}: {
  rows: HeroMatchup[];
  matchupCopy: MatchupsCopy;
  locale: Locale;
  noValueLabel: string;
}) {
  if (rows.length === 0) {
    return <p className="text-sm text-text-2">{matchupCopy.empty}</p>;
  }

  return (
    <ul data-testid="matchup-list" className="grid gap-x-6 gap-y-2.5 md:grid-cols-2 xl:grid-cols-1">
      {rows.map((row) => {
        const noValue = row.isLowSample || row.winRate == null;
        return (
          <li
            key={row.opponentHero}
            className="grid grid-cols-[3.75rem_minmax(0,1fr)_auto] items-center gap-3"
          >
            <HeroBadge hero={row.opponentHero} />
            <span className="flex min-w-0 items-center gap-2.5">
              <span
                className="w-12 shrink-0 text-right text-[13px] font-medium text-text-1 tabular-nums"
                aria-label={noValue ? noValueLabel : undefined}
              >
                {row.isLowSample ? '—' : formatNullablePercent(row.winRate)}
              </span>
              {row.isLowSample ? (
                <Badge>{matchupCopy.lowSampleTag}</Badge>
              ) : (
                <span
                  aria-hidden="true"
                  className="h-1.5 min-w-0 flex-1 overflow-hidden rounded-full bg-hover"
                >
                  <span
                    className="block h-full rounded-full bg-text-3"
                    style={{ width: `${(row.winRate ?? 0) * 100}%` }}
                  />
                </span>
              )}
            </span>
            <span className="text-xs text-text-3 tabular-nums">
              {formatInteger(row.decided, locale)} {matchupCopy.sample}
            </span>
          </li>
        );
      })}
    </ul>
  );
}

import { useEffect, useMemo, useState, type CSSProperties } from 'react';

import type { Locale, ResolvedSpaLocation } from '../../app/router';
import { BAZAARDB_ICON_PATH, BAZAARDB_META_URL, getSiteCopy } from '../../content/site-copy';
import HeroBadge from '../../shared/components/HeroBadge';
import { SegmentedButton, SegmentedControl } from '../../shared/components/ScopeFilterPanel';
import StatsPageShell from '../../shared/components/StatsPageShell';
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

  useEffect(() => {
    setSelectedWindow(requestedScope.window);
    setSelectedSegment(requestedScope.segment);
  }, [requestedScope.segment, requestedScope.window]);

  const analysis = useMemo(
    () => analyzeHeroes(dataset, { window: selectedWindow, segment: selectedSegment }, focusedHero),
    [dataset, focusedHero, selectedSegment, selectedWindow]
  );

  useEffect(() => {
    if (analysis.focus.hero !== focusedHero) {
      setFocusedHero(analysis.focus.hero);
    }
  }, [analysis.focus.hero, focusedHero]);

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

  const emptyState = !hasAnyData ? (
    <section className="surface flex flex-col items-center gap-3 px-6 py-16 text-center">
      <h2 className="font-display text-2xl font-semibold text-[color:var(--color-text-base)]">
        {heroCopy.unavailable.title}
      </h2>
      <p className="max-w-md text-sm leading-6 text-[color:var(--color-text-muted)]">
        {heroCopy.unavailable.body}
      </p>
    </section>
  ) : null;

  return (
    <StatsPageShell
      locale={locale}
      location={location}
      eyebrow={heroCopy.eyebrow}
      title={heroCopy.title}
      generatedAt={analysis.generatedAt}
      actions={
        <a
          href={BAZAARDB_META_URL}
          target="_blank"
          rel="noreferrer"
          className="group inline-flex min-h-10 items-center justify-center gap-2 rounded-full border border-[color:var(--color-border-bright)] px-4 py-2 text-sm font-medium text-[color:var(--color-accent-bright)] transition hover:border-[color:var(--color-accent)] hover:bg-[rgba(232,185,74,0.08)] hover:text-[color:var(--color-text-base)]"
        >
          <span
            aria-hidden="true"
            className="flex h-5 w-5 shrink-0 items-center justify-center overflow-hidden rounded-sm bg-[rgba(255,245,220,0.06)]"
          >
            <img
              src={BAZAARDB_ICON_PATH}
              alt=""
              className="h-full w-full object-contain"
              decoding="async"
            />
          </span>
          <span>{heroCopy.detailLinkLabel}</span>
          <span aria-hidden="true" className="transition group-hover:translate-x-0.5">
            ↗
          </span>
        </a>
      }
      filters={null}
    >
      {emptyState ?? (
        <section className="grid min-w-0 gap-6">
          <section
            data-testid="hero-focus-panel"
            className="surface relative overflow-hidden p-4 sm:p-6"
          >
            <div
              aria-hidden="true"
              className="pointer-events-none absolute inset-0"
              style={{
                background: focusedTrendSeries
                  ? `radial-gradient(ellipse at 78% 12%, ${focusedTrendSeries.color}18, transparent 55%)`
                  : undefined,
              }}
            />

            <div className="relative grid min-w-0 gap-5">
              <div>
                <p className="eyebrow eyebrow-rule">
                  {WINDOW_LABELS['7d']} {heroCopy.trend.winrateTrend}
                </p>
                <h2 className="mt-3 font-display text-2xl font-semibold tracking-tight text-[color:var(--color-text-base)] sm:text-3xl">
                  {heroCopy.trend.title}
                </h2>
              </div>

              <div className="grid min-w-0 gap-4 xl:grid-cols-[minmax(0,1.65fr)_minmax(300px,0.85fr)]">
                <HeroTrendPanel
                  locale={locale}
                  trend={analysis.trend}
                  focusedHero={resolvedFocusedHero}
                  onFocusHero={setFocusedHero}
                />

                <section
                  data-testid="matchup-panel"
                  className="min-w-0 rounded-2xl border border-[color:var(--color-border-soft)] bg-[rgba(10,8,5,0.58)] p-4 sm:p-5"
                >
                  <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                    <h3 className="font-display text-lg font-semibold text-[color:var(--color-text-base)]">
                      {heroCopy.matchups.title}
                    </h3>
                    {focusedTrendSeries ? (
                      <div
                        data-testid="selected-matchup-hero"
                        aria-label={`${heroCopy.matchups.selectedHeroLabel}: ${focusedTrendSeries.hero}`}
                        className="inline-flex w-full min-w-0 items-center gap-2 rounded-lg border border-[color:var(--color-border-soft)] bg-[rgba(15,12,8,0.72)] px-3 py-2 sm:w-auto"
                      >
                        <span
                          className="h-5 w-1 shrink-0 rounded-sm"
                          style={{
                            backgroundColor: focusedTrendSeries.color,
                            boxShadow: `0 0 10px ${focusedTrendSeries.color}88`,
                          }}
                          aria-hidden="true"
                        />
                        <span className="min-w-0 truncate font-display text-base font-semibold text-[color:var(--color-text-base)]">
                          {focusedTrendSeries.hero}
                        </span>
                        <span className="tnum shrink-0 text-[0.68rem] uppercase tracking-[0.14em] text-[color:var(--color-text-muted)]">
                          {getHeroShortLabel(focusedTrendSeries.hero)} ·{' '}
                          {WINDOW_LABELS[selectedWindow]}
                        </span>
                      </div>
                    ) : null}
                  </div>
                  <div className="mt-4">
                    <MatchupList
                      rows={matchups}
                      matchupCopy={heroCopy.matchups}
                      locale={locale}
                      noValueLabel={noValueLabel}
                      className="grid gap-2 md:grid-cols-2 xl:grid-cols-1"
                    />
                  </div>
                </section>
              </div>
            </div>
          </section>

          <section data-testid="ranking-panel" className="surface overflow-hidden">
            <div className="border-b border-[color:var(--color-border-soft)] px-6 py-5">
              <div className="flex flex-col gap-5 xl:flex-row xl:items-end xl:justify-between">
                <div>
                  <p className="eyebrow eyebrow-rule">{heroCopy.snapshot.label}</p>
                  <h2 className="mt-2 font-display text-2xl font-semibold tracking-tight text-[color:var(--color-text-base)]">
                    {heroCopy.snapshot.title}
                  </h2>
                </div>

                {availableWindows.length > 0 ? (
                  <div
                    data-testid="ranking-filters"
                    role="group"
                    aria-label={copy.common.filters}
                    className="flex flex-wrap items-end gap-x-8 gap-y-4"
                  >
                    <div role="group" aria-label={scopeCopy.window} className="min-w-0 space-y-2.5">
                      <p className="eyebrow text-[0.7rem] tracking-[0.22em]">{scopeCopy.window}</p>
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
                      className="min-w-0 space-y-2.5"
                    >
                      <p className="eyebrow text-[0.7rem] tracking-[0.22em]">{scopeCopy.segment}</p>
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
            </div>

            {failedWindowDays.length > 0 && rankingHasRows ? (
              <p className="border-b border-[color:var(--color-border-soft)] px-6 py-2.5 text-[0.74rem] text-[color:var(--color-text-muted)]">
                {coverageCopy.someDaysUnavailable}{' '}
                <span className="tnum text-[color:var(--color-text-faint)]">
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
              <div className="px-6 py-12 text-center">
                <h3 className="font-display text-xl font-semibold text-[color:var(--color-text-base)]">
                  {loadedWindowDays.length === 0
                    ? heroCopy.unavailable.title
                    : heroCopy.segmentEmpty.title}
                </h3>
                <p className="mt-2 text-sm text-[color:var(--color-text-muted)]">
                  {loadedWindowDays.length === 0
                    ? heroCopy.unavailable.body
                    : heroCopy.segmentEmpty.body}
                </p>
              </div>
            )}
          </section>
        </section>
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
  className = 'grid gap-2 sm:grid-cols-2 lg:grid-cols-3',
}: {
  rows: HeroMatchup[];
  matchupCopy: MatchupsCopy;
  locale: Locale;
  noValueLabel: string;
  className?: string;
}) {
  if (rows.length === 0) {
    return <p className="text-sm text-[color:var(--color-text-muted)]">{matchupCopy.empty}</p>;
  }

  return (
    <ul data-testid="matchup-list" className={className}>
      {rows.map((row) => (
        <li key={row.opponentHero} className="grid grid-cols-[auto_1fr_auto] items-center gap-2.5">
          <HeroBadge hero={row.opponentHero} size="sm" />
          <span
            className="databar databar-pos relative block h-6 rounded-sm"
            style={
              {
                '--bar-width': `${(row.isLowSample ? 0 : (row.winRate ?? 0)) * 100}%`,
              } as CSSProperties
            }
          >
            <span
              className="absolute inset-y-0 left-2 flex items-center gap-2 tnum text-[0.78rem] text-[color:var(--color-text-base)]"
              aria-label={row.isLowSample || row.winRate == null ? noValueLabel : undefined}
            >
              {row.isLowSample ? '—' : formatNullablePercent(row.winRate)}
              {row.isLowSample ? (
                <span className="rounded-sm border border-[color:var(--color-border-soft)] px-1 py-0.5 text-[0.58rem] uppercase tracking-[0.1em] text-[color:var(--color-text-faint)]">
                  {matchupCopy.lowSampleTag}
                </span>
              ) : null}
            </span>
          </span>
          <span className="tnum text-[0.7rem] text-[color:var(--color-text-faint)]">
            {formatInteger(row.decided, locale)} {matchupCopy.sample}
          </span>
        </li>
      ))}
    </ul>
  );
}

import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, test, vi } from 'vitest';

import { createMemorySpaLocationAdapter, createSpaLocation } from '../src/app/router';
import { BAZAARDB_ICON_PATH, BAZAARDB_META_URL } from '../src/content/site-copy';
import HeroOverviewDashboard from '../src/features/heroes/HeroOverviewDashboard';
import type {
  HeroMetricsDataset,
  HeroMetricsDay,
  HeroMetricsRow,
} from '../src/features/heroes/hero-metrics-dataset';

const DAYS = ['2026-06-04', '2026-06-05', '2026-06-06'];

function makeRow(
  overrides: Omit<Partial<HeroMetricsRow>, 'hero'> & { hero: string }
): HeroMetricsRow {
  const { hero, ...rest } = overrides;
  return {
    hero,
    segment: 'legend',
    runs: { completed: 0, scored: 0, ten_win: 0 },
    outcomes: { perfect: 0, gold: 0, silver: 0, bronze: 0 },
    ten_win_days: { known_count: 0, sum_days: 0 },
    matchups: [],
    ...rest,
  };
}

function makeDayRows(dayIndex: number): HeroMetricsRow[] {
  const stelleTenWins = 40 + dayIndex;
  return [
    makeRow({
      hero: 'Stelle',
      runs: { completed: 100, scored: 100, ten_win: stelleTenWins },
      outcomes: { perfect: 0, gold: stelleTenWins, silver: 20, bronze: 20 },
      ten_win_days: { known_count: stelleTenWins, sum_days: 11 * stelleTenWins },
      matchups: [
        { opponent_hero: 'Mak', decided: 20, wins: 15, losses: 5 },
        { opponent_hero: 'Jules', decided: 10, wins: 4, losses: 6 },
      ],
    }),
    makeRow({
      hero: 'Jules',
      runs: { completed: 100, scored: 100, ten_win: 50 },
      outcomes: { perfect: 30, gold: 20, silver: 20, bronze: 20 },
      ten_win_days: { known_count: 45, sum_days: 10 * 30 + 12 * 15 },
      matchups: [
        { opponent_hero: 'Stelle', decided: 100, wins: 70, losses: 30 },
        { opponent_hero: 'Vanessa', decided: 30, wins: 10, losses: 20 },
        { opponent_hero: 'Dooley', decided: 5, wins: 4, losses: 1 },
        { opponent_hero: 'Jules', decided: 50, wins: 25, losses: 25 },
      ],
    }),
    makeRow({
      hero: 'Vanessa',
      runs:
        dayIndex === 1
          ? { completed: 0, scored: 0, ten_win: 0 }
          : { completed: 80, scored: 80, ten_win: 20 },
      outcomes:
        dayIndex === 1
          ? { perfect: 0, gold: 0, silver: 0, bronze: 0 }
          : { perfect: 20, gold: 0, silver: 20, bronze: 20 },
    }),
    makeRow({ hero: 'Karnok' }),
    makeRow({
      hero: 'Common',
      runs: { completed: 10, scored: 10, ten_win: 9 },
      outcomes: { perfect: 0, gold: 9, silver: 1, bronze: 0 },
    }),
    makeRow({
      hero: 'Mak',
      segment: 'non_legend',
      runs: { completed: 50, scored: 50, ten_win: 20 },
      outcomes: { perfect: 0, gold: 20, silver: 10, bronze: 10 },
      ten_win_days: { known_count: 20, sum_days: 220 },
      matchups: [{ opponent_hero: 'Stelle', decided: 25, wins: 10, losses: 15 }],
    }),
    makeRow({
      hero: 'Dooley',
      segment: 'non_legend',
      runs: { completed: 30, scored: 30, ten_win: 12 },
      outcomes: { perfect: 0, gold: 12, silver: 6, bronze: 6 },
      ten_win_days: { known_count: 12, sum_days: 132 },
    }),
    makeRow({
      hero: 'Vanessa',
      segment: 'non_legend',
      runs: { completed: 20, scored: 20, ten_win: 10 },
      outcomes: { perfect: 2, gold: 8, silver: 4, bronze: 4 },
      ten_win_days: { known_count: 10, sum_days: 100 },
    }),
  ];
}

function makeDay(day: string, dayIndex: number): HeroMetricsDay {
  return { day, rows: makeDayRows(dayIndex) };
}

function renderDashboard(options?: {
  excludeDays?: string[];
  days?: HeroMetricsDay[];
  url?: string;
}) {
  const excludeDays = options?.excludeDays ?? [];
  const url = options?.url ?? '/heroes?lang=en&w=3d';
  const usableDates = options?.days
    ? options.days.map((day) => day.day)
    : DAYS.filter((day) => !excludeDays.includes(day));
  const days = options?.days ?? usableDates.map((day) => makeDay(day, DAYS.indexOf(day)));
  const dataset: HeroMetricsDataset = {
    generatedAt: '2026-06-07T09:04:17Z',
    window: { start: DAYS[0], end: DAYS.at(-1)!, days: DAYS.length },
    days,
    coverage: {
      requestedDates: DAYS,
      usableDates,
      failedDates: excludeDays,
    },
  };
  const memory = createMemorySpaLocationAdapter(url);
  const location = createSpaLocation(memory.adapter).current();
  const onScopeChange = vi.fn();

  return {
    ...render(
      <HeroOverviewDashboard
        locale={location.locale}
        location={location}
        dataset={dataset}
        requestedScope={location.scope}
        onScopeChange={onScopeChange}
      />
    ),
    onScopeChange,
  };
}

function getRankingTable() {
  const table = screen
    .getAllByRole('table')
    .find((candidate) => candidate.querySelectorAll('col').length === 11);
  expect(table).toBeDefined();
  return table!;
}

describe('HeroOverviewDashboard', () => {
  test('consumes normalized scope without owning URL canonicalization', () => {
    const { onScopeChange } = renderDashboard({
      url: '/heroes?lang=zh&w=invalid&t=high&s=all',
    });

    expect(onScopeChange).not.toHaveBeenCalled();
    expect(screen.queryByRole('button', { name: '高' })).not.toBeInTheDocument();
  });

  test('hydrates window and segment controls inside the ranking panel and keeps the BazaarDB link', () => {
    renderDashboard({ url: '/heroes?lang=en&w=3d&s=legend' });

    const detailDataLink = screen.getByRole('link', { name: 'View detailed stats on BazaarDB' });
    expect(detailDataLink).toHaveAttribute('href', BAZAARDB_META_URL);
    expect(detailDataLink).toHaveAttribute('target', '_blank');
    expect(detailDataLink.querySelector('img')).toHaveAttribute('src', BAZAARDB_ICON_PATH);
    expect(
      screen.queryByRole('link', { name: 'Learn how data syncs to BazaarDB' })
    ).not.toBeInTheDocument();

    const filters = within(screen.getByTestId('ranking-panel')).getByRole('group', {
      name: 'Filters',
    });
    expect(within(filters).getByRole('button', { name: '3D' })).toHaveAttribute(
      'aria-pressed',
      'true'
    );
    expect(within(filters).getByRole('button', { name: 'Legend' })).toHaveAttribute(
      'aria-pressed',
      'true'
    );
    expect(within(filters).getByRole('button', { name: 'All' })).toHaveAttribute(
      'aria-pressed',
      'false'
    );
    expect(within(filters).getByRole('button', { name: 'Non-Legend' })).toBeInTheDocument();
    expect(screen.queryByTestId('coverage-strip')).not.toBeInTheDocument();
  });

  test('one segment control drives ranking, trend, and matchups', () => {
    const { container, onScopeChange } = renderDashboard();

    fireEvent.click(screen.getByRole('button', { name: 'Non-Legend' }));
    expect(onScopeChange).toHaveBeenCalledWith({ window: '3d', segment: 'non_legend' });

    const table = getRankingTable();
    expect(within(table).getByText('Mak')).toBeInTheDocument();
    expect(within(table).getByText('Dooley')).toBeInTheDocument();
    expect(within(table).queryByText('Jules')).not.toBeInTheDocument();
    expect(
      container.querySelector('[data-testid="daily-winrate-line"][data-hero="Mak"]')
    ).not.toBeNull();
    fireEvent.click(within(table).getByRole('button', { name: 'Mak' }));
    expect(screen.getByTestId('selected-matchup-hero')).toHaveTextContent('Mak');
    expect(screen.getByTestId('matchup-list')).toHaveTextContent('Stelle');
  });

  test('uses the actual three-day snapshot for 7D coverage and trend without inventing four days', () => {
    const { onScopeChange } = renderDashboard();
    const chart = screen.getByTestId('daily-winrate-chart');

    expect(within(chart).getByText('Jun 4')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: '1D' }));
    expect(onScopeChange).toHaveBeenCalledWith({ window: '1d', segment: 'all' });
    expect(within(chart).getByText('Jun 4')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: '7D' }));
    expect(onScopeChange).toHaveBeenCalledWith({ window: '7d', segment: 'all' });
    expect(within(chart).getByText('Jun 4')).toBeInTheDocument();
    expect(within(chart).getByText('Jun 6')).toBeInTheDocument();
  });

  test('renders daily trend gaps, excludes non-canonical heroes, and keeps tooltips working', () => {
    const { container } = renderDashboard({ url: '/heroes?lang=en&w=3d&s=legend' });
    const vanessaLine = container.querySelector(
      '[data-testid="daily-winrate-line"][data-hero="Vanessa"]'
    )!;
    const vanessaCircles = vanessaLine.querySelectorAll('circle');

    expect(vanessaCircles).toHaveLength(2);
    expect(vanessaLine.querySelectorAll('polyline:not([stroke="transparent"])')).toHaveLength(2);
    expect(container.querySelector('[data-hero="Common"]')).toBeNull();

    fireEvent.click(within(getRankingTable()).getByRole('button', { name: 'Vanessa' }));
    expect(screen.getByText('Some days have no calculable trend point.')).toBeInTheDocument();
    fireEvent.mouseEnter(vanessaCircles[0]);
    expect(screen.getByTestId('trend-point-tooltip')).toBeInTheDocument();
  });

  test('keeps matchups ordered, focused, and tagged for low samples', () => {
    renderDashboard({ url: '/heroes?lang=en&w=3d&s=legend' });

    const list = screen.getByTestId('matchup-list');
    const heroOrder = Array.from(list.querySelectorAll('[data-hero-badge]')).map((badge) =>
      badge.getAttribute('data-hero-badge')
    );
    expect(heroOrder).toEqual(['Stelle', 'Jules', 'Vanessa', 'Dooley']);
    expect(within(list).getByText('70.0%')).toBeInTheDocument();
    expect(within(list).getByText('low sample')).toBeInTheDocument();
    expect(within(list).getByText(/15 battles/)).toBeInTheDocument();

    fireEvent.click(within(getRankingTable()).getByRole('button', { name: 'Stelle' }));
    expect(screen.getByTestId('selected-matchup-hero')).toHaveTextContent('Stelle');
    expect(screen.getByTestId('matchup-list')).toHaveTextContent('Mak');
  });

  test('shows all outcome buckets including derived Misfortune and has no battle-day UI', () => {
    renderDashboard();

    const table = getRankingTable();
    expect(within(table).getByRole('button', { name: 'Misfortune' })).toBeInTheDocument();
    expect(within(table).getByText('10.0%')).toBeInTheDocument();
    expect(screen.queryByTestId('stage-panel')).not.toBeInTheDocument();
    expect(screen.queryByText('Battle win rate')).not.toBeInTheDocument();
    expect(screen.queryByText('Day 1')).not.toBeInTheDocument();
  });

  test('failed dates remain explicit while usable snapshot data stays visible', () => {
    renderDashboard({ excludeDays: ['2026-06-05'] });

    expect(
      screen.getByText('Some days are unavailable; this view includes loaded days only.')
    ).toBeInTheDocument();
    expect(screen.queryByTestId('coverage-strip')).not.toBeInTheDocument();
    expect(getRankingTable()).toBeInTheDocument();
  });

  test('renders snapshot unavailable for empty usable data', () => {
    renderDashboard({ excludeDays: DAYS, days: [] });

    expect(screen.getByRole('heading', { name: 'Snapshot unavailable' })).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });
});

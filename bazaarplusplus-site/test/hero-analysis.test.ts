// @vitest-environment node

import { describe, expect, test } from 'vitest';

import { analyzeHeroes } from '../src/features/heroes/hero-analysis';
import type {
  HeroMetricsDataset,
  HeroMetricsDay,
  HeroMetricsRow,
} from '../src/features/heroes/hero-metrics-dataset';

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

function makeDay(day: string, rows: HeroMetricsRow[]): HeroMetricsDay {
  return { day, rows };
}

function makeDataset(
  requestedDates: string[],
  days: HeroMetricsDay[],
  failedDates: string[] = []
): HeroMetricsDataset {
  return {
    generatedAt: '2026-06-08T09:00:00Z',
    window: {
      start: requestedDates[0] ?? '2026-06-01',
      end: requestedDates.at(-1) ?? '2026-06-07',
      days: requestedDates.length,
    },
    days,
    coverage: {
      requestedDates,
      usableDates: days.map((payload) => payload.day),
      failedDates,
    },
  };
}

function makeCountedRow(hero: string, completed: number, tenWin: number): HeroMetricsRow {
  return makeRow({
    hero,
    runs: { completed, scored: completed, ten_win: tenWin },
    outcomes: { perfect: 0, gold: tenWin, silver: 0, bronze: 0 },
    ten_win_days: { known_count: tenWin, sum_days: tenWin * 10 },
  });
}

describe('analyzeHeroes', () => {
  test('aggregates the latest 1D, 3D, and 7D windows field by field', () => {
    const dates = Array.from({ length: 7 }, (_, index) => `2026-06-0${index + 1}`);
    const dataset = makeDataset(
      dates,
      dates.map((day, index) => {
        const row = makeCountedRow('Vanessa', (index + 1) * 10, index + 1);
        row.matchups = [
          {
            opponent_hero: 'Mak',
            decided: 20,
            wins: 8 + index,
            losses: 12 - index,
          },
        ];
        return makeDay(day, [row]);
      })
    );

    const oneDay = analyzeHeroes(dataset, { window: '1d', segment: 'legend' }, null);
    const threeDay = analyzeHeroes(dataset, { window: '3d', segment: 'legend' }, null);
    const sevenDay = analyzeHeroes(dataset, { window: '7d', segment: 'legend' }, null);

    expect(oneDay.ranking[0]).toMatchObject({ runsCompleted: 70, tenWinCount: 7 });
    expect(threeDay.ranking[0]).toMatchObject({ runsCompleted: 180, tenWinCount: 18 });
    expect(sevenDay.ranking[0]).toMatchObject({ runsCompleted: 280, tenWinCount: 28 });
    expect(sevenDay.focus.matchups[0]).toMatchObject({
      opponentHero: 'Mak',
      decided: 140,
      wins: 77,
      losses: 63,
    });
    expect(threeDay.coverage).toEqual({
      requestedDates: dates.slice(-3),
      usableDates: dates.slice(-3),
      failedDates: [],
      nominalDateCount: 3,
    });
    expect(threeDay.scope.availableWindows).toEqual(['1d', '3d', '7d']);
    expect(threeDay.scope.availableSegments).toEqual(['all', 'legend', 'non_legend']);
    expect(sevenDay.coverage.nominalDateCount).toBe(7);
    expect(sevenDay.trend.dayAxis).toEqual(dates);
    expect(sevenDay.trend.series[0]?.points).toHaveLength(7);
  });

  test('uses all five available days for 7D and only the latest three for 3D', () => {
    const dates = ['2026-06-03', '2026-06-04', '2026-06-05', '2026-06-06', '2026-06-07'];
    const dataset = makeDataset(
      dates,
      dates.map((day, index) => {
        const row = makeCountedRow('Vanessa', (index + 1) * 10, index + 1);
        row.matchups = [
          {
            opponent_hero: 'Mak',
            decided: 20,
            wins: 8 + index,
            losses: 12 - index,
          },
        ];
        return makeDay(day, [row]);
      })
    );

    const threeDay = analyzeHeroes(dataset, { window: '3d', segment: 'legend' }, 'Vanessa');
    const sevenDay = analyzeHeroes(dataset, { window: '7d', segment: 'legend' }, 'Vanessa');

    expect(threeDay.ranking[0]).toMatchObject({ runsCompleted: 120, tenWinCount: 12 });
    expect(threeDay.focus.matchups[0]).toMatchObject({ decided: 60, wins: 33, losses: 27 });
    expect(threeDay.coverage).toEqual({
      requestedDates: dates.slice(-3),
      usableDates: dates.slice(-3),
      failedDates: [],
      nominalDateCount: 3,
    });

    expect(sevenDay.ranking[0]).toMatchObject({ runsCompleted: 150, tenWinCount: 15 });
    expect(sevenDay.focus.matchups[0]).toMatchObject({ decided: 100, wins: 50, losses: 50 });
    expect(sevenDay.coverage).toEqual({
      requestedDates: dates,
      usableDates: dates,
      failedDates: [],
      nominalDateCount: 5,
    });
    expect(sevenDay.trend.dayAxis).toEqual(dates);
    expect(sevenDay.trend.series[0]?.points).toHaveLength(5);
  });

  test('derives all by summing legend and non_legend counters, outcomes, and matchups', () => {
    const dataset = makeDataset(
      ['2026-06-07'],
      [
        makeDay('2026-06-07', [
          makeRow({
            hero: 'Vanessa',
            segment: 'legend',
            runs: { completed: 40, scored: 40, ten_win: 20 },
            outcomes: { perfect: 5, gold: 15, silver: 10, bronze: 5 },
            ten_win_days: { known_count: 20, sum_days: 200 },
            matchups: [{ opponent_hero: 'Mak', decided: 30, wins: 18, losses: 12 }],
          }),
          makeRow({
            hero: 'Vanessa',
            segment: 'non_legend',
            runs: { completed: 60, scored: 50, ten_win: 10 },
            outcomes: { perfect: 2, gold: 8, silver: 10, bronze: 20 },
            ten_win_days: { known_count: 8, sum_days: 96 },
            matchups: [{ opponent_hero: 'Mak', decided: 20, wins: 8, losses: 12 }],
          }),
          makeRow({
            hero: 'Mak',
            segment: 'legend',
            runs: { completed: 100, scored: 0, ten_win: 0 },
          }),
        ]),
      ]
    );

    const all = analyzeHeroes(dataset, { window: '7d', segment: 'all' }, 'Vanessa');
    const legend = analyzeHeroes(dataset, { window: '3d', segment: 'legend' }, 'Vanessa');
    const vanessa = all.ranking.find((row) => row.hero === 'Vanessa')!;

    expect(vanessa).toMatchObject({
      runsCompleted: 100,
      scoredRuns: 90,
      tenWinCount: 30,
      runShare: 0.5,
      tenWinRate: 0.3,
    });
    expect(vanessa.avgRunDays10w).toBeCloseTo(296 / 28, 12);
    expect(vanessa.perfectRate).toBeCloseTo(7 / 90, 12);
    expect(vanessa.goldRate).toBeCloseTo(23 / 90, 12);
    expect(vanessa.silverRate).toBeCloseTo(20 / 90, 12);
    expect(vanessa.bronzeRate).toBeCloseTo(25 / 90, 12);
    expect(vanessa.misfortuneRate).toBeCloseTo(15 / 90, 12);
    expect(legend.ranking.find((row) => row.hero === 'Vanessa')?.runsCompleted).toBe(40);
    expect(all.focus.matchups).toEqual([
      {
        opponentHero: 'Mak',
        decided: 50,
        wins: 26,
        losses: 24,
        winRate: 0.52,
        isLowSample: false,
      },
    ]);
    expect(all.coverage).toEqual({
      requestedDates: ['2026-06-07'],
      usableDates: ['2026-06-07'],
      failedDates: [],
      nominalDateCount: 1,
    });
    expect(all.trend.dayAxis).toEqual(['2026-06-07']);
    expect(all.trend.series.find((series) => series.hero === 'Vanessa')?.points).toEqual([
      { day: '2026-06-07', winRate: 0.3 },
    ]);
  });

  test('returns null for every zero denominator, including all-segment and matchup rates', () => {
    const dataset = makeDataset(
      ['2026-06-07'],
      [
        makeDay('2026-06-07', [
          makeRow({
            hero: 'Vanessa',
            segment: 'legend',
            matchups: [{ opponent_hero: 'Mak', decided: 0, wins: 0, losses: 0 }],
          }),
          makeRow({ hero: 'Vanessa', segment: 'non_legend' }),
        ]),
      ]
    );

    const analysis = analyzeHeroes(dataset, { window: '1d', segment: 'all' }, 'Vanessa');
    const row = analysis.ranking[0];

    expect(row.runShare).toBeNull();
    expect(row.tenWinRate).toBeNull();
    expect(row.avgRunDays10w).toBeNull();
    expect(row.perfectRate).toBeNull();
    expect(row.goldRate).toBeNull();
    expect(row.silverRate).toBeNull();
    expect(row.bronzeRate).toBeNull();
    expect(row.misfortuneRate).toBeNull();
    expect(analysis.focus.matchups[0]).toMatchObject({ winRate: null, isLowSample: true });
  });

  test('derives the seven-day daily trend after segment aggregation and splits zero-denominator gaps', () => {
    const dates = ['2026-06-05', '2026-06-06', '2026-06-07'];
    const dataset = makeDataset(dates, [
      makeDay('2026-06-05', [
        makeCountedRow('Vanessa', 10, 4),
        { ...makeCountedRow('Vanessa', 10, 6), segment: 'non_legend' },
      ]),
      makeDay('2026-06-06', [
        makeRow({ hero: 'Vanessa', segment: 'legend' }),
        makeRow({ hero: 'Vanessa', segment: 'non_legend' }),
      ]),
      makeDay('2026-06-07', [
        makeCountedRow('Vanessa', 20, 5),
        { ...makeCountedRow('Vanessa', 20, 15), segment: 'non_legend' },
      ]),
    ]);

    const analysis = analyzeHeroes(dataset, { window: '1d', segment: 'all' }, 'Vanessa');
    const trend = analysis.trend.series[0];

    expect(trend.points).toEqual([
      { day: '2026-06-05', winRate: 0.5 },
      { day: '2026-06-07', winRate: 0.5 },
    ]);
    expect(trend.segments).toEqual([
      [{ day: '2026-06-05', winRate: 0.5 }],
      [{ day: '2026-06-07', winRate: 0.5 }],
    ]);
    expect(trend.nullPointCount).toBe(1);
  });

  test('preserves Dataset Coverage and does not zero-fill a failed date', () => {
    const dates = ['2026-06-05', '2026-06-06', '2026-06-07'];
    const dataset = makeDataset(
      dates,
      [
        makeDay('2026-06-05', [makeCountedRow('Vanessa', 10, 4)]),
        makeDay('2026-06-07', [makeCountedRow('Vanessa', 10, 6)]),
      ],
      ['2026-06-06']
    );

    const analysis = analyzeHeroes(dataset, { window: '3d', segment: 'legend' }, null);

    expect(analysis.ranking[0]).toMatchObject({ runsCompleted: 20, tenWinCount: 10 });
    expect(analysis.coverage).toEqual({
      requestedDates: dates,
      usableDates: ['2026-06-05', '2026-06-07'],
      failedDates: ['2026-06-06'],
      nominalDateCount: 3,
    });
  });

  test('filters non-canonical heroes and matchups while preserving mirror and low-sample behavior', () => {
    const analysis = analyzeHeroes(
      makeDataset(
        ['2026-06-07'],
        [
          makeDay('2026-06-07', [
            makeRow({
              hero: 'Vanessa',
              runs: { completed: 40, scored: 40, ten_win: 20 },
              outcomes: { perfect: 5, gold: 15, silver: 10, bronze: 5 },
              matchups: [
                { opponent_hero: 'Mak', decided: 40, wins: 30, losses: 10 },
                { opponent_hero: 'Jules', decided: 200, wins: 130, losses: 70 },
                { opponent_hero: 'Vanessa', decided: 100, wins: 50, losses: 50 },
                { opponent_hero: 'Karnok', decided: 5, wins: 5, losses: 0 },
                { opponent_hero: 'Common', decided: 20, wins: 20, losses: 0 },
              ],
            }),
            makeCountedRow('Common', 10, 9),
          ]),
        ]
      ),
      { window: '1d', segment: 'legend' },
      'Vanessa'
    );

    expect(analysis.ranking.map((row) => row.hero)).toEqual(['Vanessa']);
    expect(analysis.focus.matchups.map((row) => row.opponentHero)).toEqual([
      'Mak',
      'Jules',
      'Vanessa',
      'Karnok',
    ]);
    expect(analysis.focus.matchups.at(-1)?.isLowSample).toBe(true);
    expect(analysis).not.toHaveProperty('stages');
  });

  test('handles an empty snapshot and clears focus and scope controls', () => {
    const dates = ['2026-06-01', '2026-06-02', '2026-06-03'];
    const analysis = analyzeHeroes(
      makeDataset(dates, [], dates),
      { window: '7d', segment: 'non_legend' },
      'Vanessa'
    );

    expect(analysis.scope.availableWindows).toEqual([]);
    expect(analysis.scope.availableSegments).toEqual([]);
    expect(analysis.ranking).toEqual([]);
    expect(analysis.trend.series).toEqual([]);
    expect(analysis.focus.hero).toBeNull();
  });
});

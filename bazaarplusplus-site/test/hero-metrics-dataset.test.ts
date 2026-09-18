// @vitest-environment node

import { describe, expect, test, vi } from 'vitest';

import {
  loadHeroMetricsDataset,
  type HeroMetricsTransport,
} from '../src/features/heroes/hero-metrics-dataset';
import { HEROES } from '../src/shared/lib/heroes';

const DATES = [
  '2026-06-01',
  '2026-06-02',
  '2026-06-03',
  '2026-06-04',
  '2026-06-05',
  '2026-06-06',
  '2026-06-07',
];

function makeRow(overrides: Record<string, unknown> = {}) {
  return {
    hero: 'Vanessa',
    segment: 'legend',
    runs: { completed: 8, scored: 8, ten_win: 2 },
    outcomes: { perfect: 1, gold: 1, silver: 2, bronze: 2 },
    ten_win_days: { known_count: 2, sum_days: 22 },
    matchups: [{ opponent_hero: 'Mak', decided: 10, wins: 6, losses: 4 }],
    ...overrides,
  };
}

function makeDay(day: string, overrides = [makeRow()]) {
  const overrideByKey = new Map(overrides.map((row) => [`${row.hero}\0${row.segment}`, row]));
  const rows = HEROES.flatMap((hero) =>
    (['legend', 'non_legend'] as const).map(
      (segment) => overrideByKey.get(`${hero}\0${segment}`) ?? makeRow({ hero, segment })
    )
  );
  rows.push(
    ...overrides.filter(
      (row) =>
        !(HEROES as readonly string[]).includes(row.hero) ||
        (row.segment !== 'legend' && row.segment !== 'non_legend')
    )
  );
  return { day, rows };
}

function makeSnapshot(
  days: unknown[] = [...DATES].reverse().map((day) => makeDay(day)),
  windowDates: string[] = DATES
) {
  return {
    schema_version: 1,
    kind: 'hero_metrics',
    generated_at: '2026-06-08T09:00:00Z',
    window: {
      start: windowDates[0],
      end: windowDates.at(-1),
      days: windowDates.length,
    },
    days,
  };
}

function makeTransport(
  handler: (path: string, signal?: AbortSignal) => Promise<unknown> = async () => makeSnapshot()
): HeroMetricsTransport {
  return {
    load: vi.fn((path: string, options?: { signal?: AbortSignal }) =>
      handler(path, options?.signal)
    ),
  };
}

describe('loadHeroMetricsDataset', () => {
  test('loads and decodes only analyzer-v5/heroes/latest.json', async () => {
    const transport = makeTransport();

    const dataset = await loadHeroMetricsDataset(transport);

    expect(transport.load).toHaveBeenCalledTimes(1);
    expect(transport.load).toHaveBeenCalledWith('analyzer-v5/heroes/latest.json', {
      signal: undefined,
    });
    expect(dataset).toMatchObject({
      generatedAt: '2026-06-08T09:00:00Z',
      window: { start: '2026-06-01', end: '2026-06-07', days: 7 },
      coverage: {
        requestedDates: DATES,
        usableDates: DATES,
        failedDates: [],
      },
    });
    expect(dataset.days.map((day) => day.day)).toEqual(DATES);
  });

  test.each([1, 5, 7])(
    'accepts a %i-day newest-first snapshot and preserves its actual window',
    async (dayCount) => {
      const windowDates = DATES.slice(-dayCount);
      const snapshotDays = [...windowDates].reverse().map((day) => makeDay(day));

      const dataset = await loadHeroMetricsDataset(
        makeTransport(async () => makeSnapshot(snapshotDays, windowDates))
      );

      expect(dataset.window).toEqual({
        start: windowDates[0],
        end: windowDates.at(-1),
        days: dayCount,
      });
      expect(dataset.coverage).toEqual({
        requestedDates: windowDates,
        usableDates: windowDates,
        failedDates: [],
      });
      expect(dataset.days.map((day) => day.day)).toEqual(windowDates);
    }
  );

  test('accepts additive fields and non-canonical heroes without decoding battle_days', async () => {
    const snapshot = makeSnapshot([
      makeDay('2026-06-07', [
        makeRow({
          hero: 'Common',
          segment: 'non_legend',
          matchups: [
            {
              opponent_hero: 'FutureHero',
              decided: 3,
              wins: 2,
              losses: 1,
              additive_matchup_field: true,
            },
          ],
          battle_days: { day_1: { decided: 4, wins: 1, losses: 3 } },
          additive_row_field: true,
        }),
      ]),
      ...DATES.slice(0, -1)
        .reverse()
        .map((day) => makeDay(day)),
    ]);
    Object.assign(snapshot, { additive_snapshot_field: true });
    const transport = makeTransport(async () => snapshot);

    const dataset = await loadHeroMetricsDataset(transport);
    const row = dataset.days.at(-1)?.rows.find((candidate) => candidate.hero === 'Common');

    expect(row).toMatchObject({
      hero: 'Common',
      segment: 'non_legend',
      matchups: [{ opponent_hero: 'FutureHero', decided: 3, wins: 2, losses: 1 }],
    });
    expect(row).not.toHaveProperty('battle_days');
  });

  test.each([
    ['schema version', { schema_version: '1' }],
    ['kind', { kind: 'hero_web_daily' }],
    ['generated timestamp', { generated_at: 'not-a-timestamp' }],
    ['window start', { window: { start: 'bad', end: '2026-06-07', days: 7 } }],
    ['window day count', { window: { start: '2026-06-01', end: '2026-06-07', days: 6 } }],
    ['zero-day window', { window: { start: '2026-06-07', end: '2026-06-07', days: 0 } }],
    ['eight-day window', { window: { start: '2026-05-31', end: '2026-06-07', days: 8 } }],
    ['days array', { days: null }],
  ])('fails the page for an invalid snapshot %s', async (_label, override) => {
    const transport = makeTransport(async () => ({ ...makeSnapshot(), ...override }));

    await expect(loadHeroMetricsDataset(transport)).rejects.toThrow(
      'Unexpected hero metrics snapshot format'
    );
  });

  test('requires the days array length to equal window.days', async () => {
    const fiveDates = DATES.slice(-5);
    const transport = makeTransport(async () =>
      makeSnapshot(
        [...fiveDates]
          .reverse()
          .slice(1)
          .map((day) => makeDay(day)),
        fiveDates
      )
    );

    await expect(loadHeroMetricsDataset(transport)).rejects.toThrow(
      'Unexpected hero metrics snapshot format'
    );
  });

  test('requires snapshot entries in newest-first order', async () => {
    const fiveDates = DATES.slice(-5);
    const transport = makeTransport(async () =>
      makeSnapshot(
        fiveDates.map((day) => makeDay(day)),
        fiveDates
      )
    );

    await expect(loadHeroMetricsDataset(transport)).rejects.toThrow(
      'Unexpected hero metrics snapshot format'
    );
  });

  test('surfaces an invalid date in Dataset Coverage without zero-filling it', async () => {
    const transport = makeTransport(async () =>
      makeSnapshot([
        makeDay('2026-06-07', [makeRow({ runs: { completed: -1, scored: 0, ten_win: 0 } })]),
        ...DATES.slice(0, -1)
          .reverse()
          .map((day) => makeDay(day)),
      ])
    );

    const dataset = await loadHeroMetricsDataset(transport);

    expect(dataset.days.map((day) => day.day)).toEqual(DATES.slice(0, -1));
    expect(dataset.coverage).toEqual({
      requestedDates: DATES,
      usableDates: DATES.slice(0, -1),
      failedDates: ['2026-06-07'],
    });
  });

  test.each([
    ['stored segment', { segment: 'all' }],
    ['required counter', { runs: { completed: 8, scored: 8, ten_win: Number.NaN } }],
    ['ten-win invariant', { runs: { completed: 8, scored: 8, ten_win: 3 } }],
    ['outcome invariant', { outcomes: { perfect: 1, gold: 1, silver: 4, bronze: 3 } }],
    [
      'matchup invariant',
      { matchups: [{ opponent_hero: 'Mak', decided: 10, wins: 6, losses: 3 }] },
    ],
  ])('marks a date failed for an invalid row %s', async (_label, rowOverride) => {
    const transport = makeTransport(async () =>
      makeSnapshot([
        makeDay('2026-06-07', [makeRow(rowOverride)]),
        ...DATES.slice(0, -1)
          .reverse()
          .map((day) => makeDay(day)),
      ])
    );

    const dataset = await loadHeroMetricsDataset(transport);

    expect(dataset.coverage.failedDates).toEqual(['2026-06-07']);
    expect(dataset.days).toHaveLength(6);
  });

  test('marks a date failed when its canonical hero-by-segment matrix is incomplete', async () => {
    const incompleteDay = makeDay('2026-06-07');
    incompleteDay.rows = incompleteDay.rows.filter(
      (row) => !(row.hero === 'Vanessa' && row.segment === 'non_legend')
    );
    const transport = makeTransport(async () =>
      makeSnapshot([
        incompleteDay,
        ...DATES.slice(0, -1)
          .reverse()
          .map((day) => makeDay(day)),
      ])
    );

    const dataset = await loadHeroMetricsDataset(transport);

    expect(dataset.coverage.failedDates).toEqual(['2026-06-07']);
  });

  test('rejects an empty days array instead of inventing missing coverage', async () => {
    await expect(
      loadHeroMetricsDataset(makeTransport(async () => makeSnapshot([])))
    ).rejects.toThrow('Unexpected hero metrics snapshot format');
  });

  test('passes caller abort to the one snapshot request', async () => {
    const controller = new AbortController();
    const transport = makeTransport(async (_path, signal) => {
      expect(signal).toBe(controller.signal);
      controller.abort(new DOMException('caller aborted', 'AbortError'));
      throw controller.signal.reason;
    });

    await expect(
      loadHeroMetricsDataset(transport, {
        signal: controller.signal,
      })
    ).rejects.toThrow(/caller aborted/i);
  });
});

import { getHeroColor, HEROES } from '../../shared/lib/heroes';
import {
  HERO_METRICS_SEGMENTS,
  type DatasetCoverage,
  type HeroBattleCounts as BattleCounts,
  type HeroMetricsDataset,
  type HeroMetricsDay,
  type HeroMetricsRow,
  type HeroMetricsSegment,
} from './hero-metrics-dataset';

export type MetricWindow = '1d' | '3d' | '7d';

export type AnalysisScope = {
  window: MetricWindow;
  segment: HeroMetricsSegment;
};

export type HeroRanking = {
  hero: string;
  runsCompleted: number;
  scoredRuns: number;
  runShare: number | null;
  tenWinCount: number;
  tenWinRate: number | null;
  avgRunDays10w: number | null;
  perfectRate: number | null;
  goldRate: number | null;
  silverRate: number | null;
  bronzeRate: number | null;
  misfortuneRate: number | null;
};

export type HeroTrendPoint = {
  day: string;
  winRate: number;
};

export type HeroTrend = {
  hero: string;
  color: string;
  points: HeroTrendPoint[];
  segments: HeroTrendPoint[][];
  latestWinRate: number | null;
  firstWinRate: number | null;
  nullPointCount: number;
};

export type HeroMatchup = {
  opponentHero: string;
  decided: number;
  wins: number;
  losses: number;
  winRate: number | null;
  isLowSample: boolean;
};

export type HeroAnalysis = {
  generatedAt: string;
  scope: {
    requested: AnalysisScope;
    availableWindows: MetricWindow[];
    availableSegments: HeroMetricsSegment[];
  };
  coverage: DatasetCoverage & { nominalDateCount: number };
  ranking: HeroRanking[];
  trend: {
    dayAxis: string[];
    series: HeroTrend[];
  };
  focus: {
    hero: string | null;
    ranking: HeroRanking | null;
    trend: HeroTrend | null;
    matchups: HeroMatchup[];
  };
};

type MergedHeroRow = {
  hero: string;
  runsCompleted: number;
  scoredRuns: number;
  tenWinCount: number;
  perfect: number;
  gold: number;
  silver: number;
  bronze: number;
  tenWinDaysKnownCount: number;
  tenWinDaysSumDays: number;
  matchups: Map<string, BattleCounts>;
};

const WINDOW_DAY_COUNT: Record<MetricWindow, number> = {
  '1d': 1,
  '3d': 3,
  '7d': 7,
};
const METRIC_WINDOW_OPTIONS: MetricWindow[] = ['1d', '3d', '7d'];
const MATCHUP_MIN_SAMPLE = 20;

function isCanonicalHero(hero: string): boolean {
  return (HEROES as readonly string[]).includes(hero);
}

function rateOrNull(successes: number, attempts: number): number | null {
  return attempts > 0 ? successes / attempts : null;
}

function selectDates(dates: string[], window: MetricWindow, windowEnd: string): string[] {
  return [...dates]
    .filter((day) => day <= windowEnd)
    .sort((left, right) => left.localeCompare(right))
    .slice(-WINDOW_DAY_COUNT[window]);
}

function collectRows(
  payloads: HeroMetricsDay[],
  selectedDates: string[],
  segment: HeroMetricsSegment
): HeroMetricsRow[] {
  const wantedDates = new Set(selectedDates);
  return payloads
    .filter((payload) => wantedDates.has(payload.day))
    .flatMap((payload) =>
      payload.rows.filter(
        (row) => (segment === 'all' || row.segment === segment) && isCanonicalHero(row.hero)
      )
    );
}

function mergeRows(rows: HeroMetricsRow[]): Map<string, MergedHeroRow> {
  const merged = new Map<string, MergedHeroRow>();

  for (const row of rows) {
    if (!isCanonicalHero(row.hero)) {
      continue;
    }

    let entry = merged.get(row.hero);
    if (!entry) {
      entry = {
        hero: row.hero,
        runsCompleted: 0,
        scoredRuns: 0,
        tenWinCount: 0,
        perfect: 0,
        gold: 0,
        silver: 0,
        bronze: 0,
        tenWinDaysKnownCount: 0,
        tenWinDaysSumDays: 0,
        matchups: new Map(),
      };
      merged.set(row.hero, entry);
    }

    entry.runsCompleted += row.runs.completed;
    entry.scoredRuns += row.runs.scored;
    entry.tenWinCount += row.runs.ten_win;
    entry.perfect += row.outcomes.perfect;
    entry.gold += row.outcomes.gold;
    entry.silver += row.outcomes.silver;
    entry.bronze += row.outcomes.bronze;
    entry.tenWinDaysKnownCount += row.ten_win_days.known_count;
    entry.tenWinDaysSumDays += row.ten_win_days.sum_days;

    for (const matchup of row.matchups) {
      if (!isCanonicalHero(matchup.opponent_hero)) {
        continue;
      }

      const existing = entry.matchups.get(matchup.opponent_hero);
      if (existing) {
        existing.decided += matchup.decided;
        existing.wins += matchup.wins;
        existing.losses += matchup.losses;
      } else {
        entry.matchups.set(matchup.opponent_hero, {
          decided: matchup.decided,
          wins: matchup.wins,
          losses: matchup.losses,
        });
      }
    }
  }

  return merged;
}

function compareNullableDescending(left: number | null, right: number | null): number {
  if (left == null || right == null) {
    if (left == null && right == null) {
      return 0;
    }
    return left == null ? 1 : -1;
  }
  return right - left;
}

function deriveRanking(merged: Map<string, MergedHeroRow>): HeroRanking[] {
  const rows = Array.from(merged.values());
  const completedInScope = rows.reduce((sum, row) => sum + row.runsCompleted, 0);

  return rows
    .map((row) => {
      const misfortune = row.scoredRuns - (row.perfect + row.gold + row.silver + row.bronze);
      return {
        hero: row.hero,
        runsCompleted: row.runsCompleted,
        scoredRuns: row.scoredRuns,
        runShare: rateOrNull(row.runsCompleted, completedInScope),
        tenWinCount: row.tenWinCount,
        tenWinRate: rateOrNull(row.tenWinCount, row.runsCompleted),
        avgRunDays10w: rateOrNull(row.tenWinDaysSumDays, row.tenWinDaysKnownCount),
        perfectRate: rateOrNull(row.perfect, row.scoredRuns),
        goldRate: rateOrNull(row.gold, row.scoredRuns),
        silverRate: rateOrNull(row.silver, row.scoredRuns),
        bronzeRate: rateOrNull(row.bronze, row.scoredRuns),
        misfortuneRate: rateOrNull(misfortune, row.scoredRuns),
      };
    })
    .sort(
      (left, right) =>
        compareNullableDescending(left.tenWinRate, right.tenWinRate) ||
        right.runsCompleted - left.runsCompleted ||
        left.hero.localeCompare(right.hero)
    );
}

function segmentTrendPoints(points: HeroTrendPoint[], dayAxis: string[]): HeroTrendPoint[][] {
  const indexByDay = new Map(dayAxis.map((day, index) => [day, index]));
  const segments: HeroTrendPoint[][] = [];
  let current: HeroTrendPoint[] = [];
  let previousIndex: number | null = null;

  for (const point of points) {
    const index = indexByDay.get(point.day);
    if (index == null) {
      continue;
    }
    if (previousIndex != null && index !== previousIndex + 1 && current.length > 0) {
      segments.push(current);
      current = [];
    }
    current.push(point);
    previousIndex = index;
  }
  if (current.length > 0) {
    segments.push(current);
  }
  return segments;
}

function deriveTrend(
  payloads: HeroMetricsDay[],
  selectedDates: string[],
  segment: HeroMetricsSegment
): HeroTrend[] {
  const payloadByDay = new Map(payloads.map((payload) => [payload.day, payload]));
  const orderedDates = [...selectedDates].sort((left, right) => left.localeCompare(right));
  const pointsByHero = new Map<string, HeroTrendPoint[]>();
  const nullCountByHero = new Map<string, number>();

  for (const day of orderedDates) {
    const payload = payloadByDay.get(day);
    if (!payload) {
      continue;
    }
    const dailyRows = payload.rows.filter(
      (row) => (segment === 'all' || row.segment === segment) && isCanonicalHero(row.hero)
    );
    for (const row of mergeRows(dailyRows).values()) {
      const winRate = rateOrNull(row.tenWinCount, row.runsCompleted);
      if (winRate == null) {
        nullCountByHero.set(row.hero, (nullCountByHero.get(row.hero) ?? 0) + 1);
        continue;
      }
      const points = pointsByHero.get(row.hero) ?? [];
      points.push({ day, winRate });
      pointsByHero.set(row.hero, points);
    }
  }

  return Array.from(pointsByHero.entries())
    .map(([hero, points]) => ({
      hero,
      color: getHeroColor(hero),
      points,
      segments: segmentTrendPoints(points, orderedDates),
      latestWinRate: points.at(-1)?.winRate ?? null,
      firstWinRate: points[0]?.winRate ?? null,
      nullPointCount: nullCountByHero.get(hero) ?? 0,
    }))
    .sort(
      (left, right) =>
        compareNullableDescending(left.latestWinRate, right.latestWinRate) ||
        left.hero.localeCompare(right.hero)
    );
}

function compareMatchups(left: HeroMatchup, right: HeroMatchup): number {
  if (left.isLowSample !== right.isLowSample) {
    return left.isLowSample ? 1 : -1;
  }
  return (
    compareNullableDescending(left.winRate, right.winRate) ||
    right.decided - left.decided ||
    left.opponentHero.localeCompare(right.opponentHero)
  );
}

function deriveMatchups(merged: MergedHeroRow | undefined): HeroMatchup[] {
  if (!merged) {
    return [];
  }
  return Array.from(merged.matchups.entries())
    .map(([opponentHero, counts]) => ({
      opponentHero,
      decided: counts.decided,
      wins: counts.wins,
      losses: counts.losses,
      winRate: rateOrNull(counts.wins, counts.decided),
      isLowSample: counts.decided < MATCHUP_MIN_SAMPLE,
    }))
    .sort(compareMatchups);
}

export function analyzeHeroes(
  dataset: HeroMetricsDataset,
  requestedScope: AnalysisScope,
  requestedFocus: string | null
): HeroAnalysis {
  const selectedDates = selectDates(
    dataset.coverage.requestedDates,
    requestedScope.window,
    dataset.window.end
  );
  const selectedDateSet = new Set(selectedDates);
  const usableDates = dataset.coverage.usableDates.filter((day) => selectedDateSet.has(day));
  const failedDates = dataset.coverage.failedDates.filter((day) => selectedDateSet.has(day));
  const merged = mergeRows(collectRows(dataset.days, selectedDates, requestedScope.segment));
  const ranking = deriveRanking(merged);
  const trendDates = selectDates(dataset.coverage.requestedDates, '7d', dataset.window.end);
  const trendSeries = deriveTrend(dataset.days, trendDates, requestedScope.segment);
  const focusHero = ranking.some((row) => row.hero === requestedFocus)
    ? requestedFocus
    : (ranking[0]?.hero ?? null);

  return {
    generatedAt: dataset.generatedAt,
    scope: {
      requested: requestedScope,
      availableWindows: dataset.coverage.usableDates.length > 0 ? [...METRIC_WINDOW_OPTIONS] : [],
      availableSegments: dataset.coverage.usableDates.length > 0 ? [...HERO_METRICS_SEGMENTS] : [],
    },
    coverage: {
      requestedDates: selectedDates,
      usableDates,
      failedDates,
      nominalDateCount: selectedDates.length,
    },
    ranking,
    trend: {
      dayAxis: trendDates,
      series: trendSeries,
    },
    focus: {
      hero: focusHero,
      ranking: ranking.find((row) => row.hero === focusHero) ?? null,
      trend: trendSeries.find((series) => series.hero === focusHero) ?? null,
      matchups: deriveMatchups(focusHero ? merged.get(focusHero) : undefined),
    },
  };
}

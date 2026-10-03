import { HEROES } from '../../shared/lib/heroes';

export type HeroMetricsStoredSegment = 'legend' | 'non_legend';

export type HeroMetricsSegment = 'all' | HeroMetricsStoredSegment;

export const HERO_METRICS_SEGMENTS: HeroMetricsSegment[] = ['all', 'legend', 'non_legend'];

export type HeroBattleCounts = {
  decided: number;
  wins: number;
  losses: number;
};

export type HeroMetricsMatchup = HeroBattleCounts & {
  opponent_hero: string;
};

export type HeroMetricsRow = {
  hero: string;
  segment: HeroMetricsStoredSegment;
  runs: {
    completed: number;
    scored: number;
    ten_win: number;
  };
  outcomes: {
    perfect: number;
    gold: number;
    silver: number;
    bronze: number;
  };
  ten_win_days: {
    known_count: number;
    sum_days: number;
  };
  matchups: HeroMetricsMatchup[];
};

export type HeroMetricsDay = {
  day: string;
  rows: HeroMetricsRow[];
};

export type DatasetCoverage = {
  requestedDates: string[];
  usableDates: string[];
  failedDates: string[];
};

export type HeroMetricsDataset = {
  generatedAt: string;
  window: {
    start: string;
    end: string;
    days: number;
  };
  days: HeroMetricsDay[];
  coverage: DatasetCoverage;
};

/** A non-2xx response from the metrics origin; `status` drives the query's retry policy. */
export class HeroMetricsHttpError extends Error {
  readonly status: number;

  constructor(status: number) {
    super(`Hero metrics snapshot responded with ${status}`);
    this.name = 'HeroMetricsHttpError';
    this.status = status;
  }
}

const DEFAULT_METRICS_BASE_URL = 'https://bpp-metrics.bazaarplusplus.com';
const HERO_METRICS_PATH = 'analyzer-v5/heroes/latest.json';
const REQUEST_TIMEOUT_MS = 15_000;
const STORED_SEGMENTS = new Set<string>(['legend', 'non_legend']);
const ISO_DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/;
const UTC_DAY_MS = 24 * 60 * 60 * 1_000;

function asRecord(value: unknown): Record<string, unknown> | null {
  return value != null && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
}

function asNonEmptyString(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null;
}

function asCounter(value: unknown): number | null {
  return typeof value === 'number' &&
    Number.isFinite(value) &&
    Number.isInteger(value) &&
    value >= 0
    ? value
    : null;
}

function asIsoDate(value: unknown): string | null {
  if (typeof value !== 'string' || !ISO_DATE_PATTERN.test(value)) {
    return null;
  }
  const timestamp = Date.parse(`${value}T00:00:00Z`);
  return Number.isFinite(timestamp) && new Date(timestamp).toISOString().slice(0, 10) === value
    ? value
    : null;
}

function asUtcTimestamp(value: unknown): string | null {
  return typeof value === 'string' &&
    /(?:Z|\+00:00)$/.test(value) &&
    Number.isFinite(Date.parse(value))
    ? value
    : null;
}

function enumerateDates(start: string, end: string): string[] | null {
  const startTimestamp = Date.parse(`${start}T00:00:00Z`);
  const endTimestamp = Date.parse(`${end}T00:00:00Z`);
  if (endTimestamp < startTimestamp) {
    return null;
  }

  return Array.from(
    { length: Math.floor((endTimestamp - startTimestamp) / UTC_DAY_MS) + 1 },
    (_, index) => new Date(startTimestamp + index * UTC_DAY_MS).toISOString().slice(0, 10)
  );
}

function decodeBattleCounts(value: unknown): HeroBattleCounts | null {
  const counts = asRecord(value);
  const decided = asCounter(counts?.decided);
  const wins = asCounter(counts?.wins);
  const losses = asCounter(counts?.losses);
  return decided == null || wins == null || losses == null || decided !== wins + losses
    ? null
    : { decided, wins, losses };
}

function decodeRow(value: unknown): HeroMetricsRow | null {
  const row = asRecord(value);
  const runs = asRecord(row?.runs);
  const outcomes = asRecord(row?.outcomes);
  const tenWinDays = asRecord(row?.ten_win_days);
  const hero = asNonEmptyString(row?.hero);
  const segment = row?.segment;
  if (
    hero == null ||
    typeof segment !== 'string' ||
    !STORED_SEGMENTS.has(segment) ||
    runs == null ||
    outcomes == null ||
    tenWinDays == null ||
    !Array.isArray(row?.matchups)
  ) {
    return null;
  }

  const completed = asCounter(runs.completed);
  const scored = asCounter(runs.scored);
  const tenWin = asCounter(runs.ten_win);
  const perfect = asCounter(outcomes.perfect);
  const gold = asCounter(outcomes.gold);
  const silver = asCounter(outcomes.silver);
  const bronze = asCounter(outcomes.bronze);
  const knownCount = asCounter(tenWinDays.known_count);
  const sumDays = asCounter(tenWinDays.sum_days);
  if (
    completed == null ||
    scored == null ||
    tenWin == null ||
    perfect == null ||
    gold == null ||
    silver == null ||
    bronze == null ||
    knownCount == null ||
    sumDays == null ||
    scored > completed ||
    tenWin !== perfect + gold ||
    perfect + gold + silver + bronze > scored ||
    knownCount > tenWin ||
    (knownCount === 0 && sumDays !== 0)
  ) {
    return null;
  }

  const matchups: HeroMetricsMatchup[] = [];
  for (const valueMatchup of row.matchups) {
    const matchup = asRecord(valueMatchup);
    const opponentHero = asNonEmptyString(matchup?.opponent_hero);
    const counts = decodeBattleCounts(matchup);
    if (opponentHero == null || counts == null) {
      return null;
    }
    matchups.push({ opponent_hero: opponentHero, ...counts });
  }

  return {
    hero,
    segment: segment as HeroMetricsStoredSegment,
    runs: { completed, scored, ten_win: tenWin },
    outcomes: { perfect, gold, silver, bronze },
    ten_win_days: { known_count: knownCount, sum_days: sumDays },
    matchups,
  };
}

function decodeDay(value: unknown, expectedDay: string): HeroMetricsDay | null {
  const payload = asRecord(value);
  if (payload?.day !== expectedDay || !Array.isArray(payload.rows)) {
    return null;
  }

  const rows: HeroMetricsRow[] = [];
  const rowKeys = new Set<string>();
  for (const valueRow of payload.rows) {
    const row = decodeRow(valueRow);
    if (row == null) {
      return null;
    }
    const rowKey = `${row.hero}\0${row.segment}`;
    if (rowKeys.has(rowKey)) {
      return null;
    }
    rowKeys.add(rowKey);
    rows.push(row);
  }

  for (const hero of HEROES) {
    for (const segment of STORED_SEGMENTS) {
      if (!rowKeys.has(`${hero}\0${segment}`)) {
        return null;
      }
    }
  }

  return { day: expectedDay, rows };
}

function decodeSnapshot(value: unknown): HeroMetricsDataset | null {
  const payload = asRecord(value);
  const window = asRecord(payload?.window);
  const generatedAt = asUtcTimestamp(payload?.generated_at);
  const start = asIsoDate(window?.start);
  const end = asIsoDate(window?.end);
  const dayCount = asCounter(window?.days);
  if (
    payload?.schema_version !== 1 ||
    payload.kind !== 'hero_metrics' ||
    generatedAt == null ||
    start == null ||
    end == null ||
    dayCount == null ||
    dayCount === 0 ||
    dayCount > 7 ||
    !Array.isArray(payload.days)
  ) {
    return null;
  }

  const requestedDates = enumerateDates(start, end);
  if (
    requestedDates == null ||
    requestedDates.length !== dayCount ||
    payload.days.length !== dayCount
  ) {
    return null;
  }

  const newestFirstDates = [...requestedDates].reverse();
  const valuesByDay = new Map<string, unknown>();
  for (const [index, valueDay] of payload.days.entries()) {
    const dayValue = asIsoDate(asRecord(valueDay)?.day);
    if (dayValue !== newestFirstDates[index]) {
      return null;
    }
    valuesByDay.set(dayValue, valueDay);
  }

  const days: HeroMetricsDay[] = [];
  const failedDates: string[] = [];
  for (const day of requestedDates) {
    const decoded = decodeDay(valuesByDay.get(day), day);
    if (decoded == null) {
      failedDates.push(day);
    } else {
      days.push(decoded);
    }
  }

  return {
    generatedAt,
    window: { start, end, days: dayCount },
    days,
    coverage: {
      requestedDates,
      usableDates: days.map((day) => day.day),
      failedDates,
    },
  };
}

export async function loadHeroMetricsDataset(signal: AbortSignal): Promise<HeroMetricsDataset> {
  const response = await fetch(`${DEFAULT_METRICS_BASE_URL}/${HERO_METRICS_PATH}`, {
    signal: AbortSignal.any([signal, AbortSignal.timeout(REQUEST_TIMEOUT_MS)]),
  });
  if (!response.ok) {
    throw new HeroMetricsHttpError(response.status);
  }
  const dataset = decodeSnapshot(await response.json());
  if (dataset == null) {
    throw new Error('Unexpected hero metrics snapshot format');
  }
  return dataset;
}

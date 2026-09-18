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

export type HeroMetricsTransport = {
  load(path: string, options?: { signal?: AbortSignal }): Promise<unknown>;
};

export class HeroMetricsTransportError extends Error {
  readonly kind: 'http' | 'network' | 'timeout';
  readonly status?: number;

  constructor(
    message: string,
    options: {
      kind?: 'http' | 'network' | 'timeout';
      status?: number;
      cause?: unknown;
    } = {}
  ) {
    super(message, options.cause === undefined ? undefined : { cause: options.cause });
    this.name = 'HeroMetricsTransportError';
    this.kind = options.kind ?? (options.status == null ? 'network' : 'http');
    this.status = options.status;
  }
}

type HttpTransportOptions = {
  metricsBaseUrl?: string;
  fetchImpl?: typeof fetch;
  requestTimeoutMs?: number;
  requestRetries?: number;
  requestRetryDelayMs?: number;
};

type LoadDatasetOptions = {
  signal?: AbortSignal;
};

const DEFAULT_METRICS_BASE_URL =
  import.meta.env.VITE_METRICS_BASE ?? 'https://bpp-metrics.bazaarplusplus.com';
const DEFAULT_REQUEST_TIMEOUT_MS = 15_000;
const DEFAULT_REQUEST_RETRIES = 2;
const DEFAULT_REQUEST_RETRY_DELAY_MS = 250;
const HERO_METRICS_PATH = 'analyzer-v5/heroes/latest.json';
const STORED_SEGMENTS = new Set<string>(['legend', 'non_legend']);
const ISO_DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/;
const UTC_DAY_MS = 24 * 60 * 60 * 1_000;

function normalizeBaseUrl(value: string): string {
  return value.endsWith('/') ? value : `${value}/`;
}

function getAbortError(signal: AbortSignal): unknown {
  return signal.reason ?? new DOMException('aborted', 'AbortError');
}

function isRetryableStatus(status: number): boolean {
  return status === 408 || status === 429 || status >= 500;
}

function isRetryableFailure(error: HeroMetricsTransportError): boolean {
  return error.kind !== 'http' || (error.status != null && isRetryableStatus(error.status));
}

function waitForRetry(ms: number, signal?: AbortSignal): Promise<void> {
  if (ms <= 0) {
    return Promise.resolve();
  }
  if (signal?.aborted) {
    return Promise.reject(getAbortError(signal));
  }

  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      cleanup();
      resolve();
    }, ms);
    function cleanup() {
      clearTimeout(timer);
      signal?.removeEventListener('abort', handleAbort);
    }
    function handleAbort() {
      cleanup();
      reject(signal ? getAbortError(signal) : new DOMException('aborted', 'AbortError'));
    }
    signal?.addEventListener('abort', handleAbort, { once: true });
  });
}

function createRequestSignal(timeoutMs: number, callerSignal?: AbortSignal) {
  const controller = new AbortController();
  let timedOut = false;

  function abortFromCaller() {
    controller.abort(callerSignal ? getAbortError(callerSignal) : undefined);
  }
  if (callerSignal?.aborted) {
    abortFromCaller();
  } else {
    callerSignal?.addEventListener('abort', abortFromCaller, { once: true });
  }

  const timer = setTimeout(() => {
    timedOut = true;
    controller.abort(new DOMException(`Timed out after ${timeoutMs}ms`, 'TimeoutError'));
  }, timeoutMs);

  return {
    signal: controller.signal,
    didTimeout: () => timedOut,
    cleanup: () => {
      clearTimeout(timer);
      callerSignal?.removeEventListener('abort', abortFromCaller);
    },
  };
}

function toTransportFailure(error: unknown, url: string): HeroMetricsTransportError {
  if (error instanceof HeroMetricsTransportError) {
    return error;
  }
  return new HeroMetricsTransportError(`Failed to fetch ${url}: ${String(error)}`, {
    kind: 'network',
    cause: error,
  });
}

export function createHeroMetricsHttpTransport(
  options: HttpTransportOptions = {}
): HeroMetricsTransport {
  const baseUrl = normalizeBaseUrl(options.metricsBaseUrl ?? DEFAULT_METRICS_BASE_URL);
  const fetchImpl = options.fetchImpl ?? fetch;
  const defaultTimeoutMs = options.requestTimeoutMs ?? DEFAULT_REQUEST_TIMEOUT_MS;
  const defaultRetries = options.requestRetries ?? DEFAULT_REQUEST_RETRIES;
  const defaultRetryDelayMs = options.requestRetryDelayMs ?? DEFAULT_REQUEST_RETRY_DELAY_MS;

  return {
    async load(path, requestOptions = {}) {
      const url = `${baseUrl}${path}`;
      let lastFailure: HeroMetricsTransportError | undefined;

      for (let attempt = 0; attempt <= defaultRetries; attempt += 1) {
        if (requestOptions.signal?.aborted) {
          throw getAbortError(requestOptions.signal);
        }

        const requestSignal = createRequestSignal(defaultTimeoutMs, requestOptions.signal);
        try {
          const response = await fetchImpl(url, { signal: requestSignal.signal });
          if (!response.ok) {
            throw new HeroMetricsTransportError(`Failed to fetch ${url}: ${response.status}`, {
              kind: 'http',
              status: response.status,
            });
          }
          return await response.json();
        } catch (error) {
          if (requestOptions.signal?.aborted) {
            throw getAbortError(requestOptions.signal);
          }
          lastFailure = requestSignal.didTimeout()
            ? new HeroMetricsTransportError(
                `Timed out fetching ${url} after ${defaultTimeoutMs}ms`,
                { kind: 'timeout', cause: error }
              )
            : toTransportFailure(error, url);
        } finally {
          requestSignal.cleanup();
        }

        if (attempt >= defaultRetries || !isRetryableFailure(lastFailure)) {
          throw lastFailure;
        }
        await waitForRetry(defaultRetryDelayMs, requestOptions.signal);
      }

      throw lastFailure ?? new HeroMetricsTransportError(`Failed to fetch ${url}`);
    },
  };
}

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

export async function loadHeroMetricsDataset(
  transport: HeroMetricsTransport,
  options: LoadDatasetOptions = {}
): Promise<HeroMetricsDataset> {
  const dataset = decodeSnapshot(
    await transport.load(HERO_METRICS_PATH, { signal: options.signal })
  );
  if (dataset == null) {
    throw new Error('Unexpected hero metrics snapshot format');
  }
  return dataset;
}

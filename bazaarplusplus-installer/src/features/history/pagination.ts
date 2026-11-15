import type {
  HistoryPeriod,
  HistoryResultFilter,
  HistoryRunFilter
} from '../../types/backend';

export const HISTORY_PAGE_SIZE = 50;

export const HISTORY_PERIODS: HistoryPeriod[] = [
  'all',
  'today',
  'last_7_days',
  'last_30_days'
];
export const HISTORY_RESULTS: HistoryResultFilter[] = ['all', 'win', 'loss'];

export const UNFILTERED: HistoryRunFilter = {
  hero: null,
  period: 'all',
  result: 'all',
  key_card: null
};

/** A History List selection: one page of the Runs one filter selects. */
export type HistoryListQuery = { page: number; filter: HistoryRunFilter };

export function parseHistoryPage(value: string | null): number {
  const page = Number(value);
  return Number.isSafeInteger(page) &&
    page > 0 &&
    Number.isSafeInteger((page - 1) * HISTORY_PAGE_SIZE)
    ? page
    : 1;
}

function oneOf<T extends string>(options: T[], value: string | null): T {
  return options.find((option) => option === value) ?? options[0];
}

/** Reads the selection from a History List URL; unknown values select everything. */
export function parseHistoryQuery(params: URLSearchParams): HistoryListQuery {
  return {
    page: parseHistoryPage(params.get('page')),
    filter: {
      hero: params.get('hero') || null,
      period: oneOf(HISTORY_PERIODS, params.get('period')),
      result: oneOf(HISTORY_RESULTS, params.get('result')),
      key_card: params.get('card') || null
    }
  };
}

/** URL parameters of a selection, leaving out the defaults. */
export function historySearchParams({
  page,
  filter
}: HistoryListQuery): Record<string, string> {
  const params: Record<string, string> = {};
  if (filter.hero) params.hero = filter.hero;
  if (filter.period !== 'all') params.period = filter.period;
  if (filter.result !== 'all') params.result = filter.result;
  if (filter.key_card) params.card = filter.key_card;
  if (page > 1) params.page = String(page);
  return params;
}

export function historyListPath(query: HistoryListQuery): string {
  const search = new URLSearchParams(historySearchParams(query)).toString();
  return search ? `/history?${search}` : '/history';
}

export function isFiltered(filter: HistoryRunFilter): boolean {
  return !sameFilter(filter, UNFILTERED);
}

export function sameFilter(a: HistoryRunFilter, b: HistoryRunFilter): boolean {
  return (
    a.hero === b.hero &&
    a.period === b.period &&
    a.result === b.result &&
    a.key_card === b.key_card
  );
}

export const HISTORY_PAGE_SIZE = 50;

export function parseHistoryPage(value: string | null): number {
  const page = Number(value);
  return Number.isSafeInteger(page) &&
    page > 0 &&
    Number.isSafeInteger((page - 1) * HISTORY_PAGE_SIZE)
    ? page
    : 1;
}

export function historyListPath(page: number): string {
  return page > 1 ? `/history?page=${page}` : '/history';
}

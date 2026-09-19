export type PageRefreshState<Problem> =
  | { phase: 'idle' }
  | { phase: 'refreshing' }
  | { phase: 'failed'; problem: Problem };

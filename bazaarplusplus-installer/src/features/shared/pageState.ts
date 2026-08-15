export type PageRefreshState<Problem> =
  | { phase: 'idle' }
  | { phase: 'refreshing' }
  | { phase: 'failed'; problem: Problem };

export type PageState<Data, Problem> =
  | { phase: 'initial-loading'; requestId: number }
  | { phase: 'blocking-failure'; requestId: number; problem: Problem }
  | {
      phase: 'ready-empty' | 'ready-content';
      requestId: number;
      data: Data;
      refresh: PageRefreshState<Problem>;
    };

export function beginPageRequest<Data, Problem>(
  state: PageState<Data, Problem>,
  requestId: number
): PageState<Data, Problem> {
  if (isReadyPageState(state)) {
    return { ...state, requestId, refresh: { phase: 'refreshing' } };
  }
  return { phase: 'initial-loading', requestId };
}

export function completePageRequest<Data, Problem>(
  state: PageState<Data, Problem>,
  requestId: number,
  data: Data,
  isEmpty: (data: Data) => boolean
): PageState<Data, Problem> {
  if (state.requestId !== requestId) {
    return state;
  }
  return {
    phase: isEmpty(data) ? 'ready-empty' : 'ready-content',
    requestId,
    data,
    refresh: { phase: 'idle' }
  };
}

export function failPageRequest<Data, Problem>(
  state: PageState<Data, Problem>,
  requestId: number,
  problem: Problem
): PageState<Data, Problem> {
  if (state.requestId !== requestId) {
    return state;
  }
  if (isReadyPageState(state)) {
    return { ...state, refresh: { phase: 'failed', problem } };
  }
  return { phase: 'blocking-failure', requestId, problem };
}

export function isReadyPageState<Data, Problem>(
  state: PageState<Data, Problem>
): state is Extract<PageState<Data, Problem>, { data: Data }> {
  return state.phase === 'ready-empty' || state.phase === 'ready-content';
}

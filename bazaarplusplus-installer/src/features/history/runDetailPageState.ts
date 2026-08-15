import type { HistoryRunDetail } from '../../types/backend';
import type { PageRefreshState } from '../shared/pageState';
import type { RunDetailProblem } from './runDetailProblems';

export type RunDetailPageState =
  | { phase: 'initial-loading'; requestId: number }
  | { phase: 'not-found'; requestId: number }
  | {
      phase: 'blocking-failure';
      requestId: number;
      problem: RunDetailProblem;
    }
  | {
      phase: 'ready';
      requestId: number;
      data: HistoryRunDetail;
      refresh: PageRefreshState<RunDetailProblem>;
    };

export type RunDetailPageEvent =
  | { type: 'resource-changed'; requestId: number }
  | { type: 'request-started'; requestId: number }
  | {
      type: 'request-succeeded';
      requestId: number;
      data: HistoryRunDetail | null;
    }
  | {
      type: 'request-failed';
      requestId: number;
      problem: RunDetailProblem;
    }
  | { type: 'data-replaced'; data: HistoryRunDetail };

export const initialRunDetailPageState: RunDetailPageState = {
  phase: 'initial-loading',
  requestId: 0
};

export function reduceRunDetailPageState(
  state: RunDetailPageState,
  event: RunDetailPageEvent
): RunDetailPageState {
  switch (event.type) {
    case 'resource-changed':
      return { phase: 'initial-loading', requestId: event.requestId };
    case 'request-started':
      if (state.phase === 'ready') {
        return {
          ...state,
          requestId: event.requestId,
          refresh: { phase: 'refreshing' }
        };
      }
      return { phase: 'initial-loading', requestId: event.requestId };
    case 'request-succeeded':
      if (state.requestId !== event.requestId) return state;
      if (!event.data) {
        return { phase: 'not-found', requestId: event.requestId };
      }
      return {
        phase: 'ready',
        requestId: event.requestId,
        data: event.data,
        refresh: { phase: 'idle' }
      };
    case 'request-failed':
      if (state.requestId !== event.requestId) return state;
      if (state.phase === 'ready') {
        return {
          ...state,
          refresh: { phase: 'failed', problem: event.problem }
        };
      }
      return {
        phase: 'blocking-failure',
        requestId: event.requestId,
        problem: event.problem
      };
    case 'data-replaced':
      return replaceRunDetailData(state, event.data);
  }
}

export function replaceRunDetailData(
  state: RunDetailPageState,
  data: HistoryRunDetail
): RunDetailPageState {
  if (state.phase !== 'ready') return state;
  return { ...state, data };
}

export type RunDetailActionName =
  'screenshot' | `video:${string}` | `delete:${string}`;

export type RunDetailActionTarget = 'screenshot' | `battle:${string}`;

export type RunDetailActionFailure = {
  action: RunDetailActionName;
  problem: RunDetailProblem;
};

export type RunDetailActionState = {
  current: RunDetailActionName | null;
  problems: Partial<Record<RunDetailActionTarget, RunDetailActionFailure>>;
};

export const initialRunDetailActionState: RunDetailActionState = {
  current: null,
  problems: {}
};

export function beginRunDetailAction(
  state: RunDetailActionState,
  action: RunDetailActionName
): RunDetailActionState {
  if (state.current !== null) return state;
  const problems = { ...state.problems };
  delete problems[actionTarget(action)];
  return { current: action, problems };
}

export function completeRunDetailAction(
  state: RunDetailActionState,
  action: RunDetailActionName
): RunDetailActionState {
  if (state.current !== action) return state;
  return { ...state, current: null };
}

export function failRunDetailAction(
  state: RunDetailActionState,
  action: RunDetailActionName,
  problem: RunDetailProblem
): RunDetailActionState {
  if (state.current !== action) return state;
  const target = actionTarget(action);
  return {
    current: null,
    problems: { ...state.problems, [target]: { action, problem } }
  };
}

export function actionAvailability(
  state: RunDetailActionState,
  action: RunDetailActionName,
  pageRefreshing = false
) {
  return {
    disabled: pageRefreshing || state.current !== null,
    running: state.current === action
  };
}

export function actionProblemFor(
  state: RunDetailActionState,
  target: RunDetailActionTarget
): RunDetailActionFailure | null {
  return state.problems[target] ?? null;
}

function actionTarget(action: RunDetailActionName): RunDetailActionTarget {
  if (action === 'screenshot') return 'screenshot';
  return `battle:${action.slice(action.indexOf(':') + 1)}`;
}

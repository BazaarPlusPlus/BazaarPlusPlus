import type { HistoryRunList } from '../../types/backend';
import {
  beginPageRequest,
  completePageRequest,
  failPageRequest,
  type PageState
} from '../shared/pageState';
import type { HistoryPageProblem } from './historyProblems';

export type HistoryPageState = PageState<HistoryRunList, HistoryPageProblem>;

export type HistoryPageEvent =
  | { type: 'request-started'; requestId: number }
  | { type: 'request-succeeded'; requestId: number; data: HistoryRunList }
  | {
      type: 'request-failed';
      requestId: number;
      problem: HistoryPageProblem;
    };

export const initialHistoryPageState: HistoryPageState = {
  phase: 'initial-loading',
  requestId: 0
};

export function reduceHistoryPageState(
  state: HistoryPageState,
  event: HistoryPageEvent
): HistoryPageState {
  switch (event.type) {
    case 'request-started':
      return beginPageRequest(state, event.requestId);
    case 'request-succeeded':
      return completePageRequest(
        state,
        event.requestId,
        event.data,
        (data) => data.runs.length === 0
      );
    case 'request-failed':
      return failPageRequest(state, event.requestId, event.problem);
  }
}

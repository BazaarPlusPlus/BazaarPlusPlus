import type { Translate } from '../../i18n/LocaleProvider';
import type { MessageKey } from '../../i18n/messages';
import {
  createUiProblem,
  problemFromError,
  type UiProblem
} from '../shared/problems';

export type HistoryPageProblemCode =
  | 'history_unavailable'
  | 'history_read_failed'
  | 'history_read_blocked_by_game'
  | 'history_database_unsupported_schema'
  | 'history_preview_unavailable'
  | 'history_unexpected';

export type HistoryPageProblem = UiProblem<HistoryPageProblemCode>;

export function historyProblemFromError(error: unknown): HistoryPageProblem {
  const problem = problemFromError(error, 'history_unexpected');
  switch (problem.code) {
    case 'history_unavailable':
    case 'history_read_failed':
    case 'history_read_blocked_by_game':
    case 'history_database_unsupported_schema':
    case 'history_unexpected':
      return {
        code: problem.code,
        params: problem.params,
        diagnostic: problem.diagnostic
      };
    default:
      return createUiProblem('history_unexpected', {
        params: problem.params,
        diagnostic: problem.diagnostic
      });
  }
}

export function historyProblemMessageKey(
  problem: HistoryPageProblem
): MessageKey {
  switch (problem.code) {
    case 'history_unavailable':
      return 'historyProblemUnavailable';
    case 'history_read_failed':
      return 'historyProblemReadFailed';
    case 'history_read_blocked_by_game':
      return 'historyProblemBlockedByGame';
    case 'history_database_unsupported_schema':
      return 'historyProblemUnsupportedSchema';
    case 'history_preview_unavailable':
      return 'historyProblemPreviewUnavailable';
    case 'history_unexpected':
      return 'historyProblemUnexpected';
  }
}

export function presentHistoryProblem(
  problem: HistoryPageProblem,
  t: Translate
): string {
  return t(historyProblemMessageKey(problem), problem.params);
}

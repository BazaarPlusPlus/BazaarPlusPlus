import type { Translate } from '../../i18n/LocaleProvider';
import type { MessageKey } from '../../i18n/messages';
import {
  createUiProblem,
  problemFromError,
  type UiProblem
} from '../shared/problems';

export type RunDetailProblemCode =
  | 'history_unavailable'
  | 'history_read_failed'
  | 'history_read_blocked_by_game'
  | 'history_database_unsupported_schema'
  | 'history_action_failed'
  | 'run_detail_unexpected';

export type RunDetailProblem = UiProblem<RunDetailProblemCode>;

export function runDetailProblemFromError(error: unknown): RunDetailProblem {
  const problem = problemFromError(error, 'run_detail_unexpected');
  switch (problem.code) {
    case 'history_unavailable':
    case 'history_read_failed':
    case 'history_read_blocked_by_game':
    case 'history_database_unsupported_schema':
    case 'history_action_failed':
    case 'run_detail_unexpected':
      return problem as RunDetailProblem;
    default:
      return createUiProblem('run_detail_unexpected', {
        params: problem.params,
        diagnostic: problem.diagnostic
      });
  }
}

export function runDetailProblemMessageKey(
  problem: RunDetailProblem
): MessageKey {
  switch (problem.code) {
    case 'history_unavailable':
      return 'runDetailProblemUnavailable';
    case 'history_read_failed':
      return 'runDetailProblemReadFailed';
    case 'history_read_blocked_by_game':
      return 'historyProblemBlockedByGame';
    case 'history_database_unsupported_schema':
      return 'historyProblemUnsupportedSchema';
    case 'history_action_failed':
      switch (problem.params.operation) {
        case 'reveal_screenshot':
          return 'runDetailProblemRevealScreenshotFailed';
        case 'reveal_video':
          return 'runDetailProblemRevealVideoFailed';
        case 'delete_video':
          return 'runDetailProblemDeleteVideoFailed';
        default:
          return 'runDetailProblemUnexpected';
      }
    case 'run_detail_unexpected':
      return 'runDetailProblemUnexpected';
  }
}

export function presentRunDetailProblem(
  problem: RunDetailProblem,
  t: Translate
): string {
  return t(runDetailProblemMessageKey(problem), problem.params);
}

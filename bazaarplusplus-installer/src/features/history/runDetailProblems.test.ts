import { describe, expect, it } from 'vite-plus/test';
import { formatMessage, type MessageKey } from '../../i18n/messages';
import { createUiProblem } from '../shared/problems';
import { presentRunDetailProblem } from './runDetailProblems';

const rows = [
  [createUiProblem('history_unavailable'), 'runDetailProblemUnavailable'],
  [
    createUiProblem('history_read_failed', {
      params: { operation: 'get_run_detail' }
    }),
    'runDetailProblemReadFailed'
  ],
  [
    createUiProblem('history_read_blocked_by_game', {
      params: { operation: 'get_run_detail' }
    }),
    'historyProblemBlockedByGame'
  ],
  [
    createUiProblem('history_database_unsupported_schema', {
      params: { found: '3', supported: '1,2' }
    }),
    'historyProblemUnsupportedSchema'
  ],
  [
    createUiProblem('history_action_failed', {
      params: { operation: 'reveal_screenshot' }
    }),
    'runDetailProblemRevealScreenshotFailed'
  ],
  [
    createUiProblem('history_action_failed', {
      params: { operation: 'reveal_video' }
    }),
    'runDetailProblemRevealVideoFailed'
  ],
  [
    createUiProblem('history_action_failed', {
      params: { operation: 'delete_video' }
    }),
    'runDetailProblemDeleteVideoFailed'
  ],
  [
    createUiProblem('history_action_failed', {
      params: { operation: 'export_video' }
    }),
    'runDetailProblemUnexpected'
  ],
  [createUiProblem('run_detail_unexpected'), 'runDetailProblemUnexpected']
] as const satisfies readonly (readonly [unknown, MessageKey])[];

describe('Run Detail problem presentation', () => {
  it.each(rows)(
    'presents %o with %s in Chinese and English',
    (problem, key) => {
      for (const locale of ['zh', 'en'] as const) {
        expect(
          presentRunDetailProblem(problem, (k, params) =>
            formatMessage(locale, k, params)
          )
        ).toBe(formatMessage(locale, key, problem.params));
      }
    }
  );
});

import { describe, expect, it } from 'vitest';
import { formatMessage } from '../../i18n/messages';
import { createUiProblem } from '../shared/problems';
import { presentRunDetailProblem } from './runDetailProblems';

const problems = [
  createUiProblem('history_unavailable'),
  createUiProblem('history_read_failed', {
    params: { operation: 'get_run_detail' }
  }),
  createUiProblem('history_read_blocked_by_game', {
    params: { operation: 'get_run_detail' }
  }),
  createUiProblem('history_database_unsupported_schema', {
    params: { found: '2', expected: '1' }
  }),
  createUiProblem('history_action_failed', {
    params: { operation: 'reveal_screenshot' }
  }),
  createUiProblem('history_action_failed', {
    params: { operation: 'reveal_video' }
  }),
  createUiProblem('history_action_failed', {
    params: { operation: 'delete_video' }
  }),
  createUiProblem('run_detail_unexpected')
] as const;

describe('Run Detail problem presentation', () => {
  it.each(problems)(
    'has Chinese and English copy for $code $params',
    (problem) => {
      const zh = presentRunDetailProblem(problem, (key, params) =>
        formatMessage('zh', key, params)
      );
      const en = presentRunDetailProblem(problem, (key, params) =>
        formatMessage('en', key, params)
      );

      expect(zh).not.toBe(problem.code);
      expect(en).not.toBe(problem.code);
      expect(en).not.toBe(zh);
    }
  );
});

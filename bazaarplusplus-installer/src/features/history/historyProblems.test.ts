import { describe, expect, it } from 'vitest';
import { formatMessage } from '../../i18n/messages';
import { createUiProblem } from '../shared/problems';
import {
  presentHistoryProblem,
  type HistoryPageProblemCode
} from './historyProblems';

const codes: HistoryPageProblemCode[] = [
  'history_unavailable',
  'history_read_failed',
  'history_read_blocked_by_game',
  'history_database_unsupported_schema',
  'history_preview_unavailable',
  'history_unexpected'
];

describe('History problem presentation', () => {
  it.each(codes)('has complete Chinese and English copy for %s', (code) => {
    const problem = createUiProblem(code);
    const zh = presentHistoryProblem(problem, (key, params) =>
      formatMessage('zh', key, params)
    );
    const en = presentHistoryProblem(problem, (key, params) =>
      formatMessage('en', key, params)
    );

    expect(zh).not.toBe(code);
    expect(en).not.toBe(code);
    expect(en).not.toBe(zh);
  });
});

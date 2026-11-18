import { describe, expect, it } from 'vite-plus/test';
import { formatMessage, type MessageKey } from '../../i18n/messages';
import { createUiProblem } from '../shared/problems';
import {
  presentHistoryProblem,
  type HistoryPageProblemCode
} from './historyProblems';

const rows: [HistoryPageProblemCode, MessageKey][] = [
  ['history_unavailable', 'historyProblemUnavailable'],
  ['history_read_failed', 'historyProblemReadFailed'],
  ['history_read_blocked_by_game', 'historyProblemBlockedByGame'],
  ['history_database_unsupported_schema', 'historyProblemUnsupportedSchema'],
  ['history_thumbnails_unavailable', 'historyProblemThumbnailsUnavailable'],
  ['history_unexpected', 'historyProblemUnexpected']
];

describe('History problem presentation', () => {
  it.each(rows)('presents %s with %s in Chinese and English', (code, key) => {
    const problem = createUiProblem(code);
    for (const locale of ['zh', 'en'] as const) {
      expect(
        presentHistoryProblem(problem, (k, params) =>
          formatMessage(locale, k, params)
        )
      ).toBe(formatMessage(locale, key, problem.params));
    }
  });
});

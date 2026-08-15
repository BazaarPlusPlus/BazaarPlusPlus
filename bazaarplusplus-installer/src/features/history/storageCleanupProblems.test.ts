import { describe, expect, it } from 'vitest';
import { formatMessage } from '../../i18n/messages';
import {
  presentStorageCleanupProblem,
  storageCleanupProblemFromError
} from './storageCleanupProblems';

describe('storage cleanup problems', () => {
  it.each(['preview_storage_cleanup', 'execute_storage_cleanup'])(
    'preserves %s semantics and localizes without exposing diagnostics',
    (operation) => {
      const problem = storageCleanupProblemFromError({
        code: 'history_action_failed',
        params: { operation },
        diagnostic: 'database is locked'
      });
      const zh = presentStorageCleanupProblem(problem, (key, params) =>
        formatMessage('zh', key, params)
      );
      const en = presentStorageCleanupProblem(problem, (key, params) =>
        formatMessage('en', key, params)
      );

      expect(problem.params.operation).toBe(operation);
      expect(zh).not.toContain('database is locked');
      expect(en).not.toContain('database is locked');
      expect(zh).not.toBe(en);
    }
  );
});

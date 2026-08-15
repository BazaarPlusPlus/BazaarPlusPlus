import type { Translate } from '../../i18n/LocaleProvider';
import type { MessageKey } from '../../i18n/messages';
import {
  createUiProblem,
  problemFromError,
  type UiProblem
} from '../shared/problems';

export type StorageCleanupProblemCode =
  | 'history_unavailable'
  | 'history_action_failed'
  | 'storage_cleanup_unexpected';

export type StorageCleanupProblem = UiProblem<StorageCleanupProblemCode>;

export function storageCleanupProblemFromError(
  error: unknown
): StorageCleanupProblem {
  const problem = problemFromError(error, 'storage_cleanup_unexpected');
  switch (problem.code) {
    case 'history_unavailable':
    case 'history_action_failed':
    case 'storage_cleanup_unexpected':
      return problem as StorageCleanupProblem;
    default:
      return createUiProblem('storage_cleanup_unexpected', {
        params: problem.params,
        diagnostic: problem.diagnostic
      });
  }
}

export function presentStorageCleanupProblem(
  problem: StorageCleanupProblem,
  t: Translate
): string {
  return t(storageCleanupProblemMessageKey(problem), problem.params);
}

function storageCleanupProblemMessageKey(
  problem: StorageCleanupProblem
): MessageKey {
  switch (problem.code) {
    case 'history_unavailable':
      return 'storageCleanupProblemUnavailable';
    case 'history_action_failed':
      return problem.params.operation === 'preview_storage_cleanup'
        ? 'storageCleanupProblemPreviewFailed'
        : 'storageCleanupProblemExecuteFailed';
    case 'storage_cleanup_unexpected':
      return 'storageCleanupProblemUnexpected';
  }
}

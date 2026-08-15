import type { StreamServiceStatus } from '../../types/backend';
import { createUiProblem, problemFromError } from '../shared/problems';
import type { HistoryPageProblem } from './historyProblems';

export type HistoryPreviewState =
  | { phase: 'checking'; baseUrl: null; problem: null }
  | { phase: 'available'; baseUrl: string; problem: null }
  | {
      phase: 'unavailable';
      baseUrl: null;
      problem: HistoryPageProblem;
    };

export async function loadHistoryPreviewCapability(
  getStatus: () => Promise<StreamServiceStatus>
): Promise<HistoryPreviewState> {
  try {
    const status = await getStatus();
    if (status.running && status.base_url) {
      return {
        phase: 'available',
        baseUrl: status.base_url,
        problem: null
      };
    }
    return {
      phase: 'unavailable',
      baseUrl: null,
      problem: createUiProblem('history_preview_unavailable', {
        params: { reason: 'stream_stopped' },
        diagnostic: status.last_error
      })
    };
  } catch (caught) {
    const unexpected = problemFromError(caught, 'history_preview_unavailable');
    return {
      phase: 'unavailable',
      baseUrl: null,
      problem: createUiProblem('history_preview_unavailable', {
        params: { reason: 'status_failed' },
        diagnostic: unexpected.diagnostic
      })
    };
  }
}

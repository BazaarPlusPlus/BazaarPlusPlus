import {
  useCallback,
  useEffect,
  useMemo,
  useReducer,
  useRef,
  useState
} from 'react';
import type { HistoryRunRow } from '../../types/backend';
import { getStreamStatus } from '../shared/streamSessionApi';
import { isReadyPageState } from '../shared/pageState';
import { optionalStripPreviewUrl } from './stripPreview';
import { endGameProcess, listHistoryRuns } from './historyApi';
import { historyProblemFromError } from './historyProblems';
import {
  initialHistoryPageState,
  reduceHistoryPageState
} from './historyPageState';
import {
  loadHistoryPreviewCapability,
  type HistoryPreviewState
} from './historyPreview';

export type EndGameProcessOutcome = 'terminated' | 'already-exited' | 'failed';

export function useHistoryPage() {
  const [state, dispatch] = useReducer(
    reduceHistoryPageState,
    initialHistoryPageState
  );
  const [preview, setPreview] = useState<HistoryPreviewState>({
    phase: 'checking',
    baseUrl: null,
    problem: null
  });
  const historyRequestId = useRef(0);
  const previewRequestId = useRef(0);

  const refreshHistory = useCallback(async () => {
    const requestId = ++historyRequestId.current;
    dispatch({ type: 'request-started', requestId });
    try {
      const data = await listHistoryRuns();
      dispatch({ type: 'request-succeeded', requestId, data });
    } catch (caught) {
      dispatch({
        type: 'request-failed',
        requestId,
        problem: historyProblemFromError(caught)
      });
    }
  }, []);

  const refreshPreview = useCallback(async () => {
    const requestId = ++previewRequestId.current;
    setPreview({ phase: 'checking', baseUrl: null, problem: null });
    const nextPreview = await loadHistoryPreviewCapability(getStreamStatus);
    if (previewRequestId.current === requestId) {
      setPreview(nextPreview);
    }
  }, []);

  const refresh = useCallback(() => {
    void refreshHistory();
    void refreshPreview();
  }, [refreshHistory, refreshPreview]);

  useEffect(() => {
    refresh();
  }, [refresh]);

  const [endingGameProcess, setEndingGameProcess] = useState(false);

  // Reloads on every outcome: a process that was already gone leaves the same
  // stale error on screen as one this just ended.
  const endLeftoverGameProcess =
    useCallback(async (): Promise<EndGameProcessOutcome> => {
      setEndingGameProcess(true);
      try {
        const terminated = await endGameProcess();
        return terminated ? 'terminated' : 'already-exited';
      } catch {
        return 'failed';
      } finally {
        setEndingGameProcess(false);
        refresh();
      }
    }, [refresh]);

  const previewUrl = useCallback(
    (run: HistoryRunRow) =>
      optionalStripPreviewUrl(preview.baseUrl, run.strip_url),
    [preview.baseUrl]
  );

  const payload = isReadyPageState(state) ? state.data : null;
  const summary = useMemo(
    () =>
      payload
        ? {
            runs: String(payload.summary.runs),
            videos: String(payload.summary.videos),
            winRate:
              payload.summary.win_rate === null
                ? '-'
                : `${Math.round(payload.summary.win_rate * 100)}%`
          }
        : null,
    [payload]
  );

  return {
    state,
    summary,
    previewProblem: preview.problem,
    busy:
      state.phase === 'initial-loading' ||
      (isReadyPageState(state) && state.refresh.phase === 'refreshing'),
    previewUrl,
    refresh,
    endLeftoverGameProcess,
    endingGameProcess
  };
}

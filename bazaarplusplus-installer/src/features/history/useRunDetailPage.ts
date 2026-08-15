import { useCallback, useEffect, useReducer, useRef, useState } from 'react';
import { useParams } from 'react-router-dom';
import {
  deleteBattleVideo,
  loadHistoryRunDetail,
  revealBattleVideo,
  revealRunScreenshot
} from './historyApi';
import {
  actionAvailability,
  actionProblemFor,
  beginRunDetailAction,
  completeRunDetailAction,
  failRunDetailAction,
  initialRunDetailActionState,
  initialRunDetailPageState,
  reduceRunDetailPageState,
  type RunDetailActionName,
  type RunDetailActionState,
  type RunDetailActionTarget
} from './runDetailPageState';
import {
  runDetailProblemFromError,
  type RunDetailProblem
} from './runDetailProblems';
import type { ConfirmedOperationOutcome } from '../shared/confirmedOperation';

export function useRunDetailPage() {
  const { runId } = useParams<{ runId: string }>();
  const [state, dispatch] = useReducer(
    reduceRunDetailPageState,
    initialRunDetailPageState
  );
  const [actionState, setActionState] = useState(initialRunDetailActionState);
  const actionStateRef = useRef<RunDetailActionState>(
    initialRunDetailActionState
  );
  const requestIdRef = useRef(0);
  const requestInFlightRef = useRef(false);

  const commitActionState = useCallback((next: RunDetailActionState) => {
    actionStateRef.current = next;
    setActionState(next);
  }, []);

  const load = useCallback(
    async (resourceChanged: boolean) => {
      if (actionStateRef.current.current !== null) return false;
      const requestId = ++requestIdRef.current;
      requestInFlightRef.current = true;
      dispatch({
        type: resourceChanged ? 'resource-changed' : 'request-started',
        requestId
      });
      try {
        const data = runId ? await loadHistoryRunDetail(runId) : null;
        dispatch({ type: 'request-succeeded', requestId, data });
        if (requestIdRef.current === requestId) {
          commitActionState(initialRunDetailActionState);
        }
        return true;
      } catch (caught) {
        dispatch({
          type: 'request-failed',
          requestId,
          problem: runDetailProblemFromError(caught)
        });
        return false;
      } finally {
        if (requestIdRef.current === requestId) {
          requestInFlightRef.current = false;
        }
      }
    },
    [commitActionState, runId]
  );

  const refresh = useCallback(() => load(false), [load]);

  useEffect(() => {
    void load(true);
  }, [load]);

  const runAction = useCallback(
    async (
      name: RunDetailActionName,
      task: () => Promise<void>
    ): Promise<ConfirmedOperationOutcome<RunDetailProblem>> => {
      if (requestInFlightRef.current) {
        return {
          ok: false,
          problem: runDetailProblemFromError(
            new Error('Run Detail request is already in progress.')
          )
        };
      }
      const previous = actionStateRef.current;
      const started = beginRunDetailAction(previous, name);
      if (started === previous) {
        return {
          ok: false,
          problem: runDetailProblemFromError(
            new Error('Another Run Detail action is already in progress.')
          )
        };
      }
      commitActionState(started);

      try {
        await task();
        commitActionState(
          completeRunDetailAction(actionStateRef.current, name)
        );
        return { ok: true };
      } catch (caught) {
        const problem = runDetailProblemFromError(caught);
        commitActionState(
          failRunDetailAction(actionStateRef.current, name, problem)
        );
        return { ok: false, problem };
      }
    },
    [commitActionState]
  );

  const revealScreenshot = useCallback(async () => {
    if (state.phase !== 'ready') return false;
    return (
      await runAction('screenshot', () =>
        revealRunScreenshot(state.data.run.run_id)
      )
    ).ok;
  }, [runAction, state]);

  const revealVideo = useCallback(
    async (battleId: string, videoId?: string) => {
      if (state.phase !== 'ready' || !videoId) return false;
      return (
        await runAction(`video:${battleId}`, () =>
          revealBattleVideo(battleId, videoId)
        )
      ).ok;
    },
    [runAction, state.phase]
  );

  const deleteVideo = useCallback(
    (battleId: string, videoId: string) =>
      runAction(`delete:${battleId}`, async () => {
        const data = await deleteBattleVideo(battleId, videoId);
        dispatch({ type: 'data-replaced', data });
      }),
    [runAction]
  );

  const refreshing =
    state.phase === 'ready' && state.refresh.phase === 'refreshing';
  const availability = useCallback(
    (action: RunDetailActionName) =>
      actionAvailability(actionState, action, refreshing),
    [actionState, refreshing]
  );
  const problemFor = useCallback(
    (target: RunDetailActionTarget) => actionProblemFor(actionState, target),
    [actionState]
  );

  return {
    runId,
    state,
    detail: state.phase === 'ready' ? state.data : null,
    action: actionState.current,
    busy:
      state.phase === 'initial-loading' ||
      refreshing ||
      actionState.current !== null,
    refreshing,
    refresh,
    availability,
    problemFor,
    revealScreenshot,
    revealVideo,
    deleteVideo
  };
}

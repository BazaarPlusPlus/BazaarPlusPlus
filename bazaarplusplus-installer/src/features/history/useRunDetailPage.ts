import { useCallback, useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import type { HistoryRunDetail } from '../../types/backend';
import { toErrorMessage } from '../shared/errors';
import { useAsyncAction } from '../shared/useAsyncAction';
import {
  deleteBattleVideo,
  loadHistoryRunDetail,
  revealBattleVideo,
  revealRunScreenshot
} from './historyApi';

export function useRunDetailPage() {
  const { runId } = useParams<{ runId: string }>();
  const [detail, setDetail] = useState<HistoryRunDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const { action, error, setError, run } = useAsyncAction<string>();

  const refresh = useCallback(async () => {
    if (!runId) {
      setDetail(null);
      setLoading(false);
      return;
    }
    setLoading(true);
    setError(null);
    try {
      const nextDetail = await loadHistoryRunDetail(runId);
      setDetail(nextDetail);
    } catch (caught) {
      setError(toErrorMessage(caught));
    } finally {
      setLoading(false);
    }
  }, [runId, setError]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const revealScreenshot = useCallback(() => {
    if (!detail) return;
    void run('screenshot', () => revealRunScreenshot(detail.run.run_id));
  }, [detail, run]);

  const revealVideo = useCallback(
    (battleId: string, videoId?: string) => {
      void run(`video:${battleId}`, () => revealBattleVideo(battleId, videoId));
    },
    [run]
  );

  const deleteVideo = useCallback(
    (battleId: string, videoId: string) =>
      run(`delete:${battleId}`, async () => {
        setDetail(await deleteBattleVideo(battleId, videoId));
      }),
    [run]
  );

  return {
    runId,
    detail,
    loading,
    action,
    error,
    refresh,
    revealScreenshot,
    revealVideo,
    deleteVideo
  };
}

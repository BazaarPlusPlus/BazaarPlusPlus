import { useEffect, useMemo, useSyncExternalStore } from 'react';
import { useParams } from 'react-router-dom';
import {
  deleteBattleVideo,
  loadHistoryRunDetail,
  revealBattleVideo,
  revealRunScreenshot
} from './historyApi';
import { createRunDetailWorkflow } from './runDetailWorkflow';

const commands = {
  deleteBattleVideo,
  loadHistoryRunDetail,
  revealBattleVideo,
  revealRunScreenshot
};

export function useRunDetailPage() {
  const { runId } = useParams<{ runId: string }>();
  const workflow = useMemo(
    () => createRunDetailWorkflow(runId, commands),
    [runId]
  );
  const snapshot = useSyncExternalStore(
    workflow.subscribe,
    workflow.getSnapshot,
    workflow.getSnapshot
  );
  useEffect(() => {
    void workflow.start();
    return () => workflow.dispose();
  }, [workflow]);
  return { ...snapshot, ...workflow.intents };
}

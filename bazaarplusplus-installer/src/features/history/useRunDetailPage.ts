import { useEffect, useMemo, useSyncExternalStore } from 'react';
import { useParams } from 'react-router-dom';
import { commandClient } from '../../api/commandClient';
import { createRunDetailWorkflow } from './runDetailWorkflow';

export function useRunDetailPage() {
  const { runId } = useParams<{ runId: string }>();
  const workflow = useMemo(
    () => createRunDetailWorkflow(runId, commandClient),
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

import { useEffect, useMemo, useSyncExternalStore } from 'react';
import { installCommandPort } from './installApi';
import { createInstallWorkflow } from './installWorkflow';

export function useInstallPage() {
  const workflow = useMemo(
    () =>
      createInstallWorkflow({
        commands: installCommandPort
      }),
    []
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

  return { snapshot, intents: workflow.intents };
}

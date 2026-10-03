import { useEffect, useMemo, useSyncExternalStore } from 'react';
import { commandClient } from '../../api/commandClient';
import { createInstallWorkflow } from './installWorkflow';

export function useInstallPage() {
  const workflow = useMemo(
    () =>
      createInstallWorkflow({
        commands: commandClient
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

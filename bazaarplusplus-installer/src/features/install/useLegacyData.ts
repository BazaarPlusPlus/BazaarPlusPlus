import { useEffect, useMemo, useSyncExternalStore } from 'react';
import { commandClient } from '../../api/commandClient';
import type { InstallState } from '../../types/backend';
import { createLegacyDataWorkflow } from './legacyDataWorkflow';

/**
 * Legacy Roots of the selected game directory, reloaded whenever the install
 * state is redetected, for example after a reinstall changes which Data Root
 * the mod writes. Nothing loads until the install state is known.
 */
export function useLegacyData(installState: InstallState | null) {
  const workflow = useMemo(
    () => createLegacyDataWorkflow({ commands: commandClient }),
    []
  );
  const snapshot = useSyncExternalStore(
    workflow.subscribe,
    workflow.getSnapshot,
    workflow.getSnapshot
  );

  useEffect(() => {
    if (!installState) return;
    void workflow.load(installState.selected_game_path);
  }, [workflow, installState]);

  return { snapshot, workflow };
}

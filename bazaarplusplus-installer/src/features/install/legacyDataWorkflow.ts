import type { CommandAdapter } from '../../api/commandAdapter';
import type { LegacyDataState, LegacyRoot } from '../../types/backend';
import {
  createConfirmedOperationController,
  type ConfirmedOperationState
} from '../shared/confirmedOperation';
import {
  installProblemFromError,
  type InstallProblem
} from './installProblems';

export type LegacyDataCommandPort = Pick<
  CommandAdapter,
  'getLegacyDataState' | 'deleteLegacyRoot'
>;

/** Fixed when the confirmation opens, so a refresh cannot retarget it. */
export type LegacyDeleteTarget = { gamePath: string; name: LegacyRoot };

export type LegacyDataLoad =
  | { phase: 'idle' }
  | { phase: 'loading'; data: LegacyDataState | null }
  | { phase: 'ready'; data: LegacyDataState }
  | { phase: 'failed'; problem: InstallProblem };

export type LegacyDeleteNotice = {
  id: number;
  name: LegacyRoot;
  removed: boolean;
};

export type LegacyDataSnapshot = {
  load: LegacyDataLoad;
  confirmation: ConfirmedOperationState<LegacyDeleteTarget, InstallProblem>;
  notice: LegacyDeleteNotice | null;
};

export interface LegacyDataWorkflow {
  getSnapshot(): LegacyDataSnapshot;
  subscribe(listener: () => void): () => void;
  load(gamePath: string | null): Promise<void>;
  requestDelete(name: LegacyRoot): boolean;
  confirmDelete(): Promise<boolean>;
  dismissDelete(): boolean;
  acknowledgeNotice(id: number): void;
}

/**
 * Lists the selected game directory's Legacy Roots and deletes one at a time
 * after an explicit confirmation. Nothing here deletes on its own.
 */
export function createLegacyDataWorkflow({
  commands
}: {
  commands: LegacyDataCommandPort;
}): LegacyDataWorkflow {
  const confirmation = createConfirmedOperationController<
    LegacyDeleteTarget,
    InstallProblem
  >();
  const listeners = new Set<() => void>();
  let load: LegacyDataLoad = { phase: 'idle' };
  let notice: LegacyDeleteNotice | null = null;
  let nextNoticeId = 1;
  // Each load or deletion bumps the generation; an older response is dropped.
  let generation = 0;
  let snapshot: LegacyDataSnapshot = {
    load,
    confirmation: confirmation.getSnapshot(),
    notice
  };

  const publish = () => {
    snapshot = { load, confirmation: confirmation.getSnapshot(), notice };
    for (const listener of listeners) listener();
  };
  confirmation.subscribe(publish);

  const currentData = (): LegacyDataState | null =>
    load.phase === 'ready' || load.phase === 'loading' ? load.data : null;

  return {
    getSnapshot: () => snapshot,
    subscribe: (listener) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    load: async (gamePath) => {
      const requested = ++generation;
      load = { phase: 'loading', data: currentData() };
      publish();
      try {
        const data = await commands.getLegacyDataState(gamePath);
        if (requested !== generation) return;
        load = { phase: 'ready', data };
      } catch (caught) {
        if (requested !== generation) return;
        load = { phase: 'failed', problem: installProblemFromError(caught) };
      }
      publish();
    },
    requestDelete: (name) => {
      const data = currentData();
      if (!data?.game_path) return false;
      if (!data.roots.some((root) => root.name === name)) return false;
      return confirmation.request({ gamePath: data.game_path, name });
    },
    confirmDelete: () =>
      confirmation.run(async (target) => {
        const result = await commands.deleteLegacyRoot(
          target.gamePath,
          target.name
        );
        generation += 1;
        load = { phase: 'ready', data: result.state };
        notice = {
          id: nextNoticeId++,
          name: target.name,
          removed: result.removed
        };
        return { ok: true };
      }, installProblemFromError),
    dismissDelete: () => confirmation.dismiss(),
    acknowledgeNotice: (id) => {
      if (notice?.id !== id) return;
      notice = null;
      publish();
    }
  };
}

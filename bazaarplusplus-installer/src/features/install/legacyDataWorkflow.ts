import type { CommandAdapter } from '../../api/commandAdapter';
import type {
  ImportV5HistoryResult,
  LegacyDataState,
  LegacyRoot
} from '../../types/backend';
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
  'getLegacyDataState' | 'importV5History' | 'deleteLegacyRoot'
>;

/** What a completed V5 import reports beside the offer to delete V5. */
export type LegacyImportSummary = Omit<ImportV5HistoryResult, 'state'>;

/**
 * Fixed when the confirmation opens, so a refresh cannot retarget it.
 * `afterImport` marks the offer to delete `BazaarPlusPlusV5` that follows a
 * completed import.
 */
export type LegacyDeleteTarget = {
  gamePath: string;
  name: LegacyRoot;
  afterImport?: LegacyImportSummary;
};

/** The one-time V5 import, which runs without a confirmation: it only adds to
 * the current Data Root and leaves `BazaarPlusPlusV5` as it was. */
export type LegacyImportRun =
  | { phase: 'idle' }
  | { phase: 'running'; gamePath: string }
  | { phase: 'failed'; problem: InstallProblem };

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
  importRun: LegacyImportRun;
  notice: LegacyDeleteNotice | null;
};

export interface LegacyDataWorkflow {
  getSnapshot(): LegacyDataSnapshot;
  subscribe(listener: () => void): () => void;
  load(gamePath: string | null): Promise<void>;
  importV5(): Promise<boolean>;
  requestDelete(name: LegacyRoot): boolean;
  confirmDelete(): Promise<boolean>;
  dismissDelete(): boolean;
  acknowledgeNotice(id: number): void;
}

/**
 * Lists the selected game directory's Legacy Roots, runs the one-time V5
 * import, and deletes one root at a time after an explicit confirmation. A
 * completed import only opens the confirmation for `BazaarPlusPlusV5`; nothing
 * here deletes on its own.
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
  let importRun: LegacyImportRun = { phase: 'idle' };
  let notice: LegacyDeleteNotice | null = null;
  let nextNoticeId = 1;
  // Each load or deletion bumps the generation; an older response is dropped.
  let generation = 0;
  let snapshot: LegacyDataSnapshot = {
    load,
    confirmation: confirmation.getSnapshot(),
    importRun,
    notice
  };

  const publish = () => {
    snapshot = {
      load,
      confirmation: confirmation.getSnapshot(),
      importRun,
      notice
    };
    for (const listener of listeners) listener();
  };
  confirmation.subscribe(publish);

  const currentData = (): LegacyDataState | null =>
    load.phase === 'ready' || load.phase === 'loading' ? load.data : null;

  const loadState = async (gamePath: string | null) => {
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
  };

  return {
    getSnapshot: () => snapshot,
    subscribe: (listener) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    load: loadState,
    importV5: async () => {
      const data = currentData();
      if (!data?.game_path || !data.v5_import_eligible) return false;
      if (importRun.phase === 'running' || confirmation.getSnapshot()) {
        return false;
      }
      const gamePath = data.game_path;
      importRun = { phase: 'running', gamePath };
      publish();
      let result: ImportV5HistoryResult;
      try {
        result = await commands.importV5History(gamePath);
      } catch (caught) {
        importRun = {
          phase: 'failed',
          problem: installProblemFromError(caught)
        };
        publish();
        // The refusal may stem from a state this page has not seen yet, such
        // as an import that completed from another window.
        void loadState(gamePath);
        return false;
      }
      generation += 1;
      importRun = { phase: 'idle' };
      load = { phase: 'ready', data: result.state };
      publish();
      const { state, ...summary } = result;
      if (state.roots.some((root) => root.name === 'BazaarPlusPlusV5')) {
        confirmation.request({
          gamePath,
          name: 'BazaarPlusPlusV5',
          afterImport: summary
        });
      }
      return true;
    },
    requestDelete: (name) => {
      const data = currentData();
      if (!data?.game_path) return false;
      if (importRun.phase === 'running') return false;
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

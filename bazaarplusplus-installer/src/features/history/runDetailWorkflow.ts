import type { HistoryRunDetail } from '../../types/backend';
import type { ConfirmedOperationOutcome } from '../shared/confirmedOperation';
import type { PageRefreshState } from '../shared/pageState';
import {
  runDetailProblemFromError,
  type RunDetailProblem
} from './runDetailProblems';

export interface RunDetailCommands {
  loadHistoryRunDetail(runId: string): Promise<HistoryRunDetail | null>;
  revealRunScreenshot(runId: string): Promise<void>;
  revealBattleVideo(battleId: string, videoId?: string): Promise<void>;
  deleteBattleVideo(
    battleId: string,
    videoId: string
  ): Promise<HistoryRunDetail>;
}

type Resource =
  | { phase: 'initial-loading' }
  | { phase: 'not-found' }
  | { phase: 'blocking-failure'; problem: RunDetailProblem }
  | {
      phase: 'ready';
      data: HistoryRunDetail;
      refreshProblem: RunDetailProblem | null;
    };

type PageState =
  | Exclude<Resource, { phase: 'ready' }>
  | {
      phase: 'ready';
      data: HistoryRunDetail;
      refresh: PageRefreshState<RunDetailProblem>;
    };

type Action =
  { kind: 'screenshot' } | { kind: 'reveal' | 'delete'; battleId: string };
type BattleFailure = { action: 'reveal' | 'delete'; problem: RunDetailProblem };
type Availability = { disabled: boolean; running: boolean };
type BattleActions = {
  reveal: Availability;
  delete: Availability;
  failure: BattleFailure | null;
};

export function createRunDetailWorkflow(
  runId: string | undefined,
  commands: RunDetailCommands
) {
  const listeners = new Set<() => void>();
  let active = false;
  let resource: Resource = { phase: 'initial-loading' };
  // Identity owns completion: replacing a load or restarting the lifecycle
  // invalidates the old token without maintaining a second request counter.
  let pending: Action | { kind: 'load' } | null = null;
  let screenshotProblem: RunDetailProblem | null = null;
  const battleProblems = new Map<string, BattleFailure>();
  let snapshot = deriveSnapshot();

  function deriveSnapshot() {
    const refreshing = resource.phase === 'ready' && pending?.kind === 'load';
    const busy = resource.phase === 'initial-loading' || pending !== null;
    const detail = resource.phase === 'ready' ? resource.data : null;
    const state: PageState =
      resource.phase === 'ready'
        ? {
            phase: 'ready',
            data: resource.data,
            refresh: refreshing
              ? { phase: 'refreshing' }
              : resource.refreshProblem
                ? { phase: 'failed', problem: resource.refreshProblem }
                : { phase: 'idle' }
          }
        : resource;
    const battles: Record<string, BattleActions> = Object.create(null);
    for (const battle of detail?.battles ?? []) {
      const action =
        pending &&
        'battleId' in pending &&
        pending.battleId === battle.battle_id
          ? pending.kind
          : null;
      battles[battle.battle_id] = {
        reveal: { disabled: busy, running: action === 'reveal' },
        delete: { disabled: busy, running: action === 'delete' },
        failure: battleProblems.get(battle.battle_id) ?? null
      };
    }
    return {
      state,
      detail,
      busy,
      refreshing,
      screenshot: {
        disabled: !detail || busy,
        running: pending?.kind === 'screenshot',
        problem: screenshotProblem
      },
      battles
    };
  }

  function publish() {
    snapshot = deriveSnapshot();
    for (const listener of listeners) listener();
  }

  async function load(): Promise<boolean> {
    if (!active || (pending && pending.kind !== 'load')) return false;
    const request = { kind: 'load' } as const;
    pending = request;
    if (resource.phase !== 'ready') resource = { phase: 'initial-loading' };
    publish();
    try {
      const data = runId ? await commands.loadHistoryRunDetail(runId) : null;
      if (!active || pending !== request) return false;
      pending = null;
      resource = data
        ? { phase: 'ready', data, refreshProblem: null }
        : { phase: 'not-found' };
      screenshotProblem = null;
      battleProblems.clear();
      publish();
      return true;
    } catch (caught) {
      if (!active || pending !== request) return false;
      pending = null;
      const problem = runDetailProblemFromError(caught);
      resource =
        resource.phase === 'ready'
          ? { ...resource, refreshProblem: problem }
          : { phase: 'blocking-failure', problem };
      publish();
      return false;
    }
  }

  async function runAction(
    action: Action,
    task: () => Promise<void | HistoryRunDetail>
  ): Promise<ConfirmedOperationOutcome<RunDetailProblem>> {
    if (!active || pending || resource.phase !== 'ready') {
      return {
        ok: false,
        problem: runDetailProblemFromError(
          new Error('Run Detail is not ready for another action.')
        )
      };
    }
    pending = action;
    if (action.kind === 'screenshot') screenshotProblem = null;
    else battleProblems.delete(action.battleId);
    publish();
    try {
      const data = await task();
      if (active && pending === action) {
        if (data)
          resource = {
            phase: 'ready',
            data,
            refreshProblem: resource.refreshProblem
          };
        pending = null;
        publish();
      }
      return { ok: true };
    } catch (caught) {
      const problem = runDetailProblemFromError(caught);
      if (active && pending === action) {
        if (action.kind === 'screenshot') screenshotProblem = problem;
        else
          battleProblems.set(action.battleId, { action: action.kind, problem });
        pending = null;
        publish();
      }
      return { ok: false, problem };
    }
  }

  return {
    getSnapshot: () => snapshot,
    subscribe: (listener: () => void) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    start: async () => {
      if (active) return;
      active = true;
      resource = { phase: 'initial-loading' };
      screenshotProblem = null;
      battleProblems.clear();
      await load();
    },
    dispose: () => {
      active = false;
      pending = null;
    },
    intents: {
      refresh: load,
      revealScreenshot: async () => {
        if (resource.phase !== 'ready') return false;
        const target = resource.data.run.run_id;
        return (
          await runAction({ kind: 'screenshot' }, () =>
            commands.revealRunScreenshot(target)
          )
        ).ok;
      },
      revealVideo: async (battleId: string, videoId?: string) => {
        if (!videoId) return false;
        return (
          await runAction({ kind: 'reveal', battleId }, () =>
            commands.revealBattleVideo(battleId, videoId)
          )
        ).ok;
      },
      deleteVideo: (battleId: string, videoId: string) =>
        runAction({ kind: 'delete', battleId }, () =>
          commands.deleteBattleVideo(battleId, videoId)
        )
    }
  };
}

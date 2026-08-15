import { useMemo, useSyncExternalStore } from 'react';

export type ConfirmedOperationOutcome<TProblem> =
  { ok: true } | { ok: false; problem: TProblem };

export type ConfirmedOperationState<TTarget, TProblem> =
  | {
      phase: 'confirming' | 'running';
      target: TTarget;
      problem: null;
    }
  | {
      phase: 'failed';
      target: TTarget;
      problem: TProblem;
    }
  | null;

export interface ConfirmedOperationController<TTarget, TProblem> {
  getSnapshot(): ConfirmedOperationState<TTarget, TProblem>;
  subscribe(listener: () => void): () => void;
  request(target: TTarget): boolean;
  /** Replace the retained target while still confirming (e.g. pending install options). */
  updateTarget(target: TTarget): boolean;
  dismiss(): boolean;
  clear(): void;
  run(
    execute: (target: TTarget) => Promise<ConfirmedOperationOutcome<TProblem>>,
    problemFromError: (error: unknown) => TProblem
  ): Promise<boolean>;
}

export function createConfirmedOperationController<
  TTarget,
  TProblem
>(): ConfirmedOperationController<TTarget, TProblem> {
  let state: ConfirmedOperationState<TTarget, TProblem> = null;
  const listeners = new Set<() => void>();

  const publish = (next: ConfirmedOperationState<TTarget, TProblem>) => {
    state = next;
    for (const listener of listeners) listener();
  };

  return {
    getSnapshot: () => state,
    subscribe: (listener) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    request: (target) => {
      if (state !== null) return false;
      publish({ phase: 'confirming', target, problem: null });
      return true;
    },
    updateTarget: (target) => {
      if (!state || state.phase !== 'confirming') return false;
      publish({ phase: 'confirming', target, problem: null });
      return true;
    },
    dismiss: () => {
      if (!state || state.phase === 'running') return false;
      publish(null);
      return true;
    },
    clear: () => {
      publish(null);
    },
    run: async (execute, problemFromError) => {
      if (!state || state.phase === 'running') return false;
      const target = state.target;
      publish({ phase: 'running', target, problem: null });

      let outcome: ConfirmedOperationOutcome<TProblem>;
      try {
        outcome = await execute(target);
      } catch (caught) {
        outcome = { ok: false, problem: problemFromError(caught) };
      }

      if (outcome.ok) {
        publish(null);
        return true;
      }

      publish({ phase: 'failed', target, problem: outcome.problem });
      return false;
    }
  };
}

export function useConfirmedOperation<TTarget, TProblem>() {
  const controller = useMemo(
    () => createConfirmedOperationController<TTarget, TProblem>(),
    []
  );
  const state = useSyncExternalStore(
    controller.subscribe,
    controller.getSnapshot,
    controller.getSnapshot
  );

  return { state, controller };
}

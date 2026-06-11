import { useCallback, useRef, useState } from 'react';
import { toErrorMessage } from './errors';

type AsyncActionOptions = {
  onStart?: () => void;
  errorMessage?: (error: unknown) => string;
};

type AsyncActionLifecycle<TAction extends string> = {
  onActionStart: (name: TAction) => void;
  onError: (message: string) => void;
  onActionEnd: () => void;
};

export type AsyncActionGate<TAction extends string> = {
  current: TAction | null;
  runId: number;
};

export function createAsyncActionGate<
  TAction extends string
>(): AsyncActionGate<TAction> {
  return {
    current: null,
    runId: 0
  };
}

export async function runSingleFlightAction<TAction extends string>(
  gate: AsyncActionGate<TAction>,
  name: TAction,
  task: () => Promise<void>,
  lifecycle: AsyncActionLifecycle<TAction>,
  options?: AsyncActionOptions
) {
  if (gate.current !== null) {
    return false;
  }

  gate.current = name;
  gate.runId += 1;
  const runId = gate.runId;
  lifecycle.onActionStart(name);

  try {
    await task();
    return true;
  } catch (caught) {
    if (gate.runId === runId) {
      lifecycle.onError(
        options?.errorMessage?.(caught) ?? toErrorMessage(caught)
      );
    }
    return false;
  } finally {
    if (gate.runId === runId) {
      gate.current = null;
      lifecycle.onActionEnd();
    }
  }
}

export function useAsyncAction<TAction extends string = string>() {
  const [action, setAction] = useState<TAction | null>(null);
  const [error, setError] = useState<string | null>(null);
  const gate = useRef(createAsyncActionGate<TAction>());

  const run = useCallback(
    async (
      name: TAction,
      task: () => Promise<void>,
      options?: AsyncActionOptions
    ) => {
      return runSingleFlightAction(
        gate.current,
        name,
        task,
        {
          onActionStart: (actionName) => {
            setAction(actionName);
            setError(null);
            options?.onStart?.();
          },
          onError: setError,
          onActionEnd: () => setAction(null)
        },
        options
      );
    },
    []
  );

  const clearError = useCallback(() => setError(null), []);

  return {
    action,
    error,
    setError,
    clearError,
    run,
    busy: action !== null
  };
}

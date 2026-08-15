import { useRef, useState } from 'react';
import type {
  StorageCleanupExecution,
  StorageCleanupPreset,
  StorageCleanupPreview,
  StorageCleanupScope
} from '../../types/backend';
import { useConfirmedOperation } from '../shared/confirmedOperation';
import { executeStorageCleanup, previewStorageCleanup } from './historyApi';
import {
  storageCleanupProblemFromError,
  type StorageCleanupProblem
} from './storageCleanupProblems';

export type CleanupScope = StorageCleanupScope;

export type PendingCleanup = StorageCleanupPreview & {
  preset: StorageCleanupPreset;
};

export type CleanupOutcome = StorageCleanupExecution;

export function useStorageCleanup(onCompleted: () => Promise<void> | void) {
  const [outcome, setOutcome] = useState<CleanupOutcome | null>(null);
  const [previewing, setPreviewing] = useState(false);
  const [previewProblem, setPreviewProblem] =
    useState<StorageCleanupProblem | null>(null);
  const previewInFlight = useRef(false);
  const operation = useConfirmedOperation<
    PendingCleanup,
    StorageCleanupProblem
  >();

  const requestCleanup = async (
    scope: CleanupScope,
    preset: StorageCleanupPreset
  ) => {
    if (previewInFlight.current || operation.controller.getSnapshot()) {
      return false;
    }
    previewInFlight.current = true;
    setPreviewing(true);
    setPreviewProblem(null);
    setOutcome(null);
    try {
      const preview = await previewStorageCleanup(scope, preset);
      return operation.controller.request({ ...preview, preset });
    } catch (caught) {
      setPreviewProblem(storageCleanupProblemFromError(caught));
      return false;
    } finally {
      previewInFlight.current = false;
      setPreviewing(false);
    }
  };

  const confirm = () =>
    operation.controller.run(async (target) => {
      const result = await executeStorageCleanup(target.scope, target.preset);
      await onCompleted();
      setOutcome(result);
      return { ok: true };
    }, storageCleanupProblemFromError);

  const pending = operation.state?.target ?? null;

  return {
    pending,
    outcome,
    operation: operation.state,
    problem:
      operation.state?.phase === 'failed' ? operation.state.problem : null,
    previewProblem,
    busy: previewing || operation.state?.phase === 'running',
    requestCleanup,
    confirm,
    cancel: operation.controller.dismiss
  };
}

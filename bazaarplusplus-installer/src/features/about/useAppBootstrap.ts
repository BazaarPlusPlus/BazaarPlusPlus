import { useEffect, useRef, useState } from 'react';
import type { AppBootstrap } from '../../types/backend';
import { fallbackBootstrap } from '../../api/previewDefaults';
import { loadAppBootstrap } from './aboutApi';
import {
  createAppBootstrapMachine,
  createInitialAppBootstrapSnapshot,
  type AppBootstrapMachine,
  type AppBootstrapSnapshot
} from './appBootstrap';

export function useAppBootstrapState() {
  const [resource, setResource] = useState<AppBootstrapSnapshot>(() =>
    createInitialAppBootstrapSnapshot(fallbackBootstrap)
  );
  const mountedRef = useRef(true);
  const machineRef = useRef<AppBootstrapMachine | null>(null);
  machineRef.current ??= createAppBootstrapMachine(
    { fallback: fallbackBootstrap, load: loadAppBootstrap },
    (snapshot) => {
      if (mountedRef.current) setResource(snapshot);
    }
  );
  const machine = machineRef.current;

  useEffect(() => {
    mountedRef.current = true;
    void machine.start();
    return () => {
      mountedRef.current = false;
    };
  }, [machine]);

  // The production controller always has the packaged fallback. The resource
  // union still models no-data failure for callers with a different bootstrap
  // source and keeps that case testable without weakening shell link types.
  const bootstrap: AppBootstrap = resource.data ?? fallbackBootstrap;
  return {
    bootstrap,
    resource,
    retry: () => void machine.retry()
  };
}

export type AppBootstrapController = ReturnType<typeof useAppBootstrapState>;

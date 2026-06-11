import { useEffect, useState } from 'react';
import type { AppBootstrap } from '../../types/backend';
import { fallbackBootstrap, loadAppBootstrap } from './aboutApi';

export function useAppBootstrapState() {
  const [bootstrap, setBootstrap] = useState<AppBootstrap>(fallbackBootstrap);

  useEffect(() => {
    let mounted = true;
    loadAppBootstrap()
      .then((payload) => {
        if (mounted) setBootstrap(payload);
      })
      .catch((error) => {
        console.error(
          'Failed to load app bootstrap from Tauri runtime.',
          error
        );
        if (mounted) setBootstrap(fallbackBootstrap);
      });
    return () => {
      mounted = false;
    };
  }, []);

  return { bootstrap };
}

export type AppBootstrapController = ReturnType<typeof useAppBootstrapState>;

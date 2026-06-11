import { createContext, use } from 'react';
import type { ReactNode } from 'react';
import { useUpdaterState, type UpdaterController } from './useUpdater';

const UpdaterContext = createContext<UpdaterController | null>(null);

export function UpdaterProvider({ children }: { children: ReactNode }) {
  const updater = useUpdaterState();
  return (
    <UpdaterContext.Provider value={updater}>
      {children}
    </UpdaterContext.Provider>
  );
}

export function useUpdater() {
  const updater = use(UpdaterContext);
  if (!updater) {
    throw new Error('useUpdater must be used inside UpdaterProvider.');
  }
  return updater;
}

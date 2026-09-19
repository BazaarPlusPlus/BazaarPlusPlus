import { useCallback, useEffect, useRef, useState } from 'react';
import { hasTauriRuntime } from '../../api/runtime';
import {
  claimStartupCheck,
  createUpdaterMachine,
  initialUpdaterSnapshot,
  tauriUpdaterImpl,
  type UpdaterMachine,
  type UpdaterSnapshot
} from './updater';

export type UpdaterController = UpdaterSnapshot & {
  checkNow: () => void;
  install: () => void;
  restart: () => void;
  dismiss: () => void;
};

export function useUpdaterState(): UpdaterController {
  const [snapshot, setSnapshot] = useState(initialUpdaterSnapshot);
  const machineRef = useRef<UpdaterMachine | null>(null);
  machineRef.current ??= createUpdaterMachine(tauriUpdaterImpl, setSnapshot);
  const machine = machineRef.current;

  useEffect(() => {
    if (!hasTauriRuntime()) return;
    if (!claimStartupCheck()) return;
    void machine.checkNow({ silent: true });
  }, [machine]);

  // Successful manual check results briefly replace the header action. A check
  // failure remains until retry so its localized recovery guidance is not lost.
  const { phase } = snapshot;
  useEffect(() => {
    const isHeaderResult = phase === 'current' || phase === 'preview';
    if (!isHeaderResult) return;
    const timer = window.setTimeout(() => machine.dismiss(), 3000);
    return () => window.clearTimeout(timer);
  }, [phase, machine]);

  // Stable identities: GlobalShell's updater toast effect depends on `checkNow`,
  // so a fresh arrow per render would reschedule the effect on every render.
  const checkNow = useCallback(() => void machine.checkNow(), [machine]);
  const install = useCallback(() => void machine.install(), [machine]);
  const restart = useCallback(() => void machine.restart(), [machine]);

  return {
    ...snapshot,
    checkNow,
    install,
    restart,
    dismiss: machine.dismiss
  };
}

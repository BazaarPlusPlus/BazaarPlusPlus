import { useEffect, useRef, useState } from 'react';
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

  return {
    ...snapshot,
    checkNow: () => void machine.checkNow(),
    install: () => void machine.install(),
    restart: () => void machine.restart(),
    dismiss: machine.dismiss
  };
}

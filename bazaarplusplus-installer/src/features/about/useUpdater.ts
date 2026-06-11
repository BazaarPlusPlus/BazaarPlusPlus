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

  // Manual check results (up to date / preview / check failed) are shown inside
  // the header button; auto-clear them after a moment so the button reverts to
  // its idle label instead of displaying a stale result indefinitely.
  const { phase, errorSource } = snapshot;
  useEffect(() => {
    const isHeaderResult =
      phase === 'current' ||
      phase === 'preview' ||
      (phase === 'error' && errorSource === 'check');
    if (!isHeaderResult) return;
    const timer = window.setTimeout(() => machine.dismiss(), 3000);
    return () => window.clearTimeout(timer);
  }, [phase, errorSource, machine]);

  return {
    ...snapshot,
    checkNow: () => void machine.checkNow(),
    install: () => void machine.install(),
    restart: () => void machine.restart(),
    dismiss: machine.dismiss
  };
}

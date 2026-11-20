import { useCallback, useEffect, useRef, useState } from 'react';
import { commandClient } from '../../api/commandClient';
import { observeWindowResume } from '../shared/windowActivity';

/** Refresh the close hint on demand so the shell does not poll while hidden or idle. */
export function useShellStreamServiceRunning() {
  const [running, setRunning] = useState(false);
  const request = useRef(0);
  const active = useRef(true);

  const refresh = useCallback(() => {
    const current = ++request.current;
    const accept = () => active.current && current === request.current;
    void commandClient.getStreamStatus().then(
      (status) => {
        if (accept()) setRunning(status.running);
      },
      () => {
        if (accept()) setRunning(false);
      }
    );
  }, []);

  useEffect(() => {
    active.current = true;
    refresh();
    const stopObserving = observeWindowResume(refresh);
    return () => {
      active.current = false;
      stopObserving();
    };
  }, [refresh]);

  return { running, refresh };
}

import { useCallback, useEffect, useRef, useState } from 'react';
import { commandClient } from '../../api/commandClient';
import { observeWindowResume } from '../shared/windowActivity';

/**
 * Shell hint for the window-close copy; not the Stream page source of truth.
 * It reads the stream status on mount, whenever the window comes back, and
 * through `refresh` when the user reaches for the close control, instead of
 * polling while the window sits idle or hidden in the tray.
 */
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

import { useEffect, useState } from 'react';
import { getStreamStatus } from '../shared/streamSessionApi';

/** Low-frequency shell hint for window-close copy. Not the Stream page source of truth. */
const SHELL_STREAM_STATUS_POLL_MS = 4000;

export function useShellStreamServiceRunning() {
  const [running, setRunning] = useState(false);

  useEffect(() => {
    let cancelled = false;

    const poll = async () => {
      try {
        const status = await getStreamStatus();
        if (!cancelled) {
          setRunning(Boolean(status.running));
        }
      } catch {
        if (!cancelled) {
          setRunning(false);
        }
      }
    };

    void poll();
    const timer = window.setInterval(() => {
      void poll();
    }, SHELL_STREAM_STATUS_POLL_MS);

    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, []);

  return running;
}
